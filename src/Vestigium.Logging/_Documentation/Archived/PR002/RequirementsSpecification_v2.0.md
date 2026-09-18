# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-002  
**Version:** 2.0  
**Status:** Binding for package 1.5.x  
**Date:** 18 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026  
**Supersedes:** SRS 1.11 / package 1.2–1.4 call-site and catalog addenda

## 1. Objective

Vestigium.Logging is the single logging façade for the Vestigium suite and for hosts outside the suite. It owns schema, flood protection, taxonomy, event catalog, JSON Lines persistence, and subscriber fan-out. PowerBI ingests one JSON object per line. APPID, CATEGORY, SUBCATEGORY, EVENTID, and STATUS are required on every emitted record.

## 2. Architectural constraints (binding)

### 2.1 Format is flat JSON Lines
On-disk format is UTF-8 JSON Lines, no BOM. One object per line. MESSAGE and EXCEPTION may contain newlines; they are JSON-escaped strings, never extra records.

### 2.2 LEVEL is severity; STATUS is outcome
LEVEL is Verbose, Debug, Information, Warning, Error, or Fatal. STATUS is None, Pending, Success, Timeout, Failed, or Warning. LEVEL never stores Success, Failed, or Timeout.

### 2.3 Flood identity is a four-field composite
Identity is `(APPID, CATEGORY, LEVEL, MESSAGE)` ordinal, case-sensitive. STATUS, SUBCATEGORY, CORRELATIONID, PROPERTIES, EVENTID, and EVENTNAME are not part of identity.

### 2.4 Subscribers must not touch the WPF dispatcher per line
No synchronous C# event per line. Events go through a bounded `Channel<VestigiumLogEvent>` (10,000, DropOldest). UI hosts drain with `VestigiumLogPump` (50 events or 100 ms).

### 2.5 Retention first, then a dual free-space tripwire
Rolling files cap footprint. The disk gate trips when free space is below 10 percent **or** below 5 GB, unless `DiskBytesFloorEnabled` is false.

### 2.6 Process singleton
One host per process. Libraries that log before startup use `UninitializedBehavior = NoOp` for writes only.

### 2.7 Engine-owned writer
Disk I/O is `VestigiumJsonlWriter`. Serilog is not a runtime dependency.

## 3. Canonical JSON schema

Field order: DateTime, EVENTID, EVENTNAME, PID, TID, LEVEL, STATUS, APPID, CATEGORY, SUBCATEGORY, MESSAGE, EXCEPTION, CORRELATIONID, PROPERTIES.

| Field | Type | Rules |
|---|---|---|
| DateTime | string | UTC `yyyy-MM-ddTHH:mm:ss.fffZ` |
| EVENTID | number | Required. `0` is General.Debug. |
| EVENTNAME | string or null | Catalog EventName |
| PID / TID | number | Process and managed thread |
| LEVEL | string | Verbose, Debug, Information, Warning, Error, Fatal |
| STATUS | string | None, Pending, Success, Timeout, Failed, Warning |
| APPID | string | Host AppId or per-call override. Internal: `Vestigium.Logging` |
| CATEGORY / SUBCATEGORY | string | Taxonomy spelling, else Uncategorized / Unregistered |
| MESSAGE | string | JSON-escaped |
| EXCEPTION | string or null | Per ExceptionDetail |
| CORRELATIONID | string or null | Opaque |
| PROPERTIES | object or null | Max 16 keys `[A-Za-z][A-Za-z0-9_]*`, values ≤ 256 chars |

## 4. Required call-site fields

EVENTID, STATUS, CATEGORY, SUBCATEGORY, MESSAGE required. APPID defaults to Options.AppId. Verbose / Debug / Information do not accept Exception. Warning / Error / Fatal / Write may. Thrown requires an exception.

## 5. Event catalog

| IDs | Owner |
|---|---|
| 0–4 | General messaging |
| 5–99 | General operations (5 Start … 14 Throttle) |
| 100–4999 | Embedded exceptions + engine |
| 5000+ | Host custom |

Resolve: explicit id (must exist) → exception FullName / bases (`System.Exception` only if that is the thrown type) → LEVEL 0–4.

Engine ids: aggregation **1**, taxonomy warning **11**, flood cap **14**.

`VestigiumCustomCatalog`: Open, Add/Get/Set/Remove/List, Save to `shards/custom.json` + `index.json`. No id reuse. Reject `< 5000`.

Live host: `EventCatalogPath` at Initialize, `RegisterEvent` inside Initialize, `LoadCustomCatalog(path)`, `UnloadCustomCatalog()`, `CustomCatalogPath`. Load replaces overlay. Unload drops ≥ 5000 only.

## 6. Call-site API

`Write(eventId, level, status, category, subcategory, message, …)`  
`Verbose` / `Debug` / `Information(eventId, status, …)` — no Exception  
`Warning` / `Error` / `Fatal(eventId, status, …, exception?)`  
`Thrown(ex, status)` and `Thrown(ex, status, eventId)`

## 7. Taxonomy

Defaults: Network (ICMP, TCP, DNS, HTTP, Routing), System (IO, Memory, Threading, Configuration), UI (Navigation, Binding, Input, Lifecycle), Uncategorized/Unregistered. Freeze after Initialize. Unknown pairs rewrite and warn EVENTID 11.

## 8. Flood

Threshold 5, window 30 s, cap 4096. Aggregation line EVENTID 1. Cap warning EVENTID 14.

## 9. Disk

`%ProgramData%\Vestigium\Logs\{APPID}\`  
`vestigium-{APPID}-yyyyMMdd.json`  
20 MB roll, 14 days, 90 files, FileShare.Read, queue 10,000 DropOldest, MinimumDiskLevel Information. Dual tripwire 10% or 5 GB.

## 10. Subscribers

Channel 10,000 DropOldest. One VestigiumLogPump. RecentJsonLines cap 200.

## 11. Exceptions

Full / TypeAndMessage / None. Default Full, 8192 chars, hard clip 64 KiB.

## 12. Lifetime

BindLifetime: process exit, console cancel, WPF Exit via reflection.

## 13. Non-goals

Delimited text; per-line dispatcher; auto-create catalog rows; recycle custom ids; mutate 0–4999; multiple hosts per process.

## 14. Trace

R1 schema ·3 · R2 LEVEL ≠ STATUS · R3 required fields · R4 id ranges · R5 Thrown · R6 custom catalog · R7 flood · R8 JSONL writer · R9 disk · R10 channel · R11 taxonomy freeze · R12 uninitialized policy
