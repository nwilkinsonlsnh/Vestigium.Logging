namespace Vestigium.Logging;

/// <summary>Serilog-aligned severity. Never store operational outcomes here.</summary>
public enum VestigiumLogLevel
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}
