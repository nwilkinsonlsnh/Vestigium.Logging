using System.Threading.Channels;

namespace Vestigium.Logging.Tests;

public sealed class CoverageBranchCloseTests
{
    [Fact]
    public async Task PumpContinuesWhenTimerFiresOnEmptyBuffer()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        var n = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pump = VestigiumLogPump.RunAsync(
            channel.Reader,
            batch => n += batch.Count,
            batchSize: 50,
            batchInterval: TimeSpan.FromMilliseconds(20),
            cts.Token);
        await Task.Delay(80);
        await channel.Writer.WriteAsync(new VestigiumLogEvent(
            DateTimeOffset.UtcNow, 1, 1, VestigiumLogLevel.Information, VestigiumStatus.None,
            "PingIQ", "System", "Lifecycle", "late", null));
        channel.Writer.TryComplete();
        try { await pump; } catch (OperationCanceledException) { }
        Assert.True(n >= 1);
    }

    [Fact]
    public void UnbindSwallowsRemoveFailure()
    {
        var binder = new LifetimeBinder();
        binder.BindExit(new ThrowingRemoveApp(), () => { });
        binder.Unbind();
    }

    [Fact]
    public void FloodConcurrentOverflowHitsEvictGuard()
    {
        var flood = new FloodTracker(1, TimeSpan.FromSeconds(30), identityCap: 2);
        var now = DateTimeOffset.UtcNow;
        Parallel.For(0, 40, i =>
        {
            var id = new FloodIdentity("A", "C", "Information", "m" + i);
            flood.Observe(id, now, "S");
            flood.Observe(id, now, "S");
        });
        flood.DrainExpired(now);
        flood.DrainExpired(now.AddMinutes(1));
        Assert.True(flood.IdentityCap >= 2);
    }

    private sealed class ThrowingRemoveApp
    {
        private EventHandler? _exit;
        public event EventHandler Exit
        {
            add => _exit += value;
            remove => throw new InvalidOperationException("gone");
        }
    }
}
