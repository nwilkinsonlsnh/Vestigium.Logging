namespace Vestigium.Logging.Tests;

public sealed class LogEventSubjectTests
{
    [Fact]
    public void SubscribeRejectsNull()
    {
        var subject = new LogEventSubject();
        Assert.Throws<ArgumentNullException>(() => subject.Subscribe(null!));
    }

    [Fact]
    public void PublishDeliversAndSwallowsObserverFaults()
    {
        var subject = new LogEventSubject();
        var good = new RecordingObserver();
        var bad = new ThrowingObserver();
        using var a = subject.Subscribe(good);
        using var b = subject.Subscribe(bad);

        var evt = Sample();
        subject.Publish(evt);

        Assert.Single(good.Events);
        Assert.Same(evt, good.Events[0]);
    }

    [Fact]
    public void CompleteNotifiesThenClearsAndUnsubscribeIsIdempotent()
    {
        var subject = new LogEventSubject();
        var observer = new RecordingObserver();
        var sub = subject.Subscribe(observer);

        subject.Complete();
        Assert.Equal(1, observer.Completed);

        subject.Publish(Sample());
        Assert.Empty(observer.Events);

        sub.Dispose();
        sub.Dispose();
    }

    [Fact]
    public void DisposeStopsDelivery()
    {
        var subject = new LogEventSubject();
        var observer = new RecordingObserver();
        var sub = subject.Subscribe(observer);
        sub.Dispose();
        subject.Publish(Sample());
        Assert.Empty(observer.Events);
    }

    [Fact]
    public void CompleteSwallowsObserverFaults()
    {
        var subject = new LogEventSubject();
        using var sub = subject.Subscribe(new ThrowingObserver());
        subject.Complete();
    }

    private static VestigiumLogEvent Sample() => new(
        DateTimeOffset.UtcNow, 1, 2,
        VestigiumLogLevel.Information, VestigiumStatus.Success,
        "PingIQ", "Network", "ICMP", "ok", null);

    private sealed class RecordingObserver : IObserver<VestigiumLogEvent>
    {
        public List<VestigiumLogEvent> Events { get; } = [];
        public int Completed { get; private set; }
        public void OnCompleted() => Completed++;
        public void OnError(Exception error) { }
        public void OnNext(VestigiumLogEvent value) => Events.Add(value);
    }

    private sealed class ThrowingObserver : IObserver<VestigiumLogEvent>
    {
        public void OnCompleted() => throw new InvalidOperationException("complete");
        public void OnError(Exception error) { }
        public void OnNext(VestigiumLogEvent value) => throw new InvalidOperationException("next");
    }
}
