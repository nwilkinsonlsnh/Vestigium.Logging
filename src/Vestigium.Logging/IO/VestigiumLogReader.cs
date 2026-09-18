namespace Vestigium.Logging;

public static class VestigiumLogReader
{
    public static IReadOnlyList<string> Head(int count, string? directory = null, string? appId = null)
    {
        var (dir, id, take) = Resolve(count, directory, appId);
        var result = new List<string>(take);
        foreach (var file in EnumerateFiles(dir, id))
        {
            foreach (var line in ReadCompleteLines(file))
            {
                result.Add(line);
                if (result.Count >= take) return result;
            }
        }
        return result;
    }

    public static IReadOnlyList<string> Tail(int count, string? directory = null, string? appId = null)
    {
        var (dir, id, take) = Resolve(count, directory, appId);
        var chunks = new Stack<string[]>();
        var have = 0;
        foreach (var file in EnumerateFiles(dir, id).Reverse())
        {
            var lines = ReadCompleteLines(file);
            if (lines.Count == 0) continue;
            if (have + lines.Count <= take)
            {
                chunks.Push(lines.ToArray());
                have += lines.Count;
            }
            else
            {
                var need = take - have;
                chunks.Push(lines.Skip(lines.Count - need).ToArray());
                have = take;
            }
            if (have >= take) break;
        }
        var result = new List<string>(have);
        while (chunks.Count > 0) result.AddRange(chunks.Pop());
        return result;
    }

    private static (string Directory, string AppId, int Take) Resolve(int count, string? directory, string? appId)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");
        string dir;
        string id;
        var max = 1_000;
        if (VestigiumLogger.IsInitialized)
        {
            var options = VestigiumLogger.Options;
            dir = string.IsNullOrWhiteSpace(directory) ? options.ResolveLogDirectory() : directory;
            id = string.IsNullOrWhiteSpace(appId) ? options.AppId : appId;
            max = Math.Max(1, options.LogReadMaxLines);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(appId))
                throw new InvalidOperationException("directory and appId are required when the logger is not initialized.");
            dir = directory!;
            id = appId!;
        }
        return (dir, id, Math.Min(count, max));
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string appId)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var file in Directory.GetFiles(directory, $"vestigium-{appId}-*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            yield return file;
    }

    internal static IReadOnlyList<string> ReadCompleteLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        if (text.Length == 0) return [];
        var torn = text[^1] is not '\n' and not '\r';
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (torn && lines.Length > 0) lines = lines[..^1];
        return lines.Where(static l => l.Length > 0).ToArray();
    }
}
