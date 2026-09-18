namespace Vestigium.Logging.Tests;

public sealed class LogJanitorTests
{
    [Fact]
    public void DeletesOldFilesAndKeepsRecent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-janitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var oldFile = Path.Combine(dir, "vestigium-PingIQ-20200101.json");
        var newFile = Path.Combine(dir, $"vestigium-PingIQ-{DateTime.UtcNow:yyyyMMdd}.json");
        File.WriteAllText(oldFile, "{}\n");
        File.WriteAllText(newFile, "{}\n");
        var result = VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(14), dir, "PingIQ");
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void RejectsNonPositiveAge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VestigiumLogJanitor.DeleteOlderThan(TimeSpan.Zero, "x", "PingIQ"));
    }
}
