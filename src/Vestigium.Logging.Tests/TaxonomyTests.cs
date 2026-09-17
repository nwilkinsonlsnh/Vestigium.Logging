namespace Vestigium.Logging.Tests;

public sealed class TaxonomyTests
{
    [Fact]
    public void UnknownSubcategoryKeepsRegisteredCategory()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("Network", "SMTP");
        Assert.True(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void FirstRegisteredCasingWins()
    {
        var t = new VestigiumTaxonomy();
        t.Register("Network", "ICMP");
        t.Register("network", "smtp");
        Assert.Equal("Network", t.Snapshot.Keys.Single());
        Assert.True(t.IsSubcategoryRegistered("NETWORK", "icmp"));
        Assert.True(t.IsSubcategoryRegistered("Network", "SMTP"));
        var (cat, sub, rewritten) = t.Normalize("NETWORK", "smtp");
        Assert.False(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal("smtp", sub);
    }

    [Fact]
    public void BlankSubcategoryOnlyRewritesSub()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("Network", "  ");
        Assert.True(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void RegisterRejectsBlankCategory()
    {
        var t = new VestigiumTaxonomy();
        Assert.Throws<ArgumentException>(() => t.Register(" "));
        Assert.Throws<ArgumentException>(() => t.Register(""));
    }

    [Fact]
    public void CombineNullSourceThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VestigiumTaxonomy.Combine(VestigiumTaxonomy.Defaults, null!));
    }

    [Fact]
    public void CombineWithNoSourcesIsEmpty()
    {
        var empty = VestigiumTaxonomy.Combine();
        Assert.Empty(empty.Snapshot);
        var (cat, sub, rewritten) = empty.Normalize("Network", "ICMP");
        Assert.True(rewritten);
        Assert.Equal(VestigiumTaxonomy.Uncategorized, cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void DefaultsIncludeSuiteCategories()
    {
        var snap = VestigiumTaxonomy.Defaults.Snapshot;
        Assert.Contains("Network", snap.Keys);
        Assert.Contains("System", snap.Keys);
        Assert.Contains("UI", snap.Keys);
        Assert.Contains(VestigiumTaxonomy.Uncategorized, snap.Keys);
        Assert.Contains("ICMP", snap["Network"]);
        Assert.Contains("Configuration", snap["System"]);
    }

    [Fact]
    public void SnapshotLookupIsIgnoreCase()
    {
        Assert.True(VestigiumTaxonomy.Defaults.Snapshot.ContainsKey("network"));
    }
}
