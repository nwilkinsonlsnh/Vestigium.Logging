namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class LogSealVerifyTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _ops = Path.Combine(Path.GetTempPath(), "vestigium-ops-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "ring.json");

    public LogSealVerifyTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_ops);
    }

    [Fact]
    public void SealedFileVerifiesValid()
    {
        var file = SealOne();
        var ring = VestigiumSealKeyRing.Open(_key);
        var report = VestigiumLogSeal.Verify(file, ring);
        Assert.Equal(VestigiumSealVerifyResult.Valid, report.Result);
        Assert.Equal(ring.KeyId, report.KeyId);
    }

    [Fact]
    public void TamperedLineIsHashMismatch()
    {
        var file = SealOne();
        File.WriteAllText(file, File.ReadAllText(file).Replace("seal-me", "TAMPER"));
        Assert.Equal(VestigiumSealVerifyResult.HashMismatch,
            VestigiumLogSeal.Verify(file, VestigiumSealKeyRing.Open(_key)).Result);
    }

    [Fact]
    public void TamperedSignatureIsBadSignature()
    {
        var file = SealOne();
        var lines = File.ReadAllLines(file);
        lines[^1] = System.Text.RegularExpressions.Regex.Replace(lines[^1], "\"Sig\":\"[^\"]*\"", "\"Sig\":\"AAAA\"");
        File.WriteAllText(file, string.Join('\n', lines) + "\n");
        Assert.Equal(VestigiumSealVerifyResult.BadSignature,
            VestigiumLogSeal.Verify(file, VestigiumSealKeyRing.Open(_key)).Result);
    }

    [Fact]
    public void MissingTrailerIsNoTrailer()
    {
        var path = Path.Combine(_logs, "plain.json");
        File.WriteAllText(path, "{\"EVENTID\":1}\n");
        var ring = VestigiumSealKeyRing.Open(_key);
        Assert.Equal(VestigiumSealVerifyResult.NoTrailer, VestigiumLogSeal.Verify(path, ring).Result);
        Assert.Equal(VestigiumSealVerifyResult.MissingFile, VestigiumLogSeal.Verify(path + ".gone", ring).Result);
    }

    private string SealOne()
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
        return Directory.GetFiles(_logs, "vestigium-PingIQ-*.json").Single();
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_ops, true); } catch { }
    }
}
