using System.Text.Json;

namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class LoggerBehaviorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));

    public LoggerBehaviorTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void InitializeRequiresAppId()
    {
        Assert.Throws<ArgumentException>(() =>
            VestigiumLogger.Initialize(cfg =>
            {
                cfg.AppId = " ";
                cfg.LogDirectory = _dir;
            }));
        Assert.Throws<ArgumentNullException>(() => VestigiumLogger.Initialize(null!));
    }

    [Fact]
    public void ReinitializeResetsHost()
    {
        Init();
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "first-host");
        Assert.True(VestigiumLogger.WrittenCount >= 1);

        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "TraceIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.FloodThresholdCount = 50;
        });

        Assert.Equal(0, VestigiumLogger.WrittenCount);
        Assert.Equal("TraceIQ", VestigiumLogger.Options.AppId);
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "second-host");
        Assert.Equal(1, VestigiumLogger.WrittenCount);
        Assert.Contains("TraceIQ", VestigiumLogger.RecentJsonLines.Last());
    }

    [Fact]
    public void UninitializedCountersAreZeroAndSafe()
    {
        VestigiumLogger.Shutdown();
        Assert.False(VestigiumLogger.IsInitialized);
        Assert.Equal(0, VestigiumLogger.WrittenCount);
        Assert.Equal(0, VestigiumLogger.SuppressedCount);
        Assert.Empty(VestigiumLogger.RecentJsonLines);
        Assert.False(VestigiumLogger.IsDiskTripped);
        VestigiumLogger.Flush();
        VestigiumLogger.OverrideDiskPressure(true);
        VestigiumLogger.BindLifetime(null);
    }

    [Fact]
    public void UninitializedPolicySurvivesInitialize()
    {
        var previous = VestigiumLogger.UninitializedBehavior;
        try
        {
            VestigiumLogger.UninitializedBehavior = VestigiumUninitializedBehavior.NoOp;
            Init();
            Assert.Equal(VestigiumUninitializedBehavior.NoOp, VestigiumLogger.UninitializedBehavior);
        }
        finally
        {
            VestigiumLogger.UninitializedBehavior = previous;
        }
    }

    [Fact]
    public void DiskTripDropsVerboseAndDebugKeepsInformation()
    {
        Init();
        VestigiumLogger.OverrideDiskPressure(true);
        VestigiumLog.Verbose(VestigiumStatus.None, "Network", "ICMP", "v");
        VestigiumLog.Debug(VestigiumStatus.None, "Network", "ICMP", "d");
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "i");
        VestigiumLog.Warning(VestigiumStatus.Warning, "Network", "ICMP", "w");

        var lines = VestigiumLogger.RecentJsonLines;
        Assert.DoesNotContain(lines, l => l.Contains("\"MESSAGE\":\"v\""));
        Assert.DoesNotContain(lines, l => l.Contains("\"MESSAGE\":\"d\""));
        Assert.Contains(lines, l => l.Contains("\"MESSAGE\":\"i\""));
        Assert.Contains(lines, l => l.Contains("\"MESSAGE\":\"w\""));
    }

    [Fact]
    public void EventsAndEventReaderReceiveWrites()
    {
        Init();
        var received = new List<VestigiumLogEvent>();
        using var sub = VestigiumLogger.Events.Subscribe(new RecordingObserver(received));

        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "piped");
        Assert.Single(received);
        Assert.Equal("piped", received[0].Message);
        Assert.True(VestigiumLogger.EventReader.TryRead(out var evt));
        Assert.Equal("piped", evt.Message);
    }

    [Fact]
    public void ThrowingSubscriberDoesNotFailWrite()
    {
        Init();
        using var sub = VestigiumLogger.Events.Subscribe(new ThrowingObserver());
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "still-ok");
        Assert.Equal(1, VestigiumLogger.WrittenCount);
    }

    [Fact]
    public void ShutdownCompletesObservers()
    {
        Init();
        var completed = false;
        using var sub = VestigiumLogger.Events.Subscribe(new RecordingObserver([], () => completed = true));
        VestigiumLogger.Shutdown();
        Assert.True(completed);
    }

    [Fact]
    public void UnknownTaxonomyWritesInternalWarning()
    {
        Init();
        VestigiumLog.Information(VestigiumStatus.Success, "Widgets", "Thing", "custom");
        Assert.Contains(
            VestigiumLogger.RecentJsonLines,
            l => l.Contains("Unregistered taxonomy used") && l.Contains("Vestigium.Logging"));
        Assert.Contains(
            VestigiumLogger.RecentJsonLines,
            l => l.Contains("\"CATEGORY\":\"Uncategorized\"") && l.Contains("custom"));
    }

    [Fact]
    public void WriteHonorsAppIdOverrideAndLevelHelpers()
    {
        Init();
        VestigiumLog.Verbose(VestigiumStatus.None, "Network", "ICMP", "verbose");
        VestigiumLog.Debug(VestigiumStatus.Pending, "Network", "TCP", "debug");
        VestigiumLog.Warning(VestigiumStatus.Warning, "System", "IO", "warn");
        VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "err");
        VestigiumLog.Fatal(VestigiumStatus.Failed, "System", "IO", "fatal");
        VestigiumLog.Write(
            VestigiumLogLevel.Information, VestigiumStatus.Success,
            "Network", "HTTP", "over", appId: "HttpIQ");

        var text = string.Join('\n', VestigiumLogger.RecentJsonLines);
        Assert.Contains("\"LEVEL\":\"Verbose\"", text);
        Assert.Contains("\"LEVEL\":\"Debug\"", text);
        Assert.Contains("\"LEVEL\":\"Warning\"", text);
        Assert.Contains("\"LEVEL\":\"Error\"", text);
        Assert.Contains("\"LEVEL\":\"Fatal\"", text);
        Assert.Contains("\"APPID\":\"HttpIQ\"", text);
    }

    [Fact]
    public void FlushPersistsJsonLineToDisk()
    {
        Init();
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "persist-me");
        VestigiumLogger.Flush();

        var files = Directory.GetFiles(_dir, "*.json");
        Assert.NotEmpty(files);
        var contents = string.Join('\n', files.Select(ReadShared));
        Assert.Contains("persist-me", contents);
    }

    [Fact]
    public void SubscriberChannelDropsOldest()
    {
        Init(cfg => cfg.SubscriberChannelCapacity = 2);
        for (var i = 0; i < 5; i++)
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", $"n{i}");

        var drained = new List<string>();
        while (VestigiumLogger.EventReader.TryRead(out var evt))
            drained.Add(evt.Message);

        Assert.Equal(2, drained.Count);
        Assert.Equal("n3", drained[0]);
        Assert.Equal("n4", drained[1]);
    }

    [Fact]
    public void RecentJsonLineCapDropsOldest()
    {
        Init(cfg => cfg.RecentJsonLineCap = 3);
        for (var i = 0; i < 5; i++)
            VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", $"cap{i}");

        var lines = VestigiumLogger.RecentJsonLines;
        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, l => l.Contains("cap4"));
        Assert.DoesNotContain(lines, l => l.Contains("\"MESSAGE\":\"cap0\""));
    }

    [Fact]
    public void MinimumDiskLevelStillCapturesInMemory()
    {
        Init(cfg => cfg.MinimumDiskLevel = VestigiumLogLevel.Error);
        VestigiumLog.Information(VestigiumStatus.Success, "Network", "ICMP", "memory-only");
        Assert.Contains(VestigiumLogger.RecentJsonLines, l => l.Contains("memory-only"));
        VestigiumLogger.Flush();
        var contents = string.Join('\n', Directory.GetFiles(_dir, "*.json").Select(ReadShared));
        Assert.DoesNotContain("memory-only", contents);
    }

    [Fact]
    public void IndependentAppIdsDoNotShareHostFloodCounters()
    {
        Init(cfg => cfg.FloodThresholdCount = 5);
        for (var i = 0; i < 5; i++)
        {
            VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "ICMP", "Timeout", appId: "PingIQ");
            VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "ICMP", "Timeout", appId: "TraceIQ");
        }

        Assert.Equal(10, VestigiumLogger.WrittenCount);
        Assert.Equal(0, VestigiumLogger.SuppressedCount);
        VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.Timeout, "Network", "ICMP", "Timeout", appId: "PingIQ");
        Assert.Equal(1, VestigiumLogger.SuppressedCount);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    /// <summary>
    /// Serilog keeps the rolling file open with shared: true. Exclusive
    /// File.ReadAllText fails on Windows.
    /// </summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Init(Action<VestigiumLoggerOptions>? extra = null)
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
            cfg.FloodThresholdCount = 50;
            extra?.Invoke(cfg);
        });
    }

    private sealed class RecordingObserver : IObserver<VestigiumLogEvent>
    {
        private readonly List<VestigiumLogEvent> _sink;
        private readonly Action? _onCompleted;
        public RecordingObserver(List<VestigiumLogEvent> sink, Action? onCompleted = null)
        {
            _sink = sink;
            _onCompleted = onCompleted;
        }

        public void OnCompleted() => _onCompleted?.Invoke();
        public void OnError(Exception error) { }
        public void OnNext(VestigiumLogEvent value) => _sink.Add(value);
    }

    private sealed class ThrowingObserver : IObserver<VestigiumLogEvent>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(VestigiumLogEvent value) => throw new InvalidOperationException("subscriber");
    }
}
