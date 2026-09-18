namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class CustomCatalogHostTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));

    public CustomCatalogHostTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_catalog);
    }

    [Fact]
    public void ThrownResolvesSavedCustomFullName()
    {
        var custom = VestigiumCustomCatalog.Open(_catalog);
        custom.Add("ProbeTimeout", typeof(ProbeTimeoutException).FullName!, "Network", "ICMP", severity: "Warning");
        custom.Save();

        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.EventCatalogPath = _catalog;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Thrown(new ProbeTimeoutException(), VestigiumStatus.Timeout);
        var json = VestigiumLogger.RecentJsonLines.Last();
        Assert.Contains("\"EVENTID\":5000", json);
        Assert.Contains("\"EVENTNAME\":\"ProbeTimeout\"", json);
        Assert.Contains("\"STATUS\":\"Timeout\"", json);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_catalog, true); } catch { }
    }

    private sealed class ProbeTimeoutException() : Exception("probe timed out");
}
