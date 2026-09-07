namespace Vestigium.Logging;

/// <summary>Structured log record delivered to disk (as JSON Lines) and to in-process subscribers.</summary>
public sealed record VestigiumLogEvent(
    DateTimeOffset Timestamp,
    int Pid,
    int Tid,
    VestigiumLogLevel Level,
    VestigiumStatus Status,
    string AppId,
    string Category,
    string Subcategory,
    string Message,
    string? Exception)
{
    public string ToJsonLine() => VestigiumJsonFormatter.Serialize(this);
}
