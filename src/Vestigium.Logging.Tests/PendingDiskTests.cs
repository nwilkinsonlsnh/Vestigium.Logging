namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class PendingDiskTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public PendingDiskTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void PeekDoesNotDequeueAndFlushClears()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "pending-a");
        var before = VestigiumLogger.PendingDiskCount;
        _ = VestigiumLogger.PeekPendingDisk(10);
        Assert.Equal(before, VestigiumLogger.PendingDiskCount);
        VestigiumLogger.Flush();
        Assert.Equal(0, VestigiumLogger.PendingDiskCount);
    }

    [Fact]
    public void PeekRejectsZero()
    {
        VestigiumLogger.Initialize(cfg => { cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir; });
        Assert.Throws<ArgumentOutOfRangeException>(() => VestigiumLogger.PeekPendingDisk(0));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
