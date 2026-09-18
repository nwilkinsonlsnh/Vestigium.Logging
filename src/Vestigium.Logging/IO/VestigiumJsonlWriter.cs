using System.Collections.Concurrent;
using System.Text;

namespace Vestigium.Logging;

internal sealed partial class VestigiumJsonlWriter : IVestigiumJsonlWriter
{
    private readonly string _directory;
    private readonly string _appId;
    private readonly long _sizeLimit;
    private readonly TimeSpan _retainTime;
    private readonly int _retainCount;
    private readonly int _queueCap;
    private readonly Func<DateTime> _clock;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Task _loop;
    private readonly ManualResetEventSlim _progress = new(initialState: true);
    private int _queued, _dropped, _written, _issued, _ioFaults, _stopped, _flushGen, _flushDone;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private DateTime _fileDate;
    private int _filePart = 1;
    private long _fileLength;

    public VestigiumJsonlWriter(string directory, string appId, long fileSizeLimitBytes = 20L * 1024 * 1024,
        TimeSpan? retainedFileTimeLimit = null, int retainedFileCountLimit = 90, int queueCapacity = 10_000,
        Func<DateTime>? utcClock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        _directory = directory;
        _appId = appId;
        _sizeLimit = fileSizeLimitBytes <= 0 ? 20L * 1024 * 1024 : fileSizeLimitBytes;
        _retainTime = retainedFileTimeLimit is null || retainedFileTimeLimit <= TimeSpan.Zero ? TimeSpan.FromDays(14) : retainedFileTimeLimit.Value;
        _retainCount = retainedFileCountLimit <= 0 ? 90 : retainedFileCountLimit;
        _queueCap = queueCapacity <= 0 ? 10_000 : queueCapacity;
        _clock = utcClock ?? (() => DateTime.UtcNow);
        Directory.CreateDirectory(_directory);
        ApplyRetention();
        _loop = Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public VestigiumJsonlWriter(VestigiumLoggerOptions options, Func<DateTime>? utcClock = null)
        : this(options.ResolveLogDirectory(), options.AppId, options.FileSizeLimitBytes, options.RetainedFileTimeLimit,
            options.RetainedFileCountLimit, options.DiskQueueCapacity, utcClock) { }

    public int QueuedCount => Math.Max(0, Volatile.Read(ref _queued));
    public int DroppedCount => Volatile.Read(ref _dropped);
    public int WrittenCount => Volatile.Read(ref _written);
    public int IoFaultCount => Volatile.Read(ref _ioFaults);
    public string? ActivePath { get; private set; }

    public void Enqueue(string jsonLine)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        ArgumentNullException.ThrowIfNull(jsonLine);
        Interlocked.Increment(ref _issued);
        _queue.Enqueue(jsonLine);
        var depth = Interlocked.Increment(ref _queued);
        while (depth > _queueCap && _queue.TryDequeue(out _))
        {
            depth = Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _dropped);
        }
        _progress.Reset();
        _signal.Release();
    }

    public bool Flush(TimeSpan timeout)
    {
        if (Volatile.Read(ref _stopped) != 0) return Volatile.Read(ref _queued) == 0;
        var targetIssued = Volatile.Read(ref _issued);
        var gen = Interlocked.Increment(ref _flushGen);
        _signal.Release();
        if (timeout <= TimeSpan.Zero) return IsCaughtUp(targetIssued, gen);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !IsCaughtUp(targetIssued, gen))
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            _progress.Wait(left > TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : left);
        }
        return IsCaughtUp(targetIssued, gen);
    }

    private bool IsCaughtUp(int targetIssued, int gen) =>
        Volatile.Read(ref _written) >= targetIssued && Volatile.Read(ref _flushDone) >= gen;

    public void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1) return;
        _signal.Release();
        try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        CloseStream();
        _progress.Set();
    }

    public void Dispose() => Complete();

    private void Pump()
    {
        while (true)
        {
            var stopped = Volatile.Read(ref _stopped) != 0;
            var drained = true;
            while (_queue.TryDequeue(out var line))
            {
                drained = false;
                Interlocked.Decrement(ref _queued);
                WriteLine(line);
                Interlocked.Increment(ref _written);
            }
            var gen = Volatile.Read(ref _flushGen);
            if (gen > Volatile.Read(ref _flushDone))
            {
                try { _writer?.Flush(); _stream?.Flush(flushToDisk: true); }
                catch { Interlocked.Increment(ref _ioFaults); CloseStream(); }
                Volatile.Write(ref _flushDone, gen);
            }
            _progress.Set();
            if (stopped && drained) break;
            try { _signal.Wait(TimeSpan.FromMilliseconds(100)); }
            catch (ObjectDisposedException) { break; }
        }
        CloseStream();
    }

    private void WriteLine(string line)
    {
        try
        {
            EnsureStream(Encoding.UTF8.GetByteCount(line) + 1);
            _writer!.Write(line);
            if (!line.EndsWith('\n')) _writer.Write('\n');
            _fileLength += Encoding.UTF8.GetByteCount(line) + (line.EndsWith('\n') ? 0 : 1);
            _lineCount++;
        }
        catch { Interlocked.Increment(ref _ioFaults); CloseStream(); }
    }

    private void EnsureStream(int incomingBytes)
    {
        var today = _clock().Date;
        var needNewDate = _writer is null || today != _fileDate;
        var needSizeRoll = _writer is not null && _fileLength > 0 && _fileLength + incomingBytes > _sizeLimit;
        if (!needNewDate && !needSizeRoll && _writer is not null) return;
        var rolledFrom = ActivePath;
        CloseStream();
        if (rolledFrom is not null && (needNewDate || needSizeRoll))
            OpsSink?.Invoke(5010, "Log file rolled.", new Dictionary<string, string?> { ["from"] = rolledFrom });
        RollCounters(today, needNewDate, needSizeRoll);
        OpenAppend(incomingBytes);
    }

    private void RollCounters(DateTime today, bool needNewDate, bool needSizeRoll)
    {
        if (needNewDate) { _fileDate = today; _filePart = 1; ApplyRetention(); return; }
        if (needSizeRoll) { _filePart++; ApplyRetention(); }
    }

    private void OpenAppend(int incomingBytes)
    {
        while (true)
        {
            var path = FilePath(_fileDate, _filePart);
            var exists = File.Exists(path);
            var existingLength = exists ? new FileInfo(path).Length : 0;
            if (exists && existingLength > 0 && existingLength + incomingBytes > _sizeLimit) { _filePart++; continue; }
            _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.SequentialScan);
            _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false, NewLine = "\n" };
            ActivePath = path;
            _fileLength = _stream.Length;
            return;
        }
    }

    private string FilePath(DateTime utcDate, int part)
    {
        var stamp = utcDate.ToString("yyyyMMdd");
        var name = part <= 1 ? $"vestigium-{_appId}-{stamp}.json" : $"vestigium-{_appId}-{stamp}-{part}.json";
        return Path.Combine(_directory, name);
    }

    private void ApplyRetention()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;
            var files = Directory.GetFiles(_directory, $"vestigium-{_appId}-*.json")
                .Select(p => new FileInfo(p))
                .Where(f => !string.Equals(f.FullName, ActivePath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc).ToList();
            while (files.Count > _retainCount) { TryDelete(files[0]); files.RemoveAt(0); }
        }
        catch { Interlocked.Increment(ref _ioFaults); }
    }

    private static void TryDelete(FileInfo file) { try { file.Delete(); } catch { } }

    private void CloseStream()
    {
        var path = ActivePath;
        var lines = _lineCount;
        try { _writer?.Flush(); } catch { }
        try { _stream?.Flush(flushToDisk: true); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _writer = null; _stream = null; ActivePath = null;
        TryAppendTrailer(path, lines);
        _lineCount = 0;
    }
}
