namespace Vestigium.Logging.Tests;

public sealed class CustomCatalogTests
{
    [Fact]
    public void OpenEmptyFolderStartsAt5000()
    {
        var root = NewRoot();
        var catalog = VestigiumCustomCatalog.Open(root);
        Assert.Equal(0, catalog.Count);
        Assert.Equal(5000, catalog.NextCustomId);
        Assert.True(Directory.Exists(catalog.ShardsDirectory));
    }

    [Fact]
    public void OpenLoadsCustomRows()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        File.WriteAllText(Path.Combine(root, "shards", "custom.json"),
            """
            [{ "EventId": 5000, "EventName": "ProbeTimeout", "FullName": "PingIQ.ProbeTimeoutException", "Category": "Network", "Subcategory": "ICMP", "Kind": "Custom" }]
            """);
        var catalog = VestigiumCustomCatalog.Open(root);
        Assert.Equal(1, catalog.Count);
        Assert.Equal(5005, catalog.NextCustomId);
        Assert.True(catalog.TryGet(5000, out var row));
        Assert.Equal("PingIQ.ProbeTimeoutException", row.FullName);
    }

    [Fact]
    public void OpenRejectsReservedRange()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        File.WriteAllText(Path.Combine(root, "shards", "bad.json"),
            """[{ "EventId": 2110, "EventName": "Bad", "FullName": "App.Bad" }]""");
        var ex = Assert.Throws<InvalidOperationException>(() => VestigiumCustomCatalog.Open(root));
        Assert.Contains("5000", ex.Message);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
