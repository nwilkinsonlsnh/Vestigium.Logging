# Vestigium.Logging

[![build](https://github.com/nwilkinsonlsnh/Vestigium.Logging/actions/workflows/build.yml/badge.svg)](https://github.com/nwilkinsonlsnh/Vestigium.Logging/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Owned JSON Lines logger for the Vestigium suite and for hosts outside it. One JSON object per line for PowerBI, required EVENTID, flood protection, optional HMAC seal, and WPF-safe subscribers.

**Not Serilog.** Disk I/O is `VestigiumJsonlWriter`. There is no Serilog package at runtime.

**Target:** .NET 10 LTS / WPF / Visual Studio 2026  
**Architecture:** MVVM (`CommunityToolkit.Mvvm`) + bounded `Channel` subscribers  
**License:** MIT

Current documents (in the library project):

- Requirements: [`src/Vestigium.Logging/_Documentation/RequirementsSpecification_v2.3.md`](src/Vestigium.Logging/_Documentation/RequirementsSpecification_v2.3.md)
- Design: [`src/Vestigium.Logging/_Documentation/DesignDocument_v2.2.md`](src/Vestigium.Logging/_Documentation/DesignDocument_v2.2.md)
- Developers guide: [`src/Vestigium.Logging/_Documentation/DevelopersGuide_v2.2.md`](src/Vestigium.Logging/_Documentation/DevelopersGuide_v2.2.md)
- Collaboration: [`src/Vestigium.Logging/_Documentation/Contributors.md`](src/Vestigium.Logging/_Documentation/Contributors.md)

Files under repository-root `_Documentation/` are historical PR01 notes.

## Why this library

Diagnostic modules log stack traces and HTTP bodies. Pipe-delimited lines split those payloads. This module writes **one JSON object per line**, keeps **LEVEL** as severity and **STATUS** as outcome, suppresses retry floods with a four-field identity, and never fires a WPF event per line.

## Open in Visual Studio

1. Clone this repository.
2. Open `Vestigium.Logging.slnx` in Visual Studio 2026.
3. Restore NuGet, set **Vestigium.Logging.Demo** as the startup project.
4. Run on Windows.

## Host at startup

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Vestigium", "Logs", "PingIQ");
    cfg.MinimumDiskLevel = VestigiumLogLevel.Information;
});
VestigiumLogger.BindLifetime(Application.Current);
```

Operations logging is **on** by default (`APPID=Vestigium.Logging`, EVENTID 5000–9999). Set `OperationsLogEnabled = false` to turn it off.

`Flush()` persists the disk queue and keeps accepting writes. `Shutdown()` (also hooked by `BindLifetime`) is process-exit.

EVENTID is required:

```csharp
VestigiumLog.Information(1, VestigiumStatus.None, "System", "Lifecycle", "Started");
VestigiumLog.Warning(2, VestigiumStatus.Timeout, "Network", "ICMP", "Echo timed out");
VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "Failed to open database", ex);
VestigiumLog.Thrown(ex, VestigiumStatus.Failed);
```

Keep MESSAGE stable so flood aggregation still works. Put varying values in `PROPERTIES`.

Custom application events use EVENTID **10000+** via `VestigiumCustomCatalog` and `LoadCustomCatalog`.

## Defaults (SRS 2.3)

| Setting | Value |
|---|---|
| File size cap | 20 MB |
| Count retain | 90 inactive files (`0` keeps none) |
| Age delete | Off unless janitor is opted in |
| Flood | 5 hits / 30 s / cap 4,096 |
| Disk tripwire | 10% free **or** 5 GB |
| Subscriber channel | 10,000, DropOldest |
| UI batch | 50 events / 100 ms |
| Operations log | On |
| Seal / archive / janitor | Off until the host opts in |

Flood identity: `(APPID, CATEGORY, LEVEL, MESSAGE)`.

## Projects

| Project | Role |
|---|---|
| `Vestigium.Logging` | Engine (JSONL writer, catalog, flood, disk, seal, subscribers) |
| `Vestigium.Logging.Tests` | Behavior and coverage |
| `Vestigium.Logging.Demo` | MVVM configuration gallery |

## Contracts that do not move

- Flat JSON Lines. No CSV / pipe / tab output path.
- No Serilog runtime dependency.
- LEVEL is never Success / Failed / Timeout.
- EVENTID 0–4999 library, 5000–9999 operations, ≥ 10000 custom.
- Unregistered taxonomy becomes `Uncategorized` / `Unregistered` plus an internal Warning.
- Subscribers receive `VestigiumLogEvent`, not a pre-rendered string.
