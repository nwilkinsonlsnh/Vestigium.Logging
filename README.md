# Vestigium.Logging

[![build](https://github.com/nwilkinsonlsnh/Vestigium.Logging/actions/workflows/build.yml/badge.svg)](https://github.com/nwilkinsonlsnh/Vestigium.Logging/actions/workflows/build.yml)

Centralized, subscribable Serilog host for the Vestigium suite (PingIQ, DnsIQ, TraceIQ, HttpIQ).

**Target:** .NET 10 LTS / WPF / Visual Studio 2026  
**Architecture:** MVVM (`CommunityToolkit.Mvvm`) + bounded `Channel` subscribers  
**Startup project:** `Vestigium.Logging.Demo` (configuration gallery)

Requirements: [`_Documentation/RequirementsSpecification_v1.3.md`](_Documentation/RequirementsSpecification_v1.3.md)  
Design: [`_Documentation/DesignDocument_v1.0.md`](_Documentation/DesignDocument_v1.0.md)  
Implementation notes: [`_Documentation/DevelopersGuide_v1.0.md`](_Documentation/DevelopersGuide_v1.0.md)

## Why this library

Diagnostic modules log stack traces and HTTP bodies. Pipe-delimited lines split those payloads. This module writes **one JSON object per line**, maps **LEVEL** to Serilog severity and **STATUS** to outcome, suppresses retry floods with a four-field identity, and never fires a WPF event per line.

## Open in Visual Studio

1. Clone this repository.
2. Open `Vestigium.Logging.slnx` in Visual Studio 2026.
3. Restore NuGet, set **Vestigium.Logging.Demo** as the startup project.
4. Run on Windows.

The demo is a dark tabbed gallery: Overview, Configuration, Flood, Live feed, Schema, Taxonomy. Sliders change rolling-file and flood defaults; the live feed is filled from `ChannelReader` in 50-event / 100 ms batches via `WeakReferenceMessenger`.

## Host at startup

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId                    = "PingIQ";
    cfg.LogDirectory             = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Vestigium", "Logs", "PingIQ");
    cfg.FileSizeLimitBytes       = 20L * 1024 * 1024;
    cfg.RetainedFileTimeLimit    = TimeSpan.FromDays(14);
    cfg.RetainedFileCountLimit   = 90;
    cfg.FloodThresholdCount      = 5;
    cfg.FloodWindow              = TimeSpan.FromMilliseconds(30_000);
    cfg.MinimumDiskLevel         = VestigiumLogLevel.Information;
    cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
});
VestigiumLogger.BindLifetime(Application.Current);
```

Call sites always pass STATUS:

```csharp
VestigiumLog.Information(VestigiumStatus.Timeout, "Network", "ICMP",
    $"Echo request to {host} timed out after {ms} ms");

VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "Failed to open database", ex);
```

## Defaults (SRS v1.3)

| Setting | Value |
|---|---|
| File size cap | 20 MB |
| Time retention | 14 days |
| File cap | 90 per APPID |
| FloodThresholdCount | 5 |
| FloodWindowMs | 30,000 |
| Disk tripwire | 10% free **or** 5 GB |
| Subscriber channel | 10,000, DropOldest |
| UI batch | 50 events / 100 ms |

Flood identity: `(APPID, CATEGORY, LEVEL, MESSAGE)` — a value tuple, never a concatenated string.

## Projects

| Project | Role |
|---|---|
| `Vestigium.Logging` | Engine (Serilog JSON Lines, flood, disk, subscribers) |
| `Vestigium.Logging.Tests` | Flood, schema, taxonomy, initialize-guard |
| `Vestigium.Logging.Demo` | MVVM configuration gallery |

## Contracts that do not move

- Flat JSON Lines. No CSV / pipe / tab output path.
- LEVEL is never Success / Failed / Timeout.
- Unregistered taxonomy becomes `Uncategorized` / `Unregistered` plus an internal Warning.
- Process exit flushes the async buffer.
- Subscribers receive the structured `VestigiumLogEvent`, not a pre-rendered string.
