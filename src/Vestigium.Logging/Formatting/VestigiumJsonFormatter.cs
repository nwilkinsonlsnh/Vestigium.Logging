using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Vestigium.Logging;

internal sealed class VestigiumJsonRecord
{
    public string DateTime { get; set; } = "";
    public int PID { get; set; }
    public int TID { get; set; }
    public string LEVEL { get; set; } = "";
    public string STATUS { get; set; } = "";
    public string APPID { get; set; } = "";
    public string CATEGORY { get; set; } = "";
    public string SUBCATEGORY { get; set; } = "";
    public string MESSAGE { get; set; } = "";
    public string? EXCEPTION { get; set; }
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
            PID = e.Pid,
            TID = e.Tid,
            LEVEL = e.Level.ToString(),
            STATUS = e.Status.ToString(),
            APPID = e.AppId,
            CATEGORY = e.Category,
            SUBCATEGORY = e.Subcategory,
            MESSAGE = e.Message,
            EXCEPTION = e.Exception
        };
        return JsonSerializer.Serialize(record, Options);
    }
}

internal sealed class VestigiumSerilogFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (!logEvent.Properties.TryGetValue("VestigiumJson", out var value))
            return;

        var json = value is ScalarValue { Value: string s } ? s : value.ToString().Trim('"');
        output.WriteLine(json);
    }
}
