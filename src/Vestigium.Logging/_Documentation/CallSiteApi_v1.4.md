# Vestigium.Logging — Call-site API (1.4.0)

**SRS:** 1.12  
**Breaking:** `eventId` is the first argument on every severity helper and `Write`.

## Required fields

APPID, CATEGORY, SUBCATEGORY, EVENTID, STATUS. JSON `EXCEPTION` is always present (`null` when there is no throw).

## Methods

| Method | Exception parameter | Typical EVENTID |
|---|---|---|
| `Verbose` / `Debug` | no | 0 |
| `Information` | no | 1, 5–14 |
| `Warning` | optional | 2, 9, 13, 14 |
| `Error` / `Fatal` | optional | 3 / 4 or mapped |
| `Write` | optional | caller |
| `Thrown` | required (`Exception`) | catalog or override |

```csharp
VestigiumLog.Information(5, VestigiumStatus.None, "System", "Lifecycle", "listener started");
VestigiumLog.Error(3, VestigiumStatus.Failed, "System", "IO", "write failed", ex);
VestigiumLog.Thrown(ex, VestigiumStatus.Failed);
VestigiumLog.Thrown(ex, VestigiumStatus.Failed, 5010);
VestigiumLog.Thrown(ex, VestigiumStatus.Timeout, category: "Network", subcategory: "ICMP");
```

`Thrown` without an id: `FullName` → bases, but **not** a bare `System.Exception` unless that is what was thrown. No row → throw. Custom `RegisterEvent` ≥ 5000 with the same `FullName` wins.

## Ranges

| IDs | Owner |
|---|---|
| 0–4 | General messaging |
| 5–99 | General operations |
| 100–4999 | Embedded exceptions + engine |
| 5000+ | Host custom |

Engine internals: taxonomy warning **11**, flood cap **14**, aggregation **1**.
