$backupDir = 'C:\Users\badbo\AppData\Local\Temp\LockscreenGif_QA_Backup'
if (!(Test-Path $backupDir)) {
    throw "Backup directory not found at $backupDir"
}

Write-Host "=== RESTORING SYSTEM STATE FROM BACKUP ==="
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$cachePath = "C:\ProgramData\Microsoft\Windows\SystemData\$sid\ReadOnly"

# Restore registry
if (Test-Path "$backupDir\Desktop_Registry.reg") {
    reg import "$backupDir\Desktop_Registry.reg"
    Write-Host "Restored HKCU\Control Panel\Desktop"
}

# Restore AppData
$appData = "$env:LOCALAPPDATA\LockscreenGif"
if (Test-Path "$backupDir\AppData") {
    Copy-Item -Path "$backupDir\AppData\*" -Destination $appData -Recurse -Force
    Write-Host "Restored AppData settings."
}

# Broadcast wallpaper change
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class DesktopRefresh {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int SystemParametersInfo(uint uAction, uint uParam, string lpvParam, uint fuWinIni);
}
"@
[DesktopRefresh]::SystemParametersInfo(0x0014, 0, $null, (0x01 -bor 0x02)) | Out-Null

Write-Host "System state restored and desktop refreshed."
