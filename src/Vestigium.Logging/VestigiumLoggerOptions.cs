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

    /// <summary>Maximum distinct flood identities retained. Oldest entries are evicted in <c>DrainExpired</c>.</summary>
    public int FloodIdentityCap { get; set; } = 10_000;

    public int DiskFreePercentThreshold { get; set; } = 10;

    public long DiskFreeBytesFloor { get; set; } = 5L * 1024 * 1024 * 1024;

    public TimeSpan DiskPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int SubscriberChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Host/UI hint for ViewModel drain cadence. Not used by the logging engine.
    /// </summary>
    public TimeSpan UiBatchInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Host/UI hint for ViewModel drain batch size. Not used by the logging engine.
    /// </summary>
    public int UiBatchSize { get; set; } = 50;

    /// <summary>
    /// Maximum time <see cref="VestigiumLogger.Flush"/> waits for the Serilog async buffer to close. Default 5 seconds. Zero means do not wait on a worker thread (flush runs inline).
    /// </summary>
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public VestigiumLogLevel MinimumDiskLevel { get; set; } = VestigiumLogLevel.Information;

    public int SerilogAsyncBuffer { get; set; } = 10_000;

    public int RecentJsonLineCap { get; set; } = 200;

    public VestigiumTaxonomy Taxonomy { get; } = new();

    /// <summary>Test seam. Production reads CommonApplicationData.</summary>
    internal Func<string> GetCommonApplicationData { get; set; } =
        static () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public string ResolveLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(LogDirectory))
            return LogDirectory;

        var root = GetCommonApplicationData();
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
