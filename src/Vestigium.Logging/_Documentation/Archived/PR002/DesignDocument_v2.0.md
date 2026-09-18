# Vestigium.Logging — Design Document

**Document ID:** VEST-LOG-DES-002  
**Version:** 2.0  
**Date:** 18 September 2026  
**Status:** Binding companion to Requirements Specification 2.1  
**Package:** 1.6.0  
**PR:** PR01 close / PR02 open  
**Supersedes:** Design Document 1.4 / 1.0

This document describes how Vestigium.Logging is built. Requirements live in RequirementsSpecification_v2.1.md. This file does not depend on Design 1.x.

## Revision history

| Version | Date | Package | Summary |
|---|---|---|---|
| 1.0–1.4 | 2026-09-18 | 1.3.x | Façade, flood, owned JSONL writer, taxonomy, pump. |
| **2.0** | 2026-09-18 | **1.6.0** | Event catalog, Thrown, custom catalog Load/Unload, Peek/Head/Tail, janitor, SHA256 archive. |

## 1. Intent

Single logging façade for the Vestigium suite and external hosts. Initialize once. Diagnostic threads call VestigiumLog.* and return immediately. Disk, flood, catalog, and UI subscribers are isolated. PowerBI reads UTF-8 JSON Lines. APPID, CATEGORY, SUBCATEGORY, EVENTID, STATUS required.

## 2. Projects

Vestigium.Logging (net10.0, no WPF): Catalog/, Disk/, Flood/, Formatting/, IO/, Lifetime/, Observability/, EventCatalog shards, Resources/PowerShell, _Documentation. Tests xUnit collection VestigiumLogger. Demo net10.0-windows WPF. InternalsVisibleTo Tests. MIT.

## 3. Runtime shape

VestigiumLog.Write/Thrown → Catalog.Resolve → Taxonomy.Normalize → disk gate (drop Verbose/Debug if tripped) → FloodTracker.Observe → drop or aggregation EVENTID 1 or VestigiumLogEvent → writer Enqueue + Channel + Subject + RecentJsonLines. Pump 50/100ms → WeakReferenceMessenger.

## 4. Process host

Initialize: options, LoadDefault, EventCatalogPath merge, RegisterEvent queue, Freeze catalog+taxonomy, start writer/timer/poll. Shutdown stops accepting and Completes writer. Flush drains flood+disk, writes continue. BindLifetime → Shutdown. LoadCustomCatalog = ReplaceCustomFromDirectory. UnloadCustomCatalog = ClearCustom. PeekPendingDisk / PendingDiskCount / ActiveLogPath. Host is internal sealed partial. UninitializedBehavior Throw or NoOp for writes only.

## 5. Call-site façade

EventId first. Verbose/Debug/Information have no Exception. Thrown looks up FullName/bases (not System.Exception unless exact). Thrown with eventId requires that id. STATUS caller-owned. LEVEL from shard Severity else Error. Internal lines use ids 1, 11, 14.

## 6. Event catalog

VestigiumEventDefinition record. Catalog _byId + _byFullName. 0–14 seeded generals. 100–4999 embedded shards step 5. 5000+ overlay. Resolve: explicit id → exception type → LEVEL 0–4. Freeze blocks Merge/RegisterCustom. ClearCustom/ReplaceCustomFromDirectory allowed after freeze. VestigiumCustomCatalog Open rejects <5000. Add auto 5000,5005… Remove does not reuse ids. Save atomic custom.json + index.json.

## 7. Taxonomy

Ignore-case to first registered spelling. Defaults Network/System/UI/Uncategorized. Unknown → rewrite + EVENTID 11 APPID Vestigium.Logging. Freeze at Initialize. Combine merges suite catalogs.

## 8. Flood

Identity record (AppId, Category, Level, Message). First 5 in 30 s write. Then suppress. DrainExpired writes aggregation EVENTID 1 STATUS=None. Cap 4096 evicts expired then idle. 90% cap EVENTID 14.

## 9. JSON and properties

VestigiumJsonFormatter never omits null EXCEPTION/CORRELATIONID/PROPERTIES. DateTime UTC yyyy-MM-ddTHH:mm:ss.fffZ. PropertyBag: legal keys, max 16, 256 chars. ExceptionFormatter Full/TypeAndMessage/None, 8192 / 64 KiB.

## 10. Disk writer

IVestigiumJsonlWriter Enqueue/Flush/Complete/QueuedCount/Peek/ActivePath. One consumer. FileShare.Read. vestigium-{APPID}-yyyyMMdd.json, 20 MB or date roll, -n same day. Retain 14 days / 90 files. MinimumDiskLevel filters disk only. Peek is ToArray copy.

## 11. Reader, janitor, archive

File APIs. Flush first to include the queue. Reader Head/Tail complete lines, FileShare.ReadWrite, drop torn last line, cap LogReadMaxLines. Janitor yyyyMMdd cutoff, skip ActiveLogPath. Archive copy → uppercase SHA256 both → sidecar HEX  filename → delete source. Mismatch leaves source Failed++. No zip.

## 12. Disk tripwire

DriveInfo 30 s. Trip <10% or <5 GB. OverrideDiskPressure for Demo. DiskStatus empty before Initialize.

## 13. Subscribers

Subject swallows OnNext exceptions. Channel 10k DropOldest one pump. RecentJsonLines cap 200.

## 14. Failure policy

Poll exceptions swallowed. Queues DropOldest. Flush timeout does not throw. Exit paths Shutdown. Unknown EVENTID / Thrown miss throw on caller. Writes after Shutdown throw unless NoOp. LifetimeBinder reflects Exit.

## 15. WPF demo

OnStartup Initialize + BindLifetime. PumpAsync + messenger + one dispatcher marshal per batch. Apply re-Initialize.

## 16. Threading

Writes on caller. Flood Observe on caller, Drain on 1 s timer. Writer dedicated task. Disk poll 30 s. No file I/O lock on the probe path.

## 17. Tests

Collection VestigiumLogger serializes host tests. Offline reader/janitor/archive/custom Open do not need the host.

## 18. Mapping to SRS 2.1

R1–R3 §§3,5,9. R4–R6 §6. R7 §8. R8 §10. R9 §12. R10 §13. R11 §7. R12 §4. R13–R17 §§4,10,11. R18 §11.1. R19 §11.2. R20 §11.3.
