namespace Vestigium.Logging.Tests;

public sealed class LogArchiveTests
{
    [Fact]
    public void ArchivesOldFileWithUppercaseSha256AndDeletesSource()
    {
        var src = Path.Combine(Path.GetTempPath(), "vestigium-arch-src-" + Guid.NewGuid().ToString("N"));
        var dst = Path.Combine(Path.GetTempPath(), "vestigium-arch-dst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        var name = "vestigium-PingIQ-20200101.json";
        File.WriteAllText(Path.Combine(src, name), "{\"EVENTID\":1}\n");
        var result = VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(14), dst, src, "PingIQ");
        Assert.Equal(1, result.Archived);
        Assert.Equal(1, result.Deleted);
        Assert.False(File.Exists(Path.Combine(src, name)));
        var sidecar = File.ReadAllText(Path.Combine(dst, name + ".sha256")).Trim();
        Assert.Matches("^[0-9A-F]{64}  vestigium-PingIQ-20200101.json$", sidecar);
    }

    [Fact]
    public void RejectsNonPositiveAge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VestigiumLogArchive.ArchiveOlderThan(TimeSpan.Zero, "x", "y", "PingIQ"));
    }
}
