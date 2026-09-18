namespace Vestigium.Logging.Tests;

public sealed class ArchivePathTests
{
    [Fact]
    public void OperationsArchiveDoesNotInheritHostArchive()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = Path.Combine("C:", "logs", "PingIQ"),
            ArchiveDirectory = Path.Combine("D:", "offload", "PingIQ"),
            OperationsLogDirectory = Path.Combine("C:", "logs", "engine"),
            OperationsArchiveDirectory = Path.Combine("E:", "audit", "engine")
        };
        Assert.Equal(options.ArchiveDirectory, options.ResolveArchiveDirectory());
        Assert.Equal(options.OperationsArchiveDirectory, options.ResolveOperationsArchiveDirectory());
        Assert.NotEqual(options.ResolveArchiveDirectory(), options.ResolveOperationsArchiveDirectory());
    }

    [Fact]
    public void DefaultsKeepArchiveBesideEachLiveFolder()
    {
        var options = new VestigiumLoggerOptions { AppId = "PingIQ" };
        Assert.Equal(Path.Combine(options.ResolveLogDirectory(), "Archive"), options.ResolveArchiveDirectory());
        Assert.Equal(Path.Combine(options.ResolveOperationsLogDirectory(), "Archive"), options.ResolveOperationsArchiveDirectory());
        Assert.NotEqual(options.ResolveArchiveDirectory(), options.ResolveOperationsArchiveDirectory());
    }
}
