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
        QueryDrive = QueryPhysicalDrive;
        _timer = new Timer(static s => ((DiskSpaceMonitor)s!).Poll(), this, TimeSpan.Zero, options.DiskPollInterval);
    }

    /// <summary>Test seam. Production uses <see cref="QueryPhysicalDrive"/>.</summary>
    internal Func<string, DriveQuery> QueryDrive { get; set; }

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
            var query = QueryDrive(dir);
            if (string.IsNullOrEmpty(query.Root))
                return;

            if (!query.IsReady)
                return;

            LastDrive = query.Name;
            LastAvailableBytes = query.AvailableFreeSpace;
            var percentFloor = query.TotalSize > 0
                ? query.TotalSize * _options.DiskFreePercentThreshold / 100
                : 0;
            var tripped = query.AvailableFreeSpace < percentFloor
                          || query.AvailableFreeSpace < _options.DiskFreeBytesFloor;

            lock (_gate)
                _tripped = tripped;
        }
        catch
        {
            // Never throw from the poller.
        }
    }

    public void Dispose() => _timer.Dispose();

    private static DriveQuery QueryPhysicalDrive(string dir)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(dir));
        if (string.IsNullOrEmpty(root))
            return new DriveQuery(root, IsReady: false, TotalSize: 0, AvailableFreeSpace: 0, Name: "");

        var drive = new DriveInfo(root);
        return new DriveQuery(root, drive.IsReady, drive.TotalSize, drive.AvailableFreeSpace, drive.Name);
    }

    internal readonly record struct DriveQuery(
        string? Root,
        bool IsReady,
        long TotalSize,
        long AvailableFreeSpace,
        string Name);
}
