namespace Vestigium.Logging;

/// <summary>One row in the Event ID catalog.</summary>
public sealed record VestigiumEventDefinition(
    int EventId,
    string EventName,
    string FullName,
    string Category,
    string Subcategory,
    string Severity,
    string Kind,
    bool Enabled,
    string? Namespace = null,
    string? Description = null);
