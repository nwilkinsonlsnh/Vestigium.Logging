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
        var (dir, id) = VestigiumLogPaths.Resolve(directory, appId);
        Directory.CreateDirectory(archiveDirectory);
        if (!Directory.Exists(dir))
            return new VestigiumArchiveResult(0, 0, 0, []);
        var cutoff = DateTime.UtcNow.Date - age;
        var active = VestigiumLogPaths.ActivePathOrNull();
        var archived = 0;
        var deleted = 0;
        var failed = 0;
        var manifest = new List<string>();
        foreach (var file in Directory.GetFiles(dir, $"vestigium-{id}-*.json"))
        {
            if (!TryArchiveFile(file, archiveDirectory, cutoff, active, out var name))
            {
                if (name is not null) failed++;
                continue;
            }
            archived++; deleted++; manifest.Add(name);
        }
        return new VestigiumArchiveResult(archived, deleted, failed, manifest);
    }

    private static bool TryArchiveFile(string file, string archiveDirectory, DateTime cutoff, string? active, out string? name)
    {
        name = null;
        if (VestigiumLogPaths.IsLiveFile(file, active)) return false;
        if (VestigiumLogJanitor.FileDate(file) >= cutoff) return false;
        name = Path.GetFileName(file);
        var dest = Path.Combine(archiveDirectory, name);
        try
        {
            File.Copy(file, dest, overwrite: true);
            var sourceHash = HashFile(file);
            if (!string.Equals(sourceHash, HashFile(dest), StringComparison.Ordinal)) return false;
            File.WriteAllText(dest + ".sha256", $"{sourceHash}  {name}\n", Encoding.ASCII);
            File.Delete(file);
            return true;
        }
        catch (IOException) { return false; }
    }

    internal static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
