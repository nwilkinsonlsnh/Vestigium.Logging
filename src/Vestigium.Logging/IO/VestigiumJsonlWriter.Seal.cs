using System.Security.Cryptography;
using System.Text.Json;

namespace Vestigium.Logging;

internal sealed partial class VestigiumJsonlWriter
{
    private VestigiumSealKeyRing? _ring;
    private int _lineCount;
    internal Action<int, string, IReadOnlyDictionary<string, string?>?>? OpsSink { get; set; }

    internal void AttachSeal(VestigiumSealKeyRing ring, Action<int, string, IReadOnlyDictionary<string, string?>?>? opsSink = null)
    {
        _ring = ring;
        if (opsSink is not null)
            OpsSink = opsSink;
    }

    private void TryAppendTrailer(string? path, int lineCount)
    {
        if (_ring is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        try
        {
            var content = File.ReadAllBytes(path);
            if (content.Length == 0)
                return;
            var sha = Convert.ToHexString(SHA256.HashData(content));
            var sig = Convert.ToBase64String(_ring.Sign(content));
            var trailer = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["VESTIGIUM_TRAILER"] = 1,
                ["LineCount"] = lineCount,
                ["ContentSha256"] = sha,
                ["KeyId"] = _ring.KeyId,
                ["Alg"] = _ring.Alg,
                ["UtcClosed"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["Sig"] = sig
            });
            File.AppendAllText(path, trailer + "\n");
            OpsSink?.Invoke(5015, "Log file sealed.", new Dictionary<string, string?>
            {
                ["path"] = path,
                ["sha256"] = sha,
                ["keyId"] = _ring.KeyId,
                ["lineCount"] = lineCount.ToString()
            });
        }
        catch (Exception ex)
        {
            OpsSink?.Invoke(5020, "Seal failed.", new Dictionary<string, string?>
            {
                ["path"] = path,
                ["error"] = ex.GetType().Name
            });
        }
    }
}
