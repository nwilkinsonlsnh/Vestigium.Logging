namespace Vestigium.Logging.Tests;

public sealed class CustomCatalogTests
{
    [Fact]
    public void OpenEmptyFolderStartsAt10000()
    {
        var catalog = VestigiumCustomCatalog.Open(NewRoot());
        Assert.Equal(0, catalog.Count);
        Assert.Equal(10000, catalog.NextCustomId);
    }

    [Fact]
    public void OpenLoadsCustomRows()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        File.WriteAllText(Path.Combine(root, "shards", "custom.json"),
            """[{ "EventId": 10000, "EventName": "ProbeTimeout", "FullName": "PingIQ.ProbeTimeoutException", "Kind": "Custom" }]""");
        var catalog = VestigiumCustomCatalog.Open(root);
        Assert.Equal(10005, catalog.NextCustomId);
        Assert.True(catalog.TryGet(10000, out _));
    }

    [Fact]
    public void OpenRejectsReservedRange()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        File.WriteAllText(Path.Combine(root, "shards", "bad.json"),
            """[{ "EventId": 2110, "EventName": "Bad", "FullName": "App.Bad" }]""");
        var ex = Assert.Throws<InvalidOperationException>(() => VestigiumCustomCatalog.Open(root));
        Assert.Contains("10000", ex.Message);
    }

    [Fact]
    public void AddAssigns10000Then10005()
    {
        var catalog = VestigiumCustomCatalog.Open(NewRoot());
        Assert.Equal(10000, catalog.Add("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP").EventId);
        Assert.Equal(10005, catalog.Add("Http502", "PingIQ.BadGatewayException", "Network", "HTTP").EventId);
        Assert.Equal(10010, catalog.NextCustomId);
    }

    [Fact]
    public void AddRejectsReservedAndDuplicates()
    {
        var catalog = VestigiumCustomCatalog.Open(NewRoot());
        catalog.Add("A", "App.A", "Network", "ICMP");
        Assert.Throws<InvalidOperationException>(() => catalog.Add("B", "App.B", "Network", "ICMP", eventId: 2110));
        Assert.Throws<InvalidOperationException>(() => catalog.Add("A2", "App.A", "Network", "ICMP"));
        Assert.Throws<InvalidOperationException>(() => catalog.Add("A3", "App.C", "Network", "ICMP", eventId: 10000));
    }

    [Fact]
    public void SetAndRemoveDoNotReuseIds()
    {
        var catalog = VestigiumCustomCatalog.Open(NewRoot());
        catalog.Add("A", "App.A", "Network", "ICMP");
        catalog.Set(10000, description: "updated", severity: "Warning");
        Assert.Equal("updated", catalog.Get(10000).Description);
        Assert.True(catalog.Remove(10000));
        Assert.Equal(10005, catalog.NextCustomId);
        Assert.Equal(10005, catalog.Add("B", "App.B", "Network", "HTTP").EventId);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
