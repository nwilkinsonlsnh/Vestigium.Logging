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

        var props = new Dictionary<string, string?>
        {
            ["host"] = "8.8.8.8",
            ["rttMs"] = "12",
            ["bad-key"] = "nope",
            ["1starts"] = "nope",
            ["empty"] = null,
            ["long"] = new string('x', 300)
        };

        VestigiumLog.Information(
            VestigiumStatus.Timeout, "Network", "ICMP",
            "Echo request timed out",
            properties: props);

        var json = VestigiumLogger.RecentJsonLines.Last();
        using var doc = JsonDocument.Parse(json);
        var bag = doc.RootElement.GetProperty("PROPERTIES");
        Assert.Equal(JsonValueKind.Object, bag.ValueKind);
        Assert.Equal("8.8.8.8", bag.GetProperty("host").GetString());
        Assert.Equal("12", bag.GetProperty("rttMs").GetString());
        Assert.False(bag.TryGetProperty("bad-key", out _));
        Assert.False(bag.TryGetProperty("1starts", out _));
        Assert.False(bag.TryGetProperty("empty", out _));
        Assert.Equal(256, bag.GetProperty("long").GetString()!.Length);
    }

    [Fact]
    public void OverCapKeysAreDropped()
    {
        var source = Enumerable.Range(0, 20).ToDictionary(i => $"k{i}", i => (string?)$"{i}");
        var sanitized = VestigiumPropertyBag.Sanitize(source)!;
        Assert.Equal(16, sanitized.Count);
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
        {
            VestigiumLog.Information(
                VestigiumStatus.Timeout, "Network", "ICMP",
                "Echo request timed out",
                properties: new Dictionary<string, string?> { ["attempt"] = i.ToString() });
        }

        Assert.Equal(5, VestigiumLogger.WrittenCount);
        Assert.Equal(3, VestigiumLogger.SuppressedCount);
    }

    [Fact]
    public void ExceptionFullIncludesStack()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.Full;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        var json = VestigiumLogger.RecentJsonLines.Last();
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("EXCEPTION").GetString();
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("at ", text);
    }

    [Fact]
    public void ExceptionTypeAndMessageOmitsStack()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.TypeAndMessage;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        using var doc = JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last());
        var text = doc.RootElement.GetProperty("EXCEPTION").GetString()!;
        Assert.Contains("InvalidOperationException: outer", text);
        Assert.Contains("---> System.ArgumentException: inner", text);
        Assert.DoesNotContain("at ", text);
    }

    [Fact]
    public void ExceptionNoneIsJsonNull()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.None;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        using var doc = JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("EXCEPTION").ValueKind);
        Assert.Contains("boom", doc.RootElement.GetProperty("MESSAGE").GetString());
    }

    [Fact]
    public void ExceptionTruncatesToMaxChars()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.ExceptionDetail = VestigiumExceptionDetail.Full;
            cfg.ExceptionMaxChars = 40;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });

        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "boom", Boom());
        using var doc = JsonDocument.Parse(VestigiumLogger.RecentJsonLines.Last());
        Assert.Equal(40, doc.RootElement.GetProperty("EXCEPTION").GetString()!.Length);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private static Exception Boom()
    {
        try
        {
            throw new InvalidOperationException("outer", new ArgumentException("inner"));
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
