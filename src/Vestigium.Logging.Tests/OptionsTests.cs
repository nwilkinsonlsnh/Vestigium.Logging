namespace Vestigium.Logging.Tests;

public sealed class OptionsTests
{
    [Fact]
    public void ConstructorRegistersDefaultTaxonomy()
    {
        var options = new VestigiumLoggerOptions();
        Assert.True(options.Taxonomy.IsCategoryRegistered("Network"));
        Assert.Equal(VestigiumExceptionDetail.Full, options.ExceptionDetail);
        Assert.Equal(8192, options.ExceptionMaxChars);
        Assert.True(options.DiskBytesFloorEnabled);
        Assert.Equal(FloodTracker.DefaultIdentityCap, options.FloodIdentityCap);
        Assert.Equal(10_000, options.DiskQueueCapacity);
    }

    [Fact]
    public void SerilogAsyncBufferAliasesDiskQueueCapacity()
    {
#pragma warning disable CS0618
        var options = new VestigiumLoggerOptions { SerilogAsyncBuffer = 32 };
        Assert.Equal(32, options.DiskQueueCapacity);
        options.DiskQueueCapacity = 64;
        Assert.Equal(64, options.SerilogAsyncBuffer);
#pragma warning restore CS0618
    }

    [Fact]
    public void ResolveLogDirectoryHonorsOverride()
    {
        var options = new VestigiumLoggerOptions { AppId = "PingIQ", LogDirectory = "/tmp/vestigium-override" };
        Assert.Equal("/tmp/vestigium-override", options.ResolveLogDirectory());
    }

    [Fact]
    public void ResolveLogDirectoryUsesAppIdWhenUnset()
    {
        var options = new VestigiumLoggerOptions { AppId = "HttpIQ", LogDirectory = null };
        Assert.Contains(Path.Combine("Vestigium", "Logs", "HttpIQ"), options.ResolveLogDirectory());
    }

    [Fact]
    public void RegisterTaxonomyMerges()
    {
        var extra = new VestigiumTaxonomy();
        extra.Register("Helpers", "Session");
        var options = new VestigiumLoggerOptions();
        options.RegisterTaxonomy(extra);
        Assert.True(options.Taxonomy.IsCategoryRegistered("Helpers"));
        Assert.True(options.Taxonomy.IsCategoryRegistered("Network"));
    }

    [Fact]
    public void RegisterTaxonomyNullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new VestigiumLoggerOptions().RegisterTaxonomy(null!));
    }
}
