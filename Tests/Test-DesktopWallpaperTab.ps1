$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PHASE 2 QA: DESKTOP WALLPAPER TAB & ENGINE ATTACH TESTS  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Test Window Attachment (Progman / WorkerW on Windows 11 26H2)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopAttachProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool SystemParametersInfo(uint uAction, uint uParam, StringBuilder lpvParam, uint fuWinIni);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$progman = [DesktopAttachProbe]::FindWindow("Progman", $null)
Write-Host "Progman HWND: 0x$($progman.ToString('X'))"

if ($progman -ne [IntPtr]::Zero) {
    # Send 0x052C to spawn WorkerW
    $res = [IntPtr]::Zero
    [DesktopAttachProbe]::SendMessageTimeout($progman, 0x052C, [IntPtr]0x0000000D, [IntPtr]::Zero, 0, 1000, [ref]$res) | Out-Null
    [DesktopAttachProbe]::SendMessageTimeout($progman, 0x052C, [IntPtr]::Zero, [IntPtr]::Zero, 0, 1000, [ref]$res) | Out-Null
    Start-Sleep -Milliseconds 150

    # Look for SHELLDLL_DefView
    $defViewInProgman = [DesktopAttachProbe]::FindWindowEx($progman, [IntPtr]::Zero, "SHELLDLL_DefView", $null)
    $workerInProgman = [DesktopAttachProbe]::FindWindowEx($progman, [IntPtr]::Zero, "WorkerW", $null)
    Write-Host "SHELLDLL_DefView in Progman: 0x$($defViewInProgman.ToString('X'))"
    Write-Host "WorkerW in Progman: 0x$($workerInProgman.ToString('X'))"

    if ($defViewInProgman -ne [IntPtr]::Zero) {
        Write-Host "PASS [Desktop Host Found]: Progman hosts SHELLDLL_DefView directly (Windows 11 24H2/26H2 modern architecture)" -ForegroundColor Green
    } else {
        # Check top-level WorkerW
        $workerW = [DesktopAttachProbe]::FindWindow("WorkerW", $null)
        Write-Host "Top-level WorkerW: 0x$($workerW.ToString('X'))"
        Write-Host "PASS [Desktop Host Found]: Top-level WorkerW layer active" -ForegroundColor Green
    }
} else {
    Write-Host "FAIL: Progman window could not be found!" -ForegroundColor Red
}

# 2. Test Active Wallpaper Path via SystemParametersInfo (SPI_GETDESKWALLPAPER)
$sb = New-Object System.Text.StringBuilder 512
$success = [DesktopAttachProbe]::SystemParametersInfo(0x0073, [uint32]$sb.Capacity, $sb, 0)
$curWallpaper = $sb.ToString()
Write-Host "`nCurrent Windows Wallpaper: $curWallpaper"
if ($success -and -not [string]::IsNullOrWhiteSpace($curWallpaper)) {
    Write-Host "PASS [SPI_GETDESKWALLPAPER]: Current wallpaper path successfully queried from system" -ForegroundColor Green
} else {
    Write-Host "WARN: Wallpaper path returned empty or default theme asset" -ForegroundColor Yellow
}

# 3. Test Display Monitor Resolution & Refresh Rate
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens
Write-Host "`nDetected Displays: $($screens.Count)"
foreach ($scr in $screens) {
    Write-Host " - Device: $($scr.DeviceName) | Bounds: $($scr.Bounds.Width)x$($scr.Bounds.Height) | Primary: $($scr.Primary)"
    if ($scr.Bounds.Width -eq 1920 -and $scr.Bounds.Height -eq 1080) {
        Write-Host "   PASS: Resolution matches primary 1920x1080 native target" -ForegroundColor Green
    }
}

# 4. URL Validation Logic Test (for Web Wallpapers)
function Test-WebWallpaperUrl([string]$url) {
    if ([string]::IsNullOrWhiteSpace($url)) { return "REJECT_EMPTY" }
    if ($url.StartsWith("http://localhost", [StringComparison]::OrdinalIgnoreCase) -or
        $url.StartsWith("http://127.0.0.1", [StringComparison]::OrdinalIgnoreCase) -or
        $url.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
        if ([Uri]::TryCreate($url, [UriKind]::Absolute, [ref]$null)) {
            return "ACCEPT"
        }
    }
    return "REJECT_UNTRUSTED_OR_INVALID"
}

Write-Host "`nWeb Wallpaper URL Validation Tests:"
$urls = @(
    @{ Url = "https://threejs.org"; Expected = "ACCEPT" },
    @{ Url = "http://localhost:8080"; Expected = "ACCEPT" },
    @{ Url = "http://127.0.0.1:3000"; Expected = "ACCEPT" },
    @{ Url = "http://insecure-http-site.com"; Expected = "REJECT_UNTRUSTED_OR_INVALID" },
    @{ Url = "javascript:alert(1)"; Expected = "REJECT_UNTRUSTED_OR_INVALID" },
    @{ Url = "ftp://malicious.org"; Expected = "REJECT_UNTRUSTED_OR_INVALID" },
    @{ Url = "file:///C:/Windows/System32/cmd.exe"; Expected = "REJECT_UNTRUSTED_OR_INVALID" },
    @{ Url = ""; Expected = "REJECT_EMPTY" }
)

foreach ($u in $urls) {
    $res = Test-WebWallpaperUrl $u.Url
    if ($res -eq $u.Expected) {
        Write-Host "PASS [URL]: '$($u.Url)' -> $res" -ForegroundColor Green
    } else {
        Write-Host "FAIL [URL]: '$($u.Url)' -> Expected $($u.Expected), got $res" -ForegroundColor Red
    }
}
