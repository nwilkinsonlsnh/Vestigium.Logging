# Rewrites pre-A0 VestigiumLog calls to required eventId-first signatures.
# Run from the repo root:
#   powershell -File src\Vestigium.Logging\Resources\PowerShell\Rewrite-VestigiumLogCalls.ps1

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
$files = Get-ChildItem -Path $root -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

$map = [ordered]@{
    'VestigiumLog.Information(VestigiumStatus.' = 'VestigiumLog.Information(1, VestigiumStatus.'
    'VestigiumLog.Verbose(VestigiumStatus.'     = 'VestigiumLog.Verbose(0, VestigiumStatus.'
    'VestigiumLog.Debug(VestigiumStatus.'       = 'VestigiumLog.Debug(0, VestigiumStatus.'
    'VestigiumLog.Warning(VestigiumStatus.'     = 'VestigiumLog.Warning(2, VestigiumStatus.'
    'VestigiumLog.Error(VestigiumStatus.'       = 'VestigiumLog.Error(3, VestigiumStatus.'
    'VestigiumLog.Fatal(VestigiumStatus.'       = 'VestigiumLog.Fatal(4, VestigiumStatus.'
    'VestigiumLog.Write(VestigiumLogLevel.Verbose,'      = 'VestigiumLog.Write(0, VestigiumLogLevel.Verbose,'
    'VestigiumLog.Write(VestigiumLogLevel.Debug,'        = 'VestigiumLog.Write(0, VestigiumLogLevel.Debug,'
    'VestigiumLog.Write(VestigiumLogLevel.Information,'  = 'VestigiumLog.Write(1, VestigiumLogLevel.Information,'
    'VestigiumLog.Write(VestigiumLogLevel.Warning,'      = 'VestigiumLog.Write(2, VestigiumLogLevel.Warning,'
    'VestigiumLog.Write(VestigiumLogLevel.Error,'        = 'VestigiumLog.Write(3, VestigiumLogLevel.Error,'
    'VestigiumLog.Write(VestigiumLogLevel.Fatal,'        = 'VestigiumLog.Write(4, VestigiumLogLevel.Fatal,'
    'VestigiumLog.Write(level, status,' = 'VestigiumLog.Write(EventIdFor(level), level, status,'
}

$changed = 0
foreach ($f in $files) {
    $t = [IO.File]::ReadAllText($f.FullName)
    $n = $t
    foreach ($k in $map.Keys) { $n = $n.Replace($k, $map[$k]) }
    # multiline Information(\n                VestigiumStatus
    $n = $n -replace 'VestigiumLog\.Information\(\r?\n(\s+)VestigiumStatus\.', 'VestigiumLog.Information($1$1`n$1`1, VestigiumStatus.'
    $n = [regex]::Replace($n, 'VestigiumLog\.Information\(\r?\n(\s+)VestigiumStatus\.', {
        param($m)
        "VestigiumLog.Information(`r`n$($m.Groups[1].Value)1, VestigiumStatus."
    })
    $n = [regex]::Replace($n, 'VestigiumLog\.Write\(\r?\n(\s+)VestigiumLogLevel\.', {
        param($m)
        "VestigiumLog.Write(`r`n$($m.Groups[1].Value)1, VestigiumLogLevel."
    })
    $n = $n.Replace(
        'VestigiumLog.Error(3, VestigiumStatus.Failed, "Network", "HTTP", "HttpIQ probe threw", ex);',
        'VestigiumLog.Thrown(ex, VestigiumStatus.Failed, category: "Network", subcategory: "HTTP");')
    if ($n -ne $t) {
        [IO.File]::WriteAllText($f.FullName, $n)
        Write-Output $f.FullName
        $changed++
    }
}
Write-Output "Updated $changed files. Rebuild the solution."
