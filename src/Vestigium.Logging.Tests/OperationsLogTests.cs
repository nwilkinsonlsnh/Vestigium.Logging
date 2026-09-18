namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class OperationsLogTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _ops = Path.Combine(Path.GetTempPath(), "vestigium-ops-" + Guid.NewGuid().ToString("N"));

    public OperationsLogTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_ops);
    }

    [Fact]
    public void DefaultDirectoryIsProgramDataVestigiumLogging()
    {
        var options = new VestigiumLoggerOptions();
        Assert.True(options.OperationsLogEnabled);
        Assert.Equal("Vestigium.Logging", VestigiumLoggerOptions.OperationsAppId);
        Assert.EndsWith(Path.Combine("Vestigium", "Logging"), options.ResolveOperationsLogDirectory());
    }

    [Fact]
    public void EnabledCreatesOpsWriterDirectory()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogDirectory = _ops;
        });
        Assert.True(VestigiumLogger.Options.OperationsLogEnabled);
        Assert.True(Directory.Exists(_ops));
    }

    [Fact]
    public void DisabledDoesNotCreateUnusedOpsDirectory()
    {
        var unused = Path.Combine(Path.GetTempPath(), "vestigium-ops-unused-" + Guid.NewGuid().ToString("N"));
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogEnabled = false;
            cfg.OperationsLogDirectory = unused;
        });
        Assert.False(VestigiumLogger.Options.OperationsLogEnabled);
        Assert.Null(VestigiumLogger.ActiveOperationsLogPath);
        Assert.False(Directory.Exists(unused));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_ops, true); } catch { }
    }
}
