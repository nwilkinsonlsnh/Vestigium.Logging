namespace Vestigium.Logging;

/// <summary>Operational outcome of the logged action. Independent of <see cref="VestigiumLogLevel"/>.</summary>
public enum VestigiumStatus
{
    None = 0,
    Pending = 1,
    Success = 2,
    Timeout = 3,
    Failed = 4
}
