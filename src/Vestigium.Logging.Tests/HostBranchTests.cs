using Serilog.Events;
using Serilog.Parsing;

namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class HostBranchTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static VestigiumLoggerOptions Options(string dir, Action<VestigiumLoggerOptions>? extra = null)
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = dir,
            FloodThresholdCount = 2,
            FloodWindow = TimeSpan.FromMilliseconds(40),
            MinimumDiskLevel = VestigiumLogLevel.Verbose,
            RecentJsonLineCap = 50,
            FlushTimeout = TimeSpan.Zero
        };
        extra?.Invoke(options);
        return options;
    }

    [Fact]
    public void FlushStopsFurtherEmitsAndDisposeIsIdempotent()
    {
        var dir = NewDir();
        using var host = new VestigiumLogger.Host(Options(dir));
        host.Emit(VestigiumLogLevel.Information, VestigiumStatus.Success, "Network", "ICMP", "before-flush", null, null);
        Assert.Equal(1, host.WrittenCount);
        host.Flush();
        host.Emit(VestigiumLogLevel.Error, VestigiumStatus.Failed, "Network", "ICMP", "after-flush", null, null);
        Assert.Equal(1, host.WrittenCount);
        host.Dispose();
        host.Dispose();
    }

    [Fact]
    public void BlankAppIdUsesOptionsAndInternalTaxonomySkipsWarning()
    {
        var dir = NewDir();
        using var host = new VestigiumLogger.Host(Options(dir));
        host.Emit(VestigiumLogLevel.Information, VestigiumStatus.Success, "Network", "ICMP", "blank-app", null, "  ");
        host.Emit(VestigiumLogLevel.Information, VestigiumStatus.Success, "Widgets", "Thing", "internal-skip", null, VestigiumTaxonomy.InternalAppId);
        var lines = host.RecentSnapshot();
        Assert.Contains(lines, l => l.Contains("blank-app") && l.Contains("PingIQ"));
        Assert.Contains(lines, l => l.Contains("internal-skip"));
        Assert.DoesNotContain(lines, l => l.Contains("Unregistered taxonomy"));
    }

    [Fact]
    public void FloodWindowFlushWritesAggregationThenFullLine()
    {
        var dir = NewDir();
        using var host = new VestigiumLogger.Host(Options(dir, o =>
        {
            o.FloodThresholdCount = 1;
            o.FloodWindow = TimeSpan.FromMilliseconds(30);
        }));
        host.Emit(VestigiumLogLevel.Warning, VestigiumStatus.Timeout, "Network", "ICMP", "repeat-me", null, null);
        host.Emit(VestigiumLogLevel.Warning, VestigiumStatus.Timeout, "Network", "ICMP", "repeat-me", null, null);
        Assert.Equal(1, host.WrittenCount);
        Assert.True(host.Flood.PendingSuppressed >= 1);
        Thread.Sleep(50);
        host.Emit(VestigiumLogLevel.Warning, VestigiumStatus.Timeout, "Network", "ICMP", "repeat-me", null, null);
        var lines = host.RecentSnapshot();
        Assert.Contains(lines, l => l.Contains("[Aggregated]"));
        Assert.Contains(lines, l => l.Contains("repeat-me") && !l.Contains("[Aggregated]"));
    }

    [Fact]
    public void DrainUsesInformationWhenLevelIsUnknown()
    {
        var dir = NewDir();
        using var host = new VestigiumLogger.Host(Options(dir, o => o.FloodWindow = TimeSpan.FromMilliseconds(20)));
        var key = new FloodIdentity("PingIQ", "Network", "NotALevel", "orphan");
        var start = DateTimeOffset.UtcNow;
        host.Flood.Observe(key, start, "ICMP");
        host.Flood.Observe(key, start, "ICMP");
        host.Flood.Observe(key, start, "ICMP");
        Thread.Sleep(40);
        host.Drain();
        var line = Assert.Single(host.RecentSnapshot().Where(l => l.Contains("[Aggregated]")));
        Assert.Contains("\"LEVEL\":\"Information\"", line);
        Assert.Contains("\"CATEGORY\":\"Network\"", line);
        Assert.Contains("\"SUBCATEGORY\":\"ICMP\"", line);
    }

    [Fact]
    public void DrainExpiredWithoutSuppressedIsEmpty()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromMilliseconds(10));
        var key = new FloodIdentity("PingIQ", "Network", "Debug", "once");
        var start = DateTimeOffset.UtcNow;
        Assert.True(tracker.Observe(key, start).WriteFull);
        Assert.Empty(tracker.DrainExpired(start.AddSeconds(1)));
        Assert.Equal(0, tracker.PendingSuppressed);
    }

    [Fact]
    public void DiskPollCanTripFromByteFloor()
    {
        var dir = NewDir();
        var options = Options(dir, o =>
        {
            o.DiskFreeBytesFloor = long.MaxValue;
            o.DiskFreePercentThreshold = 0;
        });
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Override(null);
        monitor.Poll();
        Assert.True(monitor.IsTripped);
        Assert.False(string.IsNullOrEmpty(monitor.LastDrive));
        Assert.True(monitor.LastAvailableBytes.HasValue);
    }

    [Fact]
    public void DiskPollStaysClearWhenFloorsAreZero()
    {
        var dir = NewDir();
        var options = Options(dir, o =>
        {
            o.DiskFreeBytesFloor = 0;
            o.DiskFreePercentThreshold = 0;
        });
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Override(null);
        monitor.Poll();
        Assert.False(monitor.IsTripped);
    }

    [Fact]
    public void DiskPollSwallowsInvalidPath()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), new string('x', 400))
        };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Poll();
    }

    [Fact]
    public void SerilogFormatterFallsBackWhenValueIsNotString()
    {
        var formatter = new VestigiumSerilogFormatter();
        using var writer = new StringWriter();
        var template = new MessageTemplateParser().Parse("{VestigiumJson}");
        var evt = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            [new LogEventProperty("VestigiumJson", new ScalarValue(42))]);
        formatter.Format(evt, writer);
        Assert.Contains("42", writer.ToString());
    }

    [Fact]
    public void UnknownEnumStillMapsThroughDiskWrite()
    {
        var dir = NewDir();
        using var host = new VestigiumLogger.Host(Options(dir));
        host.Emit((VestigiumLogLevel)99, VestigiumStatus.None, "Network", "HTTP", "odd-level-host", null, null);
        Assert.Contains(host.RecentSnapshot(), l => l.Contains("odd-level-host"));
    }
}
