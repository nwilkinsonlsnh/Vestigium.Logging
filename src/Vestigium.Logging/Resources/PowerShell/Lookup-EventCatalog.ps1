#Requires -Version 5.1
param(
    [string]$Root = 'C:\IT\EventCatalog',
    [int]$EventId = -1,
    [string]$FullName,
    [string]$EventName
)
$ErrorActionPreference = 'Stop'
$index = Get-Content -LiteralPath (Join-Path $Root 'index.json') -Raw | ConvertFrom-Json
function Find-Id([int]$Id) {
    $key = [string]$Id
    $ptr = $index.ByEventId.$key
    if (-not $ptr) { Write-Host "No row for EventId $Id"; return }
    $shardPath = Join-Path $Root $ptr.File
    if (-not $ptr.File) { $shardPath = Join-Path $Root ("shards\" + $ptr.Shard + ".json") }
    $rows = Get-Content -LiteralPath $shardPath -Raw | ConvertFrom-Json
    $rows | Where-Object { [int]$_.EventId -eq $Id } | Format-List EventId, EventName, Kind, FullName, Category, Subcategory, Severity
}
if ($EventId -ge 0) { Find-Id $EventId; return }
if ($FullName) {
    $id = $index.ByFullName.$FullName
    if ($null -eq $id) { Write-Host "No row for $FullName"; return }
    Find-Id ([int]$id); return
}
if ($EventName) {
    $ids = @($index.ByEventName.$EventName)
    if (-not $ids) { Write-Host "No row for $EventName"; return }
    foreach ($id in $ids) { Find-Id ([int]$id) }
    return
}
Write-Host "Root: $Root  NextEventId: $($index.NextEventId)  Shards: $(@($index.Shards).Count)"
@($index.Shards) | Format-Table Id, Count, File
