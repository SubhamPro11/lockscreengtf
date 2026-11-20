Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class WindowStationHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
}
"@

$hwinsta = [WindowStationHelper]::OpenWindowStation("WinSta0", $false, 0x037F)
Write-Host "hwinsta: 0x$($hwinsta.ToString('X'))"
if ($hwinsta -ne [IntPtr]::Zero) {
    [WindowStationHelper]::SetProcessWindowStation($hwinsta) | Out-Null
}

$hdesk = [WindowStationHelper]::OpenDesktop("Default", 0, $false, 0x01FF)
Write-Host "hdesk: 0x$($hdesk.ToString('X'))"
if ($hdesk -ne [IntPtr]::Zero) {
    [WindowStationHelper]::SetThreadDesktop($hdesk) | Out-Null
}

$progman = [WindowStationHelper]::FindWindow("Progman", $null)
$shell = [WindowStationHelper]::GetShellWindow()
Write-Host "Progman: 0x$($progman.ToString('X'))"
Write-Host "ShellWindow: 0x$($shell.ToString('X'))"
if ($progman -ne [IntPtr]::Zero) {
    $defView = [WindowStationHelper]::FindWindowEx($progman, [IntPtr]::Zero, "SHELLDLL_DefView", $null)
    Write-Host "SHELLDLL_DefView: 0x$($defView.ToString('X'))"
}
