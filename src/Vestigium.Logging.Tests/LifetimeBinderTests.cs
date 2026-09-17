namespace Vestigium.Logging.Tests;

public sealed class LifetimeBinderTests
{
    [Fact]
    public void NullInstanceIsNoOp()
    {
        var binder = new LifetimeBinder();
        var fired = 0;
        binder.BindExit(null, () => fired++);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void MissingExitEventIsIgnored()
    {
        var binder = new LifetimeBinder();
        binder.BindExit(new object(), () => throw new InvalidOperationException("should not bind"));
    }

    [Fact]
    public void CustomExitEventArgsStillBind()
    {
        var binder = new LifetimeBinder();
        var app = new WpfStyleApp();
        var fired = 0;
        binder.BindExit(app, () => fired++);
        app.Raise();
        Assert.Equal(1, fired);
        binder.Unbind();
        app.Raise();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void RebindUnsubscribesPrevious()
    {
        var binder = new LifetimeBinder();
        var first = new DummyApp();
        var second = new DummyApp();
        var fired = 0;
        binder.BindExit(first, () => fired++);
        binder.BindExit(second, () => fired++);
        first.Raise();
        Assert.Equal(0, fired);
        second.Raise();
        Assert.Equal(1, fired);
    }

    private sealed class DummyApp
    {
        public event EventHandler? Exit;
        public void Raise() => Exit?.Invoke(this, EventArgs.Empty);
    }

    private sealed class WpfExitArgs : EventArgs
    {
        public int Code { get; init; }
    }

    private delegate void ExitEventHandler(object sender, WpfExitArgs e);

    private sealed class WpfStyleApp
    {
        public event ExitEventHandler? Exit;
        public void Raise() => Exit?.Invoke(this, new WpfExitArgs { Code = 0 });
    }
}
