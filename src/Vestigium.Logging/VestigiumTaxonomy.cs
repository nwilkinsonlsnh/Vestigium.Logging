namespace Vestigium.Logging;

/// <summary>Registered category catalog used to keep PowerBI groupings stable.</summary>
public sealed class VestigiumTaxonomy
{
    public const string Uncategorized = "Uncategorized";
    public const string Unregistered = "Unregistered";
    public const string InternalAppId = "Vestigium.Logging";

    private readonly Dictionary<string, HashSet<string>> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public static VestigiumTaxonomy Defaults { get; } = CreateDefaults();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot =>
        _map.ToDictionary(
            static p => p.Key,
            static p => (IReadOnlyList<string>)p.Value.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    public void Register(string category, params string[] subcategories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!_map.TryGetValue(category, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _map[category] = set;
        }

        foreach (var sub in subcategories)
        {
            if (!string.IsNullOrWhiteSpace(sub))
                set.Add(sub);
        }
    }

    public static VestigiumTaxonomy Combine(params VestigiumTaxonomy[] sources)
    {
        var combined = new VestigiumTaxonomy();
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            foreach (var pair in source.Snapshot)
                combined.Register(pair.Key, pair.Value.ToArray());
        }

        return combined;
    }

    public bool IsCategoryRegistered(string category) =>
        !string.IsNullOrWhiteSpace(category) && _map.ContainsKey(category);

    public bool IsSubcategoryRegistered(string category, string subcategory) =>
        _map.TryGetValue(category, out var set) && set.Contains(subcategory);

    public (string Category, string Subcategory, bool Rewritten) Normalize(string category, string subcategory)
    {
        var rewritten = false;

        string cat;
        if (string.IsNullOrWhiteSpace(category))
        {
            cat = Uncategorized;
            rewritten = true;
        }
        else if (TryCanonicalCategory(category, out var canonicalCat, out _))
        {
            cat = canonicalCat;
        }
        else
        {
            cat = Uncategorized;
            rewritten = true;
        }

        string sub;
        if (string.IsNullOrWhiteSpace(subcategory))
        {
            sub = Unregistered;
            rewritten = true;
        }
        else if (TryCanonicalCategory(cat, out _, out var set) && set.Contains(subcategory))
        {
            sub = CanonicalMember(set, subcategory);
        }
        else
        {
            sub = Unregistered;
            rewritten = true;
        }

        return (cat, sub, rewritten);
    }

    private bool TryCanonicalCategory(string category, out string canonical, out HashSet<string> set)
    {
        if (_map.TryGetValue(category, out set!))
        {
            canonical = CanonicalKey(category);
            return true;
        }

        canonical = category;
        set = null!;
        return false;
    }

    private string CanonicalKey(string category)
    {
        foreach (var key in _map.Keys)
        {
            if (_map.Comparer.Equals(key, category))
                return key;
        }

        return category;
    }

    private static string CanonicalMember(HashSet<string> set, string value)
    {
        foreach (var item in set)
        {
            if (set.Comparer.Equals(item, value))
                return item;
        }

        return value;
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
