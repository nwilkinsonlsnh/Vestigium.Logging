using System.Security.Cryptography;
using System.Text;

namespace Vestigium.Logging;

public sealed record VestigiumArchiveResult(int Archived, int Deleted, int Failed, IReadOnlyList<string> Manifest);

public static class VestigiumLogArchive
{
    public static VestigiumArchiveResult ArchiveHostLogs(TimeSpan age)
    {
        var options = VestigiumLogger.Options;
        if (!options.ArchiveEnabled)
            throw new InvalidOperationException("Host archiving is disabled. Set ArchiveEnabled and ArchiveDirectory.");
        return ArchiveOlderThan(age, options.ResolveArchiveDirectory(), options.ResolveLogDirectory(), options.AppId);
    }

    public static VestigiumArchiveResult ArchiveOperationsLogs(TimeSpan age)
    {
        var options = VestigiumLogger.Options;
        if (!options.OperationsArchiveEnabled)
            throw new InvalidOperationException("Operations archiving is disabled. Set OperationsArchiveEnabled and OperationsArchiveDirectory.");
        return ArchiveOlderThan(age, options.ResolveOperationsArchiveDirectory(),
            options.ResolveOperationsLogDirectory(), VestigiumLoggerOptions.OperationsAppId);
    }

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
        var ring = ResolveRing();
        var archived = 0;
        var deleted = 0;
        var failed = 0;
        var manifest = new List<string>();
        foreach (var file in Directory.GetFiles(dir, $"vestigium-{id}-*.json"))
        {
            if (!TryArchiveFile(file, archiveDirectory, cutoff, active, ring, out var name))
            {
                if (name is not null) failed++;
                continue;
            }
            archived++; deleted++; manifest.Add(name);
        }
        return new VestigiumArchiveResult(archived, deleted, failed, manifest);
    }

    private static VestigiumSealKeyRing? ResolveRing()
    {
        if (!VestigiumLogger.IsInitialized || !VestigiumLogger.Options.LogSealEnabled)
            return null;
        return VestigiumSealKeyRing.OpenOrCreate(VestigiumLogger.Options);
    }

    private static bool TryArchiveFile(string file, string archiveDirectory, DateTime cutoff, string? active,
        VestigiumSealKeyRing? ring, out string? name)
    {
        name = null;
        if (VestigiumLogPaths.IsLiveFile(file, active)) return false;
        if (VestigiumLogJanitor.FileDate(file) >= cutoff) return false;
        name = Path.GetFileName(file);
        if (ring is not null && !AcceptSeal(file, ring)) return false;
        var dest = Path.Combine(archiveDirectory, name);
        try
        {
            File.Copy(file, dest, overwrite: true);
            var sourceHash = HashFile(file);
            if (!string.Equals(sourceHash, HashFile(dest), StringComparison.Ordinal)) return false;
            File.WriteAllText(dest + ".sha256", $"{sourceHash}  {name}\n", Encoding.ASCII);
            File.Delete(file);
            VestigiumLogger.WriteOps(5030, VestigiumStatus.Success, "Log file archived.",
                new Dictionary<string, string?> { ["source"] = file, ["dest"] = dest, ["sha256"] = sourceHash, ["keyId"] = ring?.KeyId });
            return true;
        }
        catch (IOException) { return false; }
    }

    private static bool AcceptSeal(string file, VestigiumSealKeyRing ring)
    {
        var report = VestigiumLogSeal.Verify(file, ring);
        if (report.Result is VestigiumSealVerifyResult.Valid or VestigiumSealVerifyResult.NoTrailer)
            return true;
        VestigiumLogger.WriteOps(5035, VestigiumStatus.Failed, "Archive skipped; seal check failed.",
            new Dictionary<string, string?> { ["path"] = file, ["result"] = report.Result.ToString(), ["keyId"] = report.KeyId });
        return false;
    }

    internal static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
