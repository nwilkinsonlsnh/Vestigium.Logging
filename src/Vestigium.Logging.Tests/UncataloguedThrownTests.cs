namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class UncataloguedThrownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public UncataloguedThrownTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void UnknownTypeWritesEventId3AndDebugHint()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Thrown(new NotInCatalogException(), VestigiumStatus.Failed);
        var lines = VestigiumLogger.RecentJsonLines;
        Assert.Contains(lines, l => l.Contains("\"EVENTID\":3"));
        Assert.Contains(lines, l => l.Contains("\"EVENTID\":0") && l.Contains("Uncatalogued exception"));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class NotInCatalogException() : Exception("x");
}
