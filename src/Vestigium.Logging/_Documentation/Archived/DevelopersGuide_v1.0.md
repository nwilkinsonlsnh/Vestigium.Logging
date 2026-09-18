# Vestigium.Logging — Developer’s Guide

**Version:** 1.4  
**Target:** Visual Studio 2026 / .NET 10 LTS / package 1.3.0

## Add the package to a host

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
    cfg.EventCatalogPath = @"C:\\IT\\AppCatalog"; // optional, ids >= 5000
    cfg.RegisterEvent("ProbeTimeout", "PingIQ.ProbeTimeout", "Network", "ICMP");
});
VestigiumLogger.BindLifetime(Application.Current);
```

```csharp
VestigiumLog.Information(VestigiumStatus.Timeout, "Network", "ICMP", "Echo request timed out");
VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "Failed to open database", ex);
VestigiumLog.Write(VestigiumLogLevel.Information, VestigiumStatus.None,
    "System", "Lifecycle", "listener started", eventId: 5);
```

## Event catalog

Resolve: explicit `eventId` (must exist) → exception type / bases → general from LEVEL (0–4).

| IDs | Owner |
|---|---|
| 0–4 | General messaging |
| 5–99 | General operations (`5` Start … `14` Throttle) |
| 100–4999 | Embedded exceptions + engine |
| 5000+ | Host custom |

Overlay or `RegisterEvent` below 5000 throws at Initialize. Catalog freezes after Initialize.

## Taxonomy

Register extra categories only inside `Initialize`.

## Flood

Keep MESSAGE stable. Put variables in PROPERTIES.

## Disk / UI

`Flush()` does not stop writes. One `VestigiumLogPump` per process. `DiskBytesFloorEnabled = false` for small lab disks.
