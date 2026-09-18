namespace Vestigium.Logging;

/// <summary>Host-owned event catalog on disk. Rows must be EventId 5000+.</summary>
public sealed class VestigiumCustomCatalog
{
    public const int CustomMin = VestigiumEventCatalog.CustomMin;

    private readonly Dictionary<int, VestigiumEventDefinition> _byId;
    private readonly Dictionary<string, VestigiumEventDefinition> _byFullName;

    private VestigiumCustomCatalog(string root, IReadOnlyList<VestigiumEventDefinition> rows)
    {
        Root = root;
        ShardsDirectory = Path.Combine(root, "shards");
        _byId = rows.ToDictionary(r => r.EventId);
        _byFullName = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.FullName))
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
                throw new InvalidOperationException(
                    $"Custom catalog EventId {row.EventId} ({row.FullName}) must be >= {CustomMin}.");
        }

        return new VestigiumCustomCatalog(root, rows);
    }

    public bool TryGet(int eventId, out VestigiumEventDefinition definition) =>
        _byId.TryGetValue(eventId, out definition!);

    public bool TryGetByFullName(string? fullName, out VestigiumEventDefinition definition)
    {
        definition = null!;
        return !string.IsNullOrWhiteSpace(fullName) && _byFullName.TryGetValue(fullName, out definition);
    }
}
