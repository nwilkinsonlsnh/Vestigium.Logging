using System.Threading.Channels;

namespace Vestigium.Logging.Tests;

public sealed class LogPumpTests
{
    [Fact]
    public async Task EmitsWhenBatchSizeReached()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        var batches = new List<int>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var pump = VestigiumLogPump.RunAsync(
            channel.Reader,
            batch => batches.Add(batch.Count),
            batchSize: 10,
            batchInterval: TimeSpan.FromSeconds(30),
            cts.Token);

        for (var i = 0; i < 25; i++)
            await channel.Writer.WriteAsync(Event($"m{i}"), cts.Token);

        channel.Writer.TryComplete();
        try { await pump; } catch (OperationCanceledException) { /* timeout */ }

        Assert.Contains(10, batches);
        Assert.True(batches.Sum() >= 20);
    }

    [Fact]
    public async Task EmitsOnIntervalWhenBelowBatchSize()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        var batches = new List<IReadOnlyList<VestigiumLogEvent>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var pump = VestigiumLogPump.RunAsync(
            channel.Reader,
            batch => batches.Add(batch),
            batchSize: 50,
            batchInterval: TimeSpan.FromMilliseconds(40),
            cts.Token);

        await channel.Writer.WriteAsync(Event("a"), cts.Token);
        await channel.Writer.WriteAsync(Event("b"), cts.Token);
        await channel.Writer.WriteAsync(Event("c"), cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (batches.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20, cts.Token);

        channel.Writer.TryComplete();
        cts.Cancel();
        try { await pump; } catch (OperationCanceledException) { }

        Assert.NotEmpty(batches);
        Assert.Equal(3, batches[0].Count);
    }

    [Fact]
    public async Task OptionsOverloadUsesConfiguredBatchSize()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        var sizes = new List<int>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var options = new VestigiumLoggerOptions { UiBatchSize = 4, UiBatchInterval = TimeSpan.FromSeconds(30) };

        var pump = VestigiumLogPump.RunAsync(channel.Reader, batch => sizes.Add(batch.Count), options, cts.Token);
        for (var i = 0; i < 8; i++)
            channel.Writer.TryWrite(Event($"m{i}"));
        channel.Writer.TryComplete();
        try { await pump; } catch (OperationCanceledException) { }

        Assert.All(sizes, s => Assert.Equal(4, s));
        Assert.Equal(8, sizes.Sum());
    }

    [Fact]
    public async Task CompletedEmptyChannelReturns()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        channel.Writer.TryComplete();
        await VestigiumLogPump.RunAsync(
            channel.Reader,
            _ => throw new InvalidOperationException("no batch"),
            batchSize: 10,
            batchInterval: TimeSpan.FromMilliseconds(20),
            CancellationToken.None);
    }

    [Fact]
    public async Task CancelStopsPump()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        using var cts = new CancellationTokenSource();
        var pump = VestigiumLogPump.RunAsync(
            channel.Reader,
            _ => { },
            batchSize: 10,
            batchInterval: TimeSpan.FromMilliseconds(50),
            cts.Token);
        cts.Cancel();
        try { await pump; }
        catch (OperationCanceledException) { }
        Assert.True(pump.IsCompleted);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = VestigiumLogPump.RunAsync(null!, _ => { }, new VestigiumLoggerOptions(), CancellationToken.None);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = VestigiumLogPump.RunAsync(channel.Reader, null!, new VestigiumLoggerOptions(), CancellationToken.None);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = VestigiumLogPump.RunAsync(channel.Reader, _ => { }, null!, CancellationToken.None);
        });
    }

    private static VestigiumLogEvent Event(string message) =>
        new(DateTimeOffset.UtcNow, 1, 1, VestigiumLogLevel.Information, VestigiumStatus.None,
            "PingIQ", "Network", "ICMP", message, null);
}
