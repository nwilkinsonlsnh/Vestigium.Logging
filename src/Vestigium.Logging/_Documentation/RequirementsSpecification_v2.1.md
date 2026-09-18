# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-002  
**Version:** 2.1  
**Status:** Binding — PR01 close / PR02 open  
**Date:** 18 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026  
**Package:** 1.6.0  
**Supersedes:** SRS 2.0 (archived under `_Documentation/Archived/PR002`)

This document is the single requirements specification for the Vestigium.Logging library. It does not depend on SRS 2.0.

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.2 | 2026 | 1.2.x | JSON Lines schema, flood, taxonomy, disk tripwire, WPF pump, process singleton. |
| 1.3 | 2026 | 1.3.0 | Owned VestigiumJsonlWriter. Serilog removed from runtime. |
| 1.4 | 2026-09-18 | 1.4.0 | Required EVENTID. eventId first. Thrown. Verbose/Debug/Information do not take Exception. |
| 2.0 | 2026-09-18 | 1.5.0 | Catalog 0–4999 / 5000+. Custom catalog CRUD + Save. Load/Unload. |
| **2.1** | 2026-09-18 | **1.6.0** | PeekPendingDisk, Head/Tail, janitor, SHA256 archive. This document. |

## 1. Objective

Vestigium.Logging is the single logging façade for the Vestigium suite and for hosts outside the suite. It owns schema, flood protection, taxonomy, event catalog, JSON Lines persistence, log-file housekeeping, and subscriber fan-out. PowerBI ingests one JSON object per line. APPID, CATEGORY, SUBCATEGORY, EVENTID, and STATUS are required on every emitted record.

## 2. Architectural constraints (binding)

### 2.1 Format is flat JSON Lines
UTF-8 JSON Lines, no BOM. One object per line. MESSAGE and EXCEPTION may contain newlines; they are JSON-escaped. Pipe- or tab-delimited text is forbidden.

### 2.2 LEVEL is severity; STATUS is outcome
LEVEL: Verbose, Debug, Information, Warning, Error, Fatal. STATUS: None, Pending, Success, Timeout, Failed, Warning. LEVEL never stores Success, Failed, or Timeout.

### 2.3 Flood identity is a four-field composite
`(APPID, CATEGORY, LEVEL, MESSAGE)` ordinal, case-sensitive. STATUS, SUBCATEGORY, CORRELATIONID, PROPERTIES, EVENTID, EVENTNAME are not identity.

### 2.4 Subscribers must not touch the WPF dispatcher per line
Bounded Channel 10,000 DropOldest. UI drains with VestigiumLogPump (50 events or 100 ms). IObservable is for tests and tools, not Views.

### 2.5 Retention first, then a dual free-space tripwire
Roll cap plus trip when free space is below 10 percent or 5 GB unless DiskBytesFloorEnabled is false. Janitor/archive do not replace roll retention.

### 2.6 Process singleton
One host per process. Initialize replaces. Shutdown disposes. UninitializedBehavior NoOp applies to writes only. Events, EventReader, Options, Catalog, PeekPendingDisk, Load/Unload require a host.

### 2.7 Engine-owned writer
VestigiumJsonlWriter. Serilog is not a runtime dependency. SerilogAsyncBuffer is an obsolete alias of DiskQueueCapacity.

### 2.8 No live tail
No follow / tail -f API.

## 3. Canonical JSON schema

Field order: DateTime, EVENTID, EVENTNAME, PID, TID, LEVEL, STATUS, APPID, CATEGORY, SUBCATEGORY, MESSAGE, EXCEPTION, CORRELATIONID, PROPERTIES.

| Field | Type | Rules |
|---|---|---|
| DateTime | string | UTC yyyy-MM-ddTHH:mm:ss.fffZ |
| EVENTID | number | Required. 0 is General.Debug. |
| EVENTNAME | string or null | Catalog EventName |
| PID / TID | number | Process and managed thread |
| LEVEL | string | Verbose, Debug, Information, Warning, Error, Fatal |
| STATUS | string | None, Pending, Success, Timeout, Failed, Warning |
| APPID | string | Host AppId or override. Internal: Vestigium.Logging |
| CATEGORY / SUBCATEGORY | string | Taxonomy spelling or Uncategorized / Unregistered |
| MESSAGE | string | JSON-escaped |
| EXCEPTION | string or null | Per ExceptionDetail |
| CORRELATIONID | string or null | Opaque |
| PROPERTIES | object or null | Max 16 keys [A-Za-z][A-Za-z0-9_]*, values ≤256 chars |

Do not emit Info, Warn, INFO, or ERROR.

## 4. Required call-site fields

EVENTID, STATUS, CATEGORY, SUBCATEGORY, MESSAGE required. APPID defaults to Options.AppId. Verbose/Debug/Information do not accept Exception. Warning/Error/Fatal/Write may. Thrown requires an exception.

## 5. Event catalog

### 5.1 Ranges
0–4 general messaging. 5–99 general operations (5 Start … 14 Throttle). 100–4999 embedded exceptions and engine (step 5). 5000+ host custom.

### 5.2 Embedded load
LoadDefault seeds 0–14 and merges embedded EventCatalog shards. Rows below 5000 are reserved.

### 5.3 Resolve
1. Explicit EVENTID must exist and be enabled. 2. Exception FullName then bases; System.Exception only if that is the thrown type. 3. LEVEL → 0/1/2/3/4. Custom FullName ≥5000 wins over embedded.

### 5.4 Engine-owned ids
1 aggregation. 11 taxonomy warning. 14 flood cap warning.

### 5.5 Custom catalog builder
VestigiumCustomCatalog is offline. Open creates root/shards. Empty NextCustomId=5000. Reject EventId < 5000. Add auto-assigns 5000, 5005… Duplicate EventId or FullName throws. Set does not change EventId. Remove does not recycle ids. Get/TryGet/TryGetByFullName/List. Save writes shards/custom.json and index.json (temp + replace) and removes other shard JSON in that folder.

### 5.6 Load / unload on a live host
EventCatalogPath at Initialize merges then freezes. RegisterEvent inside Initialize queues id ≥5000. LoadCustomCatalog after Initialize replaces overlay; 0–4999 stay. UnloadCustomCatalog drops ≥5000. CustomCatalogPath is the loaded path or null. Overlay EventId < 5000 at Initialize throws. RegisterEvent/MergeFromDirectory throw after freeze. Load/Unload use ReplaceCustomFromDirectory/ClearCustom.

## 6. Call-site API

Write(eventId, level, status, category, subcategory, message, exception?, appId?, correlationId?, properties?). Verbose/Debug/Information have no exception. Warning/Error/Fatal may. Thrown(ex, status) looks up catalog (missing row throws). Thrown(ex, status, eventId) requires that id. MESSAGE defaults to exception.Message. CATEGORY/SUBCATEGORY default to the catalog row. LEVEL from shard Severity else Error. STATUS is caller-owned.

## 7. Taxonomy

Defaults: Network (ICMP, TCP, DNS, HTTP, Routing), System (IO, Memory, Threading, Configuration), UI (Navigation, Binding, Input, Lifecycle), Uncategorized/Unregistered. Register only inside Initialize. Ignore-case match, first registered spelling on disk. Unknown pairs rewrite and warn EVENTID 11 APPID Vestigium.Logging. Combine merges suite catalogs. Freeze at end of Initialize.

## 8. Flood protection

Threshold 5, window 30 s, cap 4096. First N in window write full lines. Later hits increment suppressed. 1 s timer writes [Aggregated] Previous message repeated X additional times, STATUS=None, EVENTID 1. Evict expired then oldest idle. 90% cap writes EVENTID 14.

## 9. On-disk layout

Default `%ProgramData%\Vestigium\Logs\{APPID}\`. File `vestigium-{APPID}-yyyyMMdd.json` (same-day roll `-n`). UTF-8 JSONL no BOM. 20 MB roll, 14 days, 90 files, FileShare.Read, queue 10,000 DropOldest, MinimumDiskLevel Information. Flush waits FlushTimeout 5 s and does not stop writes. Shutdown stops accepting and completes writer and channel.

## 10. Disk tripwire

Poll 30 s. Trip at <10% or <5 GB when the floor is enabled. While tripped drop Verbose/Debug before flood. OverrideDiskPressure for Demo/tests. DiskStatus is empty and not tripped before Initialize.

## 11. Subscribers and recent buffer

Channel 10,000 DropOldest, one pump. IObservable for tests. RecentJsonLines cap 200. WrittenCount and SuppressedCount are host counters.

## 12. Exception text

Full = Exception.ToString(). TypeAndMessage = type and message chain. None = JSON null. ExceptionMaxChars 8192; 0 unlimited still clipped at 64 KiB.

## 13. PROPERTIES

Keys [A-Za-z][A-Za-z0-9_]*. Max 16. Values 256 chars. Null values and illegal keys dropped. Empty bag JSON null.

## 14. Lifetime

BindLifetime hooks process exit, console cancel, WPF Exit via reflection. Second bind unsubscribes the first.

## 15. Unflushed disk queue

Flush persists queue. PendingDiskCount is queued not on disk (0 if no host). PeekPendingDisk(n) copies oldest queued lines and does not dequeue. ActiveLogPath is the open file or null. n < 1 throws. Cap LogReadMaxLines default 1000 minimum 1.

## 16. Persisted Head / Tail

VestigiumLogReader.Head/Tail. Files vestigium-{APPID}-*.json name order. Head oldest first complete lines. Tail newest last complete lines. FileShare.ReadWrite. Torn last line dropped. Same n rules as §15. Offline requires directory and appId. No live follow.

## 17. Janitor

DeleteOlderThan(age). age > 0. yyyyMMdd stamp. Skip ActiveLogPath. Locked files SkippedOpen. Returns Deleted, Bytes, SkippedOpen. On-demand. §9 roll still applies.

## 18. Archive

ArchiveOlderThan(age, archiveDirectory). Copy → uppercase SHA256 both files → sidecar `HEX  filename` → delete source on match. Mismatch leaves source, Failed++. Returns Archived, Deleted, Failed, Manifest. No zip. Flush first to include the queue.

## 19. Options defaults

AppId Vestigium; LogDirectory ProgramData\Vestigium\Logs\{AppId}; file 20 MB; retain 14 days / 90 files; flood 5 / 30 s / cap 4096; disk 10% and 5 GB on, poll 30 s; channel 10,000; UI batch 50 / 100 ms; flush 5 s; MinimumDiskLevel Information; disk queue 10,000; recent 200; LogReadMaxLines 1,000; ExceptionDetail Full; ExceptionMaxChars 8192.

## 20. Package identity

PackageId Vestigium.Logging. net10.0. MIT. InternalsVisibleTo Tests. Event catalog JSON EmbeddedResource. PowerShell tools under Resources/PowerShell.

## 21. Public surface (PR01 inventory)

VestigiumLogger, VestigiumLog, VestigiumLogEvent, VestigiumLogLevel, VestigiumStatus, VestigiumLoggerOptions, VestigiumEventCatalog, VestigiumEventDefinition, VestigiumCustomCatalog, VestigiumTaxonomy, VestigiumLogPump, VestigiumLogReader, VestigiumLogJanitor, VestigiumLogArchive, VestigiumDiskStatus, VestigiumExceptionDetail, VestigiumUninitializedBehavior.

## 22. Non-goals

Delimited text; per-line dispatcher; auto-create catalog rows; recycle custom ids; mutate 0–4999; multiple hosts; live tail; zip archives; automatic janitor/archive on Shutdown.

## 23. Requirement trace

R1 schema §3. R2 LEVEL ≠ STATUS. R3 required fields. R4 id ranges. R5 Thrown. R6 custom catalog. R7 flood. R8 JSONL writer. R9 disk tripwire. R10 channel+pump. R11 taxonomy freeze. R12 uninitialized policy. R13 Flush. R14 PendingDiskCount. R15 Peek non-destructive. R16 count 1..LogReadMaxLines. R17 no live tail. R18 Head/Tail complete lines. R19 janitor. R20 archive SHA256 uppercase.
