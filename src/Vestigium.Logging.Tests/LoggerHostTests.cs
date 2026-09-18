namespace Vestigium.Logging.Tests;

[CollectionDefinition("VestigiumLogger", DisableParallelization = true)]
public sealed class VestigiumLoggerCollection;

[Collection("VestigiumLogger")]
public sealed class LoggerIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public LoggerIntegrationTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void InitializeRequiredBeforeWrite()
    {
        VestigiumLogger.Shutdown();
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "hello"));
    }

    [Fact]
    public void FloodBurstWritesFivePlusAggregation()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });
        for (var i = 0; i < 22; i++)
            VestigiumLog.Information(1, VestigiumStatus.Timeout, "Network", "ICMP",
                "Echo request to 8.8.8.8 timed out after 1000 ms");
        Assert.Equal(5, VestigiumLogger.WrittenCount);
    }

    [Fact]
    public void FlushDoesNotStopWrites()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose; });
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "before-flush");
        VestigiumLogger.Flush();
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "after-flush");
        Assert.True(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void ShutdownStopsWrites()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; });
        VestigiumLogger.Shutdown();
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "after-shutdown"));
    }

    [Fact]
    public void FlushHonorsTimeout()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; cfg.FlushTimeout = TimeSpan.Zero; cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose; });
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "timeout-flush");
        VestigiumLogger.Flush(TimeSpan.Zero);
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "still-accepting");
        Assert.True(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void BindLifetimeExitShutsDown()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; });
        var app = new DummyApp();
        VestigiumLogger.BindLifetime(app);
        app.RaiseExit();
        Assert.False(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void BindLifetimeSecondUnsubscribesFirst()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; });
        var first = new DummyApp(); var second = new DummyApp();
        VestigiumLogger.BindLifetime(first);
        VestigiumLogger.BindLifetime(second);
        first.RaiseExit();
        Assert.True(VestigiumLogger.IsInitialized);
        second.RaiseExit();
        Assert.False(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void ThrowStillDefault()
    {
        VestigiumLogger.Shutdown();
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "hello"));
    }

    [Fact]
    public void NoOpSwallowsWrite()
    {
        VestigiumLogger.Shutdown();
        var previous = VestigiumLogger.UninitializedBehavior;
        try
        {
            VestigiumLogger.UninitializedBehavior = VestigiumUninitializedBehavior.NoOp;
            VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "library-safe");
        }
        finally { VestigiumLogger.UninitializedBehavior = previous; }
    }

    [Fact]
    public void CorrelationIdIsWrittenAndIgnoredByFlood()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5; cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        for (var i = 0; i < 8; i++)
            VestigiumLog.Information(1, VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out", correlationId: $"id-{i}");
        Assert.Equal(5, VestigiumLogger.WrittenCount);
    }

    [Fact]
    public void DrainAggregationKeepsSubcategory()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5; cfg.FloodWindow = TimeSpan.FromMilliseconds(50);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        for (var i = 0; i < 8; i++)
            VestigiumLog.Information(1, VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out");
        Thread.Sleep(80);
        VestigiumLogger.Flush();
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("[Aggregated]") && l.Contains("\"SUBCATEGORY\":\"ICMP\""));
    }

    [Fact]
    public void DiskStatusEmptyWhenNotInitialized()
    {
        VestigiumLogger.Shutdown();
        Assert.False(VestigiumLogger.DiskStatus.IsTripped);
    }

    [Fact]
    public void DiskStatusReflectsOverride()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; cfg.DiskBytesFloorEnabled = false; });
        VestigiumLogger.OverrideDiskPressure(true);
        Assert.True(VestigiumLogger.IsDiskTripped);
        VestigiumLogger.OverrideDiskPressure(null);
        Assert.False(VestigiumLogger.DiskStatus.IsOverridden);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class DummyApp
    {
        public event EventHandler? Exit;
        public void RaiseExit() => Exit?.Invoke(this, EventArgs.Empty);
    }
}
