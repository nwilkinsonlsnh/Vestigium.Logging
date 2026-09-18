namespace Vestigium.Logging.Tests;

public sealed class LogReaderTests
{
    [Fact]
    public void HeadAndTailRespectCount()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vestigium-PingIQ-20260101.json"),
            "{\"EVENTID\":1,\"MESSAGE\":\"a\"}\n{\"EVENTID\":1,\"MESSAGE\":\"b\"}\n{\"EVENTID\":1,\"MESSAGE\":\"c\"}\n");
        File.WriteAllText(Path.Combine(dir, "vestigium-PingIQ-20260102.json"),
            "{\"EVENTID\":1,\"MESSAGE\":\"d\"}\n{\"EVENTID\":1,\"MESSAGE\":\"e\"}\n");
        var head = VestigiumLogReader.Head(3, dir, "PingIQ");
        Assert.Equal(3, head.Count);
        Assert.Contains("\"MESSAGE\":\"a\"", head[0]);
        Assert.Contains("\"MESSAGE\":\"c\"", head[2]);
        var tail = VestigiumLogReader.Tail(3, dir, "PingIQ");
        Assert.Equal(3, tail.Count);
        Assert.Contains("\"MESSAGE\":\"c\"", tail[0]);
        Assert.Contains("\"MESSAGE\":\"e\"", tail[2]);
    }

    [Fact]
    public void SkipsTornTrailingLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vestigium-PingIQ-20260101.json"),
            "{\"EVENTID\":1,\"MESSAGE\":\"ok\"}\n{\"EVENTID\":1,\"MESSAGE\":\"tor");
        var lines = VestigiumLogReader.Head(10, dir, "PingIQ");
        Assert.Single(lines);
        Assert.Contains("ok", lines[0]);
    }

    [Fact]
    public void HeadAndTailSkipSealTrailer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vestigium-PingIQ-20260101.json"),
            "{\"EVENTID\":1,\"MESSAGE\":\"a\"}\n{\"VESTIGIUM_TRAILER\":1,\"LineCount\":1}\n");
        var head = VestigiumLogReader.Head(10, dir, "PingIQ");
        var tail = VestigiumLogReader.Tail(10, dir, "PingIQ");
        Assert.Single(head);
        Assert.Single(tail);
        Assert.DoesNotContain("VESTIGIUM_TRAILER", string.Join('\n', head));
        Assert.Contains("\"MESSAGE\":\"a\"", head[0]);
    }

    [Fact]
    public void CountLessThanOneThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VestigiumLogReader.Head(0, "x", "PingIQ"));
    }
}
