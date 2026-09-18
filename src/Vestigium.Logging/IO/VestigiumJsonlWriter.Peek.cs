namespace Vestigium.Logging;

internal sealed partial class VestigiumJsonlWriter
{
    public IReadOnlyList<string> Peek(int count)
    {
        if (count < 1) return [];
        var snapshot = _queue.ToArray();
        return snapshot.Length <= count ? snapshot : snapshot[..count];
    }
}
