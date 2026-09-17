using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vestigium.Logging.Tests;

public sealed class JsonSchemaTests
{
    private static readonly string[] Required =
    [
        "DateTime", "PID", "TID", "LEVEL", "STATUS", "APPID",
        "CATEGORY", "SUBCATEGORY", "MESSAGE", "EXCEPTION", "CORRELATIONID", "PROPERTIES"
    ];

    [Fact]
    public void LineContainsEveryCanonicalField()
    {
        var json = Event().ToJsonLine();
        using var doc = JsonDocument.Parse(json);
        foreach (var name in Required)
            Assert.True(doc.RootElement.TryGetProperty(name, out _), name);
        Assert.Equal(Required.Length, doc.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void DateTimeIsUtcIsoWithMilliseconds()
    {
        var json = Event(timestamp: DateTimeOffset.Parse("2026-09-17T21:04:05.006Z")).ToJsonLine();
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("DateTime").GetString();
        Assert.Equal("2026-09-17T21:04:05.006Z", value);
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$"), value!);
    }

    [Fact]
    public void LevelIsEnumNameNotAlias()
    {
        foreach (var level in Enum.GetValues<VestigiumLogLevel>())
        {
            var json = Event(level: level).ToJsonLine();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(level.ToString(), doc.RootElement.GetProperty("LEVEL").GetString());
            Assert.DoesNotContain("\"LEVEL\":\"Info\"", json);
            Assert.DoesNotContain("\"LEVEL\":\"Warn\"", json);
            Assert.DoesNotContain("\"LEVEL\":\"INFO\"", json);
        }
    }

    [Fact]
    public void StatusIncludesWarning()
    {
        var json = Event(status: VestigiumStatus.Warning).ToJsonLine();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Warning", doc.RootElement.GetProperty("STATUS").GetString());
    }

    [Fact]
    public void QuotesAndUnicodeStayOnOneLine()
    {
        var json = Event(message: "said \"hello\" — café \uD83D\uDE80", exception: "line1\nline2").ToJsonLine();
        Assert.DoesNotContain("\n", json.Replace("\\n", ""));
        using var doc = JsonDocument.Parse(json);
        Assert.Contains("hello", doc.RootElement.GetProperty("MESSAGE").GetString());
        Assert.Contains("café", doc.RootElement.GetProperty("MESSAGE").GetString());
    }

    [Fact]
    public void PropertiesSerializeAsObject()
    {
        var evt = Event(properties: new Dictionary<string, string> { ["host"] = "8.8.8.8" });
        using var doc = JsonDocument.Parse(evt.ToJsonLine());
        Assert.Equal("8.8.8.8", doc.RootElement.GetProperty("PROPERTIES").GetProperty("host").GetString());
    }

    private static VestigiumLogEvent Event(
        DateTimeOffset? timestamp = null,
        VestigiumLogLevel level = VestigiumLogLevel.Information,
        VestigiumStatus status = VestigiumStatus.Success,
        string message = "ok",
        string? exception = null,
        IReadOnlyDictionary<string, string>? properties = null) =>
        new(
            timestamp ?? DateTimeOffset.Parse("2026-09-17T00:00:00.000Z"),
            10, 20, level, status, "PingIQ", "Network", "ICMP",
            message, exception, null, properties);
}
