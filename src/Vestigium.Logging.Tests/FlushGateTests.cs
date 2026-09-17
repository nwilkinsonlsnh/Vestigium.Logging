namespace Vestigium.Logging.Tests;

public sealed class FlushGateTests
{
    [Fact]
    public void IdleWaitSucceedsImmediately()
    {
        using var gate = new DisposableGate();
        Assert.True(gate.Inner.Wait(TimeSpan.Zero));
        Assert.True(gate.Inner.Wait(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void WaitBlocksUntilCompleted()
    {
        using var gate = new DisposableGate();
        gate.Inner.Issued();
        Assert.False(gate.Inner.Wait(TimeSpan.Zero));
        gate.Inner.Completed();
        Assert.True(gate.Inner.Wait(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TwoIssuedNeedTwoCompleted()
    {
        using var gate = new DisposableGate();
        gate.Inner.Issued();
        gate.Inner.Issued();
        gate.Inner.Completed();
        Assert.False(gate.Inner.Wait(TimeSpan.Zero));
        gate.Inner.Completed();
        Assert.True(gate.Inner.Wait(TimeSpan.Zero));
    }

    private sealed class DisposableGate : IDisposable
    {
        public FlushGate Inner { get; } = new();
        public void Dispose() => Inner.Dispose();
    }
}
