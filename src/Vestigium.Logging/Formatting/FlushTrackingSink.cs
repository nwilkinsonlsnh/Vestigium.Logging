namespace Vestigium.Logging;

/// <summary>
/// Issued/completed counter used by tests. The host flush path is now
/// <see cref="VestigiumJsonlWriter.Flush"/>.
/// </summary>
internal sealed class FlushGate
{
    private long _issued;
    private long _completed;
    private readonly ManualResetEventSlim _idle = new(initialState: true);

    public void Issued()
    {
        Interlocked.Increment(ref _issued);
        _idle.Reset();
    }

    public void Completed()
    {
        Interlocked.Increment(ref _completed);
        if (Interlocked.Read(ref _issued) <= Interlocked.Read(ref _completed))
            _idle.Set();
    }

    public bool Wait(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return Interlocked.Read(ref _issued) <= Interlocked.Read(ref _completed);
        return _idle.Wait(timeout);
    }

    public void Dispose() => _idle.Dispose();
}
