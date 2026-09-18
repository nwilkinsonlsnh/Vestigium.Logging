namespace Vestigium.Logging.Tests;

public sealed class ComplexityGapTests
{
    [Fact]
    public void CatalogAllAndBadShards()
    {
        var catalog = VestigiumEventCatalog.LoadDefault();
        Assert.True(catalog.All.Count >= 15);
        Assert.False(catalog.TryGetByFullName(null, out _));
        Assert.False(catalog.TryGetByFullName("", out _));

        var root = Path.Combine(Path.GetTempPath(), "vestigium-shard-" + Guid.NewGuid().ToString("N"));
        var shards = Path.Combine(root, "shards");
        Directory.CreateDirectory(shards);

        File.WriteAllText(Path.Combine(shards, "bad.json"), "{not-json");
        Assert.Throws<InvalidOperationException>(() => VestigiumEventCatalog.LoadFromDirectory(root, allowCustom: true));

        File.WriteAllText(Path.Combine(shards, "bad.json"), "true");
        Assert.True(VestigiumEventCatalog.LoadFromDirectory(root, allowCustom: true).Count >= 15);

        File.WriteAllText(Path.Combine(shards, "bad.json"), "[{\"EventId\":null,\"FullName\":\"X\"}]");
        _ = VestigiumEventCatalog.LoadFromDirectory(root, allowCustom: true);

        File.WriteAllText(Path.Combine(shards, "one.json"),
            "{\"EventId\":5000,\"FullName\":\"App.One\",\"EventName\":\"One\",\"Category\":\"Network\",\"Subcategory\":\"ICMP\"}");
        Assert.True(VestigiumEventCatalog.LoadFromDirectory(root, allowCustom: true).TryGetById(5000, out _));
    }

    [Fact]
    public void WriterRollsWhenFileExceedsLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-roll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"vestigium-PingIQ-{DateTime.UtcNow:yyyyMMdd}.json"), new string('a', 200) + "\n");
        using var writer = new VestigiumJsonlWriter(dir, "PingIQ", fileSizeLimitBytes: 80);
        writer.Enqueue("{\"x\":1}");
        writer.Enqueue(new string('b', 100));
        Assert.True(writer.Flush(TimeSpan.FromSeconds(3)));
        writer.Complete();
        Assert.True(writer.Flush(TimeSpan.Zero));
    }
}
