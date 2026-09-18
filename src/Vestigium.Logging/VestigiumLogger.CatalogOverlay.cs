namespace Vestigium.Logging;

public static partial class VestigiumLogger
{
    public static string? CustomCatalogPath => _host?.Options.EventCatalogPath;

    public static void LoadCustomCatalog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var host = Require();
        host.Catalog.ReplaceCustomFromDirectory(path);
        host.Options.EventCatalogPath = path;
        host.EmitOps(5065, VestigiumStatus.Success, "Custom catalog loaded.",
            new Dictionary<string, string?> { ["path"] = path, ["count"] = host.Catalog.Count.ToString() });
    }

    public static void UnloadCustomCatalog()
    {
        var host = Require();
        host.Catalog.ClearCustom();
        host.Options.EventCatalogPath = null;
        host.EmitOps(5070, VestigiumStatus.Success, "Custom catalog unloaded.");
    }
}
