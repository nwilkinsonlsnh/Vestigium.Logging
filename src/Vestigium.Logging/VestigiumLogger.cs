using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Vestigium.Logging;

public static class VestigiumLogger
{
    private static readonly object Gate = new();
    private static readonly LifetimeBinder Lifetime = new();
    private static Host? _host;

    public static bool IsInitialized => _host is not null;
    public static VestigiumUninitializedBehavior UninitializedBehavior { get; set; } = VestigiumUninitializedBehavior.Throw;
    public static VestigiumLoggerOptions Options => Require().Options;
    public static IObservable<VestigiumLogEvent> Events => Require().Subject;
    public static ChannelReader<VestigiumLogEvent> EventReader => Require().Channel.Reader;
    public static bool IsDiskTripped => _host?.Disk.IsTripped ?? false;
    public static VestigiumDiskStatus DiskStatus => _host?.Disk.Snapshot() ?? VestigiumDiskStatus.Empty;
    public static IReadOnlyList<string> RecentJsonLines => _host?.RecentSnapshot() ?? [];
    public static int WrittenCount => _host?.WrittenCount ?? 0;
    public static int SuppressedCount => _host?.Flood.PendingSuppressed ?? 0;
    public static VestigiumEventCatalog Catalog => Require().Catalog;

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
        lock (Gate) { _host?.Dispose(); _host = null; }
    }

    public static void Flush() => _host?.Flush();
    public static void Flush(TimeSpan timeout) => _host?.Flush(timeout);

    public static void BindLifetime(object? wpfApplication)
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress -= OnCancel;
        Console.CancelKeyPress += OnCancel;
        Lifetime.BindExit(wpfApplication, Shutdown);
    }

    public static void OverrideDiskPressure(bool? tripped) => _host?.Disk.Override(tripped);
    internal static Host Require() =>
        _host ?? throw new InvalidOperationException("VestigiumLogger.Initialize must run during application startup.");

    internal static void Emit(
        VestigiumLogLevel level, VestigiumStatus status, string category, string subcategory, string message,
        Exception? exception, string? appId = null, string? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null, int eventId = 0)
    {
        var host = _host;
        if (host is null)
        {
            if (UninitializedBehavior == VestigiumUninitializedBehavior.NoOp) return;
            throw new InvalidOperationException("VestigiumLogger.Initialize must run during application startup.");
        }
        host.Emit(level, status, category, subcategory, message, exception, appId, correlationId, properties, eventId);
    }

    private static void OnProcessExit(object? sender, EventArgs e) => Shutdown();
    private static void OnCancel(object? sender, ConsoleCancelEventArgs e) => Shutdown();

    internal sealed class Host : IDisposable
    {
        public VestigiumLoggerOptions Options { get; }
        public VestigiumEventCatalog Catalog { get; }
        public FloodTracker Flood { get; }
        public DiskSpaceMonitor Disk { get; }
        public LogEventSubject Subject { get; } = new();
        public Channel<VestigiumLogEvent> Channel { get; }
        public int WrittenCount;
        private readonly VestigiumJsonlWriter _disk;
        private readonly Timer _drainTimer;
        private readonly ConcurrentQueue<string> _recent = new();
        private readonly int _pid = Environment.ProcessId;
        private int _accepting = 1;
        private int _disposed;
        private int _closed;

        public Host(VestigiumLoggerOptions options)
        {
            Options = options;
            Catalog = VestigiumEventCatalog.LoadDefault(typeof(VestigiumLogger).Assembly);
            if (!string.IsNullOrWhiteSpace(options.EventCatalogPath))
                Catalog.MergeFromDirectory(options.EventCatalogPath);
            foreach (var pending in options.CustomEvents)
            {
                Catalog.RegisterCustom(pending.EventName, pending.FullName, pending.Category, pending.Subcategory,
                    pending.EventId, pending.Severity, pending.Description);
            }
            Catalog.Freeze();
            Flood = new FloodTracker(options.FloodThresholdCount, options.FloodWindow, options.FloodIdentityCap);
            Disk = new DiskSpaceMonitor(options);
            Channel = System.Threading.Channels.Channel.CreateBounded<VestigiumLogEvent>(new BoundedChannelOptions(options.SubscriberChannelCapacity)
            {
                SingleReader = false, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest
            });
            _disk = new VestigiumJsonlWriter(options);
            _drainTimer = new Timer(static s => ((Host)s!).Drain(), this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Emit(VestigiumLogLevel level, VestigiumStatus status, string category, string subcategory, string message,
            Exception? exception, string? appId, string? correlationId = null,
            IReadOnlyDictionary<string, string?>? properties = null, int eventId = 0)
        {
            if (Volatile.Read(ref _accepting) == 0) return;
            var now = DateTimeOffset.UtcNow;
            var app = string.IsNullOrWhiteSpace(appId) ? Options.AppId : appId;
            var originalCat = category;
            var originalSub = subcategory;
            var (cat, sub, rewritten) = Options.Taxonomy.Normalize(category, subcategory);
            if (rewritten && app != VestigiumTaxonomy.InternalAppId)
            {
                Emit(VestigiumLogLevel.Warning, VestigiumStatus.None, "System", "Configuration",
                    $"Unregistered taxonomy used: {originalCat}/{originalSub} by {app}",
                    null, VestigiumTaxonomy.InternalAppId, eventId: 11);
            }
            if (Disk.IsTripped && level <= VestigiumLogLevel.Debug) return;
            var identity = new FloodIdentity(app, cat, level.ToString(), message);
            var (writeFull, flushCount) = Flood.Observe(identity, now, sub);
            WriteAggregations(Flood.TakeEvictedSummaries());
            if (flushCount > 0)
            {
                var agg = Catalog.Resolve(1, null, VestigiumLogLevel.Information);
                WriteEvent(new VestigiumLogEvent(now, _pid, Environment.CurrentManagedThreadId, level, VestigiumStatus.None,
                    app, cat, sub, $"[Aggregated] Previous message repeated {flushCount} additional times",
                    null, correlationId, null, agg.EventId, agg.EventName));
            }
            if (!writeFull) return;
            var resolved = Catalog.Resolve(eventId, exception, level);
            WriteEvent(new VestigiumLogEvent(now, _pid, Environment.CurrentManagedThreadId, level, status,
                app, cat, sub, message,
                VestigiumExceptionFormatter.Format(exception, Options.ExceptionDetail, Options.ExceptionMaxChars),
                correlationId, VestigiumPropertyBag.Sanitize(properties), resolved.EventId, resolved.EventName));
        }

        private void WriteEvent(VestigiumLogEvent evt)
        {
            var json = evt.ToJsonLine();
            _recent.Enqueue(json);
            while (_recent.Count > Options.RecentJsonLineCap && _recent.TryDequeue(out _)) { }
            Interlocked.Increment(ref WrittenCount);
            if (evt.Level >= Options.MinimumDiskLevel) _disk.Enqueue(json);
            Subject.Publish(evt);
            Channel.Writer.TryWrite(evt);
        }

        public IReadOnlyList<string> RecentSnapshot() => _recent.ToArray();
        public void Drain() => WriteAggregations(Flood.DrainExpired(DateTimeOffset.UtcNow));

        private void WriteAggregations(List<(FloodIdentity Key, int Suppressed, string Subcategory)> items)
        {
            foreach (var (key, suppressed, subcategory) in items)
            {
                var sub = string.IsNullOrWhiteSpace(subcategory) ? VestigiumTaxonomy.Unregistered : subcategory;
                var lvl = Enum.TryParse<VestigiumLogLevel>(key.Level, out var parsed) ? parsed : VestigiumLogLevel.Information;
                var resolved = Catalog.Resolve(1, null, VestigiumLogLevel.Information);
                WriteEvent(new VestigiumLogEvent(DateTimeOffset.UtcNow, _pid, Environment.CurrentManagedThreadId,
                    lvl, VestigiumStatus.None, key.AppId, key.Category, sub,
                    $"[Aggregated] Previous message repeated {suppressed} additional times",
                    null, null, null, resolved.EventId, resolved.EventName));
            }
        }

        public void Flush() => Persist(false, Options.FlushTimeout);
        public void Flush(TimeSpan timeout) => Persist(false, timeout);
        public void Close() => Persist(true, Options.FlushTimeout);

        private void Persist(bool stopAccepting, TimeSpan timeout)
        {
            var wait = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
            if (stopAccepting) Volatile.Write(ref _accepting, 0);
            Drain();
            if (!stopAccepting) { _disk.Flush(wait); return; }
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            _disk.Complete();
            Channel.Writer.TryComplete();
            Subject.Complete();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _drainTimer.Dispose();
            Disk.Dispose();
            Close();
        }
    }
}
