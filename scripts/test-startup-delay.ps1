Add-Type -Path "D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\LockscreenGif.dll"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " TESTING STARTUP DELAY & SETTINGS INTEGRATION (DEF-06)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Test setting startup enabled with 10s delay
$res = [LockscreenGif.Features.DesktopWallpaper.Services.System.WallpaperStartupService]::SetStartupEnabled($true, 10)
Write-Host "SetStartupEnabled(true, 10) result: $res"

# 2. Check registry value
$val = (Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "LockscreenGif" -ErrorAction SilentlyContinue).LockscreenGif
Write-Host "Registry Value: $val"

if ($val -like "*--autostart --delay 10*") {
    Write-Host "PASS: Registry contains --autostart --delay 10" -ForegroundColor Green
} else {
    Write-Host "FAIL: Registry missing expected delay parameter" -ForegroundColor Red
}

# 3. Test reading back delay
$delay = [LockscreenGif.Features.DesktopWallpaper.Services.System.WallpaperStartupService]::GetStartupDelaySeconds()
Write-Host "Read-back delay: $delay"
if ($delay -eq 10) {
    Write-Host "PASS: GetStartupDelaySeconds correctly returns 10" -ForegroundColor Green
} else {
    Write-Host "FAIL: Expected 10, got $delay" -ForegroundColor Red
}

# 4. Clean up registry
[LockscreenGif.Features.DesktopWallpaper.Services.System.WallpaperStartupService]::SetStartupEnabled($false, 0) | Out-Null
Write-Host "Cleaned up startup registry key."
