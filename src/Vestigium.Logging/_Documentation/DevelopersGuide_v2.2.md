# Vestigium.Logging — Developers Guide

**Document ID:** VEST-LOG-DEV-002  
**Version:** 2.2  
**Status:** Binding with SRS 2.3  
**Date:** 18 September 2026  
**Supersedes:** Developers Guide 2.1 (archived)

## Revision history

| Version | Date | Summary |
|---|---|---|
| 1.0 | 2026 | Initialize, write, flush, shutdown. |
| 2.0 | 2026-09-18 | Required EVENTID, Thrown, custom catalog. |
| 2.1 | 2026-09-18 | Head/Tail, PeekPendingDisk, janitor, archive. |
| **2.2** | **2026-09-18** | Operations log (default on). Seal. Opt-in archive/janitor with required destinations. Custom ids ≥ 10000. |

This guide is how a host uses the library. Requirements: `RequirementsSpecification_v2.3.md`. Internals: `DesignDocument_v2.2.md`.

## 1. Add the package

Target `net10.0`. Reference `Vestigium.Logging`. Catalog shards are embedded; do not copy them to output.

## 2. Initialize

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.LogDirectory = @"C:\ProgramData\Vestigium\Logs\PingIQ";
    cfg.MinimumDiskLevel = VestigiumLogLevel.Information;
});
```

One host per process. Second `Initialize` replaces the first. `Shutdown` at process exit, or `BindLifetime(Application.Current)`.

Operations logging is **on** unless you set `cfg.OperationsLogEnabled = false`. Default ops directory is `%ProgramData%\Vestigium\Logging\`.

## 3. Write

EVENTID is first. STATUS is required. Verbose / Debug / Information do not take an exception.

```csharp
VestigiumLog.Information(1, VestigiumStatus.None, "System", "Lifecycle", "Started");
VestigiumLog.Warning(2, VestigiumStatus.None, "Network", "ICMP", "Slow echo");
VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "Write failed", ex);
```

### Thrown

```csharp
try { … }
catch (Exception ex)
{
    VestigiumLog.Thrown(ex, VestigiumStatus.Failed);
    VestigiumLog.Thrown(ex, VestigiumStatus.Failed, eventId: 785);
}
```

Unknown CLR types write EVENTID 3 plus a DEBUG hint. After 20 distinct unknown types in the process, a second DEBUG line tells you to build a custom catalog.

## 4. Custom catalog (ids ≥ 10000)

```csharp
var catalog = VestigiumCustomCatalog.Open(@"C:\ProgramData\Vestigium\Catalogs\PingIQ");
catalog.Add("App.WidgetFailed", "PingIQ.WidgetException", "System", "Lifecycle",
    eventId: 10000, severity: "Error", description: "Widget pipeline failed");
catalog.Save();

VestigiumLogger.LoadCustomCatalog(@"C:\ProgramData\Vestigium\Catalogs\PingIQ");
VestigiumLogger.UnloadCustomCatalog();
```

Rows below 10000 are rejected. 5000–9999 are reserved for the logger’s operations file.

Offline edit: Unload → Open / Add / Set / Remove / Save → Load.

## 5. Sealing (opt-in)

```csharp
cfg.LogSealEnabled = true;
cfg.LogSealKeyPath = @"C:\ProgramData\Vestigium\Logging\keys\PingIQ.json";
```

On roll and Shutdown the writer appends a trailer. Verify an archived or closed file:

```csharp
var ring = VestigiumSealKeyRing.Open(cfg.LogSealKeyPath);
var report = VestigiumLogSeal.Verify(path, ring);
```

Results: Valid, NoTrailer, Torn, HashMismatch, BadSignature, MissingFile, KeyMismatch.

Do not treat the trailer line as a PowerBI event. `Head` / `Tail` skip it.

## 6. Archive (opt-in, destination required)

```csharp
cfg.ArchiveEnabled = true;
cfg.ArchiveDirectory = @"D:\Offload\PingIQ";
cfg.OperationsArchiveEnabled = true;
cfg.OperationsArchiveDirectory = @"D:\Offload\Vestigium.Logging";

var host = VestigiumLogArchive.ArchiveHostLogs(TimeSpan.FromDays(14));
var ops = VestigiumLogArchive.ArchiveOperationsLogs(TimeSpan.FromDays(90));
```

Sealed sources must Verify as Valid or NoTrailer or they stay put (ops EVENTID 5035). Each archived file gets `{name}.sha256` with uppercase hex.

## 7. Janitor (opt-in)

```csharp
cfg.JanitorEnabled = true;
cfg.JanitorMaxAge = TimeSpan.FromDays(14);
cfg.OperationsJanitorEnabled = true;
cfg.OperationsJanitorMaxAge = TimeSpan.FromDays(90);

VestigiumLogJanitor.DeleteHostLogs();
VestigiumLogJanitor.DeleteOperationsLogs();
```

The writer does **not** delete by age. Count cap still applies (`RetainedFileCountLimit`, default 90; `0` keeps no inactive files).

## 8. Read and flush

```csharp
VestigiumLogger.Flush();
var pending = VestigiumLogger.PeekPendingDisk(50);
var head = VestigiumLogReader.Head(20);
var tail = VestigiumLogReader.Tail(20);
```

`n` is 1…`LogReadMaxLines` (default 1000). There is no live tail.

## 9. UI pump

```csharp
_ = VestigiumLogPump.RunAsync(
    VestigiumLogger.EventReader,
    batch => Dispatcher.Invoke(() => { /* bind batch */ }),
    VestigiumLogger.Options,
    cancellationToken);
```

Do not hook a C# event per line onto the dispatcher.

## 10. Disk tripwire

Defaults: 10% free **or** 5 GB. Override only in Demo/tests:

```csharp
VestigiumLogger.OverrideDiskPressure(true);
VestigiumLogger.OverrideDiskPressure(null);
```

## 11. EVENTID map (quick)

| Range | Who |
|---|---|
| 0–4 | General Debug … Fatal |
| 5–99 | General lifecycle helpers |
| 100–4999 | Embedded .NET exceptions |
| 5000–9999 | Logger operations (do not assign) |
| ≥ 10000 | Your application |

## 12. License and credit

MIT — repository root `LICENSE`. People: `_Documentation/Contributors.md`. How the work is done: `_Documentation/Collaboration.md`.
