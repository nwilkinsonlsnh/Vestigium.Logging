namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class ArchiveThrownCoverageTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-at-" + Guid.NewGuid().ToString("N"));
    private readonly string _arc = Path.Combine(Path.GetTempPath(), "vestigium-ata-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = Path.Combine(Path.GetTempPath(), "vestigium-atk", Guid.NewGuid().ToString("N"), "ring.json");
    private readonly string _custom = Path.Combine(Path.GetTempPath(), "vestigium-atc-" + Guid.NewGuid().ToString("N"));

    public ArchiveThrownCoverageTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_arc);
        Directory.CreateDirectory(_custom);
    }

    [Fact]
    public void AcceptSealAllowsNoTrailerAndValid()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.LogSealEnabled = true;
            cfg.LogSealKeyPath = _key;
            cfg.OperationsLogEnabled = false;
        });
        var plain = Path.Combine(_logs, "vestigium-PingIQ-20200101.json");
        File.WriteAllText(plain, "{\"EVENTID\":1}\n");
        var sealedFile = Path.Combine(_logs, "vestigium-PingIQ-20200102.json");
        File.WriteAllText(sealedFile, "{\"EVENTID\":1}\n");
        var ring = VestigiumSealKeyRing.Open(_key);
        var bytes = File.ReadAllBytes(sealedFile);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var sig = Convert.ToBase64String(ring.Sign(bytes));
        File.AppendAllText(sealedFile,
            "{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"" + ring.KeyId + "\",\"ContentSha256\":\"" + sha + "\",\"Sig\":\"" + sig + "\",\"LineCount\":1}\n");
        var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), _arc, _logs, "PingIQ");
        Assert.True(result.Archived >= 2);
        VestigiumLogger.Shutdown();
    }

    [Fact]
    public void ArchiveIoExceptionCountsFailed()
    {
        var file = Path.Combine(_logs, "vestigium-PingIQ-20200101.json");
        File.WriteAllText(file, "{}\n");
        Directory.CreateDirectory(Path.Combine(_arc, "vestigium-PingIQ-20200101.json"));
        var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), _arc, _logs, "PingIQ");
        Assert.True(result.Failed >= 1);
    }

    [Fact]
    public void ArchiveHashMismatchReturnsFalse()
    {
        var file = Path.Combine(_logs, "vestigium-PingIQ-20200101.json");
        File.WriteAllText(file, "{}\n");
        VestigiumLogArchive.HashOverride = path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && path.StartsWith(_arc, StringComparison.OrdinalIgnoreCase)
            ? "DEADBEEF"
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        try
        {
            var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(1), _arc, _logs, "PingIQ");
            Assert.True(result.Failed >= 1);
        }
        finally
        {
            VestigiumLogArchive.HashOverride = null;
        }
    }

    [Fact]
    public void ThrownUnknownSeverityFallsBackToError()
    {
        var catalog = VestigiumCustomCatalog.Create(_custom);
        catalog.Add(10_000, "App.Odd", "ArchiveThrownCoverageTests+OddEx", "System", "Lifecycle", "NotALevel", "x");
        catalog.Save();
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _logs;
            cfg.OperationsLogEnabled = false;
        });
        VestigiumLogger.LoadCustomCatalog(_custom);
        VestigiumLog.Thrown(new OddEx("odd"), VestigiumStatus.Failed, 10_000);
        VestigiumLog.Thrown(new OddEx("odd2"), VestigiumStatus.Failed);
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();
    }

    public void Dispose()
    {
        VestigiumLogArchive.HashOverride = null;
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_arc, true); } catch { }
        try { Directory.Delete(_custom, true); } catch { }
    }

    private sealed class OddEx : Exception
    {
        public OddEx(string message) : base(message) { }
    }
}
