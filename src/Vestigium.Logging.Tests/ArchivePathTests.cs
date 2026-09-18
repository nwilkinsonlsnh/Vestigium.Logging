namespace Vestigium.Logging.Tests;

public sealed class ArchivePathTests
{
    [Fact]
    public void ArchiveFlagsDefaultOff()
    {
        var options = new VestigiumLoggerOptions();
        Assert.False(options.ArchiveEnabled);
        Assert.False(options.OperationsArchiveEnabled);
    }

    [Fact]
    public void EnabledWithoutDestinationThrows()
    {
        Assert.Throws<ArgumentException>(() => new VestigiumLoggerOptions { ArchiveEnabled = true }.ValidateArchiveOptions());
        Assert.Throws<ArgumentException>(() => new VestigiumLoggerOptions { OperationsArchiveEnabled = true }.ValidateArchiveOptions());
    }

    [Fact]
    public void OperationsArchiveDoesNotInheritHostArchive()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            ArchiveEnabled = true,
            ArchiveDirectory = Path.Combine("D:", "offload", "PingIQ"),
            OperationsArchiveEnabled = true,
            OperationsArchiveDirectory = Path.Combine("E:", "audit", "engine")
        };
        options.ValidateArchiveOptions();
        Assert.Equal(options.ArchiveDirectory, options.ResolveArchiveDirectory());
        Assert.Equal(options.OperationsArchiveDirectory, options.ResolveOperationsArchiveDirectory());
        Assert.NotEqual(options.ResolveArchiveDirectory(), options.ResolveOperationsArchiveDirectory());
    }
}
