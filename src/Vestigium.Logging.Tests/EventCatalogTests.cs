namespace Vestigium.Logging.Tests;

public sealed class EventCatalogTests
{
    [Fact]
    public void SeedContainsReservedGenerals()
    {
        var catalog = new VestigiumEventCatalog(VestigiumEventCatalog.SeedGeneral());
        Assert.True(catalog.TryGetById(0, out var debug));
        Assert.Equal("General.Debug", debug.EventName);
        Assert.True(catalog.TryGetById(5, out var start));
        Assert.Equal("General.Start", start.EventName);
        Assert.True(catalog.TryGetByFullName("Vestigium.Logging.General.Fatal", out var fatal));
        Assert.Equal(4, fatal.EventId);
        Assert.Equal(15, catalog.Count);
    }

    [Fact]
    public void LoadFromDirectoryMapsExceptionTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "vestigium-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        File.WriteAllText(Path.Combine(root, "shards", "system.io.json"),
            """
            [
              {
                "EventId": 785,
                "EventName": "FileNotFoundException",
                "FullName": "System.IO.FileNotFoundException",
                "Category": "System",
                "Subcategory": "IO",
                "Severity": "Error",
                "Enabled": true,
                "Kind": "Exception"
              }
            ]
            """);

        var catalog = VestigiumEventCatalog.LoadFromDirectory(root);
        Assert.True(catalog.TryGetById(785, out var byId));
        Assert.Equal("System.IO.FileNotFoundException", byId.FullName);
        Assert.True(catalog.TryGetByException(new FileNotFoundException("missing"), out var byEx));
        Assert.Equal(785, byEx.EventId);
        Assert.True(catalog.NextReservedId >= 790);
    }

    [Fact]
    public void DisabledRowsAreNotResolved()
    {
        var catalog = new VestigiumEventCatalog(
        [
            new VestigiumEventDefinition(200, "Gone", "Temp.Gone", "System", "Core", "Error", "Exception", Enabled: false)
        ]);
        Assert.False(catalog.TryGetById(200, out _));
        Assert.False(catalog.TryGetByFullName("Temp.Gone", out _));
    }

    [Fact]
    public void EmbeddedIdAtOrAboveCustomMinThrows()
    {
        var rows = VestigiumEventCatalog.SeedGeneral().Append(
            new VestigiumEventDefinition(VestigiumEventCatalog.CustomMin, "TooHigh", "Temp.TooHigh", "System", "Core", "Error", "Exception", true));
        var ex = Assert.Throws<InvalidOperationException>(() => new VestigiumEventCatalog(rows, allowCustom: false));
        Assert.Contains("5000", ex.Message);
    }

    [Fact]
    public void CustomIdBelowCustomMinThrows()
    {
        var rows = VestigiumEventCatalog.SeedGeneral().Append(
            new VestigiumEventDefinition(2110, "HostEvent", "App.HostEvent", "Network", "ICMP", "Error", "Custom", true));
        var ex = Assert.Throws<InvalidOperationException>(() => new VestigiumEventCatalog(rows, allowCustom: true));
        Assert.Contains("5000", ex.Message);
    }

    [Fact]
    public void DuplicateEventIdDifferentFullNameThrows()
    {
        var rows = new[]
        {
            new VestigiumEventDefinition(100, "A", "Temp.A", "System", "Core", "Error", "Exception", true),
            new VestigiumEventDefinition(100, "B", "Temp.B", "System", "Core", "Error", "Exception", true)
        };
        Assert.Throws<InvalidOperationException>(() => new VestigiumEventCatalog(rows));
    }

    [Fact]
    public void LoadDefaultAlwaysHasGenerals()
    {
        var catalog = VestigiumEventCatalog.LoadDefault();
        Assert.True(catalog.TryGetById(0, out _));
        Assert.True(catalog.TryGetById(14, out _));
        Assert.True(catalog.Count >= 15);
    }
}

[Collection("VestigiumLogger")]
public sealed class EventCatalogHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public EventCatalogHostTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void InitializeLoadsCatalog()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
        });
        Assert.True(VestigiumLogger.Catalog.TryGetById(0, out var debug));
        Assert.Equal("General.Debug", debug.EventName);
        Assert.True(VestigiumLogger.Catalog.TryGetById(5, out var start));
        Assert.Equal("General.Start", start.EventName);
    }
}
