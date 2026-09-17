# Vestigium.Logging — Software Requirements Specification

**Document ID:** VEST-LOG-SRS-001  
**Version:** 1.3  
**Status:** Ready to implement  
**Date:** 6 September 2026  
**Target:** .NET 10 LTS, Visual Studio 2026, WPF MVVM

## 1. Objective

Develop a robust, subscribable, centralized logging module named **Vestigium.Logging**, optimized for PowerBI ingestion. Serilog is the engine. Flat JSON is the only on-disk format. Asynchronous queueing and resource-aware safeguards apply across PingIQ, TraceIQ, DnsIQ, HttpIQ, and any later Vestigium module that registers an APPID.

## 2. Architectural constraints (binding)

### 2.1 Format is flat JSON, never delimited text

Pipe- or tab-delimited lines break as soon as HttpIQ or TraceIQ logs a stack trace, an HTTP body, or any unexpected newline. PowerBI must ingest one JSON object per event. Multiline MESSAGE and EXCEPTION values are JSON-escaped strings, not extra records.

### 2.2 LEVEL is severity; STATUS is outcome

A failed ICMP echo during a continuous PingIQ monitor is Information or Debug with `STATUS=Timeout`. A failure to write the local database is Error with `STATUS=Failed`. LEVEL never stores Success, Failed, or Timeout.

### 2.3 Identical means four-field composite

PingIQ reporting “Timeout” and TraceIQ reporting “Timeout” use separate flood counters. Identity is the in-memory value tuple `(APPID, CATEGORY, LEVEL, MESSAGE)`. Do not concatenate those four fields with a delimiter.

### 2.4 Subscribers must not touch the WPF dispatcher per line

No synchronous C# event per log line. Events go through a bounded `System.Threading.Channels.Channel<VestigiumLogEvent>`. ViewModels drain in batches of 50 or every 100 ms, then marshal once onto the UI thread via CommunityToolkit.Mvvm `WeakReferenceMessenger`.

### 2.5 Retention first, then a dual free-space tripwire

Rolling files cap footprint. Monitoring trips when free space is below 10 percent **or** below 5 GB, whichever happens first.

## 3. Binding requirements

### 3.1 Canonical JSON schema

One JSON object per line (JSON Lines). Property names and casing are mandatory.

| Field | Type | Rules |
|---|---|---|
| DateTime | string | UTC ISO 8601 with milliseconds: `yyyy-MM-ddTHH:mm:ss.fffZ` |
| PID | number | `Process.GetCurrentProcess().Id` |
| TID | number | `Environment.CurrentManagedThreadId` |
| LEVEL | string | Verbose, Debug, Information, Warning, Error, Fatal |
| STATUS | string | None, Pending, Success, Timeout, Failed, Warning |
| APPID | string | PingIQ, TraceIQ, DnsIQ, HttpIQ, or Vestigium.Logging |
| CATEGORY | string | Registered catalog, else Uncategorized |
| SUBCATEGORY | string | Linked to category, else Unregistered |
| MESSAGE | string | JSON-escaped. Never split across output lines. |
| EXCEPTION | string or null | `Exception.ToString()` including inners, or JSON null |
| CORRELATIONID | string or null | Opaque host-supplied id. JSON null when omitted. Not part of flood identity. |

Do not emit Info, Warn, INFO, or ERROR.

### 3.2 On-disk layout

| Item | Value |
|---|---|
| Root folder | `%ProgramData%\Vestigium\Logs\{APPID}\` |
| File name pattern | `vestigium-{APPID}-.json` (Serilog rolling suffix appended) |
| Format | UTF-8, one JSON object per line, no BOM |
| File size cap | 20 MB (20,971,520 bytes) then roll |
| Time retention | 14 days |
| Count retention | 90 files per APPID |
| Shared | `shared: true` |

### 3.3 Asynchronous queueing

Every public log call returns after enqueue. Disk I/O runs on Serilog’s Async sink (`bufferSize` 10,000, `blockWhenFull: false`). Subscriber fan-out uses a bounded channel of 10,000 with `FullMode = DropOldest`. UI drain: `VestigiumLogPump` every 100 ms or 50 events, whichever first.

### 3.4 Flood protection

| Parameter | Value |
|---|---|
| Identity | `(APPID, CATEGORY, LEVEL, MESSAGE)` ordinal, case-sensitive. STATUS and SUBCATEGORY are not part of identity. |
| FloodThresholdCount | 5 |
| FloodWindowMs | 30,000 |
| FloodIdentityCap | 4,096 distinct identities. Expired keys are removed. Idle keys evicted first; pending suppressed is flushed then dropped. |
| Suppression start | Event 6 and later of the same identity inside the window |
| Summary line | `[Aggregated] Previous message repeated {X} additional times` |
| Summary LEVEL | Same as the suppressed identity |
| Summary STATUS | None |
| Summary SUBCATEGORY | Last subcategory observed for that identity |
| Reset | Window expiry (`DrainExpired` every 1 second) or next observe after expiry |

Worked example: 22 identical PingIQ Information events in 8 seconds produce 5 full lines. Events 6–22 are suppressed (X = 17). A simultaneous TraceIQ “Timeout” does not increment this counter.

### 3.5 Guaranteed flush

Subscribe to `Application.Current.Exit` when running under WPF (`VestigiumLogger.BindLifetime` uses reflection on an `Exit` event so the engine stays `net10.0`), `AppDomain.CurrentDomain.ProcessExit` for all hosts, and `Console.CancelKeyPress` when a console host is detected.

| Method | Behavior |
|---|---|
| `Flush()` / `Flush(timeout)` | Drain flood summaries and wait up to `FlushTimeout` (default 5 seconds) for the async file sink. **Writes keep being accepted.** |
| `Shutdown()` | Stop accepting writes, drain, wait, dispose Serilog, complete subscribers. |
| ProcessExit / Ctrl+C / WPF `Exit` | Call `Shutdown()`. |

`FlushTimeout` = 5 seconds. A timeout returns without throwing.

### 3.6 Disk space tripwire

Poll every 30 seconds. Trip when `AvailableFreeSpace < 10% of TotalSize` **OR** (when `DiskBytesFloorEnabled`, default true) `AvailableFreeSpace < 5 GB`. While tripped, drop Verbose and Debug. Information and above still flow. Next poll above both thresholds lifts the throttle. Hosts on small volumes set `DiskBytesFloorEnabled = false`. `VestigiumLogger.DiskStatus` exposes the last snapshot without throwing when uninitialized.

### 3.7 Initialization and taxonomy

Calling any log method before `VestigiumLogger.Initialize` throws `InvalidOperationException` with message `VestigiumLogger.Initialize must run during application startup.`

Default taxonomy:

| Category | Subcategories |
|---|---|
| Network | ICMP, TCP, DNS, HTTP, Routing |
| System | IO, Memory, Threading, Configuration |
| UI | Navigation, Binding, Input, Lifecycle |

Unknown CATEGORY → Uncategorized. Unknown SUBCATEGORY → Unregistered. Matching is ordinal-ignore-case; stored values use the first registered spelling. Blank CATEGORY or SUBCATEGORY also rewrite. One internal Warning is written (`APPID=Vestigium.Logging`, `CATEGORY=System`, `SUBCATEGORY=Configuration`) and is itself flood-protected.

Product/helper catalogs are registered by the host (`cfg.RegisterTaxonomy(...)` or `VestigiumTaxonomy.Combine`). This library does not enumerate ClosedXml / FileIo / Kql.

### 3.8 Public log API

Call sites use explicit STATUS:

```csharp
VestigiumLog.Write(
    level: VestigiumLogLevel.Information,
    status: VestigiumStatus.Timeout,
    category: "Network",
    subcategory: "ICMP",
    message: $"Echo request to {host} timed out after {ms} ms");
```

### 3.9 Subscribability

- `IObservable<VestigiumLogEvent> VestigiumLogger.Events` — tests and tools. Do not subscribe on a WPF View.
- `ChannelReader<VestigiumLogEvent> VestigiumLogger.EventReader` — UI hosts drain this off the dispatcher.
- Structured object, never a pre-rendered string only
- WPF ViewModels subscribe with `WeakReferenceMessenger` after draining the channel on a background Task

Libraries that log without a host set `VestigiumLogger.UninitializedBehavior = NoOp`. Default remains `Throw`. Only writes are soft; `Events` / `EventReader` / `Options` still require `Initialize`.

## 4. Acceptance criteria

1. A MESSAGE containing pipes, tabs, quotes, and a four-line stack trace writes as one JSON object.
2. LEVEL / STATUS are compile-time enums.
3. PingIQ Timeout and TraceIQ Timeout maintain independent flood counters.
4. 22 identical PingIQ events in 8 seconds produce 5 full lines; suppressed count is 17.
5. A 10,000-event/second background loop leaves the WPF UI responsive; dispatcher receives at most one batch every 100 ms.
6. Normal process exit calls `Shutdown`, which flushes the Serilog buffer.
7. A 20 MB file rolls; files older than 14 days or beyond the 90-file cap are deleted.
8. When free space is 4 GB on a 40 GB volume, the 5 GB floor trips; Verbose/Debug stop; Information still writes.
9. An unknown category stores `CATEGORY=Uncategorized` and emits the internal configuration warning.

## 5. Non-goals (v1)

- No OpenTelemetry export
- No remote syslog/HTTP shipper
- No runtime invention of categories
- No delimited or CSV output path

## 6. Document control

| Version | Change | Source |
|---|---|---|
| 1.0 | Gemini-revised requirements (`$$` on FloodThresholdCount) | Gemini thread |
| 1.1 | Conversation captured | Grok |
| 1.2 | Placeholders filled | Grok, 6 Sep 2026 |
| 1.3 | Rolling files: 20 MB, 14-day retention, 90-file cap | Stakeholder request, 6 Sep 2026 |
| 1.4 | P0: Flush ≠ Shutdown; WPF Exit via reflection; flood identity cap 4096 | Implementation plan P0, 17 Sep 2026 |
| 1.5 | P1: Uninitialized NoOp, CORRELATIONID, STATUS=Warning documented | Implementation plan P1, 17 Sep 2026 |
| 1.6 | P2: ignore-case taxonomy, DiskStatus, VestigiumLogPump | Implementation plan P2, 17 Sep 2026 |
