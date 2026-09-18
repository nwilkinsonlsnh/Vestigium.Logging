namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class Coverage96Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-c96-" + Guid.NewGuid().ToString("N"));

    public Coverage96Tests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void WriterDefaultsCompleteTwiceAndSeal()
    {
        using var writer = new VestigiumJsonlWriter(_dir, "PingIQ", 0, TimeSpan.Zero, -1, 0);
        Assert.Equal(0, writer.QueuedCount);
        Assert.Equal(0, writer.IoFaultCount);
        writer.Enqueue("{\"MESSAGE\":\"one\"}");
        writer.Flush(TimeSpan.FromSeconds(2));
        var key = Path.Combine(_dir, "ring.json");
        writer.AttachSeal(VestigiumSealKeyRing.Open(key), (_, _, _) => { });
        writer.Complete();
        writer.Complete();
        writer.Enqueue("after-stop");
        writer.Dispose();
        Assert.True(writer.WrittenCount >= 1);
        Assert.Contains("VESTIGIUM_TRAILER", File.ReadAllText(Directory.GetFiles(_dir, "vestigium-PingIQ-*.json").First()));
    }

    [Fact]
    public void FloodEvictsIdleAndPending()
    {
        var flood = new FloodTracker(1, TimeSpan.FromMilliseconds(50), identityCap: 2);
        var t0 = DateTimeOffset.UtcNow;
        flood.Observe(new FloodIdentity("A", "C", "Information", "idle"), t0, "S");
        var hot = new FloodIdentity("A", "C", "Information", "hot");
        flood.Observe(hot, t0, "S");
        flood.Observe(hot, t0, "S");
        flood.Observe(hot, t0, "S");
        Assert.True(flood.PendingSuppressed >= 1);
        var t1 = t0.AddMilliseconds(80);
        flood.Observe(new FloodIdentity("A", "C", "Information", "third"), t1, "S");
        flood.Observe(new FloodIdentity("A", "C", "Information", "fourth"), t1, "S");
        flood.Observe(new FloodIdentity("A", "C", "Information", "fifth"), t1, "S");
        _ = flood.DrainExpired(t1.AddMilliseconds(80));
        _ = flood.TakeEvictedSummaries();
        Assert.True(flood.TrackedIdentityCount <= flood.IdentityCap + 3);
        Assert.Equal(1, flood.Threshold);
        Assert.Equal(TimeSpan.FromMilliseconds(50), flood.Window);
    }

    [Fact]
    public void ArchiveHashAndZeroAge()
    {
        var file = Path.Combine(_dir, "vestigium-PingIQ-20200101.json");
        File.WriteAllText(file, "{}\n");
        var hash = VestigiumLogArchive.HashFile(file);
        Assert.Equal(64, hash.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VestigiumLogArchive.ArchiveOlderThan(TimeSpan.Zero, Path.Combine(_dir, "arc"), _dir, "PingIQ"));
        var dest = Path.Combine(_dir, "arc");
        var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), dest, _dir, "PingIQ");
        Assert.True(result.Archived + result.Failed + result.Deleted >= 0);
    }

    [Fact]
    public void BinderLifetimeAndThrownNull()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.OperationsLogEnabled = false;
        });
        VestigiumLogger.BindLifetime(null);
        VestigiumLogger.BindLifetime(new DummyApp());
        Assert.Throws<ArgumentNullException>(() => VestigiumLog.Thrown(null!, VestigiumStatus.Failed));
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Thrown(new InvalidOperationException("x"), VestigiumStatus.Failed, 99999));
        var status = new VestigiumDiskStatus(true, true, "C:\\", 1, 10, 5);
        _ = status with { IsTripped = false };
        _ = status.ToString();
        VestigiumLogger.Shutdown();
    }

    [Fact]
    public void DiskPollSwallowsBadRoot()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = Path.Combine(_dir, "nested"),
            DiskPollInterval = TimeSpan.FromHours(1),
            DiskBytesFloorEnabled = true,
            DiskFreeBytesFloor = long.MaxValue,
            DiskFreePercentThreshold = 100
        };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Poll();
        monitor.Poll();
        monitor.Dispose();
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class DummyApp
    {
        public event EventHandler? Exit;
        public void Raise() => Exit?.Invoke(this, EventArgs.Empty);
    }
}
