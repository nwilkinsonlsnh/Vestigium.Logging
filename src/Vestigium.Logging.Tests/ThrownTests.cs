namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class ThrownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public ThrownTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ThrownLooksUpExceptionType()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        if (!VestigiumLogger.Catalog.TryGetByFullName("System.IO.FileNotFoundException", out var mapped))
        {
            Assert.Throws<InvalidOperationException>(() =>
                VestigiumLog.Thrown(new FileNotFoundException("missing"), VestigiumStatus.Failed));
            return;
        }
        VestigiumLog.Thrown(new FileNotFoundException("missing"), VestigiumStatus.Failed);
        var json = VestigiumLogger.RecentJsonLines.Last();
        Assert.Contains($"\"EVENTID\":{mapped.EventId}", json);
        Assert.Contains("FileNotFoundException", json);
        Assert.Contains("\"STATUS\":\"Failed\"", json);
    }

    [Fact]
    public void ThrownExplicitEventIdOverridesLookup()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Thrown(new FileNotFoundException("missing"), VestigiumStatus.Failed, 3);
        Assert.Contains("\"EVENTID\":3", VestigiumLogger.RecentJsonLines.Last());
    }

    [Fact]
    public void ThrownUnknownTypeThrows()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
        });
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Thrown(new NotInCatalogException(), VestigiumStatus.Failed));
    }

    private sealed class NotInCatalogException() : Exception("x");
}
