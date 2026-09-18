namespace Vestigium.Logging;

public sealed class VestigiumLoggerOptions
{
    public VestigiumLoggerOptions() => RegisterTaxonomy(VestigiumTaxonomy.Defaults);

    public string AppId { get; set; } = "Vestigium";
    public string? LogDirectory { get; set; }
    public long FileSizeLimitBytes { get; set; } = 20L * 1024 * 1024;
    public TimeSpan RetainedFileTimeLimit { get; set; } = TimeSpan.FromDays(14);
    public int RetainedFileCountLimit { get; set; } = 90;
    public int FloodThresholdCount { get; set; } = 5;
    public TimeSpan FloodWindow { get; set; } = TimeSpan.FromMilliseconds(30_000);
    public int FloodIdentityCap { get; set; } = FloodTracker.DefaultIdentityCap;
    public int DiskFreePercentThreshold { get; set; } = 10;
    public long DiskFreeBytesFloor { get; set; } = 5L * 1024 * 1024 * 1024;
    public bool DiskBytesFloorEnabled { get; set; } = true;
    public TimeSpan DiskPollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int SubscriberChannelCapacity { get; set; } = 10_000;
    public TimeSpan UiBatchInterval { get; set; } = TimeSpan.FromMilliseconds(100);
    public int UiBatchSize { get; set; } = 50;
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public VestigiumLogLevel MinimumDiskLevel { get; set; } = VestigiumLogLevel.Information;
    public int DiskQueueCapacity { get; set; } = 10_000;
    [Obsolete("Use DiskQueueCapacity. Will be removed in 1.3.")]
    public int SerilogAsyncBuffer { get => DiskQueueCapacity; set => DiskQueueCapacity = value; }
    public int RecentJsonLineCap { get; set; } = 200;
    public int LogReadMaxLines { get; set; } = 1_000;
    public VestigiumExceptionDetail ExceptionDetail { get; set; } = VestigiumExceptionDetail.Full;
    public int ExceptionMaxChars { get; set; } = 8_192;
    public VestigiumTaxonomy Taxonomy { get; } = new();
    public string? EventCatalogPath { get; set; }
    public bool OperationsLogEnabled { get; set; } = true;
    public string? OperationsLogDirectory { get; set; }
    public const string OperationsAppId = "Vestigium.Logging";
    internal List<PendingCustomEvent> CustomEvents { get; } = [];

    public VestigiumEventDefinition RegisterEvent(string eventName, string fullName, string category, string subcategory,
        int? eventId = null, string severity = "Error", string? description = null)
    {
        CustomEvents.Add(new PendingCustomEvent(eventName, fullName, category, subcategory, eventId, severity, description));
        return new VestigiumEventDefinition(eventId ?? VestigiumEventCatalog.CustomMin, eventName, fullName, category, subcategory, severity, "Custom", true, Description: description);
    }

    public string ResolveLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(LogDirectory)) return LogDirectory;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Path.GetTempPath(), "Vestigium");
        return Path.Combine(root, "Vestigium", "Logs", AppId);
    }

    public string ResolveOperationsLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(OperationsLogDirectory)) return OperationsLogDirectory;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Path.GetTempPath(), "Vestigium");
        return Path.Combine(root, "Vestigium", "Logging");
    }

    public void RegisterTaxonomy(VestigiumTaxonomy source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var pair in source.Snapshot)
            Taxonomy.Register(pair.Key, pair.Value.ToArray());
    }
}

internal readonly record struct PendingCustomEvent(string EventName, string FullName, string Category, string Subcategory, int? EventId, string Severity, string? Description);
