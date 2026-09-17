# Vestigium.Logging — Implementation Plan (Owned JSONL writer)

**Document ID:** VEST-LOG-IMP-002  
**Version:** 1.0  
**Date:** 17 September 2026  
**Scope:** Replace Serilog as the disk backend. Public log API, JSON schema, flood, taxonomy, and subscribers stay.  
**Companion:** Requirements Specification v1.3 / v1.8 notes, Design Document v1.0, Implementation Plan P0–P3 (VEST-LOG-IMP-001)

---

## 1. Goal

Stop using Serilog as an opaque file adapter. Vestigium already formats the line (`VestigiumLogEvent.ToJsonLine` / `VestigiumJsonFormatter.Serialize`). After this work the engine owns:

- the async disk queue
- the open file handle
- day + size rolling
- retention
- Flush / Shutdown drain

Package version after this work: **1.2.0** (behavior-compatible, dependency-breaking: Serilog packages leave the nupkg).

Still non-goals: OTel, Seq, MEL `ILogger<T>`, CSV, remote shipper, WPF types in the class library.

---

## 2. Why now

Serilog is three packages wrapping a string we already built (`{VestigiumJson}` + `VestigiumSerilogFormatter`). What it still provides:

| Serilog piece | What we actually need |
|---|---|
| `WriteTo.Async` | bounded queue + one background writer |
| `WriteTo.File` rolling | append UTF-8 JSONL, roll on date or `FileSizeLimitBytes` |
| `shared: true` | not required — one process per APPID |
| `ITextFormatter` | delete |

Costs we already pay:

- `FlushGate` + `CompletingSink` exist only because the async sink is opaque.
- Windows tests that `File.ReadAllText` the live file hit sharing exceptions.
- Hosts see `SerilogAsyncBuffer` in options even though they never touch Serilog.

---

## 3. Sequencing

Do these in order. Each slice should leave the solution green.

| Slice | Theme | Risk if skipped |
|---|---|---|
| S0 | Writer contract + options rename | Host cutover becomes a rewrite |
| S1 | `VestigiumJsonlWriter` (queue, file, roll, retain) | Guessing I/O in the Host |
| S2 | Host cutover, delete Serilog packages | Dual backends, two flush stories |
| S3 | Tests, SRS/Design/README, package tags | 1.2.0 ships with Serilog in the description |

Do **not** change flood, taxonomy, PROPERTIES, or `EventReader` in this plan.

---

## 4. Shared implementation rules

1. Keep `net10.0`, nullable, no WPF types.
2. Public `VestigiumLog.*` methods still must not throw on the diagnostic thread except the documented initialize guard.
3. JSON property names stay exactly as they are. The writer appends one line + `\n`. No pretty-print, no BOM.
4. Path formula stays `%ProgramData%\Vestigium\Logs\{APPID}\vestigium-{APPID}-yyyyMMdd.json` (same prefix as today; drop Serilog’s `-.json` token).
5. Defaults stay SRS-aligned: 20 MB roll, 14-day / 90-file retention, 10_000 disk queue, 5 s flush timeout.
6. Tests in `src/Vestigium.Logging.Tests`. Demo only if a control bound `SerilogAsyncBuffer`.
7. Update SRS §3.5 and Design file-sink section in S3, not later.

---

## 5. S0 — Contract

### S0.1 Internal writer

```csharp
internal interface IVestigiumJsonlWriter : IDisposable
{
    void Enqueue(string jsonLine);          // never blocks the caller; drop-oldest when full
    bool Flush(TimeSpan timeout);           // wait until queued lines are on disk
    void Complete();                        // stop accepting, flush, close handle
    int QueuedCount { get; }
    int DroppedCount { get; }               // lines dropped because the queue was full
    string? ActivePath { get; }
}
```

Host `WriteEvent` calls `Enqueue` only when `evt.Level >= Options.MinimumDiskLevel`. In-memory `RecentJsonLines`, subject, and channel stay as they are.

### S0.2 Options

| Today | After |
|---|---|
| `SerilogAsyncBuffer` (default 10_000) | `DiskQueueCapacity` (default 10_000) |
| — | `SerilogAsyncBuffer` remains as `[Obsolete] alias` for one minor, then delete in 1.3 |

Do not add `shared: true`. One writer thread, exclusive handle (`FileShare.Read` so tests and PowerBI can open the file).

### S0.3 Delete after cutover

- package refs: `Serilog`, `Serilog.Sinks.File`, `Serilog.Sinks.Async`
- `VestigiumSerilogFormatter`
- `CompletingSink` (keep `FlushGate` only if the writer does not expose wait — prefer writer-owned wait)
- `Host.MapLevel`
- `using Serilog` / `Serilog.Events` everywhere

Acceptance:

- [ ] Interface + options compile; Host still uses Serilog until S2
- [ ] Obsolete alias documented

---

## 6. S1 — Writer

New file: `src/Vestigium.Logging/IO/VestigiumJsonlWriter.cs`.

### S1.1 Queue

- Bounded `Channel<string>` (or `Channel<(string Line, long Ticket)>` if Flush needs tickets).
- `SingleReader = true`, `SingleWriter = false`, `FullMode = DropOldest`.
- Background `Task` started in the constructor (`TaskCreationOptions.LongRunning`).
- Do not use `Thread.Sleep` on the emit path.

### S1.2 File

- UTF-8 no BOM.
- `FileStream`: `FileMode.Append`, `FileAccess.Write`, `FileShare.Read`, `FileOptions.SequentialScan`.
- Write `line + "\n"` with `StreamWriter` (`AutoFlush = false`). Flush the stream on `Flush()` and on roll.
- Create the directory if missing (Host already does; writer should too).

### S1.3 Roll

Roll when **either**:

- calendar date UTC changes (`yyyyMMdd`), or
- current file length + pending line would exceed `FileSizeLimitBytes` (default 20 MB)

Rolled name: `vestigium-{APPID}-yyyyMMdd.json`. If size-roll happens the same day, append `-{n}` before `.json` (`vestigium-PingIQ-20260917-2.json`). PowerBI folder source already picks `*.json`.

### S1.4 Retention

On roll and on writer start:

1. Delete files in the APPID directory whose last-write is older than `RetainedFileTimeLimit` (14 days).
2. If file count still exceeds `RetainedFileCountLimit` (90), delete oldest by last-write.

Only touch files matching `vestigium-{APPID}-*.json`. Do not recurse.

### S1.5 Flush / Complete

| Call | Queue | File | Writer task |
|---|---|---|---|
| `Flush(timeout)` | wait until the last enqueued line is written, ≤ timeout | `Stream.Flush` + `Flush(true)` | stays running |
| `Complete()` | stop `Enqueue`, drain, then exit task | close handle | joined, ≤ `FlushTimeout` |

Timeout returns `false` and does not throw. Same as today’s `FlushGate.Wait`.

### S1.6 Faults

IO exceptions on the writer thread: increment an internal `IoFaultCount`, keep the writer alive, try reopen on the next line. Do not throw back to `VestigiumLog.*`.

Acceptance:

- [ ] Unit tests against a temp directory: write, flush, read with exclusive `File.ReadAllText` after `Complete`, read with `FileShare.Read` while still open
- [ ] Size roll creates `-2.json`
- [ ] Date roll uses the next UTC day (inject a clock)
- [ ] Retention deletes the extra file when cap is 2
- [ ] Full queue increments `DroppedCount` and does not block

---

## 7. S2 — Host cutover

In `VestigiumLogger.Host`:

1. Construct `VestigiumJsonlWriter` from options (directory, appId, size, retention, queue cap).
2. `WriteEvent`: if disk-eligible, `_writer.Enqueue(json)` instead of `_gate.Issued()` + `_log.Write`.
3. `Persist`: flood `Drain()`, then `_writer.Flush(wait)` when `stopAccepting` is false; `_writer.Complete()` when true.
4. Keep in-flight emit wait from P4 (`WaitInFlight`) before Complete.
5. Dispose order: drain timer → disk monitor → writer Complete → complete subscriber channel/subject.

Delete Serilog construction block and package references.

`Options.SerilogAsyncBuffer` setter writes `DiskQueueCapacity`.

Acceptance:

- [ ] `dotnet test` green on Windows and Linux
- [ ] `FlushPersistsJsonLineToDisk` can use `File.ReadAllText` after `Complete` / `Shutdown`; while the host is alive it uses `FileShare.Read` (already added `ReadShared`)
- [ ] No `Serilog` string in `src/Vestigium.Logging/*.csproj` or engine `.cs` except the obsolete option
- [ ] Demo still writes JSONL the gallery can tail

---

## 8. S3 — Tests and docs

### Tests to add

| Test | Asserts |
|---|---|
| `JsonlWriterTests.FlushMakesLineVisibleToSharedReader` | line present before Complete |
| `JsonlWriterTests.CompleteReleasesExclusiveRead` | `File.ReadAllText` works after Complete |
| `JsonlWriterTests.SizeRollCreatesNumberedFile` | two files, second named `-2` |
| `JsonlWriterTests.RetentionDeletesOldest` | cap honored |
| `JsonlWriterTests.DropOldestWhenQueueFull` | `DroppedCount > 0` with cap 1 and a blocked/slow clock if needed |
| `LoggerBehaviorTests.FlushPersistsJsonLineToDisk` | keep; sharing helper stays until Complete is used |

Remove tests that poke `VestigiumSerilogFormatter` / Serilog `LogEvent`.

### Docs

- README “Why this library”: drop “Serilog host”; say “owned JSONL writer.”
- Package `Description` and `PackageTags`: remove `serilog`.
- SRS §3.5: Flush waits on the owned writer, not “the async file sink.”
- Design: replace Serilog sequence diagram with writer task + channel.
- DevelopersGuide: `DiskQueueCapacity`; note exclusive writer + `FileShare.Read`.
- SRS document control row **1.9**.

Acceptance:

- [ ] 1.2.0 in the csproj
- [ ] No leftover Serilog package in the nupkg (`dotnet pack` + inspect)

---

## 9. Public API — what does not change

```
VestigiumLogger.Initialize / Flush / Flush(timeout) / Shutdown / BindLifetime
VestigiumLog.*
VestigiumLogEvent JSON field names
Flood identity (APPID, CATEGORY, LEVEL, MESSAGE)
EventReader single-consumer + Events observable
Taxonomy freeze-after-init
PROPERTIES / ExceptionDetail
```

Hosts that set `cfg.SerilogAsyncBuffer = n` keep compiling in 1.2 with obsolete warning.

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| Writer task dies and stays silent | `IoFaultCount` + reopen; optional internal Warning once (flood-protected) |
| PowerBI has the file open | `FileShare.Read` is enough for folder refresh; do not take exclusive read ourselves |
| Size-roll naming surprises existing reports | document `-n` suffix; PowerBI is a folder source |
| DropOldest on disk queue loses Errors under flood | same as today’s `blockWhenFull: false`; do not change default |
| ProcessExit too short to drain | keep `FlushTimeout` 5 s; in-flight wait from P4 first |

---

## 11. Out of scope

- Second broadcast channel / DB shipper
- Environment-variable option overlay
- NativeAOT trim attributes on `BindLifetime`
- Message templates as flood identity
- FrozenDictionary taxonomy (catalog is tiny; already frozen after init)

---

## 12. Suggested commit series

1. `feat(logging): add IVestigiumJsonlWriter and DiskQueueCapacity`
2. `feat(logging): JSONL writer with roll and retention`
3. `feat(logging): Host writes through owned JSONL writer`
4. `chore(logging): remove Serilog packages`
5. `docs: 1.2.0 owned JSONL writer`

Do not squash (4) into (3) until tests on (3) are green with both backends temporarily wired — but do not ship a dual-backend build.
