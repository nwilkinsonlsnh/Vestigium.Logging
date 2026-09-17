using System.Collections.Concurrent;

namespace Vestigium.Logging;

internal sealed class FloodState
{
    public object Gate { get; } = new();
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset LastObserved { get; set; }
    public string LastSubcategory { get; set; } = VestigiumTaxonomy.Unregistered;
    public int Count { get; set; }
    public int Suppressed { get; set; }

    public FloodState(DateTimeOffset start)
    {
        WindowStart = start;
        LastObserved = start;
    }

    public void Reset(DateTimeOffset now)
    {
        WindowStart = now;
        LastObserved = now;
        Count = 0;
        Suppressed = 0;
    }
}

/// <summary>Per-identity backoff matching SRS §3.4, with a bounded key map.</summary>
public sealed class FloodTracker
{
    public const int DefaultIdentityCap = 4_096;

    private readonly ConcurrentDictionary<FloodIdentity, FloodState> _states = new();
    private readonly ConcurrentQueue<(FloodIdentity Key, int Suppressed, string Subcategory)> _evicted = new();
    private readonly int _threshold;
    private readonly TimeSpan _window;
    private readonly int _cap;
    private int _evicting;

    public FloodTracker(int threshold, TimeSpan window, int identityCap = DefaultIdentityCap)
    {
        _threshold = Math.Max(1, threshold);
        _window = window <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : window;
        _cap = identityCap <= 0 ? DefaultIdentityCap : identityCap;
    }

    public int Threshold => _threshold;
    public TimeSpan Window => _window;
    public int IdentityCap => _cap;
    public int TrackedIdentityCount => _states.Count;

    /// <summary>
    /// Returns whether to write a full line, and how many suppressed events to flush
    /// as an aggregation summary before that line (0 if none).
    /// </summary>
    public (bool WriteFull, int FlushCount) Observe(FloodIdentity key, DateTimeOffset now, string? subcategory = null)
    {
        var state = _states.GetOrAdd(key, static (_, start) => new FloodState(start), now);
        if (_states.Count > _cap)
            EvictOverflow(now, key);

        lock (state.Gate)
        {
            if (!string.IsNullOrWhiteSpace(subcategory))
                state.LastSubcategory = subcategory;
            state.LastObserved = now;

            var expired = now - state.WindowStart >= _window;
            if (expired && state.Suppressed > 0)
            {
                var flushed = state.Suppressed;
                state.Reset(now);
                state.Count = 1;
                if (!string.IsNullOrWhiteSpace(subcategory))
                    state.LastSubcategory = subcategory;
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
        var dump = TakeEvictedSummaries();
        var remove = new List<FloodIdentity>();
        foreach (var pair in _states)
        {
            lock (pair.Value.Gate)
            {
                if (now - pair.Value.WindowStart < _window)
                    continue;

                if (pair.Value.Suppressed > 0)
                    dump.Add((pair.Key, pair.Value.Suppressed, pair.Value.LastSubcategory));
                remove.Add(pair.Key);
            }
        }

        foreach (var key in remove)
            _states.TryRemove(key, out _);

        if (_states.Count > _cap)
            EvictOverflow(now);

        dump.AddRange(TakeEvictedSummaries());
        return dump;
    }

    public List<(FloodIdentity Key, int Suppressed, string Subcategory)> TakeEvictedSummaries()
    {
        var dump = new List<(FloodIdentity, int, string)>();
        while (_evicted.TryDequeue(out var item))
            dump.Add(item);
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

    private void EvictOverflow(DateTimeOffset now, FloodIdentity? protect = null)
    {
        if (Interlocked.Exchange(ref _evicting, 1) == 1)
            return;
        try
        {
            DropExpired(now, protect);
            if (_states.Count <= _cap)
                return;

            DropOldestIdle(protect);
            if (_states.Count <= _cap)
                return;

            FlushOldestPending(protect);
        }
        finally
        {
            Volatile.Write(ref _evicting, 0);
        }
    }

    private void DropExpired(DateTimeOffset now, FloodIdentity? protect)
    {
        var remove = new List<FloodIdentity>();
        foreach (var pair in _states)
        {
            if (protect is not null && pair.Key.Equals(protect))
                continue;
            lock (pair.Value.Gate)
            {
                if (now - pair.Value.WindowStart < _window)
                    continue;
                if (pair.Value.Suppressed > 0)
                    _evicted.Enqueue((pair.Key, pair.Value.Suppressed, pair.Value.LastSubcategory));
                remove.Add(pair.Key);
            }
        }

        foreach (var key in remove)
            _states.TryRemove(key, out _);
    }

    private void DropOldestIdle(FloodIdentity? protect)
    {
        var idle = new List<(DateTimeOffset Observed, FloodIdentity Key)>();
        foreach (var pair in _states)
        {
            if (protect is not null && pair.Key.Equals(protect))
                continue;
            lock (pair.Value.Gate)
            {
                if (pair.Value.Suppressed == 0)
                    idle.Add((pair.Value.LastObserved, pair.Key));
            }
        }

        idle.Sort(static (a, b) => a.Observed.CompareTo(b.Observed));
        var over = _states.Count - _cap;
        for (var i = 0; i < idle.Count && over > 0; i++)
        {
            if (_states.TryRemove(idle[i].Key, out _))
                over--;
        }
    }

    private void FlushOldestPending(FloodIdentity? protect)
    {
        var pending = new List<(DateTimeOffset Observed, FloodIdentity Key, int Suppressed, string Sub)>();
        foreach (var pair in _states)
        {
            if (protect is not null && pair.Key.Equals(protect))
                continue;
            lock (pair.Value.Gate)
            {
                if (pair.Value.Suppressed > 0)
                    pending.Add((pair.Value.LastObserved, pair.Key, pair.Value.Suppressed, pair.Value.LastSubcategory));
            }
        }

        pending.Sort(static (a, b) => a.Observed.CompareTo(b.Observed));
        var over = _states.Count - _cap;
        for (var i = 0; i < pending.Count && over > 0; i++)
        {
            var item = pending[i];
            if (!_states.TryRemove(item.Key, out _))
                continue;
            _evicted.Enqueue((item.Key, item.Suppressed, item.Sub));
            over--;
        }
    }
}
