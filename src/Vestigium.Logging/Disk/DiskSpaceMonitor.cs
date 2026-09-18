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
    public Action<bool, VestigiumDiskStatus>? TripwireChanged { get; set; }

    public void Override(bool? tripped)
    {
        bool previous;
        bool current;
        VestigiumDiskStatus snapshot;
        lock (_gate)
        {
            previous = _override ?? _tripped;
            _override = tripped;
            current = _override ?? _tripped;
            snapshot = new VestigiumDiskStatus(
                IsTripped: current,
                IsOverridden: _override is not null,
                Drive: LastDrive,
                AvailableBytes: LastAvailableBytes,
                PercentThreshold: _options.DiskFreePercentThreshold,
                BytesFloor: _options.DiskBytesFloorEnabled ? _options.DiskFreeBytesFloor : 0);
        }
        if (previous != current)
            TripwireChanged?.Invoke(current, snapshot);
    }

    public VestigiumDiskStatus Snapshot()
    {
        lock (_gate)
        {
            return new VestigiumDiskStatus(
                IsTripped: _override ?? _tripped,
                IsOverridden: _override is not null,
                Drive: LastDrive,
                AvailableBytes: LastAvailableBytes,
                PercentThreshold: _options.DiskFreePercentThreshold,
                BytesFloor: _options.DiskBytesFloorEnabled ? _options.DiskFreeBytesFloor : 0);
        }
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
                          || (_options.DiskBytesFloorEnabled
                              && drive.AvailableFreeSpace < _options.DiskFreeBytesFloor);

            var changed = false;
            lock (_gate)
            {
                if (_tripped != tripped)
                {
                    _tripped = tripped;
                    changed = _override is null;
                }
            }
            if (changed)
                TripwireChanged?.Invoke(tripped, Snapshot());
        }
        catch
        {
        }
    }

    public void Dispose() => _timer.Dispose();
}
