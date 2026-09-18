using System.Reflection;
using System.Text.Json;

namespace Vestigium.Logging;

/// <summary>
/// In-memory Event ID catalog. Embedded snapshot is 0–4999.
/// Host custom rows start at <see cref="CustomMin"/> (5000).
/// </summary>
public sealed class VestigiumEventCatalog
{
    public const int ReservedMax = 4999;
    public const int CustomMin = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<int, VestigiumEventDefinition> _byId;
    private readonly Dictionary<string, VestigiumEventDefinition> _byFullName;

    public VestigiumEventCatalog(
        IEnumerable<VestigiumEventDefinition> rows,
        bool allowCustom = false)
    {
        _byId = new Dictionary<int, VestigiumEventDefinition>();
        _byFullName = new Dictionary<string, VestigiumEventDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
            Add(row, allowCustom);

        var maxReserved = _byId.Keys.Where(id => id >= 100 && id <= ReservedMax).DefaultIfEmpty(95).Max();
        NextReservedId = maxReserved < 100 ? 100 : maxReserved + 5;
        if (NextReservedId > ReservedMax)
            NextReservedId = ReservedMax + 1;
        var maxCustom = _byId.Keys.Where(id => id >= CustomMin).DefaultIfEmpty(CustomMin - 5).Max();
        NextCustomId = maxCustom < CustomMin ? CustomMin : maxCustom + 5;
    }

    public int Count => _byId.Count;
    public int NextReservedId { get; }
    public int NextCustomId { get; }
    public IReadOnlyCollection<VestigiumEventDefinition> All => _byId.Values;

    public bool TryGetById(int eventId, out VestigiumEventDefinition definition)
    {
        if (_byId.TryGetValue(eventId, out var row) && row.Enabled)
        {
            definition = row;
            return true;
        }
        definition = null!;
        return false;
    }

    public bool TryGetByFullName(string? fullName, out VestigiumEventDefinition definition)
    {
        definition = null!;
        if (string.IsNullOrWhiteSpace(fullName))
            return false;
        if (_byFullName.TryGetValue(fullName, out var row) && row.Enabled)
        {
            definition = row;
            return true;
        }
        return false;
    }

    public bool TryGetByException(Exception? exception, out VestigiumEventDefinition definition)
    {
        definition = null!;
        var type = exception?.GetType();
        while (type is not null && type != typeof(object))
        {
            if (type.FullName is { } name && TryGetByFullName(name, out definition))
                return true;
            type = type.BaseType;
        }
        return false;
    }

    /// <summary>Generals 0–14 plus any embedded EventCatalog JSON on <paramref name="assembly"/>.</summary>
    public static VestigiumEventCatalog LoadDefault(Assembly? assembly = null)
    {
        assembly ??= typeof(VestigiumEventCatalog).Assembly;
        var rows = new List<VestigiumEventDefinition>(SeedGeneral());
        rows.AddRange(ReadEmbeddedShards(assembly));
        return new VestigiumEventCatalog(rows, allowCustom: false);
    }

    public static VestigiumEventCatalog LoadFromDirectory(string root, bool allowCustom = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var rows = new List<VestigiumEventDefinition>(SeedGeneral());
        foreach (var dir in new[] { Path.Combine(root, "shards"), Path.Combine(root, "Shards") })
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                rows.AddRange(ReadShardJson(File.ReadAllText(file), file));
        }
        return new VestigiumEventCatalog(rows, allowCustom);
    }

    internal static IReadOnlyList<VestigiumEventDefinition> SeedGeneral() =>
    [
        Row(0,  "General.Debug",       "Debug",       "Generic debug message."),
        Row(1,  "General.Information", "Information", "Generic information message."),
        Row(2,  "General.Warning",     "Warning",     "Generic warning message."),
        Row(3,  "General.Error",       "Error",       "Generic error message."),
        Row(4,  "General.Fatal",       "Fatal",       "Generic fatal message."),
        Row(5,  "General.Start",       "Information", "Host, probe, or batch started."),
        Row(6,  "General.Stop",        "Information", "Host, probe, or batch stopped."),
        Row(7,  "General.Heartbeat",   "Debug",       "Liveness pulse."),
        Row(8,  "General.Timeout",     "Information", "Operational timeout."),
        Row(9,  "General.Retry",       "Warning",     "Retry or backoff."),
        Row(10, "General.Cancelled",   "Information", "Cooperative cancel."),
        Row(11, "General.Config",      "Information", "Configuration loaded or rejected."),
        Row(12, "General.NotFound",    "Warning",     "Business miss (not FileNotFoundException)."),
        Row(13, "General.Denied",      "Warning",     "Authorization or policy denied."),
        Row(14, "General.Throttle",    "Warning",     "Rate limit or disk tripwire.")
    ];

    private void Add(VestigiumEventDefinition row, bool allowCustom)
    {
        if (row.EventId < 0)
            throw new InvalidOperationException($"EventId {row.EventId} is invalid.");
        if (!allowCustom && row.EventId >= CustomMin)
            throw new InvalidOperationException(
                $"Embedded catalog EventId {row.EventId} ({row.FullName}) is >= {CustomMin}. Reserved range is 0–{ReservedMax}.");
        if (allowCustom && row.Kind.Equals("Custom", StringComparison.OrdinalIgnoreCase) && row.EventId < CustomMin)
            throw new InvalidOperationException(
                $"Custom EventId {row.EventId} ({row.FullName}) must be >= {CustomMin}.");

        if (_byId.TryGetValue(row.EventId, out var existing))
        {
            if (string.Equals(existing.FullName, row.FullName, StringComparison.Ordinal))
                return;
            throw new InvalidOperationException(
                $"EventId {row.EventId} is assigned to both '{existing.FullName}' and '{row.FullName}'.");
        }

        _byId[row.EventId] = row;
        if (!string.IsNullOrWhiteSpace(row.FullName) && !_byFullName.ContainsKey(row.FullName))
            _byFullName[row.FullName] = row;
    }

    private static VestigiumEventDefinition Row(int id, string name, string severity, string description) =>
        new(id, name, "Vestigium.Logging." + name, "System", "Lifecycle", severity, "General",
            Enabled: true, Namespace: "Vestigium.Logging", Description: description);

    private static IEnumerable<VestigiumEventDefinition> ReadEmbeddedShards(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.IndexOf("EventCatalog", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;
            using var reader = new StreamReader(stream);
            foreach (var row in ReadShardJson(reader.ReadToEnd(), name))
                yield return row;
        }
    }

    private static IEnumerable<VestigiumEventDefinition> ReadShardJson(string json, string source)
    {
        List<Dto>? list;
        try { list = JsonSerializer.Deserialize<List<Dto>>(json, JsonOptions); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Event catalog shard '{source}' is not valid JSON.", ex);
        }
        if (list is null)
            yield break;
        foreach (var dto in list)
        {
            if (dto.EventId is null || string.IsNullOrWhiteSpace(dto.FullName))
                continue;
            yield return new VestigiumEventDefinition(
                dto.EventId.Value,
                dto.EventName ?? dto.FullName,
                dto.FullName,
                dto.Category ?? "System",
                dto.Subcategory ?? "Core",
                dto.Severity ?? "Error",
                string.IsNullOrWhiteSpace(dto.Kind) ? "Exception" : dto.Kind,
                dto.Enabled ?? true,
                dto.Namespace,
                dto.Description);
        }
    }

    private sealed class Dto
    {
        public int? EventId { get; set; }
        public string? EventName { get; set; }
        public string? FullName { get; set; }
        public string? Category { get; set; }
        public string? Subcategory { get; set; }
        public string? Severity { get; set; }
        public string? Kind { get; set; }
        public bool? Enabled { get; set; }
        public string? Namespace { get; set; }
        public string? Description { get; set; }
    }
}
