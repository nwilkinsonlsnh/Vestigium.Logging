namespace Vestigium.Logging;

public static partial class VestigiumLogger
{
    internal sealed partial class Host
    {
        public int DiskQueueCount => _disk.QueuedCount;
        public string? ActiveLogPath => _disk.ActivePath;
        public string? ActiveOpsLogPath => _ops?.ActivePath;
        public IReadOnlyList<string> PeekDisk(int count) => _disk.Peek(count);
    }
}
