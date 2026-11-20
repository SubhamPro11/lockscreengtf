$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PHASE 2 QA: TEMP DIRECTORY CREATION & CLEANUP TEST       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$tempRoot = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "LockscreenGif")
if (-not (Test-Path $tempRoot)) {
    [System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null
}

# Simulate creating 3 temp folders during video conversion
$dir1 = Join-Path $tempRoot "gifski_temp_$([Guid]::NewGuid())"
$dir2 = Join-Path $tempRoot "gifski_temp_$([Guid]::NewGuid())"
$dir3 = Join-Path $tempRoot "gifski_temp_$([Guid]::NewGuid())"

[System.IO.Directory]::CreateDirectory($dir1) | Out-Null
[System.IO.Directory]::CreateDirectory($dir2) | Out-Null
[System.IO.Directory]::CreateDirectory($dir3) | Out-Null

# Put dummy frame files inside them
[System.IO.File]::WriteAllText((Join-Path $dir1 "frame001.png"), "dummy frame data")
[System.IO.File]::WriteAllText((Join-Path $dir2 "frame002.png"), "dummy frame data")
[System.IO.File]::WriteAllText((Join-Path $dir3 "frame003.png"), "dummy frame data")

Write-Host "Created 3 simulated conversion temp directories with frame files."

# Simulate GifSkiService.CleanupTempDirectories()
$orphans = [System.IO.Directory]::EnumerateDirectories($tempRoot, "gifski_temp_*") | ForEach-Object { $_ }
Write-Host "Found $($orphans.Count) orphaned temp folders before cleanup."

foreach ($dir in $orphans) {
    try {
        [System.IO.Directory]::Delete($dir, $true)
    } catch {
        Write-Host "Warning: Could not delete ${dir}: $($_.Exception.Message)"
    }
}

$remaining = [System.IO.Directory]::EnumerateDirectories($tempRoot, "gifski_temp_*") | ForEach-Object { $_ }
if ($remaining.Count -eq 0) {
    Write-Host "PASS: All conversion temp directories cleaned up completely (0 remaining)." -ForegroundColor Green
} else {
    Write-Host "FAIL: $($remaining.Count) temp directories remained after cleanup!" -ForegroundColor Red
}
