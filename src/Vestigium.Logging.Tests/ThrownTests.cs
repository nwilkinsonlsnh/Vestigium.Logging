namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class ThrownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public ThrownTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void ThrownUnknownTypeUsesGeneralError()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Thrown(new NotInCatalogException(), VestigiumStatus.Failed);
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("\"EVENTID\":3"));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class NotInCatalogException() : Exception("x");
}
