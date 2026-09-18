# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-002  
**Version:** 2.2  
**Status:** Binding — PR01 close / PR02 open  
**Date:** 18 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026  
**Package:** 1.6.0  
**Supersedes:** SRS 2.1 (archived under `_Documentation/Archived/PR02`)

This document is the single requirements specification for the Vestigium.Logging library. It does not depend on SRS 2.0.

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.2 | 2026 | 1.2.x | JSON Lines schema, flood, taxonomy, disk tripwire, WPF pump, process singleton. |
| 1.3 | 2026 | 1.3.0 | Owned `VestigiumJsonlWriter`. Serilog removed from runtime. |
| 1.4 | 2026-09-18 | 1.4.0 | Required EVENTID. `eventId` first on the façade. `Thrown`. Verbose/Debug/Information do not take Exception. |
| 2.0 | 2026-09-18 | 1.5.0 | Event catalog 0–4999 reserved / 5000+ custom. `VestigiumCustomCatalog` CRUD + Save. `LoadCustomCatalog` / `UnloadCustomCatalog`. |
| 2.1 | 2026-09-18 | 1.6.0 | `PeekPendingDisk`, Head/Tail reader, on-demand janitor, SHA256 archive. |
| **2.2** | 2026-09-18 | **1.6.0** | Uncatalogued `Thrown` writes EVENTID 3 plus a DEBUG hint. After 20 distinct unknown types, a second DEBUG line recommends `VestigiumCustomCatalog`. |

## 1. Objective

Vestigium.Logging is the single logging façade for the Vestigium suite and for hosts outside the suite. It owns schema, flood protection, taxonomy, event catalog, JSON Lines persistence, log-file housekeeping, and subscriber fan-out. PowerBI ingests one JSON object per line. APPID, CATEGORY, SUBCATEGORY, EVENTID, and STATUS are required on every emitted record.

## 2. Architectural constraints (binding)

### 2.1 Format is flat JSON Lines

On-disk format is UTF-8 JSON Lines, no BOM. One object per line. MESSAGE and EXCEPTION may contain newlines; they are JSON-escaped strings, never extra records. Pipe- or tab-delimited text is forbidden.

### 2.2 LEVEL is severity; STATUS is outcome

LEVEL is Verbose, Debug, Information, Warning, Error, or Fatal. STATUS is None, Pending, Success, Timeout, Failed, or Warning. A timed-out ICMP echo is Information + `STATUS=Timeout`. A failed database open is Error + `STATUS=Failed`. LEVEL never stores Success, Failed, or Timeout.

### 2.3 Flood identity is a four-field composite

Identity is `(APPID, CATEGORY, LEVEL, MESSAGE)` ordinal, case-sensitive. STATUS, SUBCATEGORY, CORRELATIONID, PROPERTIES, EVENTID, and EVENTNAME are not part of identity.

### 2.4 Subscribers must not touch the WPF dispatcher per line

No synchronous C# event per line. Events go through a bounded `Channel<VestigiumLogEvent>` (capacity 10,000, DropOldest). UI hosts drain with `VestigiumLogPump` (50 events or 100 ms) and marshal once via `WeakReferenceMessenger`. `IObservable` (`VestigiumLogger.Events`) is for tests and tools, not Views.

### 2.5 Retention first, then a dual free-space tripwire

Rolling files cap footprint. The disk gate trips when free space is below 10 percent **or** below 5 GB, whichever happens first, unless `DiskBytesFloorEnabled` is false. On-demand janitor and archive (§17–§18) are additional; they do not replace roll retention.

### 2.6 Process singleton

One host per process. `Initialize` replaces a previous host. `Shutdown` disposes it. Libraries that log before startup use `UninitializedBehavior = NoOp` for writes only; `Events`, `EventReader`, `Options`, `Catalog`, `PeekPendingDisk`, `LoadCustomCatalog`, and `UnloadCustomCatalog` require a host.

### 2.7 Engine-owned writer

Disk I/O is `VestigiumJsonlWriter`. Serilog is not a runtime dependency. `SerilogAsyncBuffer` is an obsolete alias of `DiskQueueCapacity`.

### 2.8 No live tail

There is no follow / `tail -f` API. Head and Tail read complete persisted lines only.

## 3. Canonical JSON schema

Property names and casing are mandatory. Field order on the wire: DateTime, EVENTID, EVENTNAME, PID, TID, LEVEL, STATUS, APPID, CATEGORY, SUBCATEGORY, MESSAGE, EXCEPTION, CORRELATIONID, PROPERTIES.

| Field | Type | Rules |
|---|---|---|
| DateTime | string | UTC `yyyy-MM-ddTHH:mm:ss.fffZ` |
| EVENTID | number | Required catalog id. `0` is General.Debug, not “missing.” |
| EVENTNAME | string or null | Catalog EventName. Present on engine writes. |
| PID | number | Process id |
| TID | number | `Environment.CurrentManagedThreadId` |
| LEVEL | string | Verbose, Debug, Information, Warning, Error, Fatal |
| STATUS | string | None, Pending, Success, Timeout, Failed, Warning |
| APPID | string | Host AppId, or per-call override. Internal warnings use `Vestigium.Logging`. |
| CATEGORY | string | Registered spelling after taxonomy normalize; else Uncategorized |
| SUBCATEGORY | string | Linked spelling; else Unregistered |
| MESSAGE | string | JSON-escaped. Never split across output lines. |
| EXCEPTION | string or null | Formatted per `ExceptionDetail`. JSON null when none or `None`. |
| CORRELATIONID | string or null | Opaque host id. JSON null when omitted. |
| PROPERTIES | object or null | Flat string map. JSON null when empty. |

Do not emit Info, Warn, INFO, or ERROR.

## 4. Required call-site fields

Every public write supplies EVENTID, STATUS, CATEGORY, SUBCATEGORY, and MESSAGE. APPID defaults to `Options.AppId`.

Verbose, Debug, and Information do not accept `Exception`. JSON EXCEPTION is null. Warning, Error, Fatal, and Write may pass an exception. `Thrown` requires an exception instance.

## 5. Event catalog

### 5.1 Ranges

| IDs | Owner |
|---|---|
| 0–4 | General messaging (Debug, Information, Warning, Error, Fatal) |
| 5–99 | General operations (5 Start … 14 Throttle) |
| 100–4999 | Embedded .NET exceptions and engine (step 5; last reserved slot 4995) |
| 5000+ | Host custom overlay |

### 5.2 Embedded load

`VestigiumEventCatalog.LoadDefault` seeds generals 0–14 and merges embedded `EventCatalog` JSON shards from the library assembly. Rows below 5000 are reserved for the library.

### 5.3 Resolve

1. Explicit EVENTID — must exist and be enabled, else throw.
2. Else exception `FullName`, then base types. `System.Exception` matches only when the thrown type is exactly `Exception`.
3. Else LEVEL → 0 / 1 / 2 / 3 / 4.

Custom FullName ≥ 5000 wins over an embedded row with the same FullName.

### 5.4 Engine-owned ids

| Id | Use |
|---|---|
| 1 | Flood aggregation line |
| 11 | Unregistered taxonomy warning (`APPID=Vestigium.Logging`) |
| 14 | Flood identity cap warning |

### 5.5 Custom catalog builder

`VestigiumCustomCatalog` is the offline editor. It does not require `Initialize`.

- `Open(path)` creates `path` and `path/shards` if missing.
- Empty catalog: Count 0, NextCustomId 5000.
- Load rejects any shard row with EventId < 5000.
- `Add` assigns NextCustomId when id is omitted (5000, 5005, 5010…).
- Duplicate EventId or FullName throws.
- `Set` updates fields; EventId does not change.
- `Remove` does not recycle ids.
- `Get` / `TryGet` / `TryGetByFullName` / `List`.
- `Save` writes `shards/custom.json` and `index.json` via temp + replace. Other shard JSON files in that folder are removed.

### 5.6 Load / unload on a live host

| API | Behavior |
|---|---|
| `cfg.EventCatalogPath` at Initialize | Merge overlay, then freeze embedded + init-only `RegisterEvent` |
| `RegisterEvent` inside Initialize | Queued custom row, id ≥ 5000 |
| `LoadCustomCatalog(path)` | After Initialize. Replaces current overlay from disk. 0–4999 stay. |
| `UnloadCustomCatalog()` | Drops every EventId ≥ 5000. Path becomes null. |
| `CustomCatalogPath` | Loaded path, or null |

Offline edit does not require a host. Typical loop: Unload → Open / Add / Save → Load.

Initialize with an overlay file that contains EventId < 5000 throws.

After Initialize, `RegisterEvent` / `MergeFromDirectory` on the frozen catalog throw. Load / Unload use `ReplaceCustomFromDirectory` / `ClearCustom` and are allowed after freeze.

## 6. Call-site API

```text
Write(eventId, level, status, category, subcategory, message, exception?, appId?, correlationId?, properties?)
Verbose / Debug / Information(eventId, status, category, subcategory, message, …)   // no exception
Warning / Error / Fatal(eventId, status, category, subcategory, message, exception?, …)
Thrown(exception, status, category?, subcategory?, message?, …)
Thrown(exception, status, eventId, category?, subcategory?, message?, …)
```

`Thrown` without eventId looks up the catalog (FullName, then BaseType; `System.Exception` is not a catch-all).  
No row → write EVENTID **3** (`General.Error`) with the exception text, then one DEBUG EVENTID **0** hint naming the type: occasional custom types are fine. Distinct unknown types are counted per process (reset on `Initialize`). At **20** distinct types, one additional DEBUG line tells the host to create a `VestigiumCustomCatalog` (ids 5000+) and `LoadCustomCatalog`. The same type does not repeat the hint.  
`Thrown` with eventId requires that id; EXCEPTION still comes from the instance. MESSAGE defaults to `exception.Message`. CATEGORY / SUBCATEGORY default to the catalog row. LEVEL defaults to parsed shard Severity, else Error. STATUS is always caller-owned. The DEBUG hint uses EVENTID 0; hosts that persist only Information will not see it on disk unless `MinimumDiskLevel` is Debug or lower.

## 7. Taxonomy

Defaults (frozen snapshot, copied into options at construction):

| Category | Subcategories |
|---|---|
| Network | ICMP, TCP, DNS, HTTP, Routing |
| System | IO, Memory, Threading, Configuration |
| UI | Navigation, Binding, Input, Lifecycle |
| Uncategorized | Unregistered |

Register extra pairs only inside Initialize. Matching is ordinal-ignore-case; PowerBI sees the first registered spelling. Blank or unknown pairs rewrite to Uncategorized / Unregistered and emit a Warning from APPID `Vestigium.Logging`, EVENTID 11. `VestigiumTaxonomy.Combine` merges suite catalogs at host init. Taxonomy freezes at the end of Initialize.

## 8. Flood protection

| Parameter | Default |
|---|---|
| Identity | (APPID, CATEGORY, LEVEL, MESSAGE) |
| FloodThresholdCount | 5 |
| FloodWindow | 30 seconds |
| FloodIdentityCap | 4096 |

First N observations in the window emit full lines. Further hits increment suppressed and do not write. A 1-second timer drains expired keys and writes `[Aggregated] Previous message repeated X additional times` with STATUS=None, original LEVEL / APPID / CATEGORY, last SUBCATEGORY, EVENTID 1. Distinct identities are capped; expired then oldest idle are evicted first. At 90% cap the engine writes one internal Warning (EVENTID 14).

## 9. On-disk layout

| Item | Value |
|---|---|
| Default root | `%ProgramData%\Vestigium\Logs\{APPID}\` |
| File name | `vestigium-{APPID}-yyyyMMdd.json` (same-day size roll: `-n`) |
| Format | UTF-8, one JSON object per line, no BOM |
| File size cap | 20 MB then roll |
| Time retention | 14 days |
| Count retention | 90 files per APPID |
| Share | Writer exclusive; `FileShare.Read` for PowerBI and tests |
| Disk queue | 10,000 lines, DropOldest |
| MinimumDiskLevel | Information (Verbose/Debug skipped on disk unless lowered) |

`Flush()` drains flood summaries and waits up to `FlushTimeout` (5 s). It does not stop further writes. `Shutdown()` stops accepting, completes the writer and channel, and disposes the host.

## 10. Disk tripwire

Poll every 30 seconds via `DriveInfo`. Trip when free percent < 10 **or** (when enabled) free bytes < 5 GB. While tripped, Verbose and Debug are dropped before flood. `OverrideDiskPressure(true/false/null)` is for the Demo and tests. `DiskStatus` is safe before Initialize (empty, not tripped).

## 11. Subscribers and recent buffer

- Bounded channel 10,000, DropOldest, multi-writer, multi-reader (work-stealing). One pump per process.
- `IObservable<VestigiumLogEvent>` for tests.
- `RecentJsonLines` cap 200 (already-formatted JSON).
- `WrittenCount` / `SuppressedCount` are process counters on the current host.

## 12. Exception text

| ExceptionDetail | EXCEPTION field |
|---|---|
| Full (default) | `Exception.ToString()` |
| TypeAndMessage | Type and message chain, no stack frames |
| None | JSON null (MESSAGE still written) |

`ExceptionMaxChars` default 8192; 0 means unlimited, still clipped at 64 KiB.

## 13. PROPERTIES

Keys `[A-Za-z][A-Za-z0-9_]*`. Max 16 entries. Values truncated to 256 characters. Null values and illegal keys dropped. Empty bag → JSON null.

## 14. Lifetime

`BindLifetime(wpfApplication)` hooks process exit, console cancel, and WPF `Application.Exit` via reflection so the library stays UI-framework agnostic. Second bind unsubscribes the first.

## 15. Unflushed disk queue

| API | Behavior |
|---|---|
| `Flush()` / `Flush(timeout)` | Persist the disk queue and flood drain. Writes continue. |
| `PendingDiskCount` | Queued lines not yet on disk. `0` when no host. |
| `PeekPendingDisk(n)` | Copy of the oldest queued lines. Does not dequeue. |
| `ActiveLogPath` | Path the writer currently holds, or null. |

`n < 1` throws. `n` is capped by `LogReadMaxLines` (default 1,000, host-configurable, minimum 1).

## 16. Persisted Head / Tail

`VestigiumLogReader.Head(n, directory?, appId?)` and `Tail(...)`.

- Files: `vestigium-{APPID}-*.json`, ordinal-ignore-case name order.
- Head: oldest file, first complete lines. Tail: newest file, last complete lines.
- Open with `FileShare.ReadWrite`. If the file does not end in a newline, drop the last fragment.
- Same `n` rules as §15.
- When the logger is not initialized, `directory` and `appId` are required.
- No live follow.

## 17. Janitor

`VestigiumLogJanitor.DeleteOlderThan(age, directory?, appId?)`.

- `age <= TimeSpan.Zero` throws.
- Targets `vestigium-{APPID}-*.json`. Age is the `yyyyMMdd` stamp in the file name (UTC date).
- Must not delete `ActiveLogPath`. Locked files increment `SkippedOpen`.
- Returns `Deleted`, `Bytes`, `SkippedOpen`.
- On-demand only. Rolling retention in §9 still applies on size/time roll.

## 18. Archive

`VestigiumLogArchive.ArchiveOlderThan(age, archiveDirectory, directory?, appId?)`.

Per eligible file (same age rule as §17, skip live file):

1. Copy to `archiveDirectory` with the original file name.
2. SHA256 the source and the copy (`Convert.ToHexString` — **uppercase** hex).
3. Mismatch: leave the source, increment `Failed`.
4. Match: write `{filename}.sha256` as `HEX  filename` (two spaces), then delete the source.

Returns `Archived`, `Deleted`, `Failed`, `Manifest`. No zip. Queue contents are not archived unless the host Flushes first.

## 19. Options defaults (binding unless the host overrides)

| Option | Default |
|---|---|
| AppId | Vestigium |
| LogDirectory | `%ProgramData%\Vestigium\Logs\{AppId}\` |
| FileSizeLimitBytes | 20 MB |
| RetainedFileTimeLimit | 14 days |
| RetainedFileCountLimit | 90 |
| FloodThresholdCount | 5 |
| FloodWindow | 30 s |
| FloodIdentityCap | 4096 |
| DiskFreePercentThreshold | 10 |
| DiskFreeBytesFloor | 5 GB |
| DiskBytesFloorEnabled | true |
| DiskPollInterval | 30 s |
| SubscriberChannelCapacity | 10,000 |
| UiBatchInterval | 100 ms |
| UiBatchSize | 50 |
| FlushTimeout | 5 s |
| MinimumDiskLevel | Information |
| DiskQueueCapacity | 10,000 |
| RecentJsonLineCap | 200 |
| LogReadMaxLines | 1,000 (minimum 1) |
| ExceptionDetail | Full |
| ExceptionMaxChars | 8,192 |

## 20. Package identity

PackageId Vestigium.Logging. Target net10.0. MIT. Tests assembly InternalsVisibleTo. Event catalog JSON is EmbeddedResource. PowerShell catalog tools live under `Resources/PowerShell`.

## 21. Public surface (PR01 inventory)

| Type | Role |
|---|---|
| `VestigiumLogger` | Initialize, Shutdown, Flush, BindLifetime, Catalog, Events, EventReader, disk/flood counters, Load/Unload custom catalog, PeekPendingDisk |
| `VestigiumLog` | Write, Verbose–Fatal, Thrown |
| `VestigiumLogEvent` | In-process record + `ToJsonLine` |
| `VestigiumLogLevel` / `VestigiumStatus` | Severity vs outcome |
| `VestigiumLoggerOptions` | Host configuration |
| `VestigiumEventCatalog` / `VestigiumEventDefinition` | Runtime catalog |
| `VestigiumCustomCatalog` | Offline custom catalog editor |
| `VestigiumTaxonomy` | Category catalog |
| `VestigiumLogPump` | UI drain |
| `VestigiumLogReader` | Head / Tail |
| `VestigiumLogJanitor` | Delete older than |
| `VestigiumLogArchive` | Archive + SHA256 |
| `VestigiumDiskStatus` | Tripwire snapshot |
| `VestigiumExceptionDetail` | EXCEPTION verbosity |
| `VestigiumUninitializedBehavior` | Throw or NoOp for writes |

## 22. Non-goals

- Delimited text sinks
- Per-line WPF dispatcher invokes
- Auto-creating catalog rows from an unknown throw
- Recycling removed custom EventIds
- Mutating embedded 0–4999 at runtime
- Multiple concurrent hosts in one process
- Live tail / follow of the active file
- Zip or container formats for archives
- Automatic janitor or archive on Shutdown (the host may call §§17–18 explicitly)

## 23. Requirement trace

| ID | Requirement |
|---|---|
| R1 | JSON Lines schema §3 |
| R2 | LEVEL ≠ STATUS |
| R3 | Required EVENTID, APPID, CATEGORY, SUBCATEGORY, STATUS |
| R4 | EventId ranges 0–4999 reserved, 5000+ custom |
| R5 | Thrown lookup; uncatalogued type → EVENTID 3 + DEBUG hint; 20-type catalog recommendation |
| R6 | Custom catalog CRUD + Save + Load/Unload |
| R7 | Flood composite identity and aggregation |
| R8 | Owned JSONL writer, roll, retain, FileShare.Read |
| R9 | Dual disk tripwire |
| R10 | Bounded channel + one pump |
| R11 | Taxonomy freeze after Initialize |
| R12 | Uninitialized writes Throw or NoOp; catalog APIs require a host |
| R13 | Flush persists queue; writes continue |
| R14 | PendingDiskCount |
| R15 | PeekPendingDisk is non-destructive |
| R16 | Read count 1 … LogReadMaxLines |
| R17 | No live tail |
| R18 | Head/Tail complete lines only |
| R19 | Janitor deletes by name-stamp age, skips open file |
| R20 | Archive copy + uppercase SHA256 + verify + delete source |
