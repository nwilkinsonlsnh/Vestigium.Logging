using System.Text.Json;

namespace Vestigium.Logging.Tests;

public sealed class JsonAndTaxonomyTests
{
    [Fact]
    public void JsonPreservesMultilineAndPipes()
    {
        var evt = new VestigiumLogEvent(
            DateTimeOffset.Parse("2026-09-06T20:20:00.123Z"),
            48216, 12,
            VestigiumLogLevel.Error, VestigiumStatus.Failed,
            "HttpIQ", "Network", "HTTP",
            "payload | tabs\there\nand a stack",
            "System.InvalidOperationException: boom\n   at HttpIQ.Probe()");

        var json = evt.ToJsonLine();
        Assert.DoesNotContain("\n", json.Replace("\\n", ""));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2026-09-06T20:20:00.123Z", root.GetProperty("DateTime").GetString());
        Assert.Equal("Error", root.GetProperty("LEVEL").GetString());
        Assert.Equal("Failed", root.GetProperty("STATUS").GetString());
        Assert.Contains("payload | tabs", root.GetProperty("MESSAGE").GetString());
        Assert.Contains("stack", root.GetProperty("MESSAGE").GetString());
        Assert.Contains("InvalidOperationException", root.GetProperty("EXCEPTION").GetString());
    }

    [Fact]
    public void UnregisteredCategoryFallsBack()
    {
        var t = VestigiumTaxonomy.Defaults;
        var (cat, sub, rewritten) = t.Normalize("Widgets", "Thing");
        Assert.True(rewritten);
        Assert.Equal(VestigiumTaxonomy.Uncategorized, cat);
        Assert.Equal(VestigiumTaxonomy.Unregistered, sub);
    }

    [Fact]
    public void RegisteredNetworkIcmpPassesThrough()
    {
        var (cat, sub, rewritten) = VestigiumTaxonomy.Defaults.Normalize("Network", "ICMP");
        Assert.False(rewritten);
        Assert.Equal("Network", cat);
        Assert.Equal("ICMP", sub);
    }
}

public sealed class LoggerIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));

    public LoggerIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void InitializeRequiredBeforeWrite()
    {
        VestigiumLogger.Shutdown();
        Assert.Throws<InvalidOperationException>(() =>
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "hello"));
    }

    [Fact]
    public void FloodBurstWritesFivePlusAggregation()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });

        for (var i = 0; i < 22; i++)
        {
            VestigiumLog.Information(
                VestigiumStatus.Timeout, "Network", "ICMP",
                "Echo request to 8.8.8.8 timed out after 1000 ms");
        }

        Assert.Equal(5, VestigiumLogger.WrittenCount);
        Assert.Equal(17, VestigiumLogger.SuppressedCount);
        var last = VestigiumLogger.RecentJsonLines.Last();
        Assert.Contains("Echo request to 8.8.8.8", last);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }
}
