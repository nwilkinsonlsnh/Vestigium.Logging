# Vestigium.Logging — Design Document

**Document ID:** VEST-LOG-DES-002  
**Version:** 2.2  
**Status:** Binding with SRS 2.3  
**Date:** 18 September 2026  
**Supersedes:** Design 2.1 (archived)

## Revision history

| Version | Date | Summary |
|---|---|---|
| 1.0 | 2026 | First design: façade, writer, flood, taxonomy, pump. |
| 2.0 | 2026-09-18 | Catalog shards, required EVENTID, Thrown, custom overlay. |
| 2.1 | 2026-09-18 | Reader, janitor, archive SHA256, pending-disk peek. |
| **2.2** | **2026-09-18** | Operations log stream. Seal key ring + in-file trailer + Verify peel. Opt-in archive/janitor with separate host and operations destinations. Writer count-only retention. |

This document describes how the library is built. Requirements live in `RequirementsSpecification_v2.3.md`. Call-site recipes live in `DevelopersGuide_v2.2.md`.

## 1. Process shape

One `VestigiumLogger` host per process. `Initialize` constructs options, catalog, taxonomy snapshot, flood tracker, disk monitor, host JSONL writer, and (when enabled) operations JSONL writer and seal ring. `Shutdown` drains both writers, appends trailers if sealing is on, and disposes timers and the channel.

Libraries may call `VestigiumLog` before Initialize when `UninitializedBehavior = NoOp`. Catalog, Events, and housekeeping APIs require a host.

## 2. Write path

```
VestigiumLog.*
  → VestigiumLogger.Emit
      → require EVENTID / STATUS / CATEGORY / SUBCATEGORY / MESSAGE
      → taxonomy normalize
      → catalog resolve (explicit id, else exception type, else general 0–4)
      → flood Observe
      → format JSON line
      → channel + recent buffer
      → disk queue if LEVEL ≥ MinimumDiskLevel and disk not dropping Verbose/Debug
      → VestigiumJsonlWriter.Enqueue
```

`Thrown` without an id uses `TryGetByException`. Miss writes EVENTID 3 plus DEBUG EVENTID 0 via `UncataloguedExceptionAdvisor`. At 20 distinct unknown types a second DEBUG line recommends a custom catalog.

## 3. Catalog

Embedded JSON shards ship as `EmbeddedResource` under `EventCatalog/`. `LoadDefault` merges generals 0–14, exception shards 100–4999, and operations shard 5000–9999 (`vestigium.logging.ops.json`).

`VestigiumCustomCatalog` is a separate dictionary keyed at ≥ 10000. `LoadCustomCatalog` replaces only that overlay. `UnloadCustomCatalog` drops ≥ 10000.

Resolve order is explicit id → exception FullName / bases → LEVEL general. Custom FullName wins over embedded FullName.

## 4. Writers

`VestigiumJsonlWriter` is a single-consumer pump over a bounded queue.

- Open append, `FileShare.Read`
- Date roll at UTC midnight; size roll at `FileSizeLimitBytes`
- `ApplyRetention` deletes oldest **inactive** files while count > `RetainedFileCountLimit`
- `RetainedFileCountLimit = 0` keeps no inactive files
- Age is not applied here
- `CloseStream` flushes, disposes the handle, then `TryAppendTrailer` when a ring is attached

Host and operations logs are two writer instances. They share options for size/count/seal flags; they do not share paths.

## 5. Sealing

`VestigiumSealKeyRing` stores `{ keyId, alg, material }` as JSON. `Open` creates the file when missing. `OpenOrCreate(options)` uses `LogSealKeyPath` or `%ProgramData%\Vestigium\Logging\keys\{AppId}.json` and rejects a mismatched `LogSealKeyId`.

On close: SHA256 content (uppercase hex), HMAC-SHA256 signature (Base64), append one JSON object with `VESTIGIUM_TRAILER`.

`VestigiumLogSeal.Verify` results: MissingFile, NoTrailer, Torn, KeyMismatch, HashMismatch, BadSignature, Valid. `LineCount` is informational; non-numeric values are ignored. Head/Tail skip trailer lines.

## 6. Operations log

When `OperationsLogEnabled` (default true) the host constructs a second writer at `ResolveOperationsLogDirectory()` with APPID `Vestigium.Logging`. Initialize emits 5000; Shutdown 5005; roll 5010; seal 5015/5020; archive 5030/5035; tripwire 5060; catalog load/unload 5065/5070.

## 7. Archive and janitor

Both are **opt-in**. Flags and destinations are validated at Initialize.

Archive: eligibility → AcceptSeal when a ring exists (Valid or NoTrailer) → copy → uppercase SHA256 both sides → sidecar `{name}.sha256` → delete source. IOException or hash mismatch increments Failed and leaves the source.

Janitor: eligibility → skip live path → delete. Locked files increment SkippedOpen.

Wrappers refuse to run when the matching flag is off or the matching dest/age is missing.

## 8. Disk monitor

`DiskSpaceMonitor` polls `DriveInfo`. Trip is percent floor or optional byte floor. `Override` is a test/demo latch. `ReadyOverride` is tests-only (InternalsVisibleTo), not product API.

## 9. Flood

`FloodTracker` is a bounded concurrent map of `FloodIdentity`. EvictOverflow drops expired, then oldest idle, then oldest pending.

## 10. Pump and lifetime

`VestigiumLogPump.RunAsync` batches by size or interval. `LifetimeBinder` reflects instance `Exit`. Remove failures are swallowed.

## 11. Files of record

| Concern | Type / file |
|---|---|
| Façade | `VestigiumLog.cs` |
| Host | `VestigiumLogger*.cs` |
| Writer | `IO/VestigiumJsonlWriter.cs` + `.Seal.cs` |
| Seal | `Seal/VestigiumLogSeal.cs`, `Seal/VestigiumSealKeyRing.cs` |
| Archive / janitor / reader | `IO/VestigiumLogArchive.cs`, `Janitor/`, `VestigiumLogReader.cs` |
| Catalog | `Catalog/` |
| Ops shard | `EventCatalog/vestigium.logging.ops.json` |

## 12. Collaboration

Architect: Nathaniel Wilkinson. Developer: Grok (xAI). Process: `Collaboration.md`. License: repository root `LICENSE` (MIT).
