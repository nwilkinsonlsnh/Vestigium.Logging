using System.Security.Cryptography;
using System.Text;

namespace Vestigium.Logging;

public sealed record VestigiumArchiveResult(int Archived, int Deleted, int Failed, IReadOnlyList<string> Manifest);

public static class VestigiumLogArchive
{
    public static VestigiumArchiveResult ArchiveOlderThan(
        TimeSpan age, string archiveDirectory, string? directory = null, string? appId = null)
    {
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be greater than zero.");
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveDirectory);

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

        Directory.CreateDirectory(archiveDirectory);
        if (!Directory.Exists(dir))
            return new VestigiumArchiveResult(0, 0, 0, []);

        var cutoff = DateTime.UtcNow.Date - age;
        var active = VestigiumLogger.IsInitialized ? VestigiumLogger.ActiveLogPath : null;
        var archived = 0;
        var deleted = 0;
        var failed = 0;
        var manifest = new List<string>();

        foreach (var file in Directory.GetFiles(dir, $"vestigium-{id}-*.json"))
        {
            if (active is not null && string.Equals(file, active, StringComparison.OrdinalIgnoreCase))
                continue;
            if (VestigiumLogJanitor.FileDate(file) >= cutoff)
                continue;

            var name = Path.GetFileName(file);
            var dest = Path.Combine(archiveDirectory, name);
            try
            {
                File.Copy(file, dest, overwrite: true);
                var sourceHash = HashFile(file);
                var destHash = HashFile(dest);
                if (!string.Equals(sourceHash, destHash, StringComparison.Ordinal))
                {
                    failed++;
                    continue;
                }
                File.WriteAllText(dest + ".sha256", $"{sourceHash}  {name}\n", Encoding.ASCII);
                File.Delete(file);
                archived++;
                deleted++;
                manifest.Add(name);
            }
            catch (IOException)
            {
                failed++;
            }
        }

        return new VestigiumArchiveResult(archived, deleted, failed, manifest);
    }

    internal static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
