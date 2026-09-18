namespace Vestigium.Logging.Tests;

public sealed class CustomCatalogSaveTests
{
    [Fact]
    public void SaveRoundTripsRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = VestigiumCustomCatalog.Open(root);
        catalog.Add("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP");
        catalog.Save();
        Assert.True(File.Exists(Path.Combine(root, "shards", "custom.json")));
        Assert.True(File.Exists(Path.Combine(root, "index.json")));
        var reload = VestigiumCustomCatalog.Open(root);
        Assert.Equal(1, reload.Count);
        Assert.Equal(5005, reload.NextCustomId);
        Assert.Equal("PingIQ.ProbeTimeoutException", reload.Get(5000).FullName);
    }
}
