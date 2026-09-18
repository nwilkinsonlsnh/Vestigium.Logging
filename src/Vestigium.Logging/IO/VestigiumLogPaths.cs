namespace Vestigium.Logging;

internal static class VestigiumLogPaths
{
    public static (string Directory, string AppId) Resolve(string? directory, string? appId)
    {
        if (VestigiumLogger.IsInitialized)
        {
            var dir = string.IsNullOrWhiteSpace(directory) ? VestigiumLogger.Options.ResolveLogDirectory() : directory;
            var id = string.IsNullOrWhiteSpace(appId) ? VestigiumLogger.Options.AppId : appId;
            return (dir, id);
        }
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(appId))
            throw new InvalidOperationException("directory and appId are required when the logger is not initialized.");
        return (directory, appId);
    }

    public static string? ActivePathOrNull() =>
        VestigiumLogger.IsInitialized ? VestigiumLogger.ActiveLogPath : null;

    public static bool IsLiveFile(string file, string? active) =>
        active is not null && string.Equals(file, active, StringComparison.OrdinalIgnoreCase);
}
