<#
.SYNOPSIS
    Live UI file watcher for LockscreenGif (Development only, not shipped).
.DESCRIPTION
    Watches .xaml and .cs files under LockScreenGif\ and automatically runs
    scripts\dev-run.ps1 -Force (with debounce) whenever a file is saved.
.PARAMETER Configuration
    Build configuration passed to dev-run.ps1 (default: Debug for faster rebuilds).
.PARAMETER DebounceMs
    Milliseconds to wait after the last file change before rebuilding (default: 800).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$DebounceMs = 800,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$watchRoot = Join-Path $repoRoot 'LockScreenGif'
$devRunScript = Join-Path $PSScriptRoot 'dev-run.ps1'

if (-not (Test-Path $watchRoot)) {
    throw "Watch root not found: $watchRoot"
}
if (-not (Test-Path $devRunScript)) {
    throw "dev-run.ps1 not found: $devRunScript"
}

if ($CheckOnly) {
    Write-Host "watch-ui.ps1 ready. WatchRoot=$watchRoot Configuration=$Configuration DebounceMs=$DebounceMs" -ForegroundColor Green
    exit 0
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " LOCKSCREENGIF LIVE UI WATCHER ($Configuration)           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Watching: $watchRoot (*.xaml, *.cs)" -ForegroundColor Gray
Write-Host "Press Ctrl+C to stop watching.`n" -ForegroundColor DarkGray

# Initial launch
& $devRunScript -Force -Configuration $Configuration

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $watchRoot
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite -bor [System.IO.NotifyFilters]::FileName
$watcher.EnableRaisingEvents = $true

$pending = $false
$lastChange = [DateTime]::UtcNow

$onChanged = {
    param($sender, $e)
    $path = $e.FullPath
    if ($path -match '\\(bin|obj)\\' -or ($path -notmatch '\.(xaml|cs)$')) {
        return
    }
    $script:pending = $true
    $script:lastChange = [DateTime]::UtcNow
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Changed: $(Split-Path -Leaf $path)" -ForegroundColor Yellow
}

$handlers = @(
    (Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $onChanged),
    (Register-ObjectEvent -InputObject $watcher -EventName Created -Action $onChanged),
    (Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $onChanged)
)

try {
    while ($true) {
        Start-Sleep -Milliseconds 200
        if ($script:pending -and (([DateTime]::UtcNow - $script:lastChange).TotalMilliseconds -ge $DebounceMs)) {
            $script:pending = $false
            Write-Host "`n[$(Get-Date -Format 'HH:mm:ss')] Rebuilding and relaunching UI..." -ForegroundColor Cyan
            try {
                & $devRunScript -Force -Configuration $Configuration
            } catch {
                Write-Host "Rebuild failed: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
} finally {
    $watcher.EnableRaisingEvents = $false
    foreach ($h in $handlers) {
        Unregister-Event -SourceIdentifier $h.Name -ErrorAction SilentlyContinue
        Remove-Job -Job $h -Force -ErrorAction SilentlyContinue
    }
    $watcher.Dispose()
}
