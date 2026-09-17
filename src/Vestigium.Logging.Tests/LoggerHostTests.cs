namespace Vestigium.Logging.Tests;

public sealed class LoggerHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));

    public LoggerHostTests()
    {
        Directory.CreateDirectory(_dir);
    }

    private void Start(Action<VestigiumLoggerOptions>? extra = null)
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Information;
            cfg.RecentJsonLineCap = 3;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
            extra?.Invoke(cfg);
        });
    }

    [Fact]
    public void InitializeRejectsNullAndBlankAppId()
    {
        Assert.Throws<ArgumentNullException>(() => VestigiumLogger.Initialize(null!));
        Assert.Throws<ArgumentException>(() => VestigiumLogger.Initialize(cfg => cfg.AppId = " "));
    }

    [Fact]
    public void UninitializedSurfaceIsSafe()
    {
        VestigiumLogger.Shutdown();
        Assert.False(VestigiumLogger.IsInitialized);
        Assert.False(VestigiumLogger.IsDiskTripped);
        Assert.Empty(VestigiumLogger.RecentJsonLines);
        Assert.Equal(0, VestigiumLogger.WrittenCount);
        Assert.Equal(0, VestigiumLogger.SuppressedCount);
        VestigiumLogger.Flush();
        VestigiumLogger.OverrideDiskPressure(true);
        Assert.Throws<InvalidOperationException>(() => _ = VestigiumLogger.Options);
        Assert.Throws<InvalidOperationException>(() => _ = VestigiumLogger.Events);
        Assert.Throws<InvalidOperationException>(() => _ = VestigiumLogger.EventReader);
    }

    [Fact]
    public void ReinitializeAndBindLifetime()
    {
        Start();
        Assert.True(VestigiumLogger.IsInitialized);
        Assert.Equal("PingIQ", VestigiumLogger.Options.AppId);
        VestigiumLogger.BindLifetime(null);
        VestigiumLogger.BindLifetime(new object());
        Start();
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "second-host");
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("second-host"));
    }

    [Fact]
    public void DiskTripDropsDebugAndKeepsError()
    {
        Start();
        VestigiumLogger.OverrideDiskPressure(true);
        Assert.True(VestigiumLogger.IsDiskTripped);
        VestigiumLog.Debug(VestigiumStatus.None, "Network", "ICMP", "dropped-debug");
        VestigiumLog.Error(VestigiumStatus.Failed, "Network", "ICMP", "kept-error");
        var lines = VestigiumLogger.RecentJsonLines;
        Assert.DoesNotContain(lines, l => l.Contains("dropped-debug"));
        Assert.Contains(lines, l => l.Contains("kept-error"));
        VestigiumLogger.OverrideDiskPressure(null);
    }

    [Fact]
    public void UnregisteredTaxonomyEmitsInternalWarning()
    {
        Start();
        VestigiumLog.Information(VestigiumStatus.Success, "Widgets", "Thing", "custom");
        var lines = VestigiumLogger.RecentJsonLines;
        Assert.Contains(lines, l => l.Contains("Unregistered taxonomy"));
        Assert.Contains(lines, l => l.Contains("Vestigium.Logging"));
        Assert.Contains(lines, l => l.Contains("Uncategorized"));
    }

    [Fact]
    public void RecentCapAndChannelAndObservable()
    {
        Start();
        var seen = new List<VestigiumLogEvent>();
        using var sub = VestigiumLogger.Events.Subscribe(new DelegateObserver(
            onNext: seen.Add,
            onError: _ => { },
            onCompleted: () => { }));

        for (var i = 0; i < 8; i++)
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "TCP", $"cap-{i}");

        Assert.Equal(3, VestigiumLogger.RecentJsonLines.Count);
        Assert.True(seen.Count >= 8);
        Assert.True(VestigiumLogger.EventReader.TryRead(out _));
    }

    [Fact]
    public void BelowMinimumDiskLevelStillAppearsInRecent()
    {
        Start(cfg => cfg.MinimumDiskLevel = VestigiumLogLevel.Error);
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "HTTP", "memory-only");
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("memory-only"));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private sealed class DelegateObserver(
        Action<VestigiumLogEvent> onNext,
        Action<Exception> onError,
        Action onCompleted) : IObserver<VestigiumLogEvent>
    {
        public void OnCompleted() => onCompleted();
        public void OnError(Exception error) => onError(error);
        public void OnNext(VestigiumLogEvent value) => onNext(value);
    }
}
