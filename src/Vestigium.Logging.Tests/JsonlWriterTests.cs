namespace Vestigium.Logging.Tests;

public sealed class JsonlWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-jsonl", Guid.NewGuid().ToString("N"));

    public JsonlWriterTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void FlushMakesLineVisibleToSharedReader()
    {
        using var writer = Create();
        writer.Enqueue("""{"MESSAGE":"alive"}""");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
        Assert.NotNull(writer.ActivePath);
        Assert.Contains("alive", ReadShared(writer.ActivePath!));
    }

    [Fact]
    public void CompleteReleasesExclusiveRead()
    {
        var writer = Create();
        writer.Enqueue("""{"MESSAGE":"done"}""");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
        var path = writer.ActivePath!;
        writer.Complete();
        Assert.Contains("done", File.ReadAllText(path));
    }

    [Fact]
    public void SizeRollCreatesNumberedFile()
    {
        using var writer = Create(size: 40);
        writer.Enqueue(new string('a', 30));
        writer.Enqueue(new string('b', 30));
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
        writer.Complete();

        var files = Directory.GetFiles(_dir, "vestigium-PingIQ-*.json");
        Assert.True(files.Length >= 2, string.Join(',', files));
        Assert.Contains(files, f => Path.GetFileName(f).EndsWith("-2.json", StringComparison.Ordinal));
        var text = string.Join('\n', files.Select(File.ReadAllText));
        Assert.Contains('a', text);
        Assert.Contains('b', text);
    }

    [Fact]
    public void DateRollUsesNextUtcDay()
    {
        var day = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Clock(day);
        using var writer = Create(clock: clock.Now);
        writer.Enqueue("""{"MESSAGE":"day1"}""");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
        var first = writer.ActivePath;
        Assert.Contains("20260917", first);

        clock.Utc = day.AddDays(1);
        writer.Enqueue("""{"MESSAGE":"day2"}""");
        Assert.True(writer.Flush(TimeSpan.FromSeconds(2)));
        Assert.Contains("20260918", writer.ActivePath);
        writer.Complete();

        var names = Directory.GetFiles(_dir, "*.json").Select(Path.GetFileName).ToArray();
        Assert.Contains(names, n => n!.Contains("20260917"));
        Assert.Contains(names, n => n!.Contains("20260918"));
    }

    [Fact]
    public void RetentionDeletesOldest()
    {
        var now = new DateTime(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(_dir, "vestigium-PingIQ-20260101.json"), "{}\n");
        File.WriteAllText(Path.Combine(_dir, "vestigium-PingIQ-20260102.json"), "{}\n");
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "vestigium-PingIQ-20260101.json"), now.AddDays(-20));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "vestigium-PingIQ-20260102.json"), now.AddDays(-20));

        using var writer = Create(retainCount: 0, retainTime: TimeSpan.FromDays(1), clock: () => now);
        writer.Enqueue("""{"MESSAGE":"keep"}""");
        writer.Complete();

        var leftover = Directory.GetFiles(_dir, "vestigium-PingIQ-202601*.json");
        Assert.Empty(leftover);
    }

    [Fact]
    public void DropOldestWhenQueueFull()
    {
        using var writer = Create(queue: 1);
        for (var i = 0; i < 200; i++)
            writer.Enqueue($"{{\"n\":{i}}}");
        writer.Flush(TimeSpan.FromSeconds(2));
        Assert.True(writer.DroppedCount > 0);
    }

    [Fact]
    public void OptionsConstructorUsesDiskQueueCapacity()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = _dir,
            DiskQueueCapacity = 8
        };
        using var writer = new VestigiumJsonlWriter(options);
        Assert.Equal(0, writer.DroppedCount);
        writer.Enqueue("{}");
        writer.Complete();
        Assert.True(writer.WrittenCount >= 1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    private VestigiumJsonlWriter Create(
        long size = 20L * 1024 * 1024,
        int retainCount = 90,
        TimeSpan? retainTime = null,
        int queue = 10_000,
        Func<DateTime>? clock = null) =>
        new(_dir, "PingIQ", size, retainTime, retainCount, queue, clock);

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class Clock
    {
        public DateTime Utc;
        public Clock(DateTime utc) => Utc = utc;
        public DateTime Now() => Utc;
    }
}
