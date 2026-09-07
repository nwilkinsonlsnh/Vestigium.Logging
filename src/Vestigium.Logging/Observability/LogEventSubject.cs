namespace Vestigium.Logging;

internal sealed class LogEventSubject : IObservable<VestigiumLogEvent>
{
    private readonly object _gate = new();
    private readonly List<IObserver<VestigiumLogEvent>> _observers = [];

    public IDisposable Subscribe(IObserver<VestigiumLogEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
            _observers.Add(observer);
        return new Unsub(this, observer);
    }

    public void Publish(VestigiumLogEvent evt)
    {
        IObserver<VestigiumLogEvent>[] snapshot;
        lock (_gate)
            snapshot = _observers.ToArray();

        foreach (var observer in snapshot)
        {
            try { observer.OnNext(evt); }
            catch { /* subscriber faults must not take down logging */ }
        }
    }

    public void Complete()
    {
        IObserver<VestigiumLogEvent>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in snapshot)
        {
            try { observer.OnCompleted(); }
            catch { /* ignore */ }
        }
    }

    private sealed class Unsub : IDisposable
    {
        private LogEventSubject? _owner;
        private readonly IObserver<VestigiumLogEvent> _observer;

        public Unsub(LogEventSubject owner, IObserver<VestigiumLogEvent> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._gate)
                owner._observers.Remove(_observer);
        }
    }
}
