# Vestigium.Logging — Library backlog

**Document ID:** VEST-LOG-BL-001  
**Version:** 1.0  
**Scope:** `src/Vestigium.Logging` only (engine, tests, pack, CI). Demo is out of scope.  
**Branch:** `tests/coverage-90`  
**Baseline:** SRS v1.3, Design v1.0

Engine vs SRS is close. These items are why it is not an honest 1.0 NuGet yet.

**Already solid:** JSON Lines schema, LEVEL vs STATUS, flood identity tuple, initialize-guard, disk tripwire, MIT + pack metadata.

Priority: **P0** ship blockers / SRS correctness · **P1** 1.0 quality · **P2** robustness · **P3** later / non-goals.

Effort: **S** hours · **M** a session · **L** multi-session.

---

## P0 — correctness vs the SRS (before publish)

| ID | Item | Weakness | Why it matters | Effort |
|---|---|---|---|---|
| P0-1 | WPF `Application.Exit` is not hooked | `BindLifetime(object? wpfApplication)` ignores the argument. Only `ProcessExit` and `CancelKeyPress` are subscribed. | SRS §3.5 / AC6. WPF hosts often never flush the Async sink on window close. Last lines drop. | S |
| P0-2 | `FlushTimeout` is unused | Options default 5 s; `Host.Flush` disposes Serilog immediately with no wait. | SRS §3.5. Combined with `blockWhenFull: false`, Flush can return before disk. | S |
| P0-3 | Silent drop under load, no counter | Channel `DropOldest` and Serilog `blockWhenFull: false`. No `DroppedCount`. | 10k/s can hide loss. PowerBI looks complete when it is not. | M |
| P0-4 | Flood map never evicts | `ConcurrentDictionary<FloodIdentity, FloodState>` grows with every unique MESSAGE. | Hosts that put the target in MESSAGE (recommended) leak memory over a long run. | M |
| P0-5 | API vs docs mismatch | SRS §3.9: `VestigiumLog.Events` / `EventReader`. Code: `VestigiumLogger` only. | First consumer copies the SRS and fails to compile. | S |

---

## P1 — 1.0 quality bar (coverage, CI, package)

| ID | Item | Weakness | Why it matters | Effort |
|---|---|---|---|---|
| P1-1 | Finish coverage → merge | Coverage work is on `tests/coverage-90`, not `main`. CI does not collect Coverlet or fail under 90/90/90. | No gate; numbers regress on the next edit. | S |
| P1-2 | CI does not pack | `build.yml` restore/build/test only. No `dotnet pack`, symbols, or SourceLink. | Library is not a package pipeline yet. | S |
| P1-3 | Coverage script is outside the repo | Lives under `C:\IT\SCRIPTS\ReportGen`. | Clone + CI cannot reproduce the HTML report. | S |
| P1-4 | Static singleton vs tests | One process-wide `_host`. Tests must serialize. | Any suite test that inits Logging in the same process will flake. | M |
| P1-5 | Test seams on production types | `QueryDrive`, `GetCommonApplicationData` (internal). | Keep internal, or extract `IDiskProbe` so `Poll` stays dumb. | S |
| P1-6 | Docs stale vs code | Dead `Normalize` branch removed; README test list is still the old four cases. SRS still says “Ready to implement”. | Consumers and we will drift. | S |
| P1-7 | `TreatWarningsAsErrors` is false | `Directory.Build.props`. XML docs already suppress `CS1591`. | Easy to ship junk in a 1.0 nupkg. | S |
| P1-8 | Version is already `1.0.0` | csproj `Version` before P0 is done. | Publishing 1.0.0 now burns the version. Use `1.0.0-preview.1` until P0 closes. No CHANGELOG. | S |

---

## P2 — robustness (after 1.0 is honest)

| ID | Item | Weakness | Why it matters | Effort |
|---|---|---|---|---|
| P2-1 | Initialize / Shutdown races | Lock around replace; in-flight `Emit` can hit a disposed Host. | Re-init from tests or a host options apply path. | M |
| P2-2 | `IObservable` is sync on the caller thread | `LogEventSubject.Publish` invokes observers inline. | A UI subscriber deadlocks. Channel is the real contract; Observable is a footgun. | M |
| P2-3 | Taxonomy is ordinal / case-sensitive | `"network"` ≠ `"Network"` → Uncategorized + warning. | Call-site typos become PowerBI junk categories. | S |
| P2-4 | Dead options on the engine | `UiBatchInterval`, `UiBatchSize`, and `FlushTimeout` are not used by `Host`. | Hosts think `Initialize` honors them. | S |
| P2-5 | No multi-process `shared: true` test | Two processes on one rolling file unproven. | PingIQ + TraceIQ both logging is the suite. | M |
| P2-6 | Drain aggregation subcategory | Timer drain writes `SUBCATEGORY=Unregistered` even for a known category. | PowerBI grouping of flood summaries is wrong. | S |
| P2-7 | Disk poll swallows all exceptions | No internal warning when the drive query fails. | Tripwire can silently never trip. | S |

---

## P3 — later (SRS non-goals or suite work)

| ID | Item | Notes |
|---|---|---|
| P3-1 | OpenTelemetry / remote shipper | Explicit non-goal. Do not add. |
| P3-2 | `Microsoft.Extensions.Logging` adapter | Useful if a Generic Host appears; not needed for WPF v1. |
| P3-3 | Correlation / operation id | Keep JSON schema frozen unless you version it. |
| P3-4 | Caller info / source file | Fights flood identity if it lands in MESSAGE. |

---

## Suggested order

1. **P0-1 + P0-2** — flush on WPF exit and honor `FlushTimeout`.
2. **P0-5** — aliases on `VestigiumLog` *or* fix SRS/README to `VestigiumLogger`.
3. **P1-1** — merge `tests/coverage-90` after the coverage triple is confirmed; Coverlet on `build.yml`.
4. **P1-7 + P1-8** — `1.0.0-preview.1`, then pack. Do not push `1.0.0` to nuget.org yet.
5. **P0-4** — cap or TTL the flood dictionary.
6. **P0-3** — `DroppedCount`.
