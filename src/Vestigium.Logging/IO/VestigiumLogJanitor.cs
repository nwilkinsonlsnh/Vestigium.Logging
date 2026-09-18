namespace Vestigium.Logging;

public sealed record VestigiumJanitorResult(int Deleted, long Bytes, int SkippedOpen);

public static class VestigiumLogJanitor
{
    public static VestigiumJanitorResult DeleteOlderThan(TimeSpan age, string? directory = null, string? appId = null)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be greater than zero.");

        string dir;
        string id;
        if (VestigiumLogger.IsInitialized)
        {
            dir = string.IsNullOrWhiteSpace(directory) ? VestigiumLogger.Options.ResolveLogDirectory() : directory;
            id = string.IsNullOrWhiteSpace(appId) ? VestigiumLogger.Options.AppId : appId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(appId))
                throw new InvalidOperationException("directory and appId are required when the logger is not initialized.");
            dir = directory!;
            id = appId!;
        }

        if (!Directory.Exists(dir))
            return new VestigiumJanitorResult(0, 0, 0);

        var cutoff = DateTime.UtcNow.Date - age;
        var active = VestigiumLogger.IsInitialized ? VestigiumLogger.ActiveLogPath : null;
        var deleted = 0;
        var bytes = 0L;
        var skipped = 0;

        foreach (var file in Directory.GetFiles(dir, $"vestigium-{id}-*.json"))
        {
            if (active is not null && string.Equals(file, active, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }
            var stamp = FileDate(file);
            if (stamp >= cutoff) continue;
            try
            {
                var size = new FileInfo(file).Length;
                File.Delete(file);
                deleted++;
                bytes += size;
            }
            catch (IOException) { skipped++; }
        }
        return new VestigiumJanitorResult(deleted, bytes, skipped);
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
