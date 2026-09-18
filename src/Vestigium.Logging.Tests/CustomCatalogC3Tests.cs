namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class CustomCatalogC3Tests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _catalog = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));

    public CustomCatalogC3Tests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(Path.Combine(_catalog, "shards"));
    }

    [Fact]
    public void EventCatalogPathRejectsReservedIdsAtInitialize()
    {
        File.WriteAllText(Path.Combine(_catalog, "shards", "bad.json"),
            """[{ "EventId": 2110, "EventName": "Bad", "FullName": "App.Bad" }]""");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            VestigiumLogger.Initialize(cfg =>
            {
                cfg.AppId = "PingIQ";
                cfg.LogDirectory = _logs;
                cfg.EventCatalogPath = _catalog;
            }));
        Assert.Contains("10000", ex.Message);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_catalog, true); } catch { }
    }
}
