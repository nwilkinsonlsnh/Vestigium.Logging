using Vestigium.Logging;

namespace Vestigium.Logging.Demo;

public sealed class LogBatchMessage
{
    public required IReadOnlyList<VestigiumLogEvent> Events { get; init; }
}
