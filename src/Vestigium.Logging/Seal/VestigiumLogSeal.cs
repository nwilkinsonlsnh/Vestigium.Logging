using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vestigium.Logging;

public enum VestigiumSealVerifyResult
{
    Valid,
    NoTrailer,
    Torn,
    HashMismatch,
    BadSignature,
    MissingFile,
    KeyMismatch
}

public readonly record struct VestigiumSealVerifyReport(
    VestigiumSealVerifyResult Result,
    string? Path,
    string? KeyId,
    string? ContentSha256,
    int? LineCount);

public static class VestigiumLogSeal
{
    public static VestigiumSealVerifyReport Verify(string path, VestigiumSealKeyRing ring)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(ring);
        if (!File.Exists(path))
            return new(VestigiumSealVerifyResult.MissingFile, path, null, null, null);

        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytes = new byte[stream.Length];
            if (bytes.Length > 0)
                stream.ReadExactly(bytes);
        }

        if (bytes.Length == 0)
            return new(VestigiumSealVerifyResult.NoTrailer, path, null, null, null);

        if (!TryPeelTrailer(bytes, out var content, out var trailerJson))
            return new(VestigiumSealVerifyResult.NoTrailer, path, null, null, null);

        Dictionary<string, JsonElement>? trailer;
        try { trailer = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(trailerJson); }
        catch (JsonException)
        {
            return new(VestigiumSealVerifyResult.Torn, path, null, null, null);
        }

        if (trailer is null || !trailer.ContainsKey("VESTIGIUM_TRAILER"))
            return new(VestigiumSealVerifyResult.NoTrailer, path, null, null, null);

        var keyId = GetString(trailer, "KeyId");
        var claimed = GetString(trailer, "ContentSha256");
        var sig = GetString(trailer, "Sig");
        int? lines = null;
        if (trailer.TryGetValue("LineCount", out var nEl)
            && nEl.ValueKind == JsonValueKind.Number
            && nEl.TryGetInt32(out var n))
            lines = n;

        if (!string.IsNullOrWhiteSpace(keyId)
            && !string.Equals(keyId, ring.KeyId, StringComparison.OrdinalIgnoreCase))
            return new(VestigiumSealVerifyResult.KeyMismatch, path, keyId, claimed, lines);

        var actual = Convert.ToHexString(SHA256.HashData(content));
        if (!string.Equals(actual, claimed, StringComparison.OrdinalIgnoreCase))
            return new(VestigiumSealVerifyResult.HashMismatch, path, keyId, actual, lines);

        if (string.IsNullOrWhiteSpace(sig))
            return new(VestigiumSealVerifyResult.BadSignature, path, keyId, actual, lines);

        byte[] sigBytes;
        try { sigBytes = Convert.FromBase64String(sig); }
        catch (FormatException)
        {
            return new(VestigiumSealVerifyResult.BadSignature, path, keyId, actual, lines);
        }

        if (!ring.Verify(content, sigBytes))
            return new(VestigiumSealVerifyResult.BadSignature, path, keyId, actual, lines);

        return new(VestigiumSealVerifyResult.Valid, path, keyId, actual, lines);
    }

    private static string? GetString(Dictionary<string, JsonElement> trailer, string name) =>
        trailer.TryGetValue(name, out var el) ? el.GetString() : null;

    internal static bool TryPeelTrailer(byte[] bytes, out byte[] content, out string trailerJson)
    {
        content = bytes;
        trailerJson = "";
        var end = bytes.Length;
        if (end > 0 && bytes[end - 1] == (byte)'\n')
            end--;
        var start = end;
        while (start > 0 && bytes[start - 1] != (byte)'\n')
            start--;
        trailerJson = Encoding.UTF8.GetString(bytes, start, end - start).TrimEnd('\r');
        if (!trailerJson.Contains("VESTIGIUM_TRAILER", StringComparison.Ordinal))
            return false;
        content = bytes[..start];
        return true;
    }
}
