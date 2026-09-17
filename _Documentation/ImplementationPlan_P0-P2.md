# Vestigium.Logging — Implementation plan (P0–P2)

**Document ID:** VEST-LOG-PLAN-001  
**Version:** 1.0  
**Scope:** `src/Vestigium.Logging` plus tests, CI, and docs. Demo is out of scope.  
**Branch:** `tests/coverage-90` then PR to `main`  
**Backlog:** [`Backlog_v1.0.md`](Backlog_v1.0.md)

Rule: one wave per session when possible. Each wave ends with `dotnet test` and a coverage triple. Do not publish `1.0.0` until Wave 4 is in `main` and Wave 6 has soaked.

---

## Wave 1 — Flush actually happens (P0-1, P0-2, P2-4 partial)

**Goal:** process exit, Ctrl+C, and WPF `Application.Exit` all flush. `FlushTimeout` is real.

1. Keep `ProcessExit` and `CancelKeyPress`.
2. `BindLifetime`: if `wpfApplication` exposes event `Exit`, subscribe via reflection so the library stays `net10.0` (no WPF TFM). Unsubscribe on `Shutdown`.
3. `Host.Flush`: stop accepting, `Drain()`, close Serilog, wait up to `Options.FlushTimeout`. Never block forever.
4. XML-doc `UiBatchInterval` / `UiBatchSize` as host/UI hints, **not used by the engine**. Keep the properties (breaking to delete).

**Files:** `VestigiumLogger.cs`, `VestigiumLoggerOptions.cs`

**Tests:** existing exit/cancel handlers; dummy object no-ops; test double with `Exit` event raises Flush; uninitialized Flush no-ops; `FlushTimeout` 50 ms returns in bound.

**Done when:** a WPF host passing `Application.Current` gets the last Information line on disk after Exit.

---

## Wave 2 — Public API matches the SRS (P0-5)

**Goal:** SRS call sites compile.

Add on `VestigiumLog`: `Events` and `EventReader` forwarding to `VestigiumLogger`. Do not remove the Logger surface.

**Files:** `VestigiumLog.cs`, `README.md`, SRS §3.9 one-line note.

**Tests:** after Initialize, `VestigiumLog.EventReader` is the same instance as `VestigiumLogger.EventReader`.

---

## Wave 3 — Flood correctness (P0-4, P2-6)

**Goal:** dictionary cannot grow forever; aggregation rows keep the real category.

1. New option `FloodIdentityCap` (default 10_000). `DrainExpired` evicts expired-and-empty entries; if still over cap, drop oldest.
2. Store last-seen subcategory on `FloodState`. `Host.Drain` writes that subcategory (not a hard-coded `Unregistered`) and the identity category.

**Files:** `FloodTracker.cs`, `FloodIdentity.cs` / `FloodState`, `VestigiumLoggerOptions.cs`, `VestigiumLogger.cs`

**Tests:** 22-in-window still 5+17; 20_000 distinct messages → map size ≤ cap; drain JSON keeps `CATEGORY=Network`.

---

## Wave 4 — Visibility of loss (P0-3)

**Goal:** drops are countable. No OpenTelemetry.

Public counters, reset on Initialize:

- `RejectedAfterFlush` — emit while `_accepting == 0`
- `DroppedDebugUnderPressure` — Verbose/Debug while disk tripped
- `DroppedCount` — sum (honest enough; DropOldest is not directly observable without changing FullMode)

**Files:** `VestigiumLogger.cs`

**Tests:** write after Flush increments rejected; disk trip + Debug increments debug-drop; Information does not.

---

## Wave 5 — 1.0 quality gate (P1-1 … P1-8)

Do **P1-8 first** so we never publish a fake 1.0.0.

| ID | Change |
|---|
| P1-8 | `<Version>1.0.0-preview.1</Version>` + `CHANGELOG.md` |
| P1-7 | `TreatWarningsAsErrors` true; fill XML gaps |
| P1-1 | PR this branch to `main`. `build.yml`: Coverlet; fail if line, branch, or method < 90 |
| P1-2 | `dotnet pack`; upload nupkg artifact; SourceLink. Not nuget.org |
| P1-3 | Commit `scripts/CodeCoverageReport.ps1` with clone-relative paths |
| P1-6 | SRS status Implemented; README test list current; DevelopersGuide matches Wave 2 |
| P1-5 | Leave seams `internal` |
| P1-4 | Keep static facade. Document one `Initialize` per process. Collection attribute stays. DI is P3 |

**Done when:** Action on `main` shows tests + coverage + nupkg; version is preview.1.

---

## Wave 6 — Hardening (remaining P2)

| ID | Plan |
|---|
| P2-7 | `OnPollError` callback from `DiskSpaceMonitor` to Host; one internal Warning, flood-protected |
| P2-3 | Taxonomy `OrdinalIgnoreCase`; snapshot keeps first-registered casing. CHANGELOG note |
| P2-1 | `Emit` snapshots `Host?`; disposed host no-ops (no throw). Parallel Emit vs Shutdown test |
| P2-2 | XML-doc: `Events` is not dispatcher-safe; use `EventReader`. Test throwing observer does not kill the next |
| P2-5 | Two `Host`s, same directory, `shared: true`, 20 lines each, Flush, file has ≥ 40 JSON objects |
| P2-4 | Closed in Wave 1 docs |

---

## Out of this plan

P3 items, Demo, Helpers NuGet, PingIQ wiring.

---

## Session map

| Session | Wave | Deliverable |
|---|---|---|
| 1 | Wave 1 | WPF Exit + FlushTimeout |
| 2 | Wave 2 | `VestigiumLog.Events` aliases |
| 3 | Wave 3 | Flood cap + drain subcategory |
| 4 | Wave 4 | Drop / reject counters |
| 5 | Wave 5 | preview version, CI pack, coverage gate, merge |
| 6 | Wave 6 | races, taxonomy case, disk warning, shared file |

After session 5: consume `1.0.0-preview.1` from a local or GitHub nupkg. Stable `1.0.0` only after session 6 and a PingIQ soak.
