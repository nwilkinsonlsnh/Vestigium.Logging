namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class VestigiumLogFacadeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));

    public VestigiumLogFacadeTests()
    {
        Directory.CreateDirectory(_dir);
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 10_000;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.RecentJsonLineCap = 200;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });
    }

    [Fact]
    public void AllFacadesWriteAndMapLevels()
    {
        VestigiumLog.Verbose(0, VestigiumStatus.Pending, "Network", "ICMP", "v");
        VestigiumLog.Debug(0, VestigiumStatus.None, "Network", "TCP", "d");
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "DNS", "i");
        VestigiumLog.Warning(2, VestigiumStatus.Warning, "System", "Configuration", "w");
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "e", new InvalidOperationException("boom"));
        VestigiumLog.Fatal(4, VestigiumStatus.Failed, "System", "Memory", "f");
        VestigiumLog.Write(1, VestigiumLogLevel.Information, VestigiumStatus.Success, "UI", "Lifecycle", "direct", appId: "TraceIQ");

        var lines = VestigiumLogger.RecentJsonLines;
        Assert.True(lines.Count >= 7, $"expected >= 7 recent lines, got {lines.Count}");
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Verbose\""));
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Debug\""));
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Information\""));
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Warning\""));
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Error\""));
        Assert.Contains(lines, l => l.Contains("\"LEVEL\":\"Fatal\""));
        Assert.Contains(lines, l => l.Contains("InvalidOperationException"));
        Assert.Contains(lines, l => l.Contains("TraceIQ"));
    }

    [Fact]
    public void UnknownEnumLevelStillWrites()
    {
        VestigiumLog.Write(
            1, VestigiumLogLevel.Information, VestigiumStatus.Success,
            "Network", "HTTP", "over", appId: "HttpIQ");
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("odd-level"));
    }

    [Fact]
    public void WriteGoesToEventReader()
    {
        VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "via-log");
        Assert.True(VestigiumLogger.EventReader.TryRead(out var evt));
        Assert.Contains("via-log", evt.Message);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }
}
