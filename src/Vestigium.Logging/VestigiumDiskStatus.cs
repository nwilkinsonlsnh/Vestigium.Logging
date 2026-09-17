namespace Vestigium.Logging;

/// <summary>Snapshot of the log-volume tripwire. Safe to read when the host is not initialized.</summary>
public sealed record VestigiumDiskStatus(
    bool IsTripped,
    bool IsOverridden,
    string? Drive,
    long? AvailableBytes,
    int PercentThreshold,
    long BytesFloor)
{
    public static VestigiumDiskStatus Empty { get; } = new(false, false, null, null, 0, 0);
}
