namespace Vestigium.Logging;

/// <summary>Composite flood key. Value equality — never concatenate with a delimiter.</summary>
public sealed record FloodIdentity(string AppId, string Category, string Level, string Message);
