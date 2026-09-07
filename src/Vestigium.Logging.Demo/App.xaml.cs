using System.IO;
using System.Windows;
using Vestigium.Logging;

namespace Vestigium.Logging.Demo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Vestigium", "Logs", "PingIQ");

        VestigiumLogger.Initialize(cfg =>
        {
            cfg.AppId = "PingIQ";
            cfg.LogDirectory = logDir;
            cfg.FileSizeLimitBytes = 20L * 1024 * 1024;
            cfg.RetainedFileTimeLimit = TimeSpan.FromDays(14);
            cfg.RetainedFileCountLimit = 90;
            cfg.FloodThresholdCount = 5;
            cfg.FloodWindow = TimeSpan.FromMilliseconds(30_000);
            cfg.DiskFreePercentThreshold = 10;
            cfg.DiskFreeBytesFloor = 5L * 1024 * 1024 * 1024;
            cfg.MinimumDiskLevel = VestigiumLogLevel.Information;
            cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
        });
        VestigiumLogger.BindLifetime(Current);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VestigiumLogger.Flush();
        VestigiumLogger.Shutdown();
        base.OnExit(e);
    }
}
