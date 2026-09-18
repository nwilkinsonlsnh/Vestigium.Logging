namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class CustomCatalogLoadTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));

    public CustomCatalogLoadTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_catalog);
    }

    [Fact]
    public void LoadUnloadAndReloadAfterOfflineEdit()
    {
        var offline = VestigiumCustomCatalog.Open(_catalog);
        offline.Add("ProbeTimeout", typeof(ProbeTimeoutException).FullName!, "Network", "ICMP");
        offline.Save();

        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLogger.LoadCustomCatalog(_catalog);
        Assert.True(VestigiumLogger.Catalog.TryGetById(5000, out _));
        VestigiumLog.Thrown(new ProbeTimeoutException(), VestigiumStatus.Timeout);
        Assert.Contains("\"EVENTID\":5000", VestigiumLogger.RecentJsonLines.Last());

        VestigiumLogger.UnloadCustomCatalog();
        Assert.Null(VestigiumLogger.CustomCatalogPath);
        Assert.False(VestigiumLogger.Catalog.TryGetById(5000, out _));

        offline.Add("Http502", typeof(BadGatewayException).FullName!, "Network", "HTTP");
        offline.Save();
        VestigiumLogger.LoadCustomCatalog(_catalog);
        Assert.True(VestigiumLogger.Catalog.TryGetById(5005, out _));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_catalog, true); } catch { }
    }

    private sealed class ProbeTimeoutException() : Exception("timeout");
    private sealed class BadGatewayException() : Exception("502");
}
