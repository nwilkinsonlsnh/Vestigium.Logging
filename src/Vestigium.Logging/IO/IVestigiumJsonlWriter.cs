namespace Vestigium.Logging;

/// <summary>
/// Owned JSONL disk backend. S1 implements this; S2 switches the host off Serilog.
/// Callers never block. Full queue drops the oldest line.
/// </summary>
internal interface IVestigiumJsonlWriter : IDisposable
{
    /// <summary>Queue one already-serialized JSON object (no trailing newline required).</summary>
    void Enqueue(string jsonLine);

    /// <summary>Wait until previously queued lines are on disk. Returns false on timeout.</summary>
    bool Flush(TimeSpan timeout);

    /// <summary>Stop accepting, drain, close the file handle, join the writer.</summary>
    void Complete();

    int QueuedCount { get; }

    int DroppedCount { get; }

    string? ActivePath { get; }
}
