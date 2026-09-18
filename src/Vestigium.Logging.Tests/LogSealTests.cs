using System.Security.Cryptography;
using System.Text.Json;

namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class LogSealTests : IDisposable
{
    private readonly string _logs = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    private readonly string _ops = Path.Combine(Path.GetTempPath(), "vestigium-ops-" + Guid.NewGuid().ToString("N"));
    private readonly string _key = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "ring.json");

    public LogSealTests()
    {
        Directory.CreateDirectory(_logs);
        Directory.CreateDirectory(_ops);
    }

    [Fact]
    public void ShutdownWritesTrailerMatchingContentHash()
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

        var file = Directory.GetFiles(_logs, "vestigium-PingIQ-*.json").Single();
        var lines = File.ReadAllLines(file);
        Assert.Contains("VESTIGIUM_TRAILER", lines[^1]);
        using var doc = JsonDocument.Parse(lines[^1]);
        var sha = doc.RootElement.GetProperty("ContentSha256").GetString();
        var prefix = File.ReadAllBytes(file);
        var trailerBytes = System.Text.Encoding.UTF8.GetByteCount(lines[^1] + "\n");
        var content = prefix.AsSpan(0, prefix.Length - trailerBytes).ToArray();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), sha);
        Assert.True(File.Exists(_key));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_logs, true); } catch { }
        try { Directory.Delete(_ops, true); } catch { }
    }
}
