namespace Vestigium.Logging.Tests;

public sealed class CatalogResolveCoverageTests
{
    [Fact]
    public void ResolveCoversLevelsAndFailures()
    {
        var catalog = VestigiumEventCatalog.LoadDefault();
        Assert.Equal(0, catalog.Resolve(null, null, VestigiumLogLevel.Verbose).EventId);
        Assert.Equal(0, catalog.Resolve(null, null, VestigiumLogLevel.Debug).EventId);
        Assert.Equal(1, catalog.Resolve(null, null, VestigiumLogLevel.Information).EventId);
        Assert.Equal(2, catalog.Resolve(null, null, VestigiumLogLevel.Warning).EventId);
        Assert.Equal(3, catalog.Resolve(null, null, VestigiumLogLevel.Error).EventId);
        Assert.Equal(4, catalog.Resolve(null, null, VestigiumLogLevel.Fatal).EventId);
        Assert.Equal(1, catalog.Resolve(null, null, (VestigiumLogLevel)99).EventId);
        Assert.True(catalog.Resolve(null, new FileNotFoundException("x"), VestigiumLogLevel.Error).EventId >= 100);
        Assert.Throws<InvalidOperationException>(() => catalog.Resolve(999999, null, VestigiumLogLevel.Error));
        Assert.True(catalog.TryResolve(1, null, VestigiumLogLevel.Information, out _));
        Assert.False(catalog.TryResolve(999999, null, VestigiumLogLevel.Error, out _));
    }

    [Fact]
    public void RegisterCustomThenFreeze()
    {
        var catalog = VestigiumEventCatalog.LoadDefault();
        var row = catalog.RegisterCustom("HostEx", "App.HostEx", "Network", "ICMP");
        Assert.True(row.EventId >= 10000);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterCustom("Bad", "App.Bad", "Network", "ICMP", eventId: 100));
        catalog.Freeze();
        Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterCustom("Late", "App.Late", "Network", "ICMP"));
    }
}

public sealed class HousekeepingCoverageTests
{
    [Fact]
    public void ReaderJanitorArchiveGuardBranches()
    {
        Assert.Throws<InvalidOperationException>(() => VestigiumLogReader.Head(1));
        Assert.Throws<InvalidOperationException>(() => VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(1)));
        Assert.Throws<InvalidOperationException>(() => VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), "x"));
        Assert.Throws<ArgumentException>(() => VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), " "));
        var missing = Path.Combine(Path.GetTempPath(), "vestigium-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(VestigiumLogReader.Head(5, missing, "PingIQ"));
        Assert.Equal(0, VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(1), missing, "PingIQ").Deleted);
        Assert.Equal(0, VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), missing + "-a", missing, "PingIQ").Archived);
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-hk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"vestigium-PingIQ-{DateTime.UtcNow:yyyyMMdd}.json"), "{}\n");
        File.WriteAllText(Path.Combine(dir, "vestigium-PingIQ-notadate.json"), "{}\n");
        Assert.NotEmpty(VestigiumLogReader.Tail(10, dir, "PingIQ"));
        Assert.Equal(0, VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(30), dir, "PingIQ").Deleted);
        _ = VestigiumLogJanitor.FileDate(Path.Combine(dir, "vestigium-PingIQ-notadate.json"));
    }
}

[Collection("VestigiumLogger")]
public sealed class HostCoverageBoostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public HostCoverageBoostTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void PropertyBagAdvisorRegisterEvent()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.RegisterEvent("HostEx", "App.HostEx", "Network", "ICMP");
        });
        var bag = new Dictionary<string, string?> { ["ok"] = "v", ["bad-key"] = "x", ["n"] = null };
        for (var i = 0; i < 20; i++) bag["k" + i] = new string('x', 300);
        VestigiumLog.Information(1, VestigiumStatus.None, "Network", "ICMP", "props", properties: bag);
        VestigiumLog.Information(1, VestigiumStatus.None, "Network", "ICMP", "empty", properties: new Dictionary<string, string?>());
        UncataloguedExceptionAdvisor.Reset();
        UncataloguedExceptionAdvisor.Note("");
        for (var i = 0; i < 20; i++) UncataloguedExceptionAdvisor.Note("Type" + i);
        UncataloguedExceptionAdvisor.Note("Type0");
        Assert.Equal(20, UncataloguedExceptionAdvisor.DistinctCount);
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("20 uncatalogued"));
        _ = VestigiumLogReader.Head(2);
        _ = VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(1), _dir, "PingIQ");
        _ = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), Path.Combine(_dir, "arc"), _dir, "PingIQ");
    }

    [Fact]
    public void CustomCatalogSetAndGetMiss()
    {
        var catalog = VestigiumCustomCatalog.Open(Path.Combine(_dir, "cat"));
        catalog.Add("A", "App.A", "Network", "ICMP");
        catalog.Add("B", "App.B", "Network", "HTTP");
        Assert.Throws<InvalidOperationException>(() => catalog.Get(10010));
        Assert.Throws<InvalidOperationException>(() => catalog.Set(10000, fullName: "App.B"));
        var updated = catalog.Set(10000, eventName: "A2", category: "System", subcategory: "IO", severity: "Warning", description: "d", enabled: false);
        Assert.Equal("A2", updated.EventName);
        catalog.Set(10000, fullName: "App.A2");
        Assert.True(catalog.TryGetByFullName("App.A2", out _));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
