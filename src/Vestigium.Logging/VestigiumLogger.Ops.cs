namespace Vestigium.Logging;

public static partial class VestigiumLogger
{
    internal sealed partial class Host
    {
        internal void BindOpsHooks()
        {
            Disk.TripwireChanged = (tripped, status) =>
                EmitOps(5060, tripped ? VestigiumStatus.Failed : VestigiumStatus.Success,
                    tripped ? "Disk tripwire on." : "Disk tripwire off.",
                    new Dictionary<string, string?>
                    {
                        ["tripped"] = tripped.ToString(),
                        ["drive"] = status.Drive,
                        ["availableBytes"] = status.AvailableBytes?.ToString()
                    });
        }

        internal void EmitOps(
            int eventId,
            VestigiumStatus status,
            string message,
            IReadOnlyDictionary<string, string?>? properties = null)
        {
            if (_ops is null)
                return;
            if (!Catalog.TryGetById(eventId, out var row))
                return;

            var level = row.Severity switch
            {
                "Debug" => VestigiumLogLevel.Debug,
                "Warning" => VestigiumLogLevel.Warning,
                "Error" or "Fatal" or "Critical" => VestigiumLogLevel.Error,
                _ => VestigiumLogLevel.Information
            };

            var evt = new VestigiumLogEvent(
                DateTimeOffset.UtcNow,
                _pid,
                Environment.CurrentManagedThreadId,
                level,
                status,
                VestigiumLoggerOptions.OperationsAppId,
                row.Category,
                row.Subcategory,
                message,
                Exception: null,
                CorrelationId: null,
                VestigiumPropertyBag.Sanitize(properties),
                row.EventId,
                row.EventName);
            _ops.Enqueue(evt.ToJsonLine());
        }
    }
}
