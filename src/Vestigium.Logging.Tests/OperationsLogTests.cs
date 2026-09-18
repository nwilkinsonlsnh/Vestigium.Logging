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

    [Fact]
    public void InitializeAndShutdownWriteOpsEventIds()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogDirectory = _ops;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLogger.Shutdown();
        var text = string.Join('\n', Directory.EnumerateFiles(_ops, "*.json").Select(File.ReadAllText));
        Assert.Contains("\"EVENTID\":5000", text);
        Assert.Contains("Engine.Start", text);
        Assert.Contains("\"EVENTID\":5005", text);
        Assert.Contains("Engine.Stop", text);
        Assert.DoesNotContain("\"APPID\":\"PingIQ\"", text);
    }

    [Fact]
    public void LoadUnloadAndTripwireWriteOpsEventIds()
    {
        var catalog = Path.Combine(Path.GetTempPath(), "vestigium-custom-" + Guid.NewGuid().ToString("N"));
        var custom = VestigiumCustomCatalog.Open(catalog);
        custom.Add("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP");
        custom.Save();
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogDirectory = _ops;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLogger.LoadCustomCatalog(catalog);
        VestigiumLogger.UnloadCustomCatalog();
        VestigiumLogger.OverrideDiskPressure(true);
        VestigiumLogger.OverrideDiskPressure(false);
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();
        var text = string.Join('\n', Directory.EnumerateFiles(_ops, "*.json").Select(File.ReadAllText));
        Assert.Contains("\"EVENTID\":5065", text);
        Assert.Contains("\"EVENTID\":5070", text);
        Assert.Contains("\"EVENTID\":5060", text);
        try { Directory.Delete(catalog, true); } catch { }
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_ops, true); } catch { }
    }
}
