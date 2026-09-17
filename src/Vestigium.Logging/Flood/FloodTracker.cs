using System.Collections.Concurrent;

namespace Vestigium.Logging;

internal sealed class FloodState
{
    public object Gate { get; } = new();
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset LastSeen { get; set; }
    public int Count { get; set; }
    public int Suppressed { get; set; }
    public string LastSubcategory { get; set; } = VestigiumTaxonomy.Unregistered;

    public FloodState(DateTimeOffset start)
    {
        WindowStart = start;
        LastSeen = start;
    }

    public void Reset(DateTimeOffset now)
    {
        WindowStart = now;
        LastSeen = now;
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
    private readonly int _identityCap;

    public FloodTracker(int threshold, TimeSpan window, int identityCap = 10_000)
    {
        _threshold = Math.Max(1, threshold);
        _window = window <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : window;
        _identityCap = Math.Max(1, identityCap);
    }

    public int Threshold => _threshold;
    public TimeSpan Window => _window;
    public int IdentityCap => _identityCap;
    public int IdentityCount => _states.Count;

    /// <summary>
    /// Returns whether to write a full line, and how many suppressed events to flush
    /// as an aggregation summary before that line (0 if none).
    /// </summary>
    public (bool WriteFull, int FlushCount) Observe(FloodIdentity key, DateTimeOffset now, string? subcategory = null)
    {
        var state = _states.GetOrAdd(key, static (_, start) => new FloodState(start), now);
        lock (state.Gate)
        {
            state.LastSeen = now;
            if (!string.IsNullOrWhiteSpace(subcategory))
                state.LastSubcategory = subcategory;

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

    public List<(FloodIdentity Key, int Suppressed, string Subcategory)> DrainExpired(DateTimeOffset now)
    {
        var dump = new List<(FloodIdentity, int, string)>();
        foreach (var pair in _states)
        {
            lock (pair.Value.Gate)
            {
                if (now - pair.Value.WindowStart < _window)
                    continue;

                if (pair.Value.Suppressed > 0)
                    dump.Add((pair.Key, pair.Value.Suppressed, pair.Value.LastSubcategory));

                _states.TryRemove(pair.Key, out _);
            }
        }

        TrimToCap(now, dump);
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

    private void TrimToCap(DateTimeOffset now, List<(FloodIdentity Key, int Suppressed, string Subcategory)> dump)
    {
        if (_states.Count <= _identityCap)
            return;

        foreach (var pair in _states.OrderBy(static p => p.Value.LastSeen))
        {
            if (_states.Count <= _identityCap)
                break;

            lock (pair.Value.Gate)
            {
                if (pair.Value.Suppressed > 0)
                    dump.Add((pair.Key, pair.Value.Suppressed, pair.Value.LastSubcategory));
                _states.TryRemove(pair.Key, out _);
            }
        }
    }
}
