using System.Text.RegularExpressions;

namespace Vestigium.Logging;

internal static class VestigiumPropertyBag
{
    public const int MaxEntries = 16;
    public const int MaxValueChars = 256;

    private static readonly Regex LegalKey = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string>? Sanitize(IReadOnlyDictionary<string, string?>? source)
    {
        if (source is null || source.Count == 0)
            return null;

        Dictionary<string, string>? result = null;
        foreach (var (key, value) in source)
        {
            if (value is null || !LegalKey.IsMatch(key))
                continue;

            result ??= new Dictionary<string, string>(MaxEntries, StringComparer.Ordinal);
            if (result.ContainsKey(key) || result.Count >= MaxEntries)
                continue;

            result[key] = Truncate(value, MaxValueChars);
        }

        return result is { Count: > 0 } ? result : null;
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;
        return value[..max];
    }
}
