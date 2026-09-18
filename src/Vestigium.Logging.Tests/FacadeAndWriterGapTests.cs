namespace Vestigium.Logging.Tests;

[Collection("VestigiumLogger")]
public sealed class FacadeAndWriterGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-log-tests", Guid.NewGuid().ToString("N"));
    public FacadeAndWriterGapTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void AllSeverityHelpersAndThrownOverrides()
    {
        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = _dir;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Verbose;
        });
        VestigiumLog.Verbose(0, VestigiumStatus.None, "System", "Lifecycle", "v");
        VestigiumLog.Debug(0, VestigiumStatus.None, "System", "Lifecycle", "d");
        VestigiumLog.Information(1, VestigiumStatus.Success, "System", "Lifecycle", "i", appId: "PingIQ");
        VestigiumLog.Warning(2, VestigiumStatus.Timeout, "Network", "ICMP", "w");
        VestigiumLog.Warning(2, VestigiumStatus.Timeout, "Network", "ICMP", "w2", new InvalidOperationException("w"));
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "e");
        VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "e2", new IOException("e"));
        VestigiumLog.Fatal(4, VestigiumStatus.Failed, "System", "IO", "f");
        VestigiumLog.Fatal(4, VestigiumStatus.Failed, "System", "IO", "f2", new Exception("f"));
        VestigiumLog.Thrown(new FileNotFoundException("missing"), VestigiumStatus.Failed, category: "System", subcategory: "IO", message: "override");
        Assert.True(VestigiumLogger.RecentJsonLines.Count >= 8);
    }

    [Fact]
    public void WriterStoppedIgnoresEnqueueAndExposesIoFaultCount()
    {
        using var writer = new VestigiumJsonlWriter(_dir, "PingIQ");
        writer.Enqueue("{\"a\":1}");
        writer.Enqueue("no-newline");
        writer.Complete();
        writer.Enqueue("after-stop");
        _ = writer.IoFaultCount;
        _ = writer.DroppedCount;
        Assert.Equal(0, writer.QueuedCount);
    }

    public void Dispose()
    {
        VestigiumLogger.Shutdown();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
