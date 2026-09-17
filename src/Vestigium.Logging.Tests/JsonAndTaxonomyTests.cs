using System.Text.Json;

namespace Vestigium.Logging.Tests;

public sealed class JsonAndTaxonomyTests
{
    [Fact]
    public void JsonPreservesMultilineAndPipes()
    {
        var evt = new VestigiumLogEvent(
            DateTimeOffset.Parse("2026-09-06T20:20:00.123Z"),
            48216, 12,
            VestigiumLogLevel.Error, VestigiumStatus.Failed,
            "HttpIQ", "Network", "HTTP",
            "payload | tabs\there\nand a stack",
            "System.InvalidOperationException: boom\n   at HttpIQ.Probe()");

        var json = evt.ToJsonLine();
        Assert.DoesNotContain("\n", json.Replace("\\n", ""));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2026-09-06T20:20:00.123Z", root.GetProperty("DateTime").GetString());
        Assert.Equal("Error", root.GetProperty("LEVEL").GetString());
        Assert.Equal("Failed", root.GetProperty("STATUS").GetString());
        Assert.Contains("payload | tabs", root.GetProperty("MESSAGE").GetString());
        Assert.Contains("stack", root.GetProperty("MESSAGE").GetString());
        Assert.Contains("InvalidOperationException", root.GetProperty("EXCEPTION").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("CORRELATIONID").ValueKind);
    }

    [Fact]
    public void JsonIncludesCorrelationId()
    {
        var evt = new VestigiumLogEvent(
            DateTimeOffset.Parse("2026-09-06T20:20:00.123Z"),
            1, 2,
            VestigiumLogLevel.Information, VestigiumStatus.Success,
            "PingIQ", "Network", "ICMP",
            "echo",
            null,
            "abc123ef");

        using var doc = JsonDocument.Parse(evt.ToJsonLine());
        Assert.Equal("abc123ef", doc.RootElement.GetProperty("CORRELATIONID").GetString());
        Assert.Equal("Warning", VestigiumStatus.Warning.ToString());
    }

    [Fact]
    public void UnregisteredCategoryFallsBack()
    {
        var t = VestigiumTaxonomy.Defaults;
        var (cat, sub, rewritten) = t.Normalize("Widgets", "Thing");
        Assert.True(rewritten);
        Assert.Equal(VestigiumTaxonomy.Uncategorized, cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void RegisteredNetworkIcmpPassesThrough()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("Network", "ICMP");
        Assert.False(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal("ICMP", sub);
    }

    [Fact]
    public void CaseInsensitiveCanonicalSpelling()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("network", "icmp");
        Assert.False(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal("ICMP", sub);
    }

    [Fact]
    public void BlankCategoryAndSubcategoryAreRewritten()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("  ", "");
        Assert.True(rewritten);
        Assert.Equal(VestigiumTaxonomy.Uncategorized, cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void CombineMergesCatalogsFirstCasingWins()
    {
        var extra = new VestigiumTaxonomy();
        extra.Register("Helpers", "Session", "Guard");
        extra.Register("network", "icmp");
        var combined = VestigiumTaxonomy.Combine(VestigiumTaxonomy.Defaults, extra);
        Assert.True(combined.IsCategoryRegistered("Helpers"));
        var (cat, sub, rewritten) = combined.Normalize("helpers", "session");
        Assert.False(rewritten);
        Assert.Equal("Helpers", cat);
        Assert.Equal("Session", sub);
        var network = combined.Normalize("NETWORK", "HTTP");
        Assert.False(network.Rewritten);
        Assert.Equal("Network", network.Category);
    }
}
