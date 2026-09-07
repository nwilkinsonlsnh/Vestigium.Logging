using System.Collections.Concurrent;
using System.Threading.Channels;
using Serilog;
using Serilog.Events;

namespace Vestigium.Logging;

/// <summary>Process-wide Vestigium logger. Call <see cref="Initialize"/> once at startup.</summary>
public static class VestigiumLogger
{
    private static readonly object Gate = new();
    private static Host? _host;

    public static bool IsInitialized => _host is not null;

    public static VestigiumLoggerOptions Options => Require().Options;

    public static IObservable<VestigiumLogEvent> Events => Require().Subject;

    public static ChannelReader<VestigiumLogEvent> EventReader => Require().Channel.Reader;

    public static bool IsDiskTripped => _host?.Disk.IsTripped ?? false;

    public static IReadOnlyList<string> RecentJsonLines => _host?.RecentSnapshot() ?? [];

    public static int WrittenCount => _host?.WrittenCount ?? 0;

    public static int SuppressedCount => _host?.Flood.PendingSuppressed ?? 0;

    public static void Initialize(Action<VestigiumLoggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new VestigiumLoggerOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.AppId))
            throw new ArgumentException("AppId is required.", nameof(configure));

        lock (Gate)
        {
            _host?.Dispose();
            _host = new Host(options);
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _host?.Dispose();
            _host = null;
        }
    }

    public static void Flush() => _host?.Flush();

    /// <summary>Hook process-exit and Ctrl+C. WPF hosts should also flush from Application.Exit (the Demo does).</summary>
    public static void BindLifetime(object? wpfApplication)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress -= OnCancel;
        Console.CancelKeyPress += OnCancel;
        _ = wpfApplication;
    }


    /// <summary>Demo / test hook. Pass <c>true</c> to force the low-disk throttle, <c>null</c> to use the real poller.</summary>
    public static void OverrideDiskPressure(bool? tripped) => _host?.Disk.Override(tripped);

    internal static Host Require() =>
        _host ?? throw new InvalidOperationException("VestigiumLogger.Initialize must run during application startup.");

    internal static void Emit(VestigiumLogLevel level, VestigiumStatus status, string category, string subcategory, string message, Exception? exception, string? appId = null)
        => Require().Emit(level, status, category, subcategory, message, exception, appId);

    private static void OnProcessExit(object? sender, EventArgs e) => Flush();

    private static void OnCancel(object? sender, ConsoleCancelEventArgs e) => Flush();

    internal sealed class Host : IDisposable
    {
        public VestigiumLoggerOptions Options { get; }
        public FloodTracker Flood { get; }
        public DiskSpaceMonitor Disk { get; }
        public LogEventSubject Subject { get; } = new();
        public Channel<VestigiumLogEvent> Channel { get; }
        public int WrittenCount;

        private readonly ILogger _log;
        private readonly Timer _drainTimer;
        private readonly ConcurrentQueue<string> _recent = new();
        private readonly int _pid = Environment.ProcessId;
        private int _accepting = 1;
        private int _disposed;


        public Host(VestigiumLoggerOptions options)
        {
            Options = options;
            Flood = new FloodTracker(options.FloodThresholdCount, options.FloodWindow);
            Disk = new DiskSpaceMonitor(options);
            Channel = System.Threading.Channels.Channel.CreateBounded<VestigiumLogEvent>(new BoundedChannelOptions(options.SubscriberChannelCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            var directory = options.ResolveLogDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"vestigium-{options.AppId}-.json");

            _log = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Async(
                    a => a.File(
                        new VestigiumSerilogFormatter(),
                        path,
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: options.FileSizeLimitBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: options.RetainedFileCountLimit,
                        retainedFileTimeLimit: options.RetainedFileTimeLimit,
                        shared: true,
                        encoding: new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
                    bufferSize: options.SerilogAsyncBuffer,
                    blockWhenFull: false)
                .CreateLogger();

            _drainTimer = new Timer(static s => ((Host)s!).Drain(), this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Emit(
            VestigiumLogLevel level,
            VestigiumStatus status,
            string category,
            string subcategory,
            string message,
            Exception? exception,
            string? appId)
        {
            if (Volatile.Read(ref _accepting) == 0)
                return;

            var now = DateTimeOffset.UtcNow;
            var app = string.IsNullOrWhiteSpace(appId) ? Options.AppId : appId;
            var originalCat = category;
            var originalSub = subcategory;
            var (cat, sub, rewritten) = Options.Taxonomy.Normalize(category, subcategory);

            if (rewritten && app != VestigiumTaxonomy.InternalAppId)
            {
                Emit(
                    VestigiumLogLevel.Warning,
                    VestigiumStatus.None,
                    "System",
                    "Configuration",
                    $"Unregistered taxonomy used: {originalCat}/{originalSub} by {app}",
                    null,
                    VestigiumTaxonomy.InternalAppId);
            }

            if (Disk.IsTripped && level <= VestigiumLogLevel.Debug)
                return;

            var identity = new FloodIdentity(app, cat, level.ToString(), message);
            var (writeFull, flushCount) = Flood.Observe(identity, now);
            if (flushCount > 0)
            {
                WriteEvent(new VestigiumLogEvent(
                    now, _pid, Environment.CurrentManagedThreadId, level, VestigiumStatus.None,
                    app, cat, sub,
                    $"[Aggregated] Previous message repeated {flushCount} additional times",
                    null));
            }

            if (!writeFull)
                return;

            WriteEvent(new VestigiumLogEvent(
                now, _pid, Environment.CurrentManagedThreadId, level, status,
                app, cat, sub, message, exception?.ToString()));
        }

        private void WriteEvent(VestigiumLogEvent evt)
        {
            var json = evt.ToJsonLine();
            _recent.Enqueue(json);
            while (_recent.Count > Options.RecentJsonLineCap && _recent.TryDequeue(out _))
            { }

            Interlocked.Increment(ref WrittenCount);

            if (evt.Level >= Options.MinimumDiskLevel)
                _log.Write(MapLevel(evt.Level), "{VestigiumJson}", json);

            Subject.Publish(evt);
            Channel.Writer.TryWrite(evt);
        }

        public IReadOnlyList<string> RecentSnapshot() => _recent.ToArray();

        public void Drain()
        {
            foreach (var (key, suppressed) in Flood.DrainExpired(DateTimeOffset.UtcNow))
            {
                WriteEvent(new VestigiumLogEvent(
                    DateTimeOffset.UtcNow, _pid, Environment.CurrentManagedThreadId,
                    Enum.TryParse<VestigiumLogLevel>(key.Level, out var lvl) ? lvl : VestigiumLogLevel.Information,
                    VestigiumStatus.None, key.AppId, key.Category, VestigiumTaxonomy.Unregistered,
                    $"[Aggregated] Previous message repeated {suppressed} additional times",
                    null));
            }
        }

        public void Flush()
        {
            Volatile.Write(ref _accepting, 0);
            Drain();
            (_log as IDisposable)?.Dispose();
            Channel.Writer.TryComplete();
            Subject.Complete();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;
            _drainTimer.Dispose();
            Disk.Dispose();
            Flush();
        }


        private static LogEventLevel MapLevel(VestigiumLogLevel level) => level switch
        {
            VestigiumLogLevel.Verbose => LogEventLevel.Verbose,
            VestigiumLogLevel.Debug => LogEventLevel.Debug,
            VestigiumLogLevel.Information => LogEventLevel.Information,
            VestigiumLogLevel.Warning => LogEventLevel.Warning,
            VestigiumLogLevel.Error => LogEventLevel.Error,
            VestigiumLogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}
