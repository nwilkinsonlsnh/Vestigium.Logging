namespace Vestigium.Logging.Tests;

[CollectionDefinition("VestigiumLogger", DisableParallelization = true)]
public sealed class VestigiumLoggerCollection;

[Collection("VestigiumLogger")]
public sealed class LoggerIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));

    public LoggerIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void InitializeRequiredBeforeWrite()
    {
        VestigiumLogger.Shutdown();
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "hello"));
    }

    [Fact]
    public void FloodBurstWritesFivePlusAggregation()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });

        for (var i = 0; i < 22; i++)
        {
            VestigiumLog.Information(
                VestigiumStatus.Timeout, "Network", "ICMP",
                "Echo request to 8.8.8.8 timed out after 1000 ms");
        }

        Assert.Equal(5, VestigiumLogger.WrittenCount);
        Assert.Equal(17, VestigiumLogger.SuppressedCount);
        var last = VestigiumLogger.RecentJsonLines.Last();
        Assert.Contains("Echo request to 8.8.8.8", last);
    }

    [Fact]
    public void FlushDoesNotStopWrites()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "before-flush");
        var before = VestigiumLogger.WrittenCount;
        VestigiumLogger.Flush();
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "after-flush");
        Assert.True(VestigiumLogger.IsInitialized);
        Assert.True(VestigiumLogger.WrittenCount > before);
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("after-flush"));
    }

    [Fact]
    public void ShutdownStopsWrites()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
        });

        VestigiumLogger.Shutdown();
        Assert.False(VestigiumLogger.IsInitialized);
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "after-shutdown"));
    }

    [Fact]
    public void FlushHonorsTimeout()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FlushTimeout = TimeSpan.Zero;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "timeout-flush");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        VestigiumLogger.Flush(TimeSpan.Zero);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "still-accepting");
        Assert.True(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void BindLifetimeExitShutsDown()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
        });

        var app = new DummyApp();
        VestigiumLogger.BindLifetime(app);
        Assert.True(VestigiumLogger.IsInitialized);
        app.RaiseExit();
        Assert.False(VestigiumLogger.IsInitialized);
    }

    [Fact]
    public void BindLifetimeSecondUnsubscribesFirst()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
        });

        var first = new DummyApp();
        var second = new DummyApp();
        VestigiumLogger.BindLifetime(first);
        VestigiumLogger.BindLifetime(second);
        first.RaiseExit();
        Assert.True(VestigiumLogger.IsInitialized);
        second.RaiseExit();
        Assert.False(VestigiumLogger.IsInitialized);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private sealed class DummyApp
    {
        public event EventHandler? Exit;
        public void RaiseExit() => Exit?.Invoke(this, EventArgs.Empty);
    }
}
