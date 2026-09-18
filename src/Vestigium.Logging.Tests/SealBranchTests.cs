namespace Vestigium.Logging.Tests;

public sealed class SealBranchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vestigium-seal-b-" + Guid.NewGuid().ToString("N"));

    public SealBranchTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void PeeledLineWithoutTrailerKeyIsNoTrailer()
    {
        var ring = VestigiumSealKeyRing.Open(Path.Combine(_dir, "ring.json"));
        var path = Path.Combine(_dir, "looks-like.json");
        File.WriteAllText(path, "{\"EVENTID\":1}\n{\"note\":\"VESTIGIUM_TRAILER\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.NoTrailer, VestigiumLogSeal.Verify(path, ring).Result);
    }

    [Fact]
    public void LineCountMissingAndNonInteger()
    {
        var ring = VestigiumSealKeyRing.Open(Path.Combine(_dir, "ring.json"));
        var content = "{\"EVENTID\":1}\n"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var sig = Convert.ToBase64String(ring.Sign(content));
        var noCount = Path.Combine(_dir, "nocount.json");
        File.WriteAllText(noCount,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"" + ring.KeyId +
            "\",\"ContentSha256\":\"" + hash + "\",\"Sig\":\"" + sig + "\"}\n");
        var ok = VestigiumLogSeal.Verify(noCount, ring);
        Assert.Equal(VestigiumSealVerifyResult.Valid, ok.Result);
        Assert.Null(ok.LineCount);

        var badCount = Path.Combine(_dir, "badcount.json");
        File.WriteAllText(badCount,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"" + ring.KeyId +
            "\",\"ContentSha256\":\"" + hash + "\",\"Sig\":\"" + sig + "\",\"LineCount\":\"x\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.Valid, VestigiumLogSeal.Verify(badCount, ring).Result);
    }

    [Fact]
    public void PeelWithoutTrailingNewline()
    {
        var payload = "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1}"u8.ToArray();
        Assert.True(VestigiumLogSeal.TryPeelTrailer(payload, out var content, out var json));
        Assert.True(content.Length > 0);
        Assert.Contains("VESTIGIUM_TRAILER", json);

        Assert.False(VestigiumLogSeal.TryPeelTrailer([] , out _, out _));
    }

    [Fact]
    public void EmptyKeyIdSkipsMismatch()
    {
        var ring = VestigiumSealKeyRing.Open(Path.Combine(_dir, "ring.json"));
        var content = "{\"EVENTID\":1}\n"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var sig = Convert.ToBase64String(ring.Sign(content));
        var path = Path.Combine(_dir, "emptykey.json");
        File.WriteAllText(path,
            "{\"EVENTID\":1}\n{\"VESTIGIUM_TRAILER\":1,\"KeyId\":\"\",\"ContentSha256\":\"" +
            hash + "\",\"Sig\":\"" + sig + "\"}\n");
        Assert.Equal(VestigiumSealVerifyResult.Valid, VestigiumLogSeal.Verify(path, ring).Result);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
