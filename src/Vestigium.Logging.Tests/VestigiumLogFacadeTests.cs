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
        VestigiumLog.Verbose(VestigiumStatus.Pending, "Network", "ICMP", "v");
        VestigiumLog.Debug(VestigiumStatus.None, "Network", "TCP", "d");
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "DNS", "i");
        VestigiumLog.Warning(VestigiumStatus.Warning, "System", "Configuration", "w");
        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "e", new InvalidOperationException("boom"));
        VestigiumLog.Fatal(VestigiumStatus.Failed, "System", "Memory", "f");
        VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Success, "UI", "Lifecycle", "direct", appId: "TraceIQ");

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
        VestigiumLog.Write((VestigiumLogLevel)99, VestigiumStatus.None, "Network", "HTTP", "odd-level");
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("odd-level"));
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }
}
