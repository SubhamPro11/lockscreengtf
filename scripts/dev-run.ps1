<#
.SYNOPSIS
    Development Launcher for LockscreenGif (Not shipped in installer).
.DESCRIPTION
    Lists running LockscreenGif* and related msedgewebview2 processes, terminates instances
    started from this repo or installed directory with approval, optionally cleans bin/obj,
    builds the solution sequentially, launches the exact compiled binary, and prints PID, path, and build stamp.
.PARAMETER Clean
    Performs a full clean (deletes bin and obj) before building to guarantee fresh XAML compilation.
.PARAMETER Force
    Stops detected conflicting processes without interactive confirmation.
.PARAMETER Configuration
    Build configuration (default: Release).
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Force,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " LOCKSCREENGIF DEVELOPMENT LAUNCHER                       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Inspect running LockscreenGif and WebView2 processes
Write-Host "`n[1/5] Checking running processes..." -ForegroundColor Yellow
$runningProcesses = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -match 'LockscreenGif|msedgewebview2'
}

$killTargets = @()
if ($runningProcesses) {
    Write-Host "Detected active matching processes:" -ForegroundColor DarkYellow
    foreach ($p in $runningProcesses) {
        $isRepoOrInstalled = ($p.ExecutablePath -and ($p.ExecutablePath -like "*$repoRoot*" -or $p.ExecutablePath -like "*SubhamPro11*Lockscreen*"))
        $tag = if ($isRepoOrInstalled) { "[TARGET]" } else { "[EXTERNAL]" }
        Write-Host "  $tag PID $($p.ProcessId): $($p.Name) -> $($p.ExecutablePath)" -ForegroundColor $(if ($isRepoOrInstalled) { 'Red' } else { 'Gray' })
        if ($isRepoOrInstalled) {
            $killTargets += $p
        }
    }
} else {
    Write-Host "  No active LockscreenGif processes detected." -ForegroundColor Green
}

# 2. Stop conflicting target processes with approval
if ($killTargets.Count -gt 0) {
    $shouldKill = $Force
    if (-not $shouldKill) {
        $title = "Terminate active LockscreenGif processes?"
        $message = "Found $($killTargets.Count) active LockscreenGif/WebView2 process(es) from repository or installed location. Stop them now?"
        $yes = New-Object System.Management.Automation.Host.ChoiceDescription "&Yes", "Stop the processes."
        $no = New-Object System.Management.Automation.Host.ChoiceDescription "&No", "Do not stop the processes."
        $options = [System.Management.Automation.Host.ChoiceDescription[]]($yes, $no)
        $result = $host.UI.PromptForChoice($title, $message, $options, 0)
        $shouldKill = ($result -eq 0)
    }

    if ($shouldKill) {
        foreach ($target in $killTargets) {
            Write-Host "  Stopping PID $($target.ProcessId) ($($target.Name))..." -ForegroundColor DarkYellow
            Stop-Process -Id $target.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 800
    } else {
        Write-Host "  Skipped process termination. Note: In-use binaries may block build." -ForegroundColor Yellow
    }
}

# 3. Clean if requested
if ($Clean) {
    Write-Host "`n[2/5] Cleaning bin and obj directories..." -ForegroundColor Yellow
    $dirsToClean = Get-ChildItem -Path $repoRoot -Recurse -Directory -Include "bin", "obj" | Where-Object { $_.FullName -notmatch "\.git" }
    foreach ($d in $dirsToClean) {
        try {
            Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
        } catch { }
    }
    Write-Host "  Clean complete." -ForegroundColor Green
} else {
    Write-Host "`n[2/5] Clean skipped (use -Clean to purge stale bin/obj)." -ForegroundColor DarkGray
}

# 4. Build / Publish application
Write-Host "`n[3/5] Publishing application ($Configuration | FolderProfile | /m:1)..." -ForegroundColor Yellow
$projPath = Join-Path $repoRoot "LockScreenGif"
& dotnet publish $projPath -c $Configuration -p:PublishProfile=FolderProfile.pubxml /m:1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
Write-Host "  Publish succeeded." -ForegroundColor Green

# 5. Locate output executable
$exeDir = Join-Path $repoRoot "artifacts\app"
$exePath = Join-Path $exeDir "LockscreenGif.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Published executable not found at: $exePath"
    exit 1
}

# Read build stamp from compiled assembly
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$gitHead = Join-Path $repoRoot ".git\HEAD"
$branch = "unknown"
if (Test-Path $gitHead) {
    $head = (Get-Content $gitHead -Raw).Trim()
    if ($head -match '^ref: refs/heads/(.+)$') {
        $branch = $matches[1]
    } else {
        $branch = $head.Substring(0, [math]::Min(7, $head.Length))
    }
}
$gitCommit = git -C $repoRoot rev-parse --short HEAD

# 6. Launch executable
Write-Host "`n[4/5] Launching exact development binary..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $exePath -WorkingDirectory $exeDir -PassThru

# 7. Print Build Stamp Summary
Write-Host "`n[5/5] EXECUTION STAMP:" -ForegroundColor Cyan
Write-Host "----------------------------------------------------------" -ForegroundColor DarkCyan
Write-Host "  Process ID:       $($proc.Id)" -ForegroundColor White
Write-Host "  Executable Path:  $exePath" -ForegroundColor White
Write-Host "  Product Version:  $($versionInfo.ProductVersion)" -ForegroundColor White
Write-Host "  File Version:     $($versionInfo.FileVersion)" -ForegroundColor White
Write-Host "  Git Commit:       $gitCommit" -ForegroundColor White
Write-Host "  Git Branch:       $branch" -ForegroundColor White
Write-Host "  Configuration:    $Configuration" -ForegroundColor White
Write-Host "  Launch Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host "----------------------------------------------------------" -ForegroundColor DarkCyan
Write-Host "LockscreenGif started successfully." -ForegroundColor Green
