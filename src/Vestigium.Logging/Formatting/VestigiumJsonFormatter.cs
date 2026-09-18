using System.Text.Json;

namespace Vestigium.Logging;

internal sealed class VestigiumJsonRecord
{
    public string DateTime { get; set; } = "";
    public int EVENTID { get; set; }
    public string? EVENTNAME { get; set; }
    public int PID { get; set; }
    public int TID { get; set; }
    public string LEVEL { get; set; } = "";
    public string STATUS { get; set; } = "";
    public string APPID { get; set; } = "";
    public string CATEGORY { get; set; } = "";
    public string SUBCATEGORY { get; set; } = "";
    public string MESSAGE { get; set; } = "";
    public string? EXCEPTION { get; set; }
    public string? CORRELATIONID { get; set; }
    public Dictionary<string, string>? PROPERTIES { get; set; }
}

public static class VestigiumJsonFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static string Serialize(VestigiumLogEvent e)
    {
        var record = new VestigiumJsonRecord
        {
            DateTime = e.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            EVENTID = e.EventId,
            EVENTNAME = e.EventName,
            PID = e.Pid,
            TID = e.Tid,
            LEVEL = e.Level.ToString(),
            STATUS = e.Status.ToString(),
            APPID = e.AppId,
            CATEGORY = e.Category,
            SUBCATEGORY = e.Subcategory,
            MESSAGE = e.Message,
            EXCEPTION = e.Exception,
            CORRELATIONID = e.CorrelationId,
            PROPERTIES = e.Properties is { Count: > 0 } ? new Dictionary<string, string>(e.Properties) : null
        };
        return JsonSerializer.Serialize(record, Options);
    }
}
