using System.Collections.Concurrent;

namespace Vestigium.Logging;

internal sealed class FloodState
{
    public object Gate { get; } = new();
    public DateTimeOffset WindowStart { get; private set; }
    public int Count { get; set; }
    public int Suppressed { get; set; }

    public FloodState(DateTimeOffset start) => WindowStart = start;

    public void Reset(DateTimeOffset now)
    {
        WindowStart = now;
        Count = 0;
        Suppressed = 0;
    }
}

/// <summary>Per-identity backoff matching SRS §3.4.</summary>
public sealed class FloodTracker
{
    private readonly ConcurrentDictionary<FloodIdentity, FloodState> _states = new();
    private readonly int _threshold;
    private readonly TimeSpan _window;

    public FloodTracker(int threshold, TimeSpan window)
    {
        _threshold = Math.Max(1, threshold);
        _window = window <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : window;
    }

    public int Threshold => _threshold;
    public TimeSpan Window => _window;

    /// <summary>
    /// Returns whether to write a full line, and how many suppressed events to flush
    /// as an aggregation summary before that line (0 if none).
    /// </summary>
    public (bool WriteFull, int FlushCount) Observe(FloodIdentity key, DateTimeOffset now)
    {
        var state = _states.GetOrAdd(key, static (_, start) => new FloodState(start), now);
        lock (state.Gate)
        {
            var expired = now - state.WindowStart >= _window;
            if (expired && state.Suppressed > 0)
            {
                var flushed = state.Suppressed;
                state.Reset(now);
                state.Count = 1;
                return (true, flushed);
            }

            if (expired)
                state.Reset(now);

            state.Count++;
            if (state.Count <= _threshold)
                return (true, 0);

            state.Suppressed++;
            return (false, 0);
        }
    }

    public List<(FloodIdentity Key, int Suppressed)> DrainExpired(DateTimeOffset now)
    {
        var dump = new List<(FloodIdentity, int)>();
        foreach (var pair in _states)
        {
            lock (pair.Value.Gate)
            {
                if (now - pair.Value.WindowStart >= _window && pair.Value.Suppressed > 0)
                {
                    dump.Add((pair.Key, pair.Value.Suppressed));
                    pair.Value.Reset(now);
                }
            }
        }

        return dump;
    }

    public int PendingSuppressed
    {
        get
        {
            var n = 0;
            foreach (var pair in _states)
            {
                lock (pair.Value.Gate)
                    n += pair.Value.Suppressed;
            }
            return n;
        }
    }
}
