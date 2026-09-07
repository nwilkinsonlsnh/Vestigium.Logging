using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Vestigium.Logging;

namespace Vestigium.Logging.Demo;

public sealed partial class MainViewModel : ObservableRecipient, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    public MainViewModel()
    {
        IsActive = true;
        Entries = [];
        JsonPreview = string.Join(Environment.NewLine, VestigiumLogger.RecentJsonLines);
        _pump = Task.Run(() => PumpAsync(_cts.Token));
        SeedWelcome();
        RefreshCounters();
    }


    [ObservableProperty] private string appId = "PingIQ";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FloodThresholdCaption))]
    private int floodThreshold = 5;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FloodWindowCaption))]
    private int floodWindowMs = 30_000;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileSizeCaption))]
    [NotifyPropertyChangedFor(nameof(FileSizeHeading))]
    [NotifyPropertyChangedFor(nameof(RollingSummary))]
    private int fileSizeMb = 20;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetainDaysCaption))]
    [NotifyPropertyChangedFor(nameof(RetainDaysLine))]
    [NotifyPropertyChangedFor(nameof(RollingSummary))]
    private int retainDays = 14;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileCapCaption))]
    [NotifyPropertyChangedFor(nameof(FileCapLine))]
    [NotifyPropertyChangedFor(nameof(RollingSummary))]
    private int fileCap = 90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskPercentCaption))]
    private int diskPercent = 10;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiskFloorCaption))]
    private int diskFloorGb = 5;
    [ObservableProperty] private string minimumDiskLevel = "Information";
    [ObservableProperty] private bool simulateLowDisk;
    [ObservableProperty] private string selectedLevel = "Information";
    [ObservableProperty] private string selectedStatus = "Timeout";
    [ObservableProperty] private string selectedCategory = "Network";
    [ObservableProperty] private string selectedSubcategory = "ICMP";
    [ObservableProperty] private string messageText = "Echo request to 8.8.8.8 timed out after 1000 ms";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BurstCountCaption))]
    private int burstCount = 22;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenCaption))]
    private int writtenCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuppressedCaption))]
    private int suppressedCount;
    [ObservableProperty] private string jsonPreview = "";
    [ObservableProperty] private string filterLevel = "All";
    [ObservableProperty] private string statusText = "Logger initialized · APPID PingIQ";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogDirectoryCaption))]
    private string logDirectory = VestigiumLogger.IsInitialized
        ? VestigiumLogger.Options.ResolveLogDirectory()
        : "";

    public string FloodThresholdCaption => $"FloodThresholdCount = {FloodThreshold}";
    public string FloodWindowCaption => $"FloodWindowMs = {FloodWindowMs}";
    public string FileSizeCaption => $"File size cap = {FileSizeMb} MB";
    public string RetainDaysCaption => $"Time retention = {RetainDays} days";
    public string FileCapCaption => $"Retained file cap = {FileCap}";
    public string DiskPercentCaption => $"Free-space percent floor = {DiskPercent}%";
    public string DiskFloorCaption => $"Hard floor = {DiskFloorGb} GB";
    public string BurstCountCaption => $"Burst count = {BurstCount}";
    public string WrittenCaption => $"Written {WrittenCount}";
    public string SuppressedCaption => $"Suppressed {SuppressedCount}";
    public string LogDirectoryCaption => string.IsNullOrWhiteSpace(LogDirectory) ? "" : $"Log directory: {LogDirectory}";
    public string RollingSummary => $"{FileSizeMb} MB files · {RetainDays} days · {FileCap} file cap";
    public string FileSizeHeading => $"{FileSizeMb} MB files";
    public string RetainDaysLine => $"Retain {RetainDays} days";
    public string FileCapLine => $"{FileCap} file cap per APPID";
    public string StartupSnippet =>
        "VestigiumLogger.Initialize(cfg => { cfg.AppId = \"PingIQ\"; cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults); });";

    public ObservableCollection<LogRow> Entries { get; }

    public IReadOnlyList<string> Levels { get; } =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    public IReadOnlyList<string> Statuses { get; } =
        ["None", "Pending", "Success", "Timeout", "Failed"];

    public IReadOnlyList<string> Categories { get; } =
        ["Network", "System", "UI", "Widgets"];

    public IReadOnlyList<string> Subcategories { get; } =
        ["ICMP", "TCP", "DNS", "HTTP", "Routing", "IO", "Memory", "Threading", "Configuration", "Navigation", "Binding", "Input", "Lifecycle", "Thing"];

    public IReadOnlyList<string> FilterLevels { get; } =
        ["All", "Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    public IReadOnlyList<string> SchemaRows { get; } =
    [
        "DateTime — UTC ISO 8601 with milliseconds (yyyy-MM-ddTHH:mm:ss.fffZ)",
        "PID — Process.GetCurrentProcess().Id",
        "TID — Environment.CurrentManagedThreadId",
        "LEVEL — Verbose, Debug, Information, Warning, Error, Fatal",
        "STATUS — None, Pending, Success, Timeout, Failed",
        "APPID — PingIQ, TraceIQ, DnsIQ, HttpIQ, Vestigium.Logging",
        "CATEGORY — registered catalog; else Uncategorized",
        "SUBCATEGORY — linked to category; else Unregistered",
        "MESSAGE — JSON-escaped, may contain newlines",
        "EXCEPTION — Exception.ToString() or null"
    ];

    public IReadOnlyList<TaxonomyRow> TaxonomyRows { get; } =
    [
        new("Network", "ICMP, TCP, DNS, HTTP, Routing"),
        new("System", "IO, Memory, Threading, Configuration"),
        new("UI", "Navigation, Binding, Input, Lifecycle"),
        new("Uncategorized", "Unregistered")
    ];

    [RelayCommand]
    private void ApplyConfiguration()
    {
        var dir = VestigiumLogger.IsInitialized
            ? VestigiumLogger.Options.ResolveLogDirectory()
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Vestigium", "Logs", AppId);

        VestigiumLogger.Shutdown();
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = AppId;
            cfg.LogDirectory = dir;
            cfg.FileSizeLimitBytes = FileSizeMb * 1024L * 1024L;
            cfg.RetainedFileTimeLimit = TimeSpan.FromDays(RetainDays);
            cfg.RetainedFileCountLimit = FileCap;
            cfg.FloodThresholdCount = FloodThreshold;
            cfg.FloodWindow = TimeSpan.FromMilliseconds(FloodWindowMs);
            cfg.DiskFreePercentThreshold = DiskPercent;
            cfg.DiskFreeBytesFloor = DiskFloorGb * 1024L * 1024L * 1024L;
            cfg.MinimumDiskLevel = Enum.Parse<VestigiumLogLevel>(MinimumDiskLevel);
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });
        VestigiumLogger.BindLifetime(Application.Current);
        VestigiumLogger.OverrideDiskPressure(SimulateLowDisk ? true : null);
        LogDirectory = VestigiumLogger.Options.ResolveLogDirectory();
        StatusText = $"Applied configuration · APPID {AppId} · flood {FloodThreshold}/{FloodWindowMs} ms · {FileSizeMb} MB / {RetainDays} d / {FileCap} files";
        RefreshCounters();
        RestartPumpNotice();
    }

    [RelayCommand]
    private void WriteOne()
    {
        var level = Enum.Parse<VestigiumLogLevel>(SelectedLevel);
        var status = Enum.Parse<VestigiumStatus>(SelectedStatus);
        VestigiumLog.Write(level, status, SelectedCategory, SelectedSubcategory, MessageText);
        RefreshCounters();
    }

    [RelayCommand]
    private void WriteBurst()
    {
        var level = Enum.Parse<VestigiumLogLevel>(SelectedLevel);
        var status = Enum.Parse<VestigiumStatus>(SelectedStatus);
        for (var i = 0; i < BurstCount; i++)
            VestigiumLog.Write(level, status, SelectedCategory, SelectedSubcategory, MessageText);
        StatusText = $"Burst {BurstCount} · first {FloodThreshold} full, remainder suppressed in-window";
        RefreshCounters();
    }

    [RelayCommand]
    private void WriteStackTrace()
    {
        try
        {
            throw new InvalidOperationException("Probe failed\n--- payload: {\"url\":\"https://edge/api?x=1&y=2\"} ---\npipe|tab\there");
        }
        catch (Exception ex)
        {
            VestigiumLog.Error(VestigiumStatus.Failed, "Network", "HTTP", "HttpIQ probe threw", ex);
        }
        StatusText = "Wrote multiline stack + pipe/tab payload as one JSON object";
        RefreshCounters();
    }

    [RelayCommand]
    private void WriteUnregistered()
    {
        VestigiumLog.Warning(VestigiumStatus.None, "Widgets", "Thing", "Operator used an unknown category");
        StatusText = "Unregistered Widgets/Thing rewritten to Uncategorized/Unregistered";
        RefreshCounters();
    }

    [RelayCommand]
    private void WritePingAndTraceTimeout()
    {
        VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "ICMP", "Timeout");
        VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "Routing", "Timeout", appId: "TraceIQ");
        StatusText = "PingIQ Timeout and TraceIQ Timeout use independent flood keys";
        RefreshCounters();
    }

    [RelayCommand]
    private void CopyLogDirectory()
    {
        if (string.IsNullOrWhiteSpace(LogDirectory)) return;
        try
        {
            Clipboard.SetText(LogDirectory);
            StatusText = "Log directory copied to clipboard";
        }
        catch
        {
            StatusText = LogDirectory;
        }
    }

    [RelayCommand]
    private void RunSuiteTour()
    {
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "Echo reply from 1.1.1.1 in 12 ms");
        VestigiumLog.Information(VestigiumStatus.Timeout, "Network", "ICMP", "Echo request to 8.8.8.8 timed out after 1000 ms");
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "DNS", "edge.vestigium.local resolved to 10.4.12.8");
        VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "Routing", "Hop 8 timed out", appId: "TraceIQ");
        try
        {
            throw new HttpRequestException("502 Bad Gateway from https://edge/api/health");
        }
        catch (Exception ex)
        {
            VestigiumLog.Error(VestigiumStatus.Failed, "Network", "HTTP", "HttpIQ probe threw", ex);
        }
        VestigiumLog.Warning(VestigiumStatus.None, "Widgets", "Thing", "Operator used an unknown category");
        for (var i = 0; i < 8; i++)
            VestigiumLog.Information(VestigiumStatus.Timeout, "Network", "ICMP", "Echo request to 8.8.8.8 timed out after 1000 ms");
        StatusText = "Suite tour: PingIQ success + timeout, DNS, TraceIQ hop, HTTP 502, unregistered Widgets, then a flood remainder";
        RefreshCounters();
    }

    private void SeedWelcome()
    {
        if (!VestigiumLogger.IsInitialized) return;
        VestigiumLog.Information(VestigiumStatus.Success, "UI", "Lifecycle", "Vestigium.Logging.Demo started");
        VestigiumLog.Information(VestigiumStatus.Success, "System", "Configuration", "Host initialized with APPID PingIQ");
        RefreshCounters();
    }


    [RelayCommand]
    private void ClearFeed()
    {
        Application.Current.Dispatcher.Invoke(() => Entries.Clear());
        StatusText = "Feed cleared (disk files retained)";
    }

    partial void OnSimulateLowDiskChanged(bool value)
    {
        VestigiumLogger.OverrideDiskPressure(value ? true : null);
        StatusText = value
            ? "Low-disk throttle ON — Verbose and Debug dropped"
            : "Low-disk throttle OFF — real drive poller";
    }

    protected override void OnActivated()
    {
        Messenger.Register<MainViewModel, LogBatchMessage>(this, static (vm, msg) => vm.OnBatch(msg));
    }

    private void OnBatch(LogBatchMessage msg)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.Invoke(() =>
        {
            foreach (var evt in msg.Events)
            {
                if (FilterLevel != "All" && evt.Level.ToString() != FilterLevel)
                    continue;
                Entries.Insert(0, LogRow.From(evt));
                while (Entries.Count > 400)
                    Entries.RemoveAt(Entries.Count - 1);
            }
            JsonPreview = string.Join(Environment.NewLine, VestigiumLogger.RecentJsonLines.Reverse());
            RefreshCounters();
        });
    }

    private void RefreshCounters()
    {
        WrittenCount = VestigiumLogger.WrittenCount;
        SuppressedCount = VestigiumLogger.SuppressedCount;
        JsonPreview = string.Join(Environment.NewLine, VestigiumLogger.RecentJsonLines.Reverse());
    }

    private void RestartPumpNotice()
    {
        StatusText += " · subscriber channel reset with the new host";
    }

    private async Task PumpAsync(CancellationToken token)
    {
        var buffer = new List<VestigiumLogEvent>(50);
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!VestigiumLogger.IsInitialized)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                    continue;
                }

                var reader = VestigiumLogger.EventReader;
                var interval = VestigiumLogger.Options.UiBatchInterval;
                var size = VestigiumLogger.Options.UiBatchSize;
                using var timer = new CancellationTokenSource(interval);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);

                buffer.Clear();
                try
                {
                    while (buffer.Count < size && await reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                    {
                        while (buffer.Count < size && reader.TryRead(out var evt))
                            buffer.Add(evt);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // batch interval elapsed
                }

                if (buffer.Count > 0)
                {
                    WeakReferenceMessenger.Default.Send(new LogBatchMessage { Events = buffer.ToArray() });
                }
                else
                {
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        IsActive = false;
        _cts.Dispose();
    }
}

public sealed class LogRow
{
    public string Time { get; init; } = "";
    public string Level { get; init; } = "";
    public string Status { get; init; } = "";
    public string AppId { get; init; } = "";
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string Accent { get; init; } = "#94A3B8";

    public static LogRow From(VestigiumLogEvent e) => new()
    {
        Time = e.Timestamp.UtcDateTime.ToString("HH:mm:ss.fff"),
        Level = e.Level.ToString(),
        Status = e.Status.ToString(),
        AppId = e.AppId,
        Category = $"{e.Category}/{e.Subcategory}",
        Message = e.Message.ReplaceLineEndings(" ↵ "),
        Accent = e.Level switch
        {
            VestigiumLogLevel.Verbose => "#64748B",
            VestigiumLogLevel.Debug => "#94A3B8",
            VestigiumLogLevel.Information => "#7DD3FC",
            VestigiumLogLevel.Warning => "#FBBF24",
            VestigiumLogLevel.Error => "#F87171",
            VestigiumLogLevel.Fatal => "#FB7185",
            _ => "#94A3B8"
        }
    };
}

public sealed record TaxonomyRow(string Category, string Subcategories);
