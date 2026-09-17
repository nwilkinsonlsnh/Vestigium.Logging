using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Vestigium.Logging;

/// <summary>
/// Counts events issued on the call path versus completed on the file sink so
/// <see cref="VestigiumLogger.Flush()"/> can wait without disposing Serilog.
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

internal sealed class CompletingSink : ILogEventSink, IDisposable
{
    private readonly ILogger _inner;
    private readonly FlushGate _gate;

    public CompletingSink(ILogger inner, FlushGate gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            _inner.Write(logEvent);
        }
        finally
        {
            _gate.Completed();
        }
    }

    public void Dispose()
    {
        // Host owns the inner file logger.
    }
}
