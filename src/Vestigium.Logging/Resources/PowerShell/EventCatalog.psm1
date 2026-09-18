#Requires -Version 5.1
<#
  Generic Event ID catalog.
  Default store: C:\IT\EventCatalog
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-EventCatalogRoot {
    param([string]$Root)
    if ([string]::IsNullOrWhiteSpace($Root)) { $Root = 'C:\IT\EventCatalog' }
    return [System.IO.Path]::GetFullPath($Root)
}
function Get-EventCatalogShardId {
    param([string]$Namespace)
    if ([string]::IsNullOrWhiteSpace($Namespace)) { return 'global' }
    $parts = $Namespace.Split('.')
    if ($parts.Length -ge 2) { return ($parts[0] + '.' + $parts[1]).ToLowerInvariant() }
    return $parts[0].ToLowerInvariant()
}
function Get-EventCatalogSuggestedTaxonomy {
    param([string]$Namespace)
    $category = 'System'; $sub = 'Core'
    switch -Regex ($Namespace) {
        '^System\.Data'           { $category = 'Database'; $sub = 'Data Access'; break }
        '^System\.IO'             { $category = 'System';   $sub = 'IO'; break }
        '^System\.Net'            { $category = 'Network';  $sub = 'Communications'; break }
        '^System\.Security'       { $category = 'System';   $sub = 'Security'; break }
        '^System\.Threading'      { $category = 'System';   $sub = 'Threading'; break }
        '^System\.Xml'            { $category = 'System';   $sub = 'Serialization'; break }
        '^System\.Text'           { $category = 'System';   $sub = 'Text'; break }
        '^System\.Reflection'     { $category = 'System';   $sub = 'Reflection'; break }
        '^System\.ComponentModel' { $category = 'System';   $sub = 'ComponentModel'; break }
        '^System\.ServiceModel'   { $category = 'Network';  $sub = 'WCF'; break }
        '^System\.Web'            { $category = 'Network';  $sub = 'HTTP'; break }
        '^System\.Management'     { $category = 'System';   $sub = 'Management'; break }
        '^Microsoft'              { $category = 'System';   $sub = 'Microsoft'; break }
    }
    [pscustomobject]@{ Category = $category; Subcategory = $sub }
}
function ConvertTo-EventCatalogEntry {
    param($Source)
    $enabled = $true
    if ($null -ne $Source.PSObject.Properties['Enabled']) { $enabled = [bool]$Source.Enabled }
    [pscustomobject]@{
        EventId=[int]$Source.EventId; EventName=[string]$Source.EventName; Category=[string]$Source.Category
        Subcategory=[string]$Source.Subcategory; FullName=[string]$Source.FullName; Namespace=[string]$Source.Namespace
        Description=[string]$Source.Description; Severity=[string]$Source.Severity; Enabled=$enabled
        Kind=$(if ($Source.PSObject.Properties['Kind']) { [string]$Source.Kind } else { 'Exception' })
    }
}
function Read-EventCatalogJson {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return ([System.IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}
function Write-EventCatalogJson {
    param([string]$Path, $Object)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = "$Path.tmp"
    [System.IO.File]::WriteAllText($tmp, ($Object | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}
function New-EventCatalogIndex {
    [pscustomobject]@{ Version=1; IdStart=5; IdStep=5; NextEventId=5; Shards=@(); ByEventId=[ordered]@{}; ByFullName=[ordered]@{}; ByEventName=[ordered]@{}; RetiredIds=@() }
}
function Get-EventCatalogIndex {
    param([string]$Root)
    $Root = Get-EventCatalogRoot $Root
    $idx = Read-EventCatalogJson (Join-Path $Root 'index.json')
    if ($null -eq $idx) { return (New-EventCatalogIndex) }
    foreach ($name in @('ByEventId','ByFullName','ByEventName')) {
        if ($null -eq $idx.$name) { $idx | Add-Member NoteProperty $name ([ordered]@{}) -Force }
    }
    if ($null -eq $idx.Shards) { $idx | Add-Member NoteProperty Shards @() -Force }
    if ($null -eq $idx.RetiredIds) { $idx | Add-Member NoteProperty RetiredIds @() -Force }
    if ($null -eq $idx.IdStep) { $idx | Add-Member NoteProperty IdStep 5 -Force }
    if ($null -eq $idx.NextEventId) { $idx | Add-Member NoteProperty NextEventId 5 -Force }
    return $idx
}
function Save-EventCatalogIndex { param([string]$Root, $Index); Write-EventCatalogJson (Join-Path (Get-EventCatalogRoot $Root) 'index.json') $Index }
function Get-EventCatalogShardPath { param([string]$Root,[string]$ShardId); Join-Path (Join-Path (Get-EventCatalogRoot $Root) 'shards') ($ShardId + '.json') }
function Read-EventCatalogShard {
    param([string]$Root,[string]$ShardId)
    $data = Read-EventCatalogJson (Get-EventCatalogShardPath $Root $ShardId)
    if ($null -eq $data) { return @() }
    return @($data)
}
function Save-EventCatalogShard {
    param([string]$Root,[string]$ShardId,[object[]]$Entries)
    $Root = Get-EventCatalogRoot $Root
    $sorted = @($Entries | Sort-Object EventId)
    Write-EventCatalogJson (Get-EventCatalogShardPath $Root $ShardId) $sorted
    $idx = Get-EventCatalogIndex $Root
    $list = @(@($idx.Shards) | Where-Object { $_.Id -ne $ShardId })
    $list += [pscustomobject]@{ Id=$ShardId; File="shards/$ShardId.json"; Count=$sorted.Count }
    $idx.Shards = @($list | Sort-Object Id)
    Save-EventCatalogIndex -Root $Root -Index $idx
}
function Register-EventCatalogIndexPointers {
    param($Index, $Entry, [string]$ShardId)
    $Index.ByEventId[[string]$Entry.EventId] = [pscustomobject]@{ Shard=$ShardId; FullName=$Entry.FullName; Enabled=$Entry.Enabled }
    $Index.ByFullName[$Entry.FullName] = $Entry.EventId
    $existing = @()
    if ($Index.ByEventName.PSObject.Properties[$Entry.EventName]) { $existing = @($Index.ByEventName.($Entry.EventName)) }
    if ($existing -notcontains $Entry.EventId) { $Index.ByEventName[$Entry.EventName] = @($existing + $Entry.EventId) }
}
function Get-EventCatalogFrameworkDlls {
    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $bases = @()
    if ($env:DOTNET_ROOT) { $bases += $env:DOTNET_ROOT }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) { $bases += (Split-Path $dotnet.Source -Parent) }
    $bases += @('C:\Program Files\dotnet', "$env:ProgramFiles\dotnet", "$env:USERPROFILE\.dotnet")
    foreach ($base in $bases) {
        $shared = Join-Path $base 'shared'
        if (-not (Test-Path -LiteralPath $shared)) { continue }
        Get-ChildItem -LiteralPath $shared -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                Get-ChildItem -LiteralPath $_.FullName -Filter '*.dll' -File -ErrorAction SilentlyContinue | ForEach-Object { [void]$paths.Add($_.FullName) }
            }
        }
    }
    return @($paths)
}
function Get-EventCatalogExceptionTypes {
    $loadErrors = New-Object System.Collections.Generic.List[string]
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $assemblies = New-Object System.Collections.Generic.List[System.Reflection.Assembly]
    foreach ($asm in [AppDomain]::CurrentDomain.GetAssemblies()) { if ($seen.Add($asm.FullName)) { $assemblies.Add($asm) } }
    foreach ($dll in Get-EventCatalogFrameworkDlls) {
        try {
            $an = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
            $already = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $an.Name } | Select-Object -First 1
            if (-not $already) { $already = [System.Reflection.Assembly]::LoadFrom($dll) }
            if ($already -and $seen.Add($already.FullName)) { $assemblies.Add($already) }
        } catch { $loadErrors.Add("LoadFrom failed: $dll :: $($_.Exception.Message)") }
    }
    $byFullName = [ordered]@{}
    foreach ($asm in $assemblies) {
        $types = $null
        try { $types = $asm.GetTypes() }
        catch [System.Reflection.ReflectionTypeLoadException] { $types = @($_.Exception.Types | Where-Object { $_ }); $loadErrors.Add("ReflectionTypeLoad: $($asm.FullName)") }
        catch { $loadErrors.Add("GetTypes failed: $($asm.FullName)"); continue }
        foreach ($t in $types) {
            if (-not $t) { continue }
            try {
                if ($t.IsAbstract) { continue }
                if (-not $t.IsPublic -and -not $t.IsNestedPublic) { continue }
                if (-not $t.IsSubclassOf([Exception]) -and $t -ne [Exception]) { continue }
                if (-not $byFullName.Contains($t.FullName)) { $byFullName[$t.FullName] = $t }
            } catch { $loadErrors.Add("Inspect failed: $($asm.FullName)") }
        }
    }
    [pscustomobject]@{ Types=@($byFullName.Values | Sort-Object FullName); LoadErrors=@($loadErrors) }
}
function Initialize-EventCatalog {
    [CmdletBinding()]
    param([string]$Root='C:\IT\EventCatalog',[int]$IdStart=5,[int]$IdStep=5)
    $Root = Get-EventCatalogRoot $Root
    New-Item -ItemType Directory -Path (Join-Path $Root 'shards') -Force | Out-Null
    $idx = Get-EventCatalogIndex $Root
    $idx.IdStart=$IdStart; $idx.IdStep=$IdStep
    if ([int]$idx.NextEventId -lt $IdStart) { $idx.NextEventId = $IdStart }
    $scan = Get-EventCatalogExceptionTypes
    $shardMap = @{}
    foreach ($t in $scan.Types) {
        $fullName=$t.FullName; $ns=$t.Namespace; $shardId=Get-EventCatalogShardId $ns
        if (-not $shardMap.ContainsKey($shardId)) {
            $shardMap[$shardId] = [System.Collections.Generic.List[object]]::new()
            foreach ($e in @(Read-EventCatalogShard $Root $shardId)) { $shardMap[$shardId].Add((ConvertTo-EventCatalogEntry $e)) }
        }
        $list = $shardMap[$shardId]
        if ($list | Where-Object { $_.FullName -eq $fullName } | Select-Object -First 1) { continue }
        if ($idx.ByFullName.PSObject.Properties[$fullName]) { $id = [int]$idx.ByFullName.$fullName }
        else { $id = [int]$idx.NextEventId; $idx.NextEventId = $id + $IdStep }
        $tax = Get-EventCatalogSuggestedTaxonomy $ns
        $sev = if ($t.Name -match 'Fatal|ExecutionEngine|StackOverflow') { 'Fatal' } else { 'Error' }
        $entry = [pscustomobject]@{ EventId=$id; EventName=$t.Name; Category=$tax.Category; Subcategory=$tax.Subcategory; FullName=$fullName; Namespace=$ns; Description="Exception type $fullName."; Severity=$sev; Enabled=$true; Kind='Exception' }
        $list.Add($entry)
        Register-EventCatalogIndexPointers -Index $idx -Entry $entry -ShardId $shardId
    }
    foreach ($shardId in ($shardMap.Keys | Sort-Object)) { Save-EventCatalogShard -Root $Root -ShardId $shardId -Entries @($shardMap[$shardId]) }
    Save-EventCatalogIndex -Root $Root -Index $idx
    [pscustomobject]@{ Root=$Root; TypeCount=$scan.Types.Count; NextEventId=$idx.NextEventId; ShardCount=@($idx.Shards).Count; LoadErrors=$scan.LoadErrors }
}
function Resolve-EventCatalogPointer {
    param([string]$Root,[Nullable[int]]$EventId,[string]$FullName,[string]$EventName)
    $idx = Get-EventCatalogIndex $Root
    if ($EventId.HasValue) {
        $key=[string]$EventId.Value
        if (-not $idx.ByEventId.PSObject.Properties[$key]) { return @() }
        $p=$idx.ByEventId.$key
        return @([pscustomobject]@{ EventId=$EventId.Value; Shard=$p.Shard; FullName=$p.FullName })
    }
    if ($FullName) {
        if (-not $idx.ByFullName.PSObject.Properties[$FullName]) { return @() }
        return (Resolve-EventCatalogPointer -Root $Root -EventId ([int]$idx.ByFullName.$FullName))
    }
    if ($EventName) {
        if (-not $idx.ByEventName.PSObject.Properties[$EventName]) { return @() }
        $out=@(); foreach ($id in @($idx.ByEventName.$EventName)) { $out += Resolve-EventCatalogPointer -Root $Root -EventId ([int]$id) }; return $out
    }
    return @()
}
function Get-EventCatalogEntry {
    [CmdletBinding(DefaultParameterSetName='ById')]
    param(
        [Parameter(ParameterSetName='ById')][int]$EventId,
        [Parameter(ParameterSetName='ByFullName')][string]$FullName,
        [Parameter(ParameterSetName='ByName')][string]$EventName,
        [Parameter(ParameterSetName='ByException')][System.Exception]$Exception,
        [string]$Root='C:\IT\EventCatalog'
    )
    $Root = Get-EventCatalogRoot $Root
    if ($PSCmdlet.ParameterSetName -eq 'ByException') { $FullName = $Exception.GetType().FullName }
    $idArg = if ($PSBoundParameters.ContainsKey('EventId')) { [Nullable[int]]$EventId } else { [Nullable[int]]$null }
    $rows=@()
    foreach ($p in (Resolve-EventCatalogPointer -Root $Root -EventId $idArg -FullName $FullName -EventName $EventName)) {
        $row = Read-EventCatalogShard $Root $p.Shard | Where-Object { $_.EventId -eq $p.EventId } | Select-Object -First 1
        if ($row) { $rows += (ConvertTo-EventCatalogEntry $row) }
    }
    return $rows
}
function Add-EventCatalogEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$EventName,[Parameter(Mandatory)][string]$FullName,[string]$Namespace,[string]$Category,[string]$Subcategory,[string]$Description,[string]$Severity='Error',[string]$Kind='Custom',[Nullable[int]]$EventId,[string]$Root='C:\IT\EventCatalog')
    $Root = Get-EventCatalogRoot $Root
    $idx = Get-EventCatalogIndex $Root
    if ($idx.ByFullName.PSObject.Properties[$FullName]) { throw "Already cataloged: $FullName (EventId $($idx.ByFullName.$FullName))" }
    if (-not $Namespace) { $dot=$FullName.LastIndexOf('.'); $Namespace = if ($dot -gt 0) { $FullName.Substring(0,$dot) } else { '' } }
    $tax = Get-EventCatalogSuggestedTaxonomy $Namespace
    if (-not $Category) { $Category=$tax.Category }
    if (-not $Subcategory) { $Subcategory=$tax.Subcategory }
    if (-not $Description) { $Description="$Kind $FullName." }
    $id = if ($EventId.HasValue) { [int]$EventId.Value } else { [int]$idx.NextEventId }
    if ($idx.ByEventId.PSObject.Properties[[string]$id]) { throw "EventId $id already assigned." }
    if (-not $EventId.HasValue -or $id -ge [int]$idx.NextEventId) { $idx.NextEventId = $id + [int]$idx.IdStep }
    $shardId = Get-EventCatalogShardId $Namespace
    $entry = [pscustomobject]@{ EventId=$id; EventName=$EventName; Category=$Category; Subcategory=$Subcategory; FullName=$FullName; Namespace=$Namespace; Description=$Description; Severity=$Severity; Enabled=$true; Kind=$Kind }
    $shard = @((Read-EventCatalogShard $Root $shardId | ForEach-Object { ConvertTo-EventCatalogEntry $_ }) + $entry)
    Register-EventCatalogIndexPointers -Index $idx -Entry $entry -ShardId $shardId
    Save-EventCatalogIndex -Root $Root -Index $idx
    Save-EventCatalogShard -Root $Root -ShardId $shardId -Entries $shard
    return $entry
}
function Set-EventCatalogEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$EventId,[string]$EventName,[string]$Category,[string]$Subcategory,[string]$Description,[string]$Severity,[string]$Kind,[Nullable[bool]]$Enabled,[string]$Root='C:\IT\EventCatalog')
    $Root = Get-EventCatalogRoot $Root
    $hit = Get-EventCatalogEntry -Root $Root -EventId $EventId | Select-Object -First 1
    if (-not $hit) { throw "EventId $EventId not found." }
    $idx = Get-EventCatalogIndex $Root
    $ptr = $idx.ByEventId.([string]$EventId)
    if ($EventName) { $hit.EventName=$EventName }
    if ($Category) { $hit.Category=$Category }
    if ($Subcategory) { $hit.Subcategory=$Subcategory }
    if ($Description) { $hit.Description=$Description }
    if ($Severity) { $hit.Severity=$Severity }
    if ($Kind) { $hit.Kind=$Kind }
    if ($Enabled.HasValue) { $hit.Enabled=$Enabled.Value }
    $shard = @(Read-EventCatalogShard $Root $ptr.Shard | ForEach-Object { if ($_.EventId -eq $EventId) { $hit } else { ConvertTo-EventCatalogEntry $_ } })
    $idx.ByEventId.([string]$EventId).Enabled = $hit.Enabled
    Save-EventCatalogIndex -Root $Root -Index $idx
    Save-EventCatalogShard -Root $Root -ShardId $ptr.Shard -Entries $shard
    return $hit
}
function Remove-EventCatalogEntry {
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)][int]$EventId,[switch]$Hard,[string]$Root='C:\IT\EventCatalog')
    $Root = Get-EventCatalogRoot $Root
    $idx = Get-EventCatalogIndex $Root
    $key=[string]$EventId
    if (-not $idx.ByEventId.PSObject.Properties[$key]) { throw "EventId $EventId not found." }
    $ptr = $idx.ByEventId.$key
    if (-not $Hard) { Set-EventCatalogEntry -Root $Root -EventId $EventId -Enabled $false | Out-Null; return }
    if ($PSCmdlet.ShouldProcess("EventId $EventId",'Delete')) {
        $shard = @(Read-EventCatalogShard $Root $ptr.Shard | Where-Object { $_.EventId -ne $EventId } | ForEach-Object { ConvertTo-EventCatalogEntry $_ })
        $idx.ByEventId.PSObject.Properties.Remove($key)
        if ($idx.ByFullName.PSObject.Properties[$ptr.FullName]) { $idx.ByFullName.PSObject.Properties.Remove($ptr.FullName) }
        $retired=@($idx.RetiredIds); if ($retired -notcontains $EventId) { $idx.RetiredIds = @($retired + $EventId) }
        Save-EventCatalogIndex -Root $Root -Index $idx
        Save-EventCatalogShard -Root $Root -ShardId $ptr.Shard -Entries $shard
    }
}
function Get-EventCatalog {
    [CmdletBinding()]
    param([string]$Root='C:\IT\EventCatalog',[string]$ShardId,[switch]$All)
    $Root = Get-EventCatalogRoot $Root
    $idx = Get-EventCatalogIndex $Root
    if ($ShardId) { return @(Read-EventCatalogShard $Root $ShardId | ForEach-Object { ConvertTo-EventCatalogEntry $_ }) }
    if ($All) {
        $rows=@(); foreach ($s in @($idx.Shards)) { $rows += @(Read-EventCatalogShard $Root $s.Id | ForEach-Object { ConvertTo-EventCatalogEntry $_ }) }; return $rows
    }
    [pscustomobject]@{ Root=$Root; NextEventId=$idx.NextEventId; Shards=@($idx.Shards) }
}
Export-ModuleMember -Function @('Initialize-EventCatalog','Get-EventCatalogEntry','Add-EventCatalogEntry','Set-EventCatalogEntry','Remove-EventCatalogEntry','Get-EventCatalog')
