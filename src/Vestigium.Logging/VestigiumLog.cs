namespace Vestigium.Logging;

/// <summary>Call-site API. STATUS is required so outcomes never leak into LEVEL.</summary>
public static class VestigiumLog
{
    public static void Write(
        VestigiumLogLevel level,
        VestigiumStatus status,
        string category,
        string subcategory,
        string message,
        Exception? exception = null,
        string? appId = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null,
        int? eventId = null) =>
        VestigiumLogger.Emit(level, status, category, subcategory, message, exception, appId, correlationId, properties, eventId);

    public static void Verbose(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Verbose, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);

    public static void Debug(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Debug, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);

    public static void Information(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Information, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);

    public static void Warning(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Warning, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);

    public static void Error(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Error, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);

    public static void Fatal(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null, string? correlationId = null, IReadOnlyDictionary<string, string?>? properties = null) =>
        Write(VestigiumLogLevel.Fatal, status, category, subcategory, message, exception, correlationId: correlationId, properties: properties);
}
