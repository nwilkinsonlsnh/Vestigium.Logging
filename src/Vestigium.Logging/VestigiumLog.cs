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
        string? appId = null) =>
        VestigiumLogger.Emit(level, status, category, subcategory, message, exception, appId);


    public static void Verbose(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Verbose, status, category, subcategory, message, exception);

    public static void Debug(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Debug, status, category, subcategory, message, exception);

    public static void Information(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Information, status, category, subcategory, message, exception);

    public static void Warning(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Warning, status, category, subcategory, message, exception);

    public static void Error(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Error, status, category, subcategory, message, exception);

    public static void Fatal(VestigiumStatus status, string category, string subcategory, string message, Exception? exception = null) =>
        Write(VestigiumLogLevel.Fatal, status, category, subcategory, message, exception);
}
