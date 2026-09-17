namespace Vestigium.Logging;

/// <summary>
/// What <see cref="VestigiumLog"/> does when <see cref="VestigiumLogger.Initialize"/> has not run.
/// Only writes are affected; <see cref="VestigiumLogger.Events"/> still requires a host.
/// </summary>
public enum VestigiumUninitializedBehavior
{
    Throw = 0,
    NoOp = 1
}
