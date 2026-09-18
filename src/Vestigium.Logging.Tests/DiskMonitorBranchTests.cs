namespace Vestigium.Logging.Tests;

public sealed class DiskMonitorBranchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-disk-" + Guid.NewGuid().ToString("N"));

    public DiskMonitorBranchTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void OverrideWithNoListenerStillFlips()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = _dir,
            DiskPollInterval = TimeSpan.FromHours(1)
        };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.TripwireChanged = null;
        monitor.Override(true);
        Assert.True(monitor.IsTripped);
        monitor.Override(false);
        Assert.False(monitor.IsTripped);
        _ = monitor.Snapshot();
    }

    [Fact]
    public void PollReturnsWhenDriveNotReady()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = _dir,
            DiskPollInterval = TimeSpan.FromHours(1)
        };
        DiskSpaceMonitor.ReadyOverride = _ => false;
        try
        {
            using var monitor = new DiskSpaceMonitor(options);
            monitor.Poll();
            Assert.Null(monitor.LastDrive);
        }
        finally
        {
            DiskSpaceMonitor.ReadyOverride = null;
        }
    }

    [Fact]
    public void PollSwallowsInvalidDirectory()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = Path.Combine(_dir, "bad" + Path.GetInvalidFileNameChars()[0] + "name"),
            DiskPollInterval = TimeSpan.FromHours(1)
        };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Poll();
    }

    [Fact]
    public void PollTripWithoutListener()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = _dir,
            DiskPollInterval = TimeSpan.FromHours(1),
            DiskBytesFloorEnabled = true,
            DiskFreeBytesFloor = long.MaxValue,
            DiskFreePercentThreshold = 100
        };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.TripwireChanged = null;
        monitor.Poll();
        _ = monitor.Snapshot();
    }

    public void Dispose()
    {
        DiskSpaceMonitor.ReadyOverride = null;
        try { Directory.Delete(_dir, true); } catch { }
    }
}
