namespace Vestigium.Logging;

public static partial class VestigiumLogger
{
    public static int PendingDiskCount => _host?.DiskQueueCount ?? 0;

    public static IReadOnlyList<string> PeekPendingDisk(int count)
    {
        var host = Require();
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");
        var max = Math.Max(1, host.Options.LogReadMaxLines);
        return host.PeekDisk(Math.Min(count, max));
    }
}
