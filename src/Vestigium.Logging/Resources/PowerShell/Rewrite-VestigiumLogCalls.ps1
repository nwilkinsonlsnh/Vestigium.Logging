# Safe rewriter: literal Replace only. No regex.
# If a previous run inserted backticks, restore first:
#   git checkout -- src/Vestigium.Logging.Tests src/Vestigium.Logging.Demo
# Then run this from the repo root.

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')
$files = Get-ChildItem -Path $root -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

$pairs = @(
    @('VestigiumLog.Information(VestigiumStatus.', 'VestigiumLog.Information(1, VestigiumStatus.'),
    @('VestigiumLog.Verbose(VestigiumStatus.',     'VestigiumLog.Verbose(0, VestigiumStatus.'),
    @('VestigiumLog.Debug(VestigiumStatus.',       'VestigiumLog.Debug(0, VestigiumStatus.'),
    @('VestigiumLog.Warning(VestigiumStatus.',     'VestigiumLog.Warning(2, VestigiumStatus.'),
    @('VestigiumLog.Error(VestigiumStatus.',       'VestigiumLog.Error(3, VestigiumStatus.'),
    @('VestigiumLog.Fatal(VestigiumStatus.',       'VestigiumLog.Fatal(4, VestigiumStatus.'),
    @('VestigiumLog.Write(VestigiumLogLevel.Verbose,',     'VestigiumLog.Write(0, VestigiumLogLevel.Verbose,'),
    @('VestigiumLog.Write(VestigiumLogLevel.Debug,',       'VestigiumLog.Write(0, VestigiumLogLevel.Debug,'),
    @('VestigiumLog.Write(VestigiumLogLevel.Information,', 'VestigiumLog.Write(1, VestigiumLogLevel.Information,'),
    @('VestigiumLog.Write(VestigiumLogLevel.Warning,',     'VestigiumLog.Write(2, VestigiumLogLevel.Warning,'),
    @('VestigiumLog.Write(VestigiumLogLevel.Error,',       'VestigiumLog.Write(3, VestigiumLogLevel.Error,'),
    @('VestigiumLog.Write(VestigiumLogLevel.Fatal,',       'VestigiumLog.Write(4, VestigiumLogLevel.Fatal,'),
    @('VestigiumLog.Write(level, status,', 'VestigiumLog.Write(1, level, status,')
)

$changed = 0
foreach ($f in $files) {
    $t = [IO.File]::ReadAllText($f.FullName)
    $n = $t
    foreach ($p in $pairs) { $n = $n.Replace($p[0], $p[1]) }
    if ($n -ne $t) {
        [IO.File]::WriteAllText($f.FullName, $n)
        Write-Output $f.FullName
        $changed++
    }
}
Write-Output "Updated $changed files."
