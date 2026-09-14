# Refresh the Windows icon cache after app.ico / the exe changes.
#
# Why this is needed:
#   Explorer keeps icons in memory and in %LOCALAPPDATA%\IconCache.db +
#   %LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db. After republishing
#   the exe with a new embedded icon, an already-running Explorer keeps showing
#   the OLD bitmap until it is restarted and the cache is rebuilt.
#
# What this does: stops explorer, backs up + deletes the icon cache DBs,
#   restarts explorer. Running applications are NOT closed - only the taskbar
#   and desktop windows are recreated (a few seconds of flicker).
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\refresh-icon-cache.ps1
$ErrorActionPreference = 'Stop'

$explorerDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
$backupDir = Join-Path $env:TEMP ('dsh-iconcache-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))

$targets = @()
$db = Join-Path $env:LOCALAPPDATA 'IconCache.db'
if (Test-Path $db) { $targets += Get-Item $db -Force }
$targets += @(Get-ChildItem $explorerDir -Filter 'iconcache_*.db' -Force -ErrorAction SilentlyContinue)

if ($targets.Count -eq 0) {
    Write-Host "No icon cache files found; nothing to do." -ForegroundColor Yellow
    exit 0
}

# Back up first: if deleting fails we can put everything back.
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
foreach ($t in $targets) { Copy-Item $t.FullName -Destination $backupDir -Force -ErrorAction SilentlyContinue }

$wasRunning = @(Get-Process explorer -ErrorAction SilentlyContinue)
$stopped = $wasRunning.Count -gt 0
if ($stopped) {
    Write-Host "Stopping explorer..." -ForegroundColor Cyan
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
}

$deleted = 0
$failed = 0
foreach ($t in $targets) {
    try { Remove-Item $t.FullName -Force -ErrorAction Stop; $deleted++ }
    catch { $failed++ }
}
Write-Host ("Deleted {0} of {1} icon cache files ({2} failed)." -f $deleted, $targets.Count, $failed)

if ($stopped) {
    Write-Host "Restarting explorer..." -ForegroundColor Cyan
    Start-Process explorer.exe
    Start-Sleep -Seconds 3
}

$still = @(Get-Process explorer -ErrorAction SilentlyContinue).Count
Write-Host ("explorer running: {0}" -f ($(if ($still -gt 0) { 'yes' } else { 'NO - restart it manually or sign out/in' })))
Write-Host ("Backup kept at: {0}" -f $backupDir) -ForegroundColor DarkGray
Write-Host "Done. Open app\ again - the exe should now show the white tile with the blue whale." -ForegroundColor Green
