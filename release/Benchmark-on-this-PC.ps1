# Races Explorer vs PowerShell vs FastDelete on THIS PC.
# Creates the same 10,100-file folder three times and deletes each copy
# with a different tool. Nothing outside the practice folder is touched.

$exe = Join-Path $PSScriptRoot 'FastDelete.exe'
if (-not (Test-Path $exe)) { Write-Host "FastDelete.exe not found next to this script." -ForegroundColor Red; exit 1 }

$base = Join-Path $env:TEMP 'FastDeleteRace'
function New-Tree($path) {
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Path $path | Out-Null
    for ($d = 0; $d -lt 100; $d++) {
        $dir = Join-Path $path ("folder{0:D3}" -f $d)
        New-Item -ItemType Directory -Path $dir | Out-Null
        for ($f = 0; $f -lt 100; $f++) {
            [System.IO.File]::WriteAllText((Join-Path $dir ("file{0:D3}.txt" -f $f)), "x")
        }
    }
}

$results = @()

# --- 1. Explorer (Shell.Application delete verb -> Recycle Bin) ---
$tree = Join-Path $base 'explorer'
New-Tree $tree
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace($tree)
$item = $folder.Self
$item.InvokeVerb('delete')
$wsh = New-Object -ComObject WScript.Shell
$deadline = [DateTime]::Now.AddSeconds(240)
while ((Test-Path $tree) -and [DateTime]::Now -lt $deadline) {
    if ($wsh.AppActivate('Delete Multiple Items')) { Start-Sleep -Milliseconds 300; $wsh.SendKeys('{ENTER}') }
    Start-Sleep -Seconds 2
}
$sw.Stop()
$results += [pscustomobject]@{ Tool = 'Windows Explorer (Recycle Bin)'; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }

# --- 2. PowerShell Remove-Item ---
$tree = Join-Path $base 'powershell'
New-Tree $tree
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Remove-Item -LiteralPath $tree -Recurse -Force
$sw.Stop()
$results += [pscustomobject]@{ Tool = 'PowerShell Remove-Item'; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }

# --- 3. FastDelete ---
$tree = Join-Path $base 'fastdelete'
New-Tree $tree
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$out = & $exe --bench $tree 2>&1 | Out-String
$sw.Stop()
if ($out -match 'RESULT seconds=([\d.]+) items=(\d+)') {
    $results += [pscustomobject]@{ Tool = 'FastDelete'; Seconds = [math]::Round([double]$Matches[1], 1) }
} else {
    $results += [pscustomobject]@{ Tool = 'FastDelete'; Seconds = 'failed: ' + $out.Trim() }
}

if (Test-Path $base) { Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "=== Race result on this PC (10,100 files) ===" -ForegroundColor Cyan
$results | ForEach-Object {
    $tag = if ($_.Tool -eq 'FastDelete') { "  <= $($_.Tool)" } else { $_.Tool }
    Write-Host ("{0,-34} {1,8} s" -f $tag, $_.Seconds)
}
$fd = ($results | Where-Object { $_.Tool -eq 'FastDelete' }).Seconds
$ex = ($results | Where-Object { $_.Tool -like 'Windows*' }).Seconds
if ($fd -is [double] -and $ex -is [double] -and $fd -gt 0) {
    Write-Host ("`nFastDelete is {0:N1}x faster than Explorer on this PC." -f ($ex / $fd)) -ForegroundColor Green
}
