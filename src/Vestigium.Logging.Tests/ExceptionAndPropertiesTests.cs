using System.Text.Json;

namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class ExceptionAndPropertiesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public ExceptionAndPropertiesTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void PropertiesObjectOmitsNullAndIllegalKeys()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Information(1, VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out",
            properties: new Dictionary<string, string?>
            {
                ["host"] = "8.8.8.8", ["rttMs"] = "12", ["bad-key"] = "nope",
                ["1starts"] = "nope", ["empty"] = null, ["long"] = new string('x', 300)
            });
        using var doc = JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last());
        var bag = doc.RootElement.GetProperty("PROPERTIES");
        Assert.Equal("8.8.8.8", bag.GetProperty("host").GetString());
        Assert.False(bag.TryGetProperty("bad-key", out _));
    }

    [Fact]
    public void OverCapKeysAreDropped()
    {
        var source = Enumerable.Range(0, 20).ToDictionary(i => $"k{i}", i => (string?)$"{i}");
        Assert.Equal(16, VestigiumPropertyBag.Sanitize(source)!.Count);
    }

    [Fact]
    public void PropertiesDoNotAffectFloodIdentity()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromSeconds(30);
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        for (var i = 0; i < 8; i++)
            VestigiumLog.Information(1, VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out",
                properties: new Dictionary<string, string?> { ["attempt"] = i.ToString() });
        Assert.Equal(5, VestigiumLogger.WrittenCount);
    }

    [Fact]
    public void ExceptionFullIncludesStack()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.Full;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        Assert.Contains("InvalidOperationException", JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last()).RootElement.GetProperty("EXCEPTION").GetString());
    }

    [Fact]
    public void ExceptionNoneIsJsonNull()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ"; cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.None;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last()).RootElement.GetProperty("EXCEPTION").ValueKind);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Exception Boom()
    {
        try { throw new InvalidOperationException("outer", new ArgumentException("inner")); }
        catch (Exception ex) { return ex; }
    }
}
