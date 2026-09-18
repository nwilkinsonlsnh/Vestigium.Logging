# Vestigium.Logging — Developer’s Guide

**Document ID:** VEST-LOG-DEV-002  
**Version:** 2.0  
**Date:** 18 September 2026  
**Target:** Visual Studio 2026 / .NET 10 LTS / package 1.6.0  
**Companions:** Requirements Specification 2.1, Design Document 2.0  
**Supersedes:** Developer’s Guide 1.4 / 1.0

This guide is how to use Vestigium.Logging. It does not depend on Guide 1.x.

## Revision history

| Version | Package | Summary |
|---|---|---|
| 1.0–1.4 | 1.3.x | Initialize, Write, flood, pump, owned writer. |
| **2.0** | **1.6.0** | Required EVENTID, Thrown, custom catalog, Peek/Head/Tail, janitor, SHA256 archive. |

## 1. Add the package

Project-reference `src/Vestigium.Logging/Vestigium.Logging.csproj` or the nupkg `Vestigium.Logging`. Target net10.0. No WPF reference on the library.

## 2. Start the host

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.LogDirectory = @"C:\\IT\\Vestigium\\Logs\\PingIQ";
    cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
});
VestigiumLogger.BindLifetime(Application.Current);
```

AppId is required. Initialize replaces a previous host. Flush persists the queue; writes continue. Shutdown (or BindLifetime) at process exit.

```csharp
VestigiumLogger.UninitializedBehavior = VestigiumUninitializedBehavior.NoOp;
```

Only writes no-op. Events, EventReader, Options, Catalog, PeekPendingDisk, LoadCustomCatalog require Initialize. DiskStatus is safe before Initialize.

## 3. Write a line

EVENTID is first. CATEGORY, SUBCATEGORY, STATUS, MESSAGE required.

```csharp
VestigiumLog.Information(1, VestigiumStatus.Success, "Network", "ICMP", "Echo reply received",
    correlationId: sessionId,
    properties: new Dictionary<string, string?> { ["host"] = name, ["rttMs"] = "12" });
VestigiumLog.Warning(2, VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out");
VestigiumLog.Error(785, VestigiumStatus.Failed, "System", "IO", "Failed to open file", ex);
VestigiumLog.Write(5, VestigiumLogLevel.Information, VestigiumStatus.None, "System", "Lifecycle", "listener started");
```

General ids 0–4 = Debug/Information/Warning/Error/Fatal. 5 Start, 6 Stop, 7 Heartbeat, 8 Timeout, 9 Retry, 10 Cancelled, 11 Config, 12 NotFound, 13 Denied, 14 Throttle.

Verbose/Debug/Information do not take Exception. Warning/Error/Fatal/Write may. Thrown requires Exception. Unknown EVENTID throws.

Do not put STATUS, LEVEL, or MESSAGE into PROPERTIES.

## 4. Thrown

```csharp
VestigiumLog.Thrown(ex, VestigiumStatus.Failed);
VestigiumLog.Thrown(ex, VestigiumStatus.Timeout, eventId: 5000);
```

Lookup is FullName then BaseType. System.Exception is not a catch-all. MESSAGE defaults to ex.Message. CATEGORY/SUBCATEGORY from the catalog row. LEVEL from shard Severity else Error. STATUS is yours.

## 5. Custom event catalog

0–4999 reserved. Host events start at 5000.

```csharp
var catalog = VestigiumCustomCatalog.Open(@"C:\\IT\\PingIQ\\EventCatalog");
catalog.Add("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP");
catalog.Set(5000, description: "ICMP budget exceeded");
catalog.Save();

VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.EventCatalogPath = catalog.Root;
    cfg.RegisterEvent("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP");
});

VestigiumLogger.LoadCustomCatalog(catalog.Root);
VestigiumLogger.UnloadCustomCatalog();
```

Add without an id uses 5000, 5005… Duplicate EventId or FullName throws. Remove does not recycle ids. Open rejects EventId < 5000. RegisterEvent is init-only. LoadCustomCatalog replaces the overlay.

## 6. Taxonomy

Register only inside Initialize. Unknown pairs become Uncategorized/Unregistered and warn EVENTID 11 from APPID Vestigium.Logging.

## 7. Unflushed lines and files

```csharp
VestigiumLogger.Flush();
var pending = VestigiumLogger.PeekPendingDisk(50);
var head = VestigiumLogReader.Head(50);
var tail = VestigiumLogReader.Tail(50, dir, "PingIQ");
VestigiumLogJanitor.DeleteOlderThan(TimeSpan.FromDays(14));
VestigiumLogArchive.ArchiveOlderThan(TimeSpan.FromDays(14), @"C:\\IT\\Vestigium\\Archive\\PingIQ");
```

Peek does not dequeue. n < 1 throws. Cap LogReadMaxLines (default 1000). No live tail. Janitor/archive skip ActiveLogPath. Archive sidecar is uppercase HEX plus two spaces plus filename. Flush before archive if the queue must be on disk.

## 8. WPF live feed

One VestigiumLogPump per process. Do not TryRead from a View. Do not subscribe Events on a View. Marshal onto the dispatcher once per batch.

## 9. Options you will actually change

Always set AppId. Optional: LogDirectory, MinimumDiskLevel, LogReadMaxLines, EventCatalogPath, DiskBytesFloorEnabled=false on small CI disks, ExceptionDetail, ExceptionMaxChars, flood threshold/window.

## 10. Wire format

DateTime UTC yyyy-MM-ddTHH:mm:ss.fffZ. LEVEL is never Success or Timeout. STATUS carries the outcome.

## 11. Do not

Write without EVENTID. Pass Exception into Information. Second pump or Events on a View. Custom events in 0–4999. Expect Remove to reuse ids. Expect archive to see the unflushed queue. Build live tail on Head/Tail.

## 12. Tests

`[Collection("VestigiumLogger")]` plus Shutdown in Dispose for host tests. Reader/janitor/archive/Open work offline with directory + AppId.
