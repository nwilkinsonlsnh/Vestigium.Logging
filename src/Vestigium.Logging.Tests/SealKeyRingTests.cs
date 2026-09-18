namespace Vestigium.Logging.Tests;

public sealed class SealKeyRingTests
{
    [Fact]
    public void OpenCreatesThenReloadsSameKeyId()
    {
        var path = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "pingiq.json");
        var first = VestigiumSealKeyRing.Open(path);
        var second = VestigiumSealKeyRing.Open(path);
        Assert.Equal(first.KeyId, second.KeyId);
        Assert.True(Guid.TryParse(first.KeyId, out _));
        Assert.Equal(VestigiumSealKeyRing.Algorithm, first.Alg);
        Assert.Equal(32, first.Material.Length);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SignAndVerifyRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "ring.json");
        var ring = VestigiumSealKeyRing.Open(path);
        var payload = "hello-seal"u8.ToArray();
        var sig = ring.Sign(payload);
        Assert.True(ring.Verify(payload, sig));
        payload[0] ^= 0xFF;
        Assert.False(ring.Verify(payload, sig));
    }

    [Fact]
    public void OpenOrCreateRejectsMismatchedKeyId()
    {
        var path = Path.Combine(Path.GetTempPath(), "vestigium-keys", Guid.NewGuid().ToString("N"), "ring.json");
        var ring = VestigiumSealKeyRing.Open(path);
        var options = new VestigiumLoggerOptions
        {
            AppId = "PingIQ",
            LogSealEnabled = true,
            LogSealKeyPath = path,
            LogSealKeyId = Guid.NewGuid().ToString("D")
        };
        Assert.Throws<InvalidOperationException>(() => VestigiumSealKeyRing.OpenOrCreate(options));
        options.LogSealKeyId = ring.KeyId;
        Assert.Equal(ring.KeyId, VestigiumSealKeyRing.OpenOrCreate(options).KeyId);
    }

    [Fact]
    public void SealOptionsDefaultOff()
    {
        var options = new VestigiumLoggerOptions();
        Assert.False(options.LogSealEnabled);
        Assert.Contains(Path.Combine("Vestigium", "Logging", "keys"), VestigiumSealKeyRing.DefaultPath("PingIQ"));
    }
}
