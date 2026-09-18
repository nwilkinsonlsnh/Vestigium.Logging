using System.Collections.Concurrent;

namespace Vestigium.Logging;

internal static class UncataloguedExceptionAdvisor
{
    public const int CatalogRecommendedAt = 20;
    private static readonly ConcurrentDictionary<string, byte> Seen = new(StringComparer.Ordinal);
    internal static int DistinctCount => Seen.Count;
    internal static void Reset() => Seen.Clear();

    public static void Note(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return;
        if (!Seen.TryAdd(fullName, 0)) return;
        var count = Seen.Count;
        VestigiumLog.Debug(0, VestigiumStatus.None, "System", "Configuration",
            $"Uncatalogued exception {fullName}. Logged as EVENTID 3. Occasional custom types are fine.");
        if (count == CatalogRecommendedAt)
        {
            VestigiumLog.Debug(0, VestigiumStatus.None, "System", "Configuration",
                $"{CatalogRecommendedAt} uncatalogued exception types in this process. Create a VestigiumCustomCatalog (EVENTID 5000+) and LoadCustomCatalog.");
        }
    }
}
