# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-003  
**Version:** 2.3  
**Status:** Binding  
**Date:** 18 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026  
**Package:** 1.7.0  
**Supersedes:** SRS 2.2 (archived under `_Documentation/Archived`)

This document is the single requirements specification for the Vestigium.Logging library. It does not depend on earlier SRS files.

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.2 | 2026 | 1.2.x | JSON Lines schema, flood, taxonomy, disk tripwire, WPF pump, process singleton. |
| 1.3 | 2026 | 1.3.0 | Owned `VestigiumJsonlWriter`. Serilog removed from runtime. |
| 1.4 | 2026-09-18 | 1.4.0 | Required EVENTID. eventId first on the façade. Thrown. Verbose/Debug/Information do not take Exception. |
| 2.0 | 2026-09-18 | 1.5.0 | Event catalog reserved / custom split. VestigiumCustomCatalog CRUD + Save. Load / Unload. |
| 2.1 | 2026-09-18 | 1.6.0 | PeekPendingDisk, Head/Tail reader, on-demand janitor, SHA256 archive. |
| 2.2 | 2026-09-18 | 1.6.0 | Uncatalogued Thrown writes EVENTID 3 plus a DEBUG hint. After 20 distinct unknown types, recommend a custom catalog. |
| **2.3** | **2026-09-18** | **1.7.0** | EVENTID ranges: 0–4999 library, 5000–9999 operations, ≥ 10000 custom. Operations log default on. HMAC-SHA256 seal trailer opt-in. Archive and janitor opt-in with separate host/ops destinations. Writer count-only retention. |

## 1. Objective

Vestigium.Logging is the single logging façade for the Vestigium suite and for hosts outside the suite. It owns schema, flood protection, taxonomy, event catalog, JSON Lines persistence, operations logging, optional sealing, log-file housekeeping, and subscriber fan-out. PowerBI ingests one JSON object per line. APPID, CATEGORY, SUBCATEGORY, EVENTID, and STATUS are required on every emitted record.

## 2. Architectural constraints (binding)

### 2.1 Format is flat JSON Lines
On-disk format is UTF-8 JSON Lines, no BOM. One object per line. MESSAGE and EXCEPTION may contain newlines; they are JSON-escaped strings, never extra records.

A seal trailer, when enabled, is one extra JSON object on the last line. It is not a log event. Head/Tail skip it. PowerBI must ignore any line whose object contains `VESTIGIUM_TRAILER`.

### 2.2 LEVEL is severity; STATUS is outcome
LEVEL is Verbose, Debug, Information, Warning, Error, or Fatal. STATUS is None, Pending, Success, Timeout, Failed, or Warning. LEVEL never stores Success, Failed, or Timeout.

### 2.3 Flood identity is a four-field composite
Identity is `(APPID, CATEGORY, LEVEL, MESSAGE)` ordinal, case-sensitive.

### 2.4 Subscribers must not touch the WPF dispatcher per line
Bounded `Channel<VestigiumLogEvent>` (10,000, DropOldest). UI hosts drain with `VestigiumLogPump`.

### 2.5 Retention first, then a dual free-space tripwire
Rolling files cap footprint by **count**. Age-delete is not performed by the writer. Disk gate: 10 percent or 5 GB unless `DiskBytesFloorEnabled` is false.

### 2.6 Process singleton
One host per process. Catalog, Events, PeekPendingDisk, Load/Unload require a host.

### 2.7 Engine-owned writer
Disk I/O is `VestigiumJsonlWriter`. Serilog is not a runtime dependency.

### 2.8 No live tail
Head and Tail read complete persisted lines only.

## 3. Canonical JSON schema

Field order: DateTime, EVENTID, EVENTNAME, PID, TID, LEVEL, STATUS, APPID, CATEGORY, SUBCATEGORY, MESSAGE, EXCEPTION, CORRELATIONID, PROPERTIES.

| Field | Type | Rules |
|---|---|---|
| DateTime | string | UTC `yyyy-MM-ddTHH:mm:ss.fffZ` |
| EVENTID | number | Required. `0` is General.Debug. |
| EVENTNAME | string or null | Catalog EventName |
| PID / TID | number | Process / managed thread |
| LEVEL | string | Verbose, Debug, Information, Warning, Error, Fatal |
| STATUS | string | None, Pending, Success, Timeout, Failed, Warning |
| APPID | string | Host AppId or override. Operations use `Vestigium.Logging` |
| CATEGORY / SUBCATEGORY | string | Taxonomy spelling or Uncategorized / Unregistered |
| MESSAGE | string | JSON-escaped |
| EXCEPTION | string or null | Per ExceptionDetail |
| CORRELATIONID | string or null | |
| PROPERTIES | object or null | Flat string map |

## 4. Required call-site fields

Every public write supplies EVENTID, STATUS, CATEGORY, SUBCATEGORY, and MESSAGE. Verbose / Debug / Information do not accept Exception. Thrown requires an exception instance.

## 5. Event catalog

### 5.1 Ranges

| IDs | Owner |
|---|---|
| 0–4 | General messaging |
| 5–99 | General operations helpers |
| 100–4999 | Embedded .NET exceptions and engine |
| 5000–9999 | Vestigium.Logging operations log. Hosts must not assign these. |
| ≥ 10000 | Host custom overlay |

### 5.2 Embedded load
`LoadDefault` seeds 0–14, merges exception shards, and loads `vestigium.logging.ops.json` for 5000–9999.

### 5.3 Resolve
1. Explicit EVENTID must exist and be enabled.
2. Else exception FullName, then bases. `System.Exception` matches only the exact type.
3. Else LEVEL → 0 / 1 / 2 / 3 / 4.
Custom FullName ≥ 10000 wins over embedded FullName.

### 5.4 Engine-owned host ids
1 flood aggregation; 11 taxonomy warning; 14 flood cap warning.

### 5.5 Operations ids
5000 start; 5005 stop; 5010 roll; 5015 sealed; 5020 seal fail; 5030 archived; 5035 archive skip; 5060 tripwire; 5065 load catalog; 5070 unload catalog.

### 5.6 Custom catalog builder
`VestigiumCustomCatalog.Open` does not require Initialize. Empty NextCustomId is **10000**. Load rejects EventId < 10000. Add steps by 5. Remove does not recycle ids. Save writes `shards/custom.json` and `index.json`.

### 5.7 Load / unload
`LoadCustomCatalog` replaces overlay ≥ 10000. `UnloadCustomCatalog` drops ≥ 10000. Overlay file with EventId < 10000 throws.

## 6. Call-site API

Write / Verbose / Debug / Information / Warning / Error / Fatal / Thrown (lookup) / Thrown (explicit id).

Uncatalogued Thrown writes EVENTID 3 plus DEBUG EVENTID 0. At 20 distinct unknown types, recommend VestigiumCustomCatalog ≥ 10000.

## 7. Taxonomy
Defaults: Network, System, UI, Uncategorized. Unknown pairs rewrite and emit EVENTID 11 from APPID Vestigium.Logging. Freeze after Initialize.

## 8. Flood protection
Identity (APPID, CATEGORY, LEVEL, MESSAGE). Default threshold 5 / 30 s / cap 4096. Aggregation EVENTID 1.

## 9. On-disk layout
Host default `%ProgramData%\Vestigium\Logs\{APPID}\`. Ops default `%ProgramData%\Vestigium\Logging\`. Name `vestigium-{APPID}-yyyyMMdd.json`. 20 MB roll. Count retain 90 inactive files; 0 keeps none. Writer does not age-delete. FileShare.Read. Queue 10,000 DropOldest. MinimumDiskLevel Information.

## 10. Disk tripwire
Poll 30 s. 10% or 5 GB. Ops EVENTID 5060 when operations logging is on.

## 11–14. Subscribers, exceptions, properties, lifetime
Channel 10,000. RecentJsonLines 200. ExceptionDetail Full default, max 8192. PROPERTIES max 16 keys. BindLifetime hooks process / console / WPF Exit.

## 15–16. Queue and Head/Tail
Flush, PendingDiskCount, PeekPendingDisk, ActiveLogPath. Head/Tail skip VESTIGIUM_TRAILER. n is 1…LogReadMaxLines (default 1000). No live follow.

## 17. Janitor (opt-in)
Off by default. `JanitorEnabled` + `JanitorMaxAge` required for DeleteHostLogs. Separate OperationsJanitorEnabled + OperationsJanitorMaxAge. Age is yyyyMMdd stamp. Skip ActiveLogPath.

## 18. Archive (opt-in)
Off by default. Destination mandatory when enabled. Separate host and operations destinations. Seal gate: Valid or NoTrailer may proceed; else Failed + EVENTID 5035. Copy, uppercase SHA256 both sides, write `{name}.sha256`, delete source, EVENTID 5030.

## 19. Operations log
Enabled by default. APPID Vestigium.Logging. EVENTID 5000–9999. Same schema and writer rules. Host may set OperationsLogEnabled = false.

## 20. Sealing (opt-in)
Off by default. One HMAC-SHA256 key ring per AppId. Trailer last line. Verify results: Valid, NoTrailer, Torn, HashMismatch, BadSignature, MissingFile, KeyMismatch. Non-numeric LineCount ignored. LogSealKeyId must match the ring when set.

## 21. Options defaults
AppId Vestigium. OperationsLogEnabled true. LogSealEnabled false. ArchiveEnabled false. JanitorEnabled false. RetainedFileCountLimit 90. RetainedFileTimeLimit unused by the writer.

## 22. Package identity
PackageId Vestigium.Logging. net10.0. MIT. Catalog JSON EmbeddedResource. Collaboration.md and Contributors.md document the team.

## 23. Public surface
VestigiumLogger, VestigiumLog, VestigiumLogEvent, levels/status, options, catalogs, taxonomy, pump, reader, janitor, archive, VestigiumLogSeal, VestigiumSealKeyRing, VestigiumSealVerifyResult, VestigiumDiskStatus.

## 24. Non-goals
Delimited sinks; per-line dispatcher; auto-create catalog rows; recycle custom ids; mutate 0–9999; host use of 5000–9999; multi-host process; live tail; zip archives; sidecar-only seal; automatic janitor/archive on Shutdown; product API for test hooks.

## 25. Requirement trace

| ID | Requirement |
|---|---|
| R1 | JSON Lines schema |
| R2 | LEVEL ≠ STATUS |
| R3 | Required EVENTID, APPID, CATEGORY, SUBCATEGORY, STATUS |
| R4 | Ranges 0–4999 / 5000–9999 / ≥ 10000 |
| R5 | Thrown + uncatalogued hint |
| R6 | Custom catalog ≥ 10000 |
| R7 | Flood composite identity |
| R8 | Owned writer, count retain |
| R9 | Dual disk tripwire |
| R10 | Bounded channel + pump |
| R11 | Taxonomy freeze |
| R12 | Uninitialized writes Throw or NoOp |
| R13–R16 | Flush, pending, peek, read cap |
| R17–R18 | No live tail; Head/Tail skip trailer |
| R19 | Janitor opt-in |
| R20 | Archive opt-in + SHA256 |
| R21 | Operations log default on |
| R22 | Seal opt-in |
| R23 | Archive requires Valid or NoTrailer when sealed |
