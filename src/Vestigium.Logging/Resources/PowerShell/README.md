# Event catalog (PowerShell)

Generic Event ID store. Any app can use it.

After `-Action Generate`:

```
C:\IT\EventCatalog\
  index.json
  shards\
    system.io.json
    system.net.json
```

```powershell
.\EventCatalog.ps1 -Action Generate
.\EventCatalog.ps1 -Action Get -FullName System.IO.FileNotFoundException
.\EventCatalog.ps1 -Action Get -EventName FileNotFoundException
.\EventCatalog.ps1 -Action Add -EventName IcmpEchoTimeout -FullName App.Network.IcmpEchoTimeout -Kind Custom
.\EventCatalog.ps1 -Action Set -EventId 5 -Category System -Subcategory IO
.\EventCatalog.ps1 -Action Remove -EventId 5
.\EventCatalog.ps1 -Action List
.\EventCatalog.ps1 -Action List -ShardId system.io
```

`Generate` keeps existing EventIds. `Remove` disables the row; `-Hard` deletes and retires the id.
