$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$logger = Join-Path $root 'VestigiumLogger.cs'
$writer = Join-Path $root 'IO\VestigiumJsonlWriter.cs'
foreach ($pair in @(
    @($logger, 'internal sealed class Host', 'internal sealed partial class Host'),
    @($writer, 'internal sealed class VestigiumJsonlWriter', 'internal sealed partial class VestigiumJsonlWriter')
)) {
    if (-not (Test-Path $pair[0])) { Write-Output "missing $($pair[0])"; continue }
    $t = [IO.File]::ReadAllText($pair[0])
    $n = $t.Replace($pair[1], $pair[2])
    if ($n -ne $t) { [IO.File]::WriteAllText($pair[0], $n); Write-Output "patched $($pair[0])" }
    else { Write-Output "already partial $($pair[0])" }
}
