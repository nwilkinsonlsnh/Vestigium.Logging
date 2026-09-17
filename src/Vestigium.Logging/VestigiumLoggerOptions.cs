namespace Vestigium.Logging;

public sealed class VestigiumLoggerOptions
{
    public VestigiumLoggerOptions()
    {
        RegisterTaxonomy(VestigiumTaxonomy.Defaults);
    }

    public string AppId { get; set; } = "Vestigium";

    public string? LogDirectory { get; set; }

    public long FileSizeLimitBytes { get; set; } = 20L * 1024 * 1024;

    public TimeSpan RetainedFileTimeLimit { get; set; } = TimeSpan.FromDays(14);

    public int RetainedFileCountLimit { get; set; } = 90;

    public int FloodThresholdCount { get; set; } = 5;

    public TimeSpan FloodWindow { get; set; } = TimeSpan.FromMilliseconds(30_000);

    /// <summary>Hard cap on distinct flood identities. Idle/expired keys are evicted first. Default 4096.</summary>
    public int FloodIdentityCap { get; set; } = FloodTracker.DefaultIdentityCap;

    public int DiskFreePercentThreshold { get; set; } = 10;

    public long DiskFreeBytesFloor { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>
    /// When false, only the percent threshold trips the disk gate (for small CI / lab volumes).
    /// Default true (SRS: 10% or 5 GB).
    /// </summary>
    public bool DiskBytesFloorEnabled { get; set; } = true;

    public TimeSpan DiskPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int SubscriberChannelCapacity { get; set; } = 10_000;

    public TimeSpan UiBatchInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public int UiBatchSize { get; set; } = 50;

    /// <summary>Maximum wait for the async file sink on <see cref="VestigiumLogger.Flush()"/> and shutdown.</summary>
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public VestigiumLogLevel MinimumDiskLevel { get; set; } = VestigiumLogLevel.Information;

    /// <summary>
    /// Bounded disk-queue capacity. Drop-oldest when full. Default 10,000.
    /// Used by the Serilog async sink until S2, then by <c>VestigiumJsonlWriter</c>.
    /// </summary>
    public int DiskQueueCapacity { get; set; } = 10_000;

    /// <summary>Obsolete alias for <see cref="DiskQueueCapacity"/>. Removed in 1.3.</summary>
    [Obsolete("Use DiskQueueCapacity. Will be removed in 1.3.")]
    public int SerilogAsyncBuffer
    {
        get => DiskQueueCapacity;
        set => DiskQueueCapacity = value;
    }

    public int RecentJsonLineCap { get; set; } = 200;

    public VestigiumExceptionDetail ExceptionDetail { get; set; } = VestigiumExceptionDetail.Full;

    /// <summary>Soft cap on EXCEPTION text. 0 means unlimited, still clipped at 64 KiB.</summary>
    public int ExceptionMaxChars { get; set; } = 8_192;

    public VestigiumTaxonomy Taxonomy { get; } = new();

    public string ResolveLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(LogDirectory))
            return LogDirectory;

        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Path.GetTempPath(), "Vestigium");

        return Path.Combine(root, "Vestigium", "Logs", AppId);
    }

    public void RegisterTaxonomy(VestigiumTaxonomy source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var pair in source.Snapshot)
            Taxonomy.Register(pair.Key, pair.Value.ToArray());
    }
}
