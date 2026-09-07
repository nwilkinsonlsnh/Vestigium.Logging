namespace Vestigium.Logging;

/// <summary>Registered category catalog used to keep PowerBI groupings stable.</summary>
public sealed class VestigiumTaxonomy
{
    public const string Uncategorized = "Uncategorized";
    public const string Unregistered = "Unregistered";
    public const string InternalAppId = "Vestigium.Logging";

    private readonly Dictionary<string, HashSet<string>> _map =
        new(StringComparer.Ordinal);

    public static VestigiumTaxonomy Defaults { get; } = CreateDefaults();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot =>
        _map.ToDictionary(
            static p => p.Key,
            static p => (IReadOnlyList<string>)p.Value.OrderBy(static s => s, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    public void Register(string category, params string[] subcategories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!_map.TryGetValue(category, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _map[category] = set;
        }

        foreach (var sub in subcategories)
        {
            if (!string.IsNullOrWhiteSpace(sub))
                set.Add(sub);
        }
    }

    public bool IsCategoryRegistered(string category) =>
        _map.ContainsKey(category);

    public bool IsSubcategoryRegistered(string category, string subcategory) =>
        _map.TryGetValue(category, out var set) && set.Contains(subcategory);

    public (string Category, string Subcategory, bool Rewritten) Normalize(string category, string subcategory)
    {
        var cat = string.IsNullOrWhiteSpace(category) ? Uncategorized : category;
        var sub = string.IsNullOrWhiteSpace(subcategory) ? Unregistered : subcategory;
        var rewritten = false;

        if (!IsCategoryRegistered(cat))
        {
            cat = Uncategorized;
            rewritten = true;
        }

        if (!IsSubcategoryRegistered(cat, sub))
        {
            if (cat != Uncategorized && !IsCategoryRegistered(category))
                cat = Uncategorized;
            sub = Unregistered;
            rewritten = true;
        }

        return (cat, sub, rewritten);
    }

    private static VestigiumTaxonomy CreateDefaults()
    {
        var t = new VestigiumTaxonomy();
        t.Register("Network", "ICMP", "TCP", "DNS", "HTTP", "Routing");
        t.Register("System", "IO", "Memory", "Threading", "Configuration");
        t.Register("UI", "Navigation", "Binding", "Input", "Lifecycle");
        t.Register(Uncategorized, Unregistered);
        return t;
    }
}
