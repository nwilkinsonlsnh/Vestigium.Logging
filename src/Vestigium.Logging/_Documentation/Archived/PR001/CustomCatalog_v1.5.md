# Vestigium.Logging — Custom event catalog (1.5.0)

Build and persist a host catalog. EventIds are **5000+**. Embedded 0–4999 stay reserved.

```csharp
var catalog = VestigiumCustomCatalog.Open(@"C:\\IT\\PingIQ\\EventCatalog");
catalog.Add("ProbeTimeout", "PingIQ.ProbeTimeoutException", "Network", "ICMP");
catalog.Set(5000, description: "ICMP budget exceeded");
catalog.Save();

VestigiumLogger.Initialize(cfg =>
{
    cfg.AppId = "PingIQ";
    cfg.EventCatalogPath = catalog.Root;
});

VestigiumLog.Thrown(ex, VestigiumStatus.Timeout);
```

`Open` creates `root\\shards`. `Save` writes `shards\\custom.json` and `index.json`. `Remove` does not recycle ids. After `Initialize` the runtime catalog is frozen — edit files with this API, then restart or re-Initialize.
