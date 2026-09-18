# Vestigium.Logging — Event catalog (package 1.3.0)

**SRS:** 1.11  
**Design / Guide:** 1.4

## Required fields

APPID, CATEGORY, SUBCATEGORY, EVENTID. `0` is `General.Debug`, not missing.

JSON order: `DateTime`, `EVENTID`, `EVENTNAME`, `PID`, …

## Ranges

| IDs | Owner |
|---|---|
| 0–4 | General messaging |
| 5–99 | General operations |
| 100–4999 | Embedded exceptions + engine (step 5, last slot 4995) |
| 5000+ | Host custom |

## Resolve

1. Explicit `eventId` — must exist and be enabled, else throw.
2. Else exception `FullName`, then base types.
3. Else LEVEL → 0 Debug, 1 Information, 2 Warning, 3 Error, 4 Fatal.

## Overlay

Embedded snapshot loads first. `EventCatalogPath` then `RegisterEvent`. Custom ids `< 5000` throw. Catalog freezes after `Initialize`.

## Tests

`EventCatalogTests` + `EventCatalogHostTests`: generals 0/5, object shards, unknown id throws, overlay reject `< 5000`, RegisterEvent 5000/5005, write stamps EVENTNAME.
