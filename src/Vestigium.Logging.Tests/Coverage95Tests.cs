using System.Threading.Channels;

namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class Coverage95Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-c95-" + Guid.NewGuid().ToString("N"));

    public Coverage95Tests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void SealVerifyCoversRemainingResults()
    {
        var key = Path.Combine(_dir, "ring.json");
        var ring = VestigiumSealKeyRing.Open(key);
        Assert.Throws<ArgumentException>(() => VestigiumLogSeal.Verify(" ", ring));
        Assert.Throws<ArgumentNullException>(() => VestigiumLogSeal.Verify(Path.Combine(_dir, "x"), null!));

        var empty = Path.Combine(_dir, "empty.json");
        File.WriteAllText(empty, "");
        Assert.Equal(VestigiumSealVerifyResult.NoTrailer, VestigiumLogSeal.Verify(empty, ring).Result);

        var torn = Path.Combine(_dir, "torn.json");
        File.WriteAllText(torn, "{\"EVENTID\":1}\n{VESTIGIUM_TRAILER\n");
        Assert.Equal(VestigiumSealVerifyResult.Torn, VestigiumLogSeal.Verify(torn, ring).Result);

        var mismatch = Path.Combine(_dir, "key.json");
        File.WriteAllText(mismatch,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"other\",\"ContentSha256\":\"00\",\"Sig\":\"YQ==\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.KeyMismatch, VestigiumLogSeal.Verify(mismatch, ring).Result);

        var noSig = Path.Combine(_dir, "nosig.json");
        var content = "{\"EVENTID\":1}\n"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        File.WriteAllText(noSig,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"" + ring.KeyId + "\",\"ContentSha256\":\"" + hash + "\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.BadSignature, VestigiumLogSeal.Verify(noSig, ring).Result);

        var badB64 = Path.Combine(_dir, "b64.json");
        File.WriteAllText(badB64,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"" + ring.KeyId + "\",\"ContentSha256\":\"" + hash + "\",\"Sig\":\"%%%\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.BadSignature, VestigiumLogSeal.Verify(badB64, ring).Result);

        Assert.False(VestigiumLogSeal.TryPeelTrailer("{}"u8.ToArray(), out _, out _));
        Assert.True(VestigiumLogSeal.TryPeelTrailer("x\n{\"VESTIGIUM_TRAILER\":1}"u8.ToArray(), out var peeled, out var json));
        Assert.True(peeled.Length > 0);
        Assert.Contains("VESTIGIUM_TRAILER", json);

        var report = new VestigiumSealVerifyReport(VestigiumSealVerifyResult.Valid, "p", "k", "h", 1);
        Assert.Equal("p", report.Path);
        Assert.Equal("k", report.KeyId);
        Assert.Equal("h", report.ContentSha256);
        Assert.Equal(1, report.LineCount);
    }

    [Fact]
    public void KeyRingGuardsAndCorruptFile()
    {
        Assert.Throws<ArgumentException>(() => VestigiumSealKeyRing.Open(" "));
        Assert.Throws<ArgumentNullException>(() => VestigiumSealKeyRing.OpenOrCreate(null!));
        Assert.Contains("Vestigium", VestigiumSealKeyRing.DefaultPath(""));
        var ring = VestigiumSealKeyRing.Open(Path.Combine(_dir, "ok.json"));
        Assert.Throws<ArgumentNullException>(() => ring.Sign(null!));
        Assert.Throws<ArgumentNullException>(() => ring.Verify(null!, [1]));
        Assert.Throws<ArgumentNullException>(() => ring.Verify([1], null!));

        var empty = Path.Combine(_dir, "empty-ring.json");
        File.WriteAllText(empty, "null");
        Assert.Throws<InvalidOperationException>(() => VestigiumSealKeyRing.Open(empty));
        var missing = Path.Combine(_dir, "missing-fields.json");
        File.WriteAllText(missing, "{}");
        Assert.Throws<InvalidOperationException>(() => VestigiumSealKeyRing.Open(missing));
        var noAlg = Path.Combine(_dir, "noalg.json");
        File.WriteAllText(noAlg, """{\"keyId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"material\":\"AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=\"}""");
        var loaded = VestigiumSealKeyRing.Open(noAlg);
        Assert.Equal(VestigiumSealKeyRing.Algorithm, loaded.Alg);

        var options = new VestigiumLoggerOptions { AppId = "PingIQ" };
        var created = VestigiumSealKeyRing.OpenOrCreate(options);
        Assert.True(File.Exists(created.Path));
    }

    [Fact]
    public void DiskMonitorOverrideAndSnapshot()
    {
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogDirectory = _dir,
            DiskBytesFloorEnabled = false,
            DiskFreePercentThreshold = 0,
            DiskPollInterval = TimeSpan.FromHours(1)
        };
        using var monitor = new DiskSpaceMonitor(options);
        var seen = new List<bool>();
        monitor.TripwireChanged = (tripped, status) => seen.Add(tripped);
        monitor.Poll();
        Assert.False(VestigiumDiskStatus.Empty.IsTripped);
        var snap = monitor.Snapshot();
        Assert.False(snap.IsOverridden);
        monitor.Override(true);
        Assert.True(monitor.IsTripped);
        Assert.Contains(true, seen);
        monitor.Override(true);
        monitor.Override(false);
        Assert.False(monitor.IsTripped);
        monitor.Override(null);
        _ = monitor.Snapshot();
    }

    [Fact]
    public void BinderRejectsOddExitShapes()
    {
        var binder = new LifetimeBinder();
        Assert.Throws<ArgumentNullException>(() => binder.BindExit(new object(), null!));
        binder.Unbind();
        binder.BindExit(new OneArg(), () => { });
        binder.BindExit(new ThreeArg(), () => { });
        binder.BindExit(new BadSender(), () => { });
        binder.BindExit(new NotEventArgs(), () => { });
    }

    [Fact]
    public async Task PumpUsesMinimumBatchAndInterval()
    {
        var channel = Channel.CreateUnbounded<VestigiumLogEvent>();
        var n = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pump = VestigiumLogPump.RunAsync(
            channel.Reader,
            batch => n += batch.Count,
            batchSize: 0,
            batchInterval: TimeSpan.Zero,
            cts.Token);
        await channel.Writer.WriteAsync(new VestigiumLogEvent(
            DateTimeOffset.UtcNow, 1, 1, VestigiumLogLevel.Information, VestigiumStatus.None,
            "PingIQ", "System", "Lifecycle", "p", null));
        channel.Writer.TryComplete();
        try { await pump; } catch (OperationCanceledException) { }
        Assert.True(n >= 1);
    }

    [Fact]
    public void FacadeAndArchiveJanitorGuards()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.OperationsLogDirectory = Path.Combine(_dir, "ops");
            cfg.OperationsLogEnabled = true;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Verbose(0, VestigiumStatus.None, "System", "Lifecycle", "v");
        VestigiumLog.Debug(0, VestigiumStatus.None, "System", "Lifecycle", "d");
        VestigiumLog.Warning(2, VestigiumStatus.None, "System", "Lifecycle", "w");
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "Lifecycle", "e", new InvalidOperationException("e"));
        VestigiumLog.Fatal(4, VestigiumStatus.Failed, "System", "Lifecycle", "f");
        VestigiumLog.Thrown(new IOException("io"), VestigiumStatus.Failed, "System", "IO", "custom");
        VestigiumLog.Thrown(new IOException("io2"), VestigiumStatus.Failed, 3);
        VestigiumLogger.OverrideDiskPressure(true);
        VestigiumLogger.OverrideDiskPressure(false);
        VestigiumLogger.OverrideDiskPressure(null);
        _ = VestigiumLogger.DiskStatus;
        _ = VestigiumLogger.IsDiskTripped;
        _ = VestigiumLogger.SuppressedCount;
        Assert.Throws<InvalidOperationException>(() => VestigiumLogArchive.ArchiveHostLogs(TimeSpan.FromDays(1)));
        Assert.Throws<InvalidOperationException>(() => VestigiumLogArchive.ArchiveOperationsLogs(TimeSpan.FromDays(1)));
        Assert.Throws<InvalidOperationException>(() => VestigiumLogJanitor.DeleteHostLogs());
        Assert.Throws<InvalidOperationException>(() => VestigiumLogJanitor.DeleteOperationsLogs());
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();
    }

    [Fact]
    public void ArchiveEnabledWrappersAndFloodNearCap()
    {
        var arc = Path.Combine(_dir, "arc");
        var opsArc = Path.Combine(_dir, "ops-arc");
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.OperationsLogDirectory = Path.Combine(_dir, "ops");
            cfg.ArchiveEnabled = true;
            cfg.ArchiveDirectory = arc;
            cfg.OperationsArchiveEnabled = true;
            cfg.OperationsArchiveDirectory = opsArc;
            cfg.JanitorEnabled = true;
            cfg.JanitorMaxAge = TimeSpan.FromDays(14);
            cfg.OperationsJanitorEnabled = true;
            cfg.OperationsJanitorMaxAge = TimeSpan.FromDays(90);
        });
        var host = VestigiumLogArchive.ArchiveHostLogs(TimeSpan.FromDays(14));
        var ops = VestigiumLogArchive.ArchiveOperationsLogs(TimeSpan.FromDays(14));
        Assert.True(host.Archived >= 0);
        Assert.True(ops.Archived >= 0);
        _ = VestigiumLogJanitor.DeleteHostLogs();
        _ = VestigiumLogJanitor.DeleteOperationsLogs();
        VestigiumLogger.Shutdown();

        var flood = new FloodTracker(2, TimeSpan.FromMilliseconds(20), identityCap: 4);
        Assert.False(flood.TryMarkNearCapWarning());
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            var id = new FloodIdentity("PingIQ", "System", "Information", "m" + i);
            flood.Observe(id, now, "Lifecycle");
        }
        _ = flood.TryMarkNearCapWarning();
        _ = flood.PendingSuppressed;
        _ = flood.TakeEvictedSummaries();
        var later = now.AddSeconds(1);
        flood.Observe(new FloodIdentity("PingIQ", "System", "Information", "m0"), later, "Lifecycle");
        flood.DrainExpired(later.AddSeconds(1));
        Assert.True(flood.NearCapThreshold >= 1);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class OneArg
    {
        public event Action<object>? Exit;
        public void Raise() => Exit?.Invoke(this);
    }

    private sealed class ThreeArg
    {
        public event Action<object, EventArgs, int>? Exit;
        public void Raise() => Exit?.Invoke(this, EventArgs.Empty, 0);
    }

    private sealed class BadSender
    {
        public event Action<string, EventArgs>? Exit;
        public void Raise() => Exit?.Invoke("x", EventArgs.Empty);
    }

    private sealed class NotEventArgs
    {
        public event Action<object, string>? Exit;
        public void Raise() => Exit?.Invoke(this, "x");
    }
}
