using System.Security.Cryptography;
using System.Text.Json;

namespace Vestigium.Logging;

public sealed class VestigiumSealKeyRing
{
    public const string Algorithm = "HMAC-SHA256";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Path { get; }
    public string KeyId { get; }
    public string Alg { get; }
    public byte[] Material { get; }

    private VestigiumSealKeyRing(string path, string keyId, string alg, byte[] material)
    {
        Path = path;
        KeyId = keyId;
        Alg = alg;
        Material = material;
    }

    public static VestigiumSealKeyRing Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = System.IO.Path.GetFullPath(path);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(full))
            return Read(full);

        var created = new VestigiumSealKeyRing(full, Guid.NewGuid().ToString("D"), Algorithm, RandomNumberGenerator.GetBytes(32));
        created.Save();
        return created;
    }

    public static VestigiumSealKeyRing OpenOrCreate(VestigiumLoggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = string.IsNullOrWhiteSpace(options.LogSealKeyPath)
            ? DefaultPath(options.AppId)
            : options.LogSealKeyPath;
        var ring = Open(path);
        if (!string.IsNullOrWhiteSpace(options.LogSealKeyId)
            && !string.Equals(options.LogSealKeyId, ring.KeyId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"LogSealKeyId '{options.LogSealKeyId}' does not match ring '{ring.KeyId}' at '{ring.Path}'.");
        }
        return ring;
    }

    public static string DefaultPath(string appId)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Vestigium");
        var id = string.IsNullOrWhiteSpace(appId) ? "Vestigium" : appId;
        return System.IO.Path.Combine(root, "Vestigium", "Logging", "keys", id + ".json");
    }

    public byte[] Sign(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return HMACSHA256.HashData(Material, content);
    }

    public bool Verify(byte[] content, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(signature);
        return CryptographicOperations.FixedTimeEquals(Sign(content), signature);
    }

    private void Save()
    {
        var dto = new Dto { KeyId = KeyId, Alg = Alg, Material = Convert.ToBase64String(Material) };
        File.WriteAllText(Path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private static VestigiumSealKeyRing Read(string path)
    {
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidOperationException($"Seal key ring '{path}' is empty.");
        if (string.IsNullOrWhiteSpace(dto.KeyId) || string.IsNullOrWhiteSpace(dto.Material))
            throw new InvalidOperationException($"Seal key ring '{path}' is missing KeyId or Material.");
        return new VestigiumSealKeyRing(path, dto.KeyId, string.IsNullOrWhiteSpace(dto.Alg) ? Algorithm : dto.Alg, Convert.FromBase64String(dto.Material));
    }

    private sealed class Dto
    {
        public string? KeyId { get; set; }
        public string? Alg { get; set; }
        public string? Material { get; set; }
    }
}
