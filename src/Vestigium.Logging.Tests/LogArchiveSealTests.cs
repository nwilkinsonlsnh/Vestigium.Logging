namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class LogArchiveSealTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _ops = Path.Combine(Path.GetTempPath(), "vestigium-ops-" + Guid.NewGuid().ToString("N"));
    private readonly string _arc = Path.Combine(Path.GetTempPath(), "vestigium-arc-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "ring.json");

    public LogArchiveSealTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_ops);
        Directory.CreateDirectory(_arc);
    }

    [Fact]
    public void SkipsTamperedSealedFileAndKeepsSource()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogDirectory = _ops;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.LogSealEnabled = true;
            cfg.LogSealKeyPath = _key;
        });
        VestigiumLog.Information(1, VestigiumStatus.None, "System", "Lifecycle", "seal-me");
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();

        var sealedFile = Directory.GetFiles(_logs, "vestigium-PingIQ-*.json").Single();
        var old = Path.Combine(_logs, "vestigium-PingIQ-20200101.json");
        File.Copy(sealedFile, old, overwrite: true);
        File.WriteAllText(old, File.ReadAllText(old).Replace("seal-me", "TAMPER"));

        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogDirectory = _ops;
            cfg.LogSealEnabled = true;
            cfg.LogSealKeyPath = _key;
        });
        var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(14), _arc, _logs, "PingIQ");
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();
        Assert.Equal(0, result.Archived);
        Assert.True(result.Failed >= 1);
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(Path.Combine(_arc, "vestigium-PingIQ-20200101.json")));
        var ops = string.Join('\n', Directory.EnumerateFiles(_ops, "*.json").Select(File.ReadAllText));
        Assert.Contains("\"EVENTID\":5035", ops);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_ops, true); } catch { }
        try { Directory.Delete(_arc, true); } catch { }
    }
}
