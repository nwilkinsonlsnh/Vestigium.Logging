namespace Vestigium.Logging;

/// <summary>Call-site API. EVENTID and STATUS are required. LEVEL never stores an outcome.</summary>
public static class VestigiumLog
{
    public static void Write(
        int eventId,
        VestigiumLogLevel level,
        VestigiumStatus status,
        string category,
        string subcategory,
        string message,
        Exception? exception = null,
        string? appId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null) =>
        VestigiumLogger.Emit(level, status, category, subcategory, message, exception, appId, correlationId, properties, eventId);

    public static void Verbose(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Verbose, status, category, subcategory, message,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Debug(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Debug, status, category, subcategory, message,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Information(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Information, status, category, subcategory, message,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Warning(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Warning, status, category, subcategory, message, exception,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Error(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Error, status, category, subcategory, message, exception,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Fatal(
        int eventId, VestigiumStatus status, string category, string subcategory, string message,
        Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null, string? appId = null) =>
        Write(eventId, VestigiumLogLevel.Fatal, status, category, subcategory, message, exception,
            appId: appId, correlationId: correlationId, properties: properties);

    public static void Thrown(
        Exception exception,
        VestigiumStatus status,
        string? category = null,
        string? subcategory = null,
        string? message = null,
        string? appId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!VestigiumLogger.Catalog.TryGetByException(exception, out var def))
            throw new InvalidOperationException($"No catalog row for '{exception.GetType().FullName}'.");
        WriteThrown(def, exception, status, category, subcategory, message, appId, correlationId, properties);
    }

    public static void Thrown(
        Exception exception,
        VestigiumStatus status,
        int eventId,
        string? category = null,
        string? subcategory = null,
        string? message = null,
        string? appId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!VestigiumLogger.Catalog.TryGetById(eventId, out var def))
            throw new InvalidOperationException($"EVENTID {eventId} is not in the catalog or is disabled.");
        WriteThrown(def, exception, status, category, subcategory, message, appId, correlationId, properties);
    }

    private static void WriteThrown(
        VestigiumEventDefinition def,
        Exception exception,
        VestigiumStatus status,
        string? category,
        string? subcategory,
        string? message,
        string? appId,
        string? correlationId,
        IReadOnlyDictionary<string, string?>? properties)
    {
        var level = Enum.TryParse<VestigiumLogLevel>(def.Severity, ignoreCase: true, out var parsed)
            ? parsed : VestigiumLogLevel.Error;
        var cat = string.IsNullOrWhiteSpace(category) ? def.Category : category;
        var sub = string.IsNullOrWhiteSpace(subcategory) ? def.Subcategory : subcategory;
        var msg = string.IsNullOrWhiteSpace(message) ? exception.Message : message;
        Write(def.EventId, level, status, cat, sub, msg, exception, appId, correlationId, properties);
    }
}
