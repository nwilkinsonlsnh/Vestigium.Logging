namespace Vestigium.Logging;

internal sealed class DiskSpaceMonitor : IDisposable
{
    private readonly VestigiumLoggerOptions _options;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _tripped;
    private bool? _override;

    public DiskSpaceMonitor(VestigiumLoggerOptions options)
    {
        _options = options;
        _timer = new Timer(static s => ((DiskSpaceMonitor)s!).Poll(), this, TimeSpan.Zero, options.DiskPollInterval);
    }

    public bool IsTripped
    {
        get
        {
            lock (_gate)
                return _override ?? _tripped;
        }
    }

    public long? LastAvailableBytes { get; private set; }

    public string? LastDrive { get; private set; }

    public void Override(bool? tripped)
    {
        lock (_gate)
            _override = tripped;
    }

    public void Poll()
    {
        try
        {
            var dir = _options.ResolveLogDirectory();
            Directory.CreateDirectory(dir);
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (string.IsNullOrEmpty(root))
                return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return;

            LastDrive = drive.Name;
            LastAvailableBytes = drive.AvailableFreeSpace;
            var percentFloor = drive.TotalSize > 0
                ? drive.TotalSize * _options.DiskFreePercentThreshold / 100
                : 0;
            var tripped = drive.AvailableFreeSpace < percentFloor
                          || drive.AvailableFreeSpace < _options.DiskFreeBytesFloor;

            lock (_gate)
                _tripped = tripped;
        }
        catch
        {
            // Never throw from the poller.
        }
    }

    public void Dispose() => _timer.Dispose();
}
