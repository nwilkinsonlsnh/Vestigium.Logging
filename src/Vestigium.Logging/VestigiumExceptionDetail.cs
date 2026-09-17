namespace Vestigium.Logging;

/// <summary>How much of an exception is written to the JSON <c>EXCEPTION</c> field.</summary>
public enum VestigiumExceptionDetail
{
    Full = 0,
    TypeAndMessage = 1,
    None = 2
}
