namespace Vestigium.Logging.Tests;

public sealed class FloodTrackerTests
{
    [Fact]
    public void FirstFiveAreWritten_ThenSuppressed()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromSeconds(30));
        var key = new FloodIdentity("PingIQ", "Network", "Information", "timeout");
        var now = DateTimeOffset.Parse("2026-09-06T20:20:00Z");
        var full = 0;
        var hidden = 0;
        for (var i = 0; i < 22; i++)
        {
            var (write, _) = tracker.Observe(key, now.AddMilliseconds(i * 10));
            if (write) full++;
            else hidden++;
        }

        Assert.Equal(5, full);
        Assert.Equal(17, hidden);
        Assert.Equal(17, tracker.PendingSuppressed);
    }

    [Fact]
    public void DifferentAppIdsDoNotShareCounters()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromSeconds(30));
        var now = DateTimeOffset.UtcNow;
        var ping = new FloodIdentity("PingIQ", "Network", "Information", "Timeout");
        var trace = new FloodIdentity("TraceIQ", "Network", "Information", "Timeout");

        for (var i = 0; i < 5; i++)
        {
            Assert.True(tracker.Observe(ping, now).WriteFull);
            Assert.True(tracker.Observe(trace, now).WriteFull);
        }

        Assert.False(tracker.Observe(ping, now).WriteFull);
        Assert.False(tracker.Observe(trace, now).WriteFull);
    }

    [Fact]
    public void WindowExpiryFlushesAggregationCount()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromSeconds(1));
        var key = new FloodIdentity("PingIQ", "Network", "Information", "echo");
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 8; i++)
            tracker.Observe(key, start);

        var dumped = tracker.DrainExpired(start.AddSeconds(2));
        Assert.Single(dumped);
        Assert.Equal(3, dumped[0].Suppressed);
    }

    [Fact]
    public void IdentityIsValueTupleNotConcatenation()
    {
        var a = new FloodIdentity("A", "B", "Information", "x|y");
        var b = new FloodIdentity("A", "B", "Information", "x|y");
        var c = new FloodIdentity("A", "B|Information", "x", "y");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
