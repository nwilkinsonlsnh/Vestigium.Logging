#Requires -Version 5.1
<#
  One-shot: shift exception ids by +95 (5 -> 100) and seed General 0-14.

  .\Renumber-EventCatalog.ps1
  .\Renumber-EventCatalog.ps1 -Root C:\IT\EventCatalog
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\IT\EventCatalog',
    [int]$ExceptionOffset = 95
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Json($Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}
function Write-Json($Path, $Object) {
    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = "$Path.tmp"
    [System.IO.File]::WriteAllText($tmp, ($Object | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Move-Item $tmp $Path -Force
}
function Shard-Id([string]$Namespace) {
    if ([string]::IsNullOrWhiteSpace($Namespace)) { return 'global' }
    $p = $Namespace.Split('.')
    if ($p.Length -ge 2) { return ($p[0] + '.' + $p[1]).ToLowerInvariant() }
    return $p[0].ToLowerInvariant()
}

$Root = [IO.Path]::GetFullPath($Root)
$shardDir = Join-Path $Root 'shards'
$indexPath = Join-Path $Root 'index.json'
if (-not (Test-Path $shardDir)) { throw "No shards folder at $shardDir. Generate first." }

$existing = Get-ChildItem $shardDir -Filter '*.json' -File
$rows = @()
foreach ($f in $existing) {
    $data = Read-Json $f.FullName
    foreach ($item in @($data)) { $rows += $item }
}
if (-not $rows) { throw "Shards are empty at $shardDir." }

if ($rows | Where-Object { $_.EventName -eq 'General.Start' -or $_.EventId -eq 0 }) {
    Write-Warning 'Already renumbered (found General.Start or EventId 0). Stopped.'
    return
}

$shifted = foreach ($r in $rows) {
    $id = [int]$r.EventId
    if ($id -ge 5 -and $id -le 5000) { $id = $id + $ExceptionOffset }
    [pscustomobject]@{
        EventId     = $id
        EventName   = [string]$r.EventName
        Category    = [string]$r.Category
        Subcategory = [string]$r.Subcategory
        FullName    = [string]$r.FullName
        Namespace   = [string]$r.Namespace
        Description = [string]$r.Description
        Severity    = [string]$r.Severity
        Enabled     = $(if ($null -ne $r.Enabled) { [bool]$r.Enabled } else { $true })
        Kind        = $(if ($r.Kind) { [string]$r.Kind } else { 'Exception' })
    }
}

$generalDefs = @(
    @{ Id=0;  Name='General.Debug';       Sev='Debug';       Desc='Generic debug message.' }
    @{ Id=1;  Name='General.Information'; Sev='Information'; Desc='Generic information message.' }
    @{ Id=2;  Name='General.Warning';     Sev='Warning';     Desc='Generic warning message.' }
    @{ Id=3;  Name='General.Error';       Sev='Error';       Desc='Generic error message.' }
    @{ Id=4;  Name='General.Fatal';       Sev='Fatal';       Desc='Generic fatal message.' }
    @{ Id=5;  Name='General.Start';       Sev='Information'; Desc='Host, probe, or batch started.' }
    @{ Id=6;  Name='General.Stop';        Sev='Information'; Desc='Host, probe, or batch stopped.' }
    @{ Id=7;  Name='General.Heartbeat';   Sev='Debug';       Desc='Liveness pulse.' }
    @{ Id=8;  Name='General.Timeout';     Sev='Information'; Desc='Operational timeout.' }
    @{ Id=9;  Name='General.Retry';       Sev='Warning';     Desc='Retry or backoff.' }
    @{ Id=10; Name='General.Cancelled';   Sev='Information'; Desc='Cooperative cancel.' }
    @{ Id=11; Name='General.Config';      Sev='Information'; Desc='Configuration loaded or rejected.' }
    @{ Id=12; Name='General.NotFound';    Sev='Warning';     Desc='Business miss (not FileNotFoundException).' }
    @{ Id=13; Name='General.Denied';      Sev='Warning';     Desc='Authorization or policy denied.' }
    @{ Id=14; Name='General.Throttle';    Sev='Warning';     Desc='Rate limit or disk tripwire.' }
)

$generals = foreach ($g in $generalDefs) {
    [pscustomobject]@{
        EventId     = [int]$g.Id
        EventName   = $g.Name
        Category    = 'System'
        Subcategory = 'Lifecycle'
        FullName    = 'Vestigium.Logging.' + $g.Name
        Namespace   = 'Vestigium.Logging'
        Description = $g.Desc
        Severity    = $g.Sev
        Enabled     = $true
        Kind        = 'General'
    }
}

$all = @($generals) + @($shifted)
$byShard = @{}
foreach ($row in $all) {
    $sid = Shard-Id $row.Namespace
    if (-not $byShard.ContainsKey($sid)) { $byShard[$sid] = New-Object System.Collections.Generic.List[object] }
    $byShard[$sid].Add($row)
}

Get-ChildItem $shardDir -Filter '*.json' -File | Remove-Item -Force

$byEventId = [ordered]@{}
$byFullName = [ordered]@{}
$byEventName = [ordered]@{}
$shardMeta = @()

foreach ($sid in ($byShard.Keys | Sort-Object)) {
    $list = @($byShard[$sid] | Sort-Object EventId)
    Write-Json (Join-Path $shardDir ($sid + '.json')) $list
    $shardMeta += [pscustomobject]@{ Id = $sid; File = "shards/$sid.json"; Count = $list.Count }
    foreach ($row in $list) {
        $byEventId[[string]$row.EventId] = [pscustomobject]@{ Shard = $sid; FullName = $row.FullName; Enabled = $row.Enabled }
        $byFullName[$row.FullName] = $row.EventId
        $names = @()
        if ($byEventName.Contains($row.EventName)) { $names = @($byEventName[$row.EventName]) }
        if ($names -notcontains $row.EventId) { $byEventName[$row.EventName] = @($names + $row.EventId) }
    }
}

$maxEx = ($shifted | Measure-Object EventId -Maximum).Maximum
$nextReserved = [int]$maxEx + 5
if ($nextReserved -lt 100) { $nextReserved = 100 }

$index = [pscustomobject]@{
    Version         = 2
    IdStart         = 100
    IdStep          = 5
    NextEventId     = $nextReserved
    NextReservedId  = $nextReserved
    NextCustomId    = 5005
    ReservedMax     = 5000
    CustomMin       = 5005
    Shards          = @($shardMeta)
    ByEventId       = $byEventId
    ByFullName      = $byFullName
    ByEventName     = $byEventName
    RetiredIds      = @()
}
Write-Json $indexPath $index

Write-Host "Renumbered $($shifted.Count) exception rows (+$ExceptionOffset)."
Write-Host "Seeded $($generals.Count) general rows (0-14) -> shards\vestigium.logging.json"
Write-Host "NextReservedId=$nextReserved  NextCustomId=5005"
Write-Host "Root: $Root"
Write-Host "Check: EventId 0 = General.Debug, 5 = General.Start, 100 = former id 5."
