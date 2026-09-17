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

    [Fact]
    public void ExpiredKeysAreRemoved()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromSeconds(1));
        var key = new FloodIdentity("PingIQ", "Network", "Information", "echo");
        var start = DateTimeOffset.UtcNow;
        tracker.Observe(key, start, "ICMP");
        Assert.Equal(1, tracker.TrackedIdentityCount);

        tracker.DrainExpired(start.AddSeconds(2));
        Assert.Equal(0, tracker.TrackedIdentityCount);
    }

    [Fact]
    public void CapEvictsOldestIdle()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromMinutes(1), identityCap: 3);
        var now = DateTimeOffset.Parse("2026-09-06T20:20:00Z");
        for (var i = 0; i < 4; i++)
        {
            var key = new FloodIdentity("PingIQ", "Network", "Information", $"msg-{i}");
            tracker.Observe(key, now.AddSeconds(i), "ICMP");
        }

        Assert.True(tracker.TrackedIdentityCount <= 3);
        var dumped = tracker.DrainExpired(now.AddMinutes(2));
        Assert.Equal(0, tracker.TrackedIdentityCount);
        Assert.Empty(dumped);
    }

    [Fact]
    public void PendingSuppressedFlushedBeforeEvict()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromMinutes(1), identityCap: 1);
        var a = new FloodIdentity("PingIQ", "Network", "Information", "alpha");
        var b = new FloodIdentity("PingIQ", "Network", "Information", "beta");
        var t0 = DateTimeOffset.Parse("2026-09-06T20:20:00Z");
        for (var i = 0; i < 8; i++)
            tracker.Observe(a, t0, "ICMP");
        for (var i = 0; i < 8; i++)
            tracker.Observe(b, t0.AddSeconds(1), "ICMP");

        var dumped = tracker.TakeEvictedSummaries();
        Assert.Contains(dumped, x => x.Key == a && x.Suppressed == 3 && x.Subcategory == "ICMP");
        Assert.True(tracker.TrackedIdentityCount <= 1);
    }

    [Fact]
    public void DrainExpiredUsesLastSubcategory()
    {
        var tracker = new FloodTracker(5, TimeSpan.FromSeconds(1));
        var key = new FloodIdentity("PingIQ", "Network", "Information", "echo");
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 8; i++)
            tracker.Observe(key, start, "ICMP");

        var dumped = tracker.DrainExpired(start.AddSeconds(2));
        Assert.Single(dumped);
        Assert.Equal(3, dumped[0].Suppressed);
        Assert.Equal("ICMP", dumped[0].Subcategory);
    }
}
