using Serilog.Events;
using Serilog.Parsing;

namespace Vestigium.Logging.Tests;

public sealed class OptionsTaxonomyAndDiskTests
{
    [Fact]
    public void ResolveLogDirectoryUsesExplicitThenFallback()
    {
        var explicitDir = Path.Combine(Path.GetTempPath(), "vestigium-explicit");
        var options = new VestigiumLoggerOptions { AppId = "PingIQ", LogDirectory = explicitDir };
        Assert.Equal(explicitDir, options.ResolveLogDirectory());

        options.LogDirectory = "  ";
        var resolved = options.ResolveLogDirectory();
        Assert.Contains("Vestigium", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PingIQ", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterTaxonomyRejectsNull()
    {
        var options = new VestigiumLoggerOptions();
        Assert.Throws<ArgumentNullException>(() => options.RegisterTaxonomy(null!));
    }

    [Fact]
    public void TaxonomyNormalizesEmptyAndPartial()
    {
        var t = VestigiumTaxonomy.Defaults;
        Assert.True(t.IsCategoryRegistered("Network"));
        Assert.True(t.IsSubcategoryRegistered("Network", "ICMP"));
        Assert.False(t.IsCategoryRegistered("Widgets"));
        Assert.False(t.IsSubcategoryRegistered("Network", "SMTP"));

        var empty = t.Normalize(" ", " ");
        Assert.True(empty.Rewritten);
        Assert.Equal(VestigiumTaxonomy.Uncategorized, empty.Category);
        Assert.Equal(VestigiumTaxonomy.Unregistered, empty.Subcategory);

        var partial = t.Normalize("Network", "SMTP");
        Assert.True(partial.Rewritten);
        Assert.Equal("Network", partial.Category);
        Assert.Equal(VestigiumTaxonomy.Unregistered, partial.Subcategory);

        t.Register(" ".Trim(), Array.Empty<string>());
        Assert.Throws<ArgumentException>(() => t.Register(" ", "x"));
        t.Register("Custom");
        t.Register("Custom", " ", "Alpha");
        Assert.True(t.IsSubcategoryRegistered("Custom", "Alpha"));
        Assert.False(t.IsSubcategoryRegistered("Custom", " "));
    }

    [Fact]
    public void DiskMonitorOverrideAndPollDoNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vestigium-disk", Guid.NewGuid().ToString("N"));
        var options = new VestigiumLoggerOptions { AppId = "PingIQ", LogDirectory = dir };
        using var monitor = new DiskSpaceMonitor(options);
        monitor.Poll();
        monitor.Override(true);
        Assert.True(monitor.IsTripped);
        monitor.Override(false);
        Assert.False(monitor.IsTripped);
        monitor.Override(null);
        _ = monitor.IsTripped;
        _ = monitor.LastAvailableBytes;
        _ = monitor.LastDrive;
    }

    [Fact]
    public void FloodCtorClampsAndObserveFlushesOnExpiry()
    {
        var tracker = new FloodTracker(0, TimeSpan.Zero);
        Assert.Equal(1, tracker.Threshold);
        Assert.Equal(TimeSpan.FromSeconds(30), tracker.Window);

        var key = new FloodIdentity("PingIQ", "Network", "Information", "tick");
        var start = DateTimeOffset.Parse("2026-09-17T07:00:00Z");
        Assert.True(tracker.Observe(key, start).WriteFull);
        Assert.False(tracker.Observe(key, start).WriteFull);
        Assert.Equal(1, tracker.PendingSuppressed);

        var after = tracker.Observe(key, start.AddSeconds(31));
        Assert.True(after.WriteFull);
        Assert.Equal(1, after.FlushCount);

        Assert.Empty(tracker.DrainExpired(start));
    }

    [Fact]
    public void SerilogFormatterWritesJsonOrSkips()
    {
        var formatter = new VestigiumSerilogFormatter();
        using var withProp = new StringWriter();
        var template = new MessageTemplateParser().Parse("{VestigiumJson}");
        var jsonEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            [new LogEventProperty("VestigiumJson", new ScalarValue("{\"ok\":true}"))]);
        formatter.Format(jsonEvent, withProp);
        Assert.Contains("ok", withProp.ToString());

        using var missing = new StringWriter();
        var emptyEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            Array.Empty<LogEventProperty>());
        formatter.Format(emptyEvent, missing);
        Assert.Equal(string.Empty, missing.ToString());
    }
}
