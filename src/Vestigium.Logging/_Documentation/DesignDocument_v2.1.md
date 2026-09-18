# Vestigium.Logging — Design Document

**Document ID:** VEST-LOG-DES-002  
**Version:** 2.1  
**Date:** 18 September 2026  
**Status:** Binding companion to Requirements Specification 2.2  
**Package:** 1.6.0  
**PR:** PR01 close / PR02 open  
**Supersedes:** Design Document 2.0 / 1.4 / 1.0 (archived under `_Documentation/Archived/PR02`)

This document describes how Vestigium.Logging is built. Requirements live in RequirementsSpecification_v2.2.md. This file does not depend on Design 1.x.

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.4 | 2026-09-18 | 1.3.x | Façade, flood, owned JSONL writer, taxonomy, pump. |
| 2.0 | 2026-09-18 | 1.6.0 | Event catalog, Thrown, custom catalog Load/Unload, Peek/Head/Tail, janitor, SHA256 archive. |
| **2.1** | 2026-09-18 | **1.6.0** | Uncatalogued Thrown: EVENTID 3 + DEBUG advisor; 20 distinct types. |

## 1. Intent

The library is the single logging façade for the Vestigium suite and for hosts outside the suite. Hosts call `VestigiumLogger.Initialize` once. Diagnostic threads call `VestigiumLog.*` and return immediately. Disk I/O, flood aggregation, catalog resolution, and UI subscribers are isolated so a stalled dispatcher or a full disk cannot stall ICMP / HTTP probes.

PowerBI reads UTF-8 JSON Lines with a frozen property set. APPID, CATEGORY, SUBCATEGORY, EVENTID, and STATUS are required on every emitted record.

## 2. Projects

```
Vestigium.Logging.slnx
├── src/Vestigium.Logging            net10.0 class library (no WPF reference)
│   ├── Catalog/                    Event catalog + custom catalog editor
│   ├── Disk/                       Free-space monitor
│   ├── Flood/                      Identity + tracker
│   ├── Formatting/                 JSON, exception text, property bag
│   ├── IO/                         Writer, reader, janitor, archive
│   ├── Lifetime/                   Process / console / WPF Exit
│   ├── Observability/              Subject + UI pump
│   ├── EventCatalog/ or Resources/EventCatalog/  Embedded shards
│   ├── Resources/PowerShell/       Catalog generation tools
│   └── _Documentation/             SRS, Design, Guide
├── src/Vestigium.Logging.Tests      xUnit, collection VestigiumLogger
└── src/Vestigium.Logging.Demo       net10.0-windows WPF MVVM gallery
```

The engine targets `net10.0` so tests run without a STA dispatcher. The demo is the only WPF project. Lifetime is bound with reflection on `Exit` so the library stays UI-framework agnostic.

`InternalsVisibleTo("Vestigium.Logging.Tests")`. PackageId `Vestigium.Logging`, MIT.

## 3. Runtime shape

```
VestigiumLog.Write / Thrown
        │
        ▼
 Catalog.Resolve(eventId, exception, level)
        │
        ▼
 Taxonomy.Normalize(category, subcategory)
        │
        ▼
 Disk gate (tripped && Verbose/Debug) ──yes──► drop
        │ no
        ▼
 FloodTracker.Observe(APPID, CATEGORY, LEVEL, MESSAGE)
        │
        ├── suppressed ──────────────────────► drop
        ├── summary due ──► aggregation event (EVENTID 1)
        └── writeFull ──► VestigiumLogEvent
                              │
                              ├── VestigiumJsonlWriter.Enqueue   (if level ≥ MinimumDiskLevel)
                              ├── Channel.Writer.TryWrite        (10k DropOldest)
                              ├── Subject.Publish                (IObservable)
                              └── RecentJsonLines ring           (cap 200)
```

UI hosts: `VestigiumLogPump` (50 events or 100 ms) → `WeakReferenceMessenger` → ViewModel dispatcher once per batch.

## 4. Process host

`VestigiumLogger` is a static façade over one `Host`.

| Call | Behavior |
|---|---|
| `Initialize(configure)` | Builds options, loads embedded catalog, optional `EventCatalogPath` merge, `RegisterEvent` queue, Freeze catalog and taxonomy, starts writer + flood timer + disk poll |
| `Shutdown` | Stop accepting, Complete writer, complete channel, dispose |
| `Flush` / `Flush(timeout)` | Drain flood + writer Flush. Writes stay accepted |
| `BindLifetime(app)` | ProcessExit, Ctrl+C, WPF Exit → Shutdown |
| `LoadCustomCatalog(path)` | `Catalog.ReplaceCustomFromDirectory` after freeze |
| `UnloadCustomCatalog` | `Catalog.ClearCustom` |
| `PeekPendingDisk(n)` | Writer.Peek, cap `LogReadMaxLines` |
| `PendingDiskCount` | Writer.QueuedCount |
| `ActiveLogPath` | Writer.ActivePath |

`UninitializedBehavior`: Throw (default) or NoOp for **writes** only.

`Host` is `internal sealed partial` so disk helpers live in `VestigiumLogger.Host.Disk.cs`. Overlay APIs live in `VestigiumLogger.CatalogOverlay.cs` and `VestigiumLogger.PendingDisk.cs`.

## 5. Call-site façade

`VestigiumLog` is the only type application code should call for writes.

- First parameter of Write and severity helpers is `int eventId`.
- Verbose / Debug / Information have no `Exception` parameter. JSON EXCEPTION is null.
- Warning / Error / Fatal / Write may pass `Exception?`.
- `Thrown(Exception, VestigiumStatus, …)` resolves EVENTID from `exception.GetType().FullName` walking `BaseType`, skipping `System.Exception` unless that is the thrown type. Missing row writes EVENTID 3 and a DEBUG EVENTID 0 hint (`UncataloguedExceptionAdvisor`). At 20 distinct unknown FullNames, a second DEBUG line recommends a custom catalog. The set resets on Initialize.
- `Thrown(Exception, VestigiumStatus, int eventId, …)` uses the explicit id (must exist). EXCEPTION still comes from the instance. MESSAGE defaults to `exception.Message`. CATEGORY / SUBCATEGORY default to the catalog row. LEVEL from shard Severity else Error. STATUS is always the caller’s.

`Write` always passes a concrete `int` eventId into `Emit`. Internal engine lines use reserved ids 1, 11, 14.

## 6. Event catalog

### 6.1 Types

`VestigiumEventDefinition` is an immutable record: EventId, EventName, FullName, Category, Subcategory, Severity, Kind, Enabled, Namespace, Description.

`VestigiumEventCatalog` holds `_byId` and `_byFullName`. Embedded load uses `allowCustom: false`. Overlay uses `allowCustom: true`.

### 6.2 Ranges

| IDs | Source |
|---|---|
| 0–14 | Seeded generals (not shards) |
| 100–4999 | Embedded JSON shards, step 5 |
| 5000+ | Host overlay only |

### 6.3 Resolve

1. Explicit id → TryGetById (enabled) else throw.  
2. Else TryGetByException (FullName / bases).  
3. Else LEVEL maps to 0 / 1 / 2 / 3 / 4.

Custom FullName overwrites the FullName index so a host type can replace an embedded exception mapping.

`Freeze()` blocks `MergeFromDirectory` and `RegisterCustom`. `ClearCustom` / `ReplaceCustomFromDirectory` remain legal after freeze.

### 6.4 Custom editor

`VestigiumCustomCatalog.Open(root)` reads `root/shards/*.json` via `ReadDirectoryRows`. Any EventId < 5000 throws.

CRUD is in-memory. `NextCustomId` is high-water + 5. Remove does not reuse ids. `Save` writes only `shards/custom.json` plus `index.json` (atomic temp + `File.Move` overwrite) and deletes other shard JSON in that folder.

Host path: Save files → `LoadCustomCatalog(root)` or `cfg.EventCatalogPath` at Initialize.

## 7. Taxonomy

`VestigiumTaxonomy` maps ignore-case names to the first registered spelling. Defaults: Network, System, UI, Uncategorized/Unregistered. `Normalize` returns rewritten=true when category or subcategory is blank or unknown; the host then emits EVENTID 11 from APPID `Vestigium.Logging`. Freeze at end of Initialize. `Combine` merges suite catalogs before freeze.

## 8. Flood

`FloodIdentity` is a record (AppId, Category, Level, Message). Not a concatenated string.

1. First `FloodThresholdCount` (5) observations in `FloodWindow` (30 s) emit full lines.  
2. Further hits increment Suppressed; `writeFull = false`.  
3. 1-second timer `DrainExpired`. If the window elapsed and Suppressed > 0, emit `[Aggregated] Previous message repeated X additional times`, STATUS=None, EVENTID 1, original LEVEL/APPID/CATEGORY, last SUBCATEGORY. Key is removed.  
4. Cap `FloodIdentityCap` 4096: evict expired, then oldest idle, then flush-and-drop oldest pending. At 90% cap emit EVENTID 14 once.

PingIQ and TraceIQ with the same MESSAGE are different keys.

## 9. JSON and properties

`VestigiumJsonFormatter` writes `VestigiumJsonRecord` with `DefaultIgnoreCondition.Never` so null EXCEPTION / CORRELATIONID / PROPERTIES stay in the object. DateTime is UTC `yyyy-MM-ddTHH:mm:ss.fffZ`.

`VestigiumPropertyBag.Sanitize`: keys `^[A-Za-z][A-Za-z0-9_]*$`, max 16 entries, values clipped to 256, null values dropped.

`VestigiumExceptionFormatter`: Full / TypeAndMessage / None. Soft cap `ExceptionMaxChars` (8192), hard cap 64 KiB.

## 10. Disk writer

`IVestigiumJsonlWriter`: Enqueue (never blocks, DropOldest), Flush(timeout), Complete, QueuedCount, DroppedCount, ActivePath, Peek(n).

`VestigiumJsonlWriter` is `internal sealed partial`. One background consumer. Exclusive write, `FileShare.Read`. Path `vestigium-{APPID}-yyyyMMdd.json`. Roll on UTC date or 20 MB; same-day size roll uses `-n`. Retention on roll: 14 days and 90 files.

`MinimumDiskLevel` (default Information) filters **disk only**. Verbose/Debug still reach subscribers unless the tripwire is active.

Peek is `ConcurrentQueue.ToArray()` then take n. It does not dequeue.

## 11. Reader, janitor, archive

These types talk to **files**. They do not drain the writer queue. Call `Flush` first if queued lines must appear.

### 11.1 VestigiumLogReader

Head: files ascending, first complete lines. Tail: files descending, last complete lines. `FileShare.ReadWrite`. If the text does not end in CR/LF, drop the last fragment. Cap `LogReadMaxLines`. Offline requires directory + appId.

### 11.2 VestigiumLogJanitor

Cutoff = UTC date − age. File date is the `yyyyMMdd` token in the name. Skip `ActiveLogPath`. `File.Delete` IOException → SkippedOpen. Result: Deleted, Bytes, SkippedOpen.

### 11.3 VestigiumLogArchive

Same eligibility as janitor. Copy → SHA256 both (`Convert.ToHexString`, uppercase) → sidecar `{file}.sha256` as `HEX  filename` → delete source. Hash mismatch increments Failed and leaves source. Result: Archived, Deleted, Failed, Manifest. No zip.

## 12. Disk tripwire

`DiskSpaceMonitor` polls `DriveInfo` every 30 s on the volume that holds `LogDirectory`. Trip = free percent < 10 **or** (if enabled) free bytes < 5 GB. `OverrideDiskPressure` for Demo/tests. `DiskStatus` before Initialize is Empty, not tripped.

## 13. Subscribers

`LogEventSubject` is a simple multicast IObservable. Subscriber `OnNext` exceptions are swallowed.

`Channel<VestigiumLogEvent>` bounded 10,000, DropOldest, multi-reader (work-stealing — one pump per process).

`VestigiumLogPump.RunAsync` batches 50 events or 100 ms.

`RecentJsonLines` is a concurrent queue cap 200.

## 14. Lifetime and failure policy

- Disk poll exceptions swallowed.  
- Channel full / disk queue full → DropOldest.  
- `Flush` timeout returns without throwing.  
- ProcessExit / Ctrl+C / WPF Exit call `Shutdown`, not Flush.  
- Unknown EVENTID on Write throws on the calling thread. Uncatalogued Thrown does not throw; it uses EVENTID 3 plus DEBUG.  
- Writes after Shutdown: throw unless NoOp.

`LifetimeBinder` reflects `GetEvent("Exit")` / `GetMethod("Invoke")` on the supplied application object.

## 15. WPF demo

`App.OnStartup` initializes and `BindLifetime(Current)`. `MainViewModel` owns configuration and the live feed. Background `PumpAsync` + messenger + one dispatcher marshal per batch. Apply tears down the host and Initialize again.

## 16. Threading

| Component | Thread |
|---|---|
| VestigiumLog.* | Caller (probe threads) |
| Flood Observe | Caller |
| Flood Drain | 1 s timer |
| JSONL consumer | Dedicated writer task |
| Disk poll | 30 s timer |
| Channel / Subject | Caller after WriteEvent |
| Pump | Host-provided async loop |

No lock is held across file I/O on the caller path except brief flood map locks.

## 17. Tests

xUnit collection `VestigiumLogger` serializes host tests (process singleton). Offline tests (reader, janitor, archive, custom catalog Open) do not need the collection unless they Initialize.

Coverage intended for PR01: flood, JSON schema, taxonomy rewrite, catalog resolve / Thrown, custom CRUD + Save + Load/Unload, Peek + Flush, Head/Tail torn line, janitor age, archive hash sidecar.

## 18. Mapping to SRS 2.2

| SRS | Design |
|---|---|
| R1–R3 schema and required fields | §§3, 5, 9 |
| R4–R6 catalog | §6 |
| R7 flood | §8 |
| R8 writer | §10 |
| R9 tripwire | §12 |
| R10 pump | §13 |
| R11 taxonomy freeze | §7 |
| R12 uninitialized | §4 |
| R13–R17 queue / no live tail | §§4, 10, 11 |
| R18 Head/Tail | §11.1 |
| R19 janitor | §11.2 |
| R20 archive SHA256 | §11.3 |
