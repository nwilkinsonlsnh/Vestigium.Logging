# Vestigium.Logging — Implementation Plan (P0–P3)

**Document ID:** VEST-LOG-IMP-001  
**Version:** 1.0  
**Date:** 17 September 2026  
**Scope:** `src/Vestigium.Logging` engine. Demo/tests updated only as needed to keep the solution building and to lock the new contracts.  
**Companion:** Requirements Specification v1.3, Design Document v1.0

---

## 1. Goal

Close the gaps found in the library review without breaking the v1 contracts that already work:

- Flat JSON Lines, fixed property names.
- LEVEL is severity; STATUS is outcome.
- Flood identity remains `(APPID, CATEGORY, LEVEL, MESSAGE)` as a value record.
- No WPF reference in the class library.
- No OTel / remote shipper / CSV (still non-goals).

Package version after this work: **1.1.0** (additive schema + behavior fixes). Bump in `Vestigium.Logging.csproj` when P1 schema fields land.

---

## 2. Sequencing

Do these in order. Each priority is a shippable slice.

| Slice | Theme | Risk if skipped |
|---|---|---|
| P0 | Lifetime/flush correctness + flood map bound | Data loss on exit; process memory growth under probes |
| P1 | Call-site policy + schema correlation + flood summary fidelity | Helpers keep inventing wrappers; PowerBI cannot slice sessions |
| P2 | Taxonomy + unused options actually doing work | Uncategorized buckets; hosts guess at disk/UI batching |
| P3 | Optional properties + exception redaction | HttpIQ/Network either leak payloads or omit useful columns |

Do **not** start P3 until P1 schema rules are written down — extra JSON fields must follow the same naming discipline as `CORRELATIONID`.

Sibling follow-up (not this repo, track after 1.1.0):

- `Vestigium.Helpers.HelperLog` can drop its no-op guard once P1 uninitialized policy exists.
- `HelperWpfHost` can drop its own `app.Exit` handler once P0 `BindLifetime` is real.
- Helpers can pass `correlationId:` instead of stuffing `id=` into MESSAGE.

---

## 3. Shared implementation rules

1. Keep `net10.0`, nullable, no WPF types in the library.
2. Public log methods still must not throw on the diagnostic thread except the documented initialize guard (when policy is `Throw`).
3. JSON property names stay stable. New fields are **added**, never renamed.
4. New options get SRS-aligned defaults so existing `Initialize` lambdas keep current behavior.
5. Tests go in `src/Vestigium.Logging.Tests`. Demo gallery only if a new option needs a visible control.
6. Update SRS §3 / Design §5–9 in the same slice that changes behavior, not as a later cleanup.

---

## 4. P0 — Correctness and resource bounds

### P0.1 Separate Flush from Shutdown

**Problem.** `VestigiumLogger.Flush()` sets `_accepting = 0`, disposes Serilog, and completes the channel/subject. Helpers already warns “do not call Flush from a UI timer.” `FlushTimeout` is unused.

**Target API**

```csharp
VestigiumLogger.Flush();                 // drain + persist; keep accepting
VestigiumLogger.Flush(TimeSpan timeout); // same, wait up to timeout
VestigiumLogger.Shutdown();              // stop accepting, flush, dispose host
```

**Behavior**

| Method | Accepting | Flood drain | Serilog persist | Channel / Subject | Host |
|---|---|---|---|---|---|
| `Flush` | stays 1 | yes | yes, wait ≤ timeout | stay open | stays |
| `Shutdown` / `Host.Dispose` | 0 | yes | yes, wait ≤ timeout | complete | disposed, `_host = null` |
| ProcessExit / Ctrl+C / WPF Exit | 0 | yes | yes | complete | disposed |

`Options.FlushTimeout` default remains 5 seconds. `Flush()` without args uses that value.

**Serilog persist (no public Flush on `ILogger`).**

Introduce an internal `FlushGate` around the file sink:

1. Keep `WriteTo.Async(..., blockWhenFull: false)`.
2. Put a thin `ILogEventSink` in front of the file formatter that increments an `issued` counter on emit and a `completed` counter after the inner sink returns.
3. `Flush(timeout)` calls `Flood.DrainExpired`, then waits on `issued == completed` (or timeout) via `ManualResetEventSlim` / `SpinWait`. Do **not** dispose the logger on `Flush`.
4. `Shutdown` does the same wait, then disposes the logger (Async sink drain), then completes channel + subject.

If the wait times out, still return; do not throw. Optionally increment a public `FlushTimedOutCount` later; not required in P0.

**Files**

- `VestigiumLogger.cs` — split `Flush` vs `Close`/`Shutdown`; stop using current `Flush` body for both.
- New `Formatting/FlushTrackingSink.cs` (or `Disk/`).
- `VestigiumLoggerOptions.cs` — document `FlushTimeout` as honored.
- Tests: `FlushDoesNotStopWrites`, `ShutdownStopsWrites`, `FlushHonorsTimeout` (use a tiny timeout; do not require a real slow disk).

**Acceptance**

- After `Flush()`, a subsequent `VestigiumLog.Information(...)` writes.
- After `Shutdown()`, a subsequent write throws or no-ops per P1 policy (P0: still throws).
- Process exit path calls `Shutdown`, not `Flush`.

---

### P0.2 Bind WPF Exit without referencing WPF

**Problem.** `BindLifetime(object? wpfApplication)` discards the argument. SRS §3.5 requires `Application.Exit`.

**Target.** Reflection subscribe to an event named `Exit` on the supplied instance.

```csharp
public static void BindLifetime(object? wpfApplication)
```

**Rules**

1. Always (re)bind `AppDomain.ProcessExit` and `Console.CancelKeyPress` as today; unsubscribe first to stay idempotent.
2. If `wpfApplication` is null, stop. Console hosts keep working.
3. If non-null, `GetType().GetEvent("Exit")`. If missing, no-op (not WPF).
4. Build a handler compatible with the event’s delegate type (`ExitEventHandler` is `(object, EventArgs)`-shaped). Handler calls `Shutdown()`.
5. Store the `(instance, EventInfo, delegate)` so a later `BindLifetime` / `Shutdown` can unsubscribe. Do not leak handlers across `Initialize`.
6. Never `dynamic` cast to `System.Windows.Application`.

**Files**

- `VestigiumLogger.cs` (or new `Lifetime/LifetimeBinder.cs`).
- Test with a dummy type that exposes `event EventHandler Exit` and assert `Shutdown` ran.

**Acceptance**

- Dummy `Exit` event triggers host dispose.
- Second `BindLifetime` does not double-flush.
- Library project still has zero WPF package references.

---

### P0.3 Bound flood map (evict + cap)

**Problem.** `ConcurrentDictionary<FloodIdentity, FloodState>` never removes keys. Probe traffic with target/RTT in MESSAGE grows until process exit.

**Identity does not change.** Still `(AppId, Category, Level, Message)`. Do not add SUBCATEGORY to the key (SRS §2.3 / §3.4).

**New options**

```csharp
public int FloodIdentityCap { get; set; } = 4_096;
```

`0` or negative treated as 4_096. Cap is a safety net, not a tuning knob hosts must set.

**Algorithm additions in `FloodTracker`**

1. Store `LastSubcategory` and `LastObserved` on `FloodState` (needed by P1.3; add now so DrainExpired can stay correct).
2. `DrainExpired(now)`:
   - If window expired and `Suppressed > 0`: emit summary as today, then **remove** the key (or Reset and remove — prefer remove).
   - If window expired and `Suppressed == 0`: **remove** the key.
3. `Observe`:
   - After GetOrAdd, if `_states.Count > FloodIdentityCap`, call `EvictOverflow()`:
     - First drop expired entries.
     - Then drop the oldest `LastObserved` entries with `Suppressed == 0`.
     - If still over cap, flush-and-drop the oldest with pending suppressed (return those as extra summaries to `Host.Drain` / `Observe`).
4. Never block `Observe` on a full scan of 4k items on every call. Overflow eviction runs only when `Count > cap` (and from the 1s drain timer).

**Files**

- `Flood/FloodTracker.cs`, `Flood/FloodIdentity.cs` (no key change).
- `VestigiumLoggerOptions.cs`
- `VestigiumLogger.Host.Drain` must accept eviction summaries from overflow, not only timer expiry.
- Tests: `ExpiredKeysAreRemoved`, `CapEvictsOldestIdle`, `PendingSuppressedFlushedBeforeEvict`.

**Acceptance**

- 10_000 distinct messages over 2 minutes do not leave 10_000 dictionary entries after windows expire.
- Existing “22 events → 5 full + 17 aggregated” test still passes.

---

## 5. P1 — Call-site policy and schema fidelity

### P1.1 Uninitialized write policy

**Problem.** `VestigiumLog.*` always throws if `Initialize` has not run. Class libraries cannot call the engine directly; `HelperLog` exists only to no-op.

**Target API** (static, because there is no Options instance before init):

```csharp
public enum VestigiumUninitializedBehavior { Throw = 0, NoOp = 1 }

VestigiumLogger.UninitializedBehavior { get; set; } // default Throw
```

**Behavior**

- `Throw`: current message, `InvalidOperationException`.
- `NoOp`: `Write` returns immediately. `Events` / `EventReader` / `Options` still throw — those require a host. Only **writes** are soft.
- `Initialize` does not change the static policy. Hosts that want library-safe writes set `NoOp` once at process start *or* libraries set it in a module initializer. Default stays `Throw` so product hosts still fail fast if they forgot `Initialize`.

**Files**

- `VestigiumLogger.cs`, `VestigiumLog.cs`.
- Tests: `NoOpSwallowsWrite`, `ThrowStillDefault`.

Document in README + DevelopersGuide: “Libraries that must compile without a host set `UninitializedBehavior = NoOp`.”

---

### P1.2 `CORRELATIONID` on the JSON contract

**Problem.** Helpers already has `AsyncLocal` correlation and embeds `id=` in MESSAGE. PowerBI cannot filter it.

**Schema addition** (additive, nullable):

| Field | Type | Rules |
|---|---|---|
| CORRELATIONID | string or null | Opaque host-supplied id. JSON null when omitted. Not part of flood identity. |

**Type changes**

```csharp
public sealed record VestigiumLogEvent(
    ... existing ...,
    string? Exception,
    string? CorrelationId = null);
```

Call site:

```csharp
VestigiumLog.Write(..., exception: null, appId: null, correlationId: id);
VestigiumLog.Information(..., exception: null, correlationId: id);
```

Keep existing overloads compiling: add optional parameters at the end of `Write` and the level helpers. Current `appId` is already last; insert `correlationId` after `appId` **or** add an overload rather than reorder (do not break positional callers that pass `appId`).

Preferred:

```csharp
Write(..., Exception? exception = null, string? appId = null, string? correlationId = null)
```

`VestigiumJsonRecord.CORRELATIONID` always serialized (`DefaultIgnoreCondition.Never`) so the column exists for PowerBI even when null.

**Files**

- `VestigiumLogEvent.cs`, `VestigiumLog.cs`, `VestigiumLogger.cs` (`Emit` signature), `Formatting/VestigiumJsonFormatter.cs`.
- Tests: field present, null emits `"CORRELATIONID":null`, flood still ignores the id.
- SRS §3.1 table + README schema mention.
- Package version → 1.1.0.

**Out of scope here.** Do not add `Operation` / `ParentId`. Helpers keeps its scope stack; it just starts passing the id through.

---

### P1.3 Aggregation subcategory + STATUS table

**Problem.** Live `Observe` summaries use the current event’s subcategory. `DrainExpired` writes `SUBCATEGORY=Unregistered`. STATUS enum includes `Warning` but SRS §3.1 does not.

**Fixes**

1. `FloodState.LastSubcategory` updated on every `Observe`. `DrainExpired` and overflow-evict summaries use that value. If never set, fall back to `Unregistered`.
2. Keep `VestigiumStatus.Warning`. Update SRS table and README to:

   `None, Pending, Success, Timeout, Failed, Warning`

   Do **not** remove the member. Treat the spec as behind the code.

3. Summary STATUS stays `None`. Summary LEVEL stays the identity level. Unchanged.

**Files**

- `Flood/FloodTracker.cs`, `VestigiumLogger.Host.Drain`.
- Test: register `Network/ICMP`, flood it, wait for drain, assert SUBCATEGORY is `ICMP` not `Unregistered`.
- SRS §3.1 STATUS row.

---

### P1.4 Doc / API name alignment (small, same slice)

README and SRS §3.9 say `VestigiumLog.Events`. The members are on `VestigiumLogger`. Fix the docs; do not add forwarding properties unless a host already compiled against the wrong name (none have).

Add one sentence: `IObservable` is for tests/tools; UI hosts must drain `EventReader`. That sets up P2.2 without implementing the pump yet.

---

## 6. P2 — Taxonomy and options that should work

### P2.1 Taxonomy matching and blank warning

**Problem.** Ordinal compare makes `icmp` ≠ `ICMP`. Blank category/subcategory remaps to Uncategorized/Unregistered with `rewritten = false`, so no internal Warning.

**Changes in `VestigiumTaxonomy`**

- Dictionaries and HashSets use `StringComparer.OrdinalIgnoreCase`.
- Snapshot still emits the **registered casing** (first registration wins).
- `Normalize`:
  - White-space category → `Uncategorized`, `rewritten = true`.
  - White-space subcategory → `Unregistered`, `rewritten = true`.
  - Unknown pair still rewrites + Warning as today.
- `Normalize` return the **canonical registered spelling** when a case-insensitive hit exists (`ICMP` not `icmp`) so PowerBI groupings stay stable.

**Suite catalog — do not bake Helpers into this library.**

Logging must not list ClosedXml / FileIo / Kql. That catalog lives in `HelperLog.Taxonomy`. What this library should add:

```csharp
public void RegisterCanonical(string category, params string[] subcategories);
// already exists as Register — keep

public static VestigiumTaxonomy Combine(params VestigiumTaxonomy[] sources);
```

Hosts keep doing `cfg.RegisterTaxonomy(HelperLog.Taxonomy)`. Document that clearly so “suite defaults” does not leak product names into the engine.

Optional: add `PingIQ`-shaped Network subs only if they already exist in Defaults (they do: ICMP, TCP, DNS, HTTP, Routing). No further product list in P2.

**Files**

- `VestigiumTaxonomy.cs`, tests for case fold + blank warning + canonical spelling.
- DevelopersGuide taxonomy section.

---

### P2.2 Honor or implement unused options

**Disk state — expose, do not hide.**

```csharp
public sealed record VestigiumDiskStatus(
    bool IsTripped,
    bool IsOverridden,
    string? Drive,
    long? AvailableBytes,
    int PercentThreshold,
    long BytesFloor);

VestigiumLogger.DiskStatus { get; } // default/empty when not initialized
```

`IsInitialized == false` → `IsTripped: false`, null drive/bytes (do not throw; galleries bind this).

**Disk floor on small volumes.** Keep the SRS OR tripwire as default. Add:

```csharp
public bool DiskBytesFloorEnabled { get; set; } = true;
```

When `false`, only the percent threshold applies. CI / 8 GB lab disks set this false. Do not change the 5 GB default.

**UI batch options — implement a pump in the library.**

New type `Vestigium.Logging.VestigiumLogPump`:

```csharp
public static Task RunAsync(
    ChannelReader<VestigiumLogEvent> reader,
    Action<IReadOnlyList<VestigiumLogEvent>> emitBatch,
    VestigiumLoggerOptions options,
    CancellationToken cancellationToken);
```

Uses `UiBatchSize` (50) and `UiBatchInterval` (100 ms). Emits on the drain thread; the caller marshals to the dispatcher. Demo `PumpAsync` should call this so there is one implementation.

**Files**

- `Disk/DiskSpaceMonitor.cs`, `VestigiumLogger.cs`, `VestigiumLoggerOptions.cs`.
- New `Observability/VestigiumLogPump.cs`.
- Demo pump slim-down is allowed (it is the consumer of the new API).
- Tests: disk record reflects override; pump emits at size and at interval (fake channel, no WPF).

---

## 7. P3 — Structured extras and redaction

Do this only after P1.2 so field naming is settled.

### P3.1 Optional property bag

**Schema**

| Field | Type | Rules |
|---|---|---|
| PROPERTIES | object or null | Flat string-to-string map. JSON null when empty. Keys `[A-Za-z][A-Za-z0-9_]*`, max 16 entries, values truncated to 256 chars. |

Not part of flood identity. Use PROPERTIES for `host`, `rttMs`, `attempt` so MESSAGE can stay stable (`Echo request timed out`) and flood aggregation still works.

```csharp
VestigiumLog.Write(..., IReadOnlyDictionary<string, string?>? properties = null);
```

Drop illegal keys silently (do not throw). Null values omitted from the object.

PowerBI: folder source will see a nested record; document “expand PROPERTIES” in README.

---

### P3.2 Exception redaction

**Options**

```csharp
public enum VestigiumExceptionDetail { Full = 0, TypeAndMessage = 1, None = 2 }

public VestigiumExceptionDetail ExceptionDetail { get; set; } = Full;
public int ExceptionMaxChars { get; set; } = 8_192; // 0 = unlimited, still cap at 64 KiB hard
```

`Full` = `Exception.ToString()` as today, then truncate.  
`TypeAndMessage` = `{Type}: {Message}` plus inner type/message chain, no stacks.  
`None` = JSON null even if an exception was passed.

No general PII regex pack in v1.1. If a host needs payload scrubbing, they sanitize MESSAGE before `Write`. A callback can wait for 1.2.

**Files**

- Formatter + `Emit` + options.
- Tests for each detail mode, truncation, empty PROPERTIES omitted as null, over-cap keys dropped.
- SRS §3.1 two new rows. Non-goals section stays: still no OTel.

---

## 8. File checklist

| File | P0 | P1 | P2 | P3 |
|---|---|---|---|---|
| `VestigiumLogger.cs` | Flush/Shutdown, lifetime, drain | Emit correlation, uninit policy | DiskStatus | properties, redact |
| `VestigiumLog.cs` | | overloads | | properties arg |
| `VestigiumLogEvent.cs` | | CorrelationId | | Properties |
| `VestigiumLoggerOptions.cs` | FloodIdentityCap | | DiskBytesFloorEnabled | Exception* |
| `VestigiumTaxonomy.cs` | | | case + blank + Combine | |
| `Flood/FloodTracker.cs` | evict/cap + LastSubcategory | drain uses LastSubcategory | | |
| `Disk/DiskSpaceMonitor.cs` | | | expose snapshot | |
| `Formatting/VestigiumJsonFormatter.cs` | | CORRELATIONID | | PROPERTIES, EXCEPTION policy |
| `Formatting/FlushTrackingSink.cs` | new | | | |
| `Lifetime/LifetimeBinder.cs` | new (optional split) | | | |
| `Observability/VestigiumLogPump.cs` | | | new | |
| `Vestigium.Logging.csproj` | | Version 1.1.0 | | |
| `_Documentation/RequirementsSpecification_*.md` | Flush wording | schema + STATUS | taxonomy case | PROPERTIES |
| `_Documentation/DesignDocument_*.md` | lifetime diagram | | pump | |
| `README.md` / DevelopersGuide | Flush vs Shutdown | Events location | pump usage | expand PROPERTIES |
| Tests | flush, lifetime dummy, flood cap | no-op, json field, drain sub | taxonomy, disk, pump | redact, props |

Leave Demo tabs alone until a slice needs a control (P2 disk override already exists; P0 flush does not need a slider).

---

## 9. Test matrix (engine)

Existing tests that must stay green the whole way:

- First five written, then suppressed.
- Different APPIDs do not share counters.
- Window expiry flushes aggregation count.
- JSON preserves pipes / multiline.
- Unregistered category fallback.
- Initialize required before write (default policy).

New tests listed under each slice. Keep the test project serial if any test uses the process singleton (`Initialize`/`Shutdown` in `finally`).

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| Serilog Async has no Flush | Counting sink + dispose only on Shutdown |
| Reflection `Exit` signature differs | Bind only if `EventHandler`-compatible; else skip |
| Record extra field breaks `with` / positional construction in tests | Optional last parameter with default |
| Case-insensitive taxonomy changes PowerBI buckets for mixed-case historical files | Canonicalize to first registered spelling going forward only |
| Nested PROPERTIES surprises existing PowerBI reports | Field is null on old-style writes; reports keep working |
| Helpers still no-ops until it opts into P1.1 | Document sibling follow-up; no hard dependency |

---

## 11. Suggested execution order (checkboxes)

**P0**

- [ ] P0.1 Flush vs Shutdown + FlushTimeout wait
- [ ] P0.2 BindLifetime WPF Exit via reflection
- [ ] P0.3 Flood evict + cap + LastSubcategory storage
- [ ] P0 tests green; existing flood tests green

**P1**

- [ ] P1.1 UninitializedBehavior
- [ ] P1.2 CORRELATIONID + package 1.1.0
- [ ] P1.3 Drain uses LastSubcategory; SRS STATUS includes Warning
- [ ] P1.4 README/SRS API names

**P2**

- [ ] P2.1 Taxonomy ignore-case + blank warning + Combine
- [ ] P2.2 DiskStatus + DiskBytesFloorEnabled + VestigiumLogPump

**P3**

- [ ] P3.1 PROPERTIES bag with key/value caps
- [ ] P3.2 ExceptionDetail + ExceptionMaxChars

Stop after P0 if you want a behavior-only patch with no schema change (still version 1.0.x). Start 1.1.0 at P1.2.
