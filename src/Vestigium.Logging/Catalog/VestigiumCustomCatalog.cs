using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vestigium.Logging;

public sealed class VestigiumCustomCatalog
{
    public const int CustomMin = VestigiumEventCatalog.CustomMin;
    private static readonly JsonSerializerOptions JsonWrite = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<int, VestigiumEventDefinition> _byId;
    private readonly Dictionary<string, VestigiumEventDefinition> _byFullName;

    private VestigiumCustomCatalog(string root, IReadOnlyList<VestigiumEventDefinition> rows)
    {
        Root = root;
        ShardsDirectory = Path.Combine(root, "shards");
        _byId = rows.ToDictionary(r => r.EventId);
        _byFullName = rows.Where(r => !string.IsNullOrWhiteSpace(r.FullName))
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var max = rows.Select(r => r.EventId).DefaultIfEmpty(CustomMin - 5).Max();
        NextCustomId = max < CustomMin ? CustomMin : max + 5;
    }

    public string Root { get; }
    public string ShardsDirectory { get; }
    public int Count => _byId.Count;
    public int NextCustomId { get; private set; }
    public IReadOnlyCollection<VestigiumEventDefinition> All => _byId.Values;

    public static VestigiumCustomCatalog Open(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "shards"));
        var rows = VestigiumEventCatalog.ReadDirectoryRows(root).ToList();
        foreach (var row in rows)
        {
            if (row.EventId < CustomMin)
                throw new InvalidOperationException($"Custom catalog EventId {row.EventId} ({row.FullName}) must be >= {CustomMin}.");
        }
        return new VestigiumCustomCatalog(root, rows);
    }

    public bool TryGet(int eventId, out VestigiumEventDefinition definition) =>
        _byId.TryGetValue(eventId, out definition!);

    public VestigiumEventDefinition Get(int eventId) =>
        TryGet(eventId, out var row) ? row
            : throw new InvalidOperationException($"EVENTID {eventId} is not in the custom catalog.");

    public bool TryGetByFullName(string? fullName, out VestigiumEventDefinition definition)
    {
        definition = null!;
        return !string.IsNullOrWhiteSpace(fullName) && _byFullName.TryGetValue(fullName, out definition);
    }

    public IReadOnlyList<VestigiumEventDefinition> List() => _byId.Values.OrderBy(r => r.EventId).ToList();

    public VestigiumEventDefinition Add(string eventName, string fullName, string category, string subcategory,
        int? eventId = null, string severity = "Error", string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(subcategory);
        var id = eventId ?? NextCustomId;
        if (id < CustomMin) throw new InvalidOperationException($"Custom EventId {id} must be >= {CustomMin}.");
        if (_byId.ContainsKey(id)) throw new InvalidOperationException($"EventId {id} is already assigned.");
        if (_byFullName.ContainsKey(fullName)) throw new InvalidOperationException($"FullName '{fullName}' is already assigned.");
        var row = new VestigiumEventDefinition(id, eventName, fullName, category, subcategory, severity, "Custom", true, Description: description);
        _byId[id] = row;
        _byFullName[fullName] = row;
        if (id >= NextCustomId) NextCustomId = id + 5;
        return row;
    }

    public VestigiumEventDefinition Set(int eventId, string? eventName = null, string? fullName = null,
        string? category = null, string? subcategory = null, string? severity = null,
        string? description = null, bool? enabled = null)
    {
        var current = Get(eventId);
        var nextFull = fullName ?? current.FullName;
        if (!string.Equals(nextFull, current.FullName, StringComparison.Ordinal) && _byFullName.ContainsKey(nextFull))
            throw new InvalidOperationException($"FullName '{nextFull}' is already assigned.");
        var row = current with
        {
            EventName = eventName ?? current.EventName,
            FullName = nextFull,
            Category = category ?? current.Category,
            Subcategory = subcategory ?? current.Subcategory,
            Severity = severity ?? current.Severity,
            Description = description ?? current.Description,
            Enabled = enabled ?? current.Enabled
        };
        _byId[eventId] = row;
        if (!string.Equals(nextFull, current.FullName, StringComparison.Ordinal))
        {
            _byFullName.Remove(current.FullName);
            _byFullName[nextFull] = row;
        }
        else _byFullName[current.FullName] = row;
        return row;
    }

    public bool Remove(int eventId)
    {
        if (!_byId.Remove(eventId, out var row)) return false;
        _byFullName.Remove(row.FullName);
        return true;
    }

    public void Save()
    {
        Directory.CreateDirectory(ShardsDirectory);
        var customPath = Path.Combine(ShardsDirectory, "custom.json");
        var payload = List().Select(r => new
        {
            r.EventId, r.EventName, r.FullName, r.Category, r.Subcategory,
            r.Severity, r.Kind, r.Enabled, r.Namespace, r.Description
        }).ToList();
        WriteAtomic(customPath, JsonSerializer.Serialize(payload, JsonWrite));
        foreach (var extra in Directory.EnumerateFiles(ShardsDirectory, "*.json"))
        {
            if (!string.Equals(Path.GetFileName(extra), "custom.json", StringComparison.OrdinalIgnoreCase))
                File.Delete(extra);
        }
        WriteAtomic(Path.Combine(Root, "index.json"), JsonSerializer.Serialize(new
        {
            NextCustomId,
            Shards = new[] { new { Id = "custom", File = "shards/custom.json", Count } }
        }, JsonWrite));
    }

    private static void WriteAtomic(string path, string json)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
