namespace Vestigium.Logging.Tests;

public sealed class DiskSpaceMonitorTests
{
    private static DiskSpaceMonitor Create(int percentThreshold = 10, long bytesFloor = 0)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-disk", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = dir,
            DiskFreePercentThreshold = percentThreshold,
            DiskFreeBytesFloor = bytesFloor,
            DiskPollInterval = TimeSpan.FromHours(1)
        };
        var monitor = new DiskSpaceMonitor(options);
        monitor.Override(null);
        return monitor;
    }

    [Fact]
    public void EmptyRootReturnsWithoutTripping()
    {
        using var monitor = Create();
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery(null, true, 100, 50, "X:");
        monitor.Poll();
        Assert.False(monitor.IsTripped);
        Assert.Null(monitor.LastDrive);
    }

    [Fact]
    public void EmptyStringRootReturnsWithoutTripping()
    {
        using var monitor = Create();
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("", true, 100, 50, "X:");
        monitor.Poll();
        Assert.False(monitor.IsTripped);
    }

    [Fact]
    public void NotReadyDriveReturnsWithoutUpdating()
    {
        using var monitor = Create();
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("D:\\", false, 100, 50, "D:\\");
        monitor.Poll();
        Assert.False(monitor.IsTripped);
        Assert.Null(monitor.LastDrive);
        Assert.Null(monitor.LastAvailableBytes);
    }

    [Fact]
    public void ZeroTotalSizeUsesZeroPercentFloorAndTripsOnBytes()
    {
        using var monitor = Create(percentThreshold: 50, bytesFloor: 20);
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("C:\\", true, 0, 10, "C:\\");
        monitor.Poll();
        Assert.True(monitor.IsTripped);
        Assert.Equal("C:\\", monitor.LastDrive);
        Assert.Equal(10, monitor.LastAvailableBytes);
    }

    [Fact]
    public void ZeroTotalSizeAndZeroFloorsDoesNotTrip()
    {
        using var monitor = Create(percentThreshold: 0, bytesFloor: 0);
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("C:\\", true, 0, 10, "C:\\");
        monitor.Poll();
        Assert.False(monitor.IsTripped);
    }

    [Fact]
    public void TripsWhenBelowPercentFloorOnly()
    {
        using var monitor = Create(percentThreshold: 50, bytesFloor: 0);
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("C:\\", true, 1000, 100, "C:\\");
        monitor.Poll();
        Assert.True(monitor.IsTripped);
    }

    [Fact]
    public void TripsWhenBelowByteFloorOnly()
    {
        using var monitor = Create(percentThreshold: 0, bytesFloor: 50);
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("C:\\", true, 1000, 10, "C:\\");
        monitor.Poll();
        Assert.True(monitor.IsTripped);
    }

    [Fact]
    public void DoesNotTripWhenBothFloorsAreMet()
    {
        using var monitor = Create(percentThreshold: 10, bytesFloor: 5);
        monitor.QueryDrive = _ => new DiskSpaceMonitor.DriveQuery("C:\\", true, 1000, 500, "C:\\");
        monitor.Poll();
        Assert.False(monitor.IsTripped);
        Assert.Equal(500, monitor.LastAvailableBytes);
    }

    [Fact]
    public void ProbeThrowIsSwallowed()
    {
        using var monitor = Create();
        monitor.QueryDrive = _ => throw new IOException("probe failed");
        monitor.Poll();
    }
}
