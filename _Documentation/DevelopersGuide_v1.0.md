# Vestigium.Logging — Developer’s Guide

**Version:** 1.1  
**Target:** Visual Studio 2026 / .NET 10 LTS

## Add the package to a host

1. Project-reference `Vestigium.Logging` (or consume the packed nupkg).
2. In `App.OnStartup`:

```csharp
VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.RegisterTaxonomy(VestigiumTaxonomy.Defaults);
});
VestigiumLogger.BindLifetime(Application.Current);
```

`Flush()` drains flood summaries and waits for the file sink; it does **not** stop further writes. Call `Shutdown()` (or rely on `BindLifetime`) at process exit.

3. Log with an explicit status:

```csharp
VestigiumLog.Information(VestigiumStatus.Success, "Network", "DNS", $"{name} resolved to {ip}", correlationId: sessionId);
VestigiumLog.Error(VestigiumStatus.Failed, "System", "IO", "Failed to open database", ex);
```

Libraries that log before a host starts:

```csharp
VestigiumLogger.UninitializedBehavior = VestigiumUninitializedBehavior.NoOp;
```

Only writes no-op. `VestigiumLogger.Events` still throws until `Initialize`.

4. In a diagnostic console ViewModel, drain `VestigiumLogger.EventReader` with `VestigiumLogPump.RunAsync` off the UI thread and send batches through `WeakReferenceMessenger`. Do not subscribe `IObservable` (`VestigiumLogger.Events`) directly on a View.

Small lab disks can disable the 5 GB floor:

```csharp
cfg.DiskBytesFloorEnabled = false; // percent threshold only
```

`VestigiumLogger.DiskStatus` is safe to read before `Initialize` (empty, not tripped).

## Taxonomy

Register extra categories at initialize time:

```csharp
cfg.Taxonomy.Register("Probe", "Schedule", "Result", "Cancel");
```

Unknown pairs are rewritten. Do not catch that as an exception — it is a Warning from `APPID=Vestigium.Logging`. Matching is ordinal-ignore-case; PowerBI sees the **first registered spelling** (`ICMP`, not `icmp`). Blank category/subcategory also rewrite and warn.

Suite catalogs (Helpers, ClosedXml, …) stay in those libraries. Combine at host init:

```csharp
cfg.RegisterTaxonomy(HelperLog.Taxonomy);
// or
var catalog = VestigiumTaxonomy.Combine(VestigiumTaxonomy.Defaults, HelperLog.Taxonomy);
```

Do not add product names to `Vestigium.Logging` itself.

## Flood

Retries with the same message are collapsed after five hits in 30 seconds. Distinct identities are capped at 4,096; expired keys are dropped. If you need every attempt (for example a per-target ping result), include the target in MESSAGE so the identity differs.

## Files

Logs land in `%ProgramData%\Vestigium\Logs\{APPID}\vestigium-{APPID}-*.json`. Point PowerBI at that folder as a JSON folder source (one record per line).

## Demo gallery

Set `Vestigium.Logging.Demo` as the startup project. Tabs:

| Tab | What to try |
|---|---|
| Overview | Read the contracts and current log directory |
| Configuration | Move sliders (20 MB / 14 d / 90 files are the v1.3 defaults) and Apply |
| Flood | Burst 22, then PingIQ + TraceIQ Timeout |
| Live feed | Watch batched rows; filter LEVEL |
| Schema | Inspect JSON Lines including multiline exceptions |
| Taxonomy | Write Widgets/Thing and confirm Uncategorized |

## Do not

- Put Success/Failed/Timeout in LEVEL
- Fire `INotifyPropertyChanged` per log line
- Concatenate the flood key with `|`
- Ship a CSV fallback
