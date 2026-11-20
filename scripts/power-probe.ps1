# Power status & Energy Saver
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class PowerStatus {
    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public struct SYSTEM_POWER_STATUS {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
"@

$status = New-Object PowerStatus+SYSTEM_POWER_STATUS
[PowerStatus]::GetSystemPowerStatus([ref]$status) | Out-Null
Write-Output "ACLineStatus: $($status.ACLineStatus) (1=Online, 0=Offline)"
Write-Output "BatteryLifePercent: $($status.BatteryLifePercent)%"
Write-Output "SystemStatusFlag (Energy Saver): $($status.SystemStatusFlag) (1=Active, 0=Inactive)"
