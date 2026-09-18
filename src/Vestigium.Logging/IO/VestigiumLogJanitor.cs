namespace Vestigium.Logging;

public sealed record VestigiumJanitorResult(int Deleted, long Bytes, int SkippedOpen);

public static class VestigiumLogJanitor
{
    public static VestigiumJanitorResult DeleteHostLogs()
    {
        var options = VestigiumLogger.Options;
        if (!options.JanitorEnabled)
            throw new InvalidOperationException("Host janitor is disabled. Set JanitorEnabled and JanitorMaxAge.");
        return DeleteOlderThan(options.JanitorMaxAge!.Value, options.ResolveLogDirectory(), options.AppId);
    }

    public static VestigiumJanitorResult DeleteOperationsLogs()
    {
        var options = VestigiumLogger.Options;
        if (!options.OperationsJanitorEnabled)
            throw new InvalidOperationException("Operations janitor is disabled. Set OperationsJanitorEnabled and OperationsJanitorMaxAge.");
        return DeleteOlderThan(options.OperationsJanitorMaxAge!.Value,
            options.ResolveOperationsLogDirectory(), VestigiumLoggerOptions.OperationsAppId);
    }

    public static VestigiumJanitorResult DeleteOlderThan(TimeSpan age, string? directory = null, string? appId = null)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be greater than zero.");
        var (dir, id) = VestigiumLogPaths.Resolve(directory, appId);
        if (!Directory.Exists(dir))
            return new VestigiumJanitorResult(0, 0, 0);
        var cutoff = DateTime.UtcNow.Date - age;
        var active = VestigiumLogPaths.ActivePathOrNull();
        var deleted = 0;
        var bytes = 0L;
        var skipped = 0;
        foreach (var file in Directory.GetFiles(dir, $"vestigium-{id}-*.json"))
        {
            var outcome = TryDeleteFile(file, cutoff, active);
            if (outcome.Deleted) { deleted++; bytes += outcome.Bytes; }
            else if (outcome.Skipped) skipped++;
        }
        return new VestigiumJanitorResult(deleted, bytes, skipped);
    }

    private static (bool Deleted, bool Skipped, long Bytes) TryDeleteFile(string file, DateTime cutoff, string? active)
    {
        if (VestigiumLogPaths.IsLiveFile(file, active)) return (false, true, 0);
        if (FileDate(file) >= cutoff) return (false, false, 0);
        try
        {
            var size = new FileInfo(file).Length;
            File.Delete(file);
            return (true, false, size);
        }
        catch (IOException) { return (false, true, 0); }
    }

    internal static DateTime FileDate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var part in name.Split('-'))
        {
            if (part.Length == 8 && DateTime.TryParseExact(part, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day))
                return DateTime.SpecifyKind(day, DateTimeKind.Utc);
        }
        return File.GetLastWriteTimeUtc(path).Date;
    }
}
