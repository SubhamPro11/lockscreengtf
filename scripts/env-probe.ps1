# scripts/env-probe.ps1 - Environment probe for LockscreenGif target machine
[CmdletBinding()]
param()

$report = [ordered]@{}

# 1. OS & Windows Build
$os = Get-CimInstance Win32_OperatingSystem
$regNt = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion"
$displayVersion = (Get-ItemProperty $regNt).DisplayVersion
$currentBuild = (Get-ItemProperty $regNt).CurrentBuild
$ubr = (Get-ItemProperty $regNt).UBR
$editionId = (Get-ItemProperty $regNt).EditionID
$productName = (Get-ItemProperty $regNt).ProductName

# Feature Experience Pack
$fepPkg = Get-AppxPackage -AllUsers *ExperienceHost* -ErrorAction SilentlyContinue | Select-Object -First 1
$fepVersion = if ($fepPkg) { $fepPkg.Version } else { "Unknown" }

$report["OS_Name"] = $os.Caption
$report["OS_Edition"] = $editionId
$report["OS_DisplayVersion"] = $displayVersion
$report["OS_Build"] = "$currentBuild.$ubr"
$report["FEP_Version"] = $fepVersion

# 2. System Architecture & Chassis
$cs = Get-CimInstance Win32_ComputerSystem
$systemEnclosure = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue
$chassisTypes = @{
    1="Other"; 2="Unknown"; 3="Desktop"; 4="Low Profile Desktop"; 5="Pizza Box"; 6="Mini Tower"; 7="Tower";
    8="Portable"; 9="Laptop"; 10="Notebook"; 11="Hand Held"; 12="Docking Station"; 13="All in One";
    14="Sub Notebook"; 15="Space-Saving"; 16="Lunch Box"; 17="Main Server Chassis"; 18="Expansion Chassis";
    19="SubChassis"; 20="Bus Expansion Chassis"; 21="Peripheral Chassis"; 22="Storage Chassis";
    30="Tablet"; 31="Convertible"; 32="Detachable"
}
$chassisId = if ($systemEnclosure -and $systemEnclosure.ChassisTypes) { $systemEnclosure.ChassisTypes[0] } else { 0 }
$chassisName = if ($chassisTypes.ContainsKey([int]$chassisId)) { $chassisTypes[[int]$chassisId] } else { "Chassis $chassisId" }

$report["Manufacturer"] = $cs.Manufacturer
$report["Model"] = $cs.Model
$report["ChassisType"] = $chassisName
$report["IsLaptop"] = ($chassisId -in 8, 9, 10, 14, 30, 31, 32)

# 3. CPU
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$report["CPU_Name"] = $cpu.Name.Trim()
$report["CPU_Cores"] = $cpu.NumberOfCores
$report["CPU_LogicalProcessors"] = $cpu.NumberOfLogicalProcessors
$report["CPU_MaxClockSpeedMHz"] = $cpu.MaxClockSpeed

# 4. RAM
$totalRamMb = [math]::Round($os.TotalVisibleMemorySize / 1024)
$freeRamMb = [math]::Round($os.FreePhysicalMemory / 1024)
$report["RAM_TotalMB"] = $totalRamMb
$report["RAM_FreeMB"] = $freeRamMb

# 5. Battery & Power
$battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
$hasBattery = $null -ne $battery
$report["HasBattery"] = $hasBattery
if ($hasBattery) {
    $report["Battery_ChargeStatus"] = $battery.BatteryStatus
    $report["Battery_Percent"] = $battery.EstimatedChargeRemaining
}

# Energy Saver / Power Plan
$activePlanGuid = (powercfg /getactivescheme) -replace ".*:\s*([a-f0-9\-]+).*", '$1'
$report["Power_ActivePlanGuid"] = $activePlanGuid.Trim()

# 6. GPUs (hybrid iGPU / dGPU detection)
$gpus = Get-CimInstance Win32_VideoController
$gpuList = @()
foreach ($g in $gpus) {
    $vramMb = [math]::Round($g.AdapterRAM / (1024 * 1024))
    $gpuList += [PSCustomObject]@{
        Name = $g.Name
        DriverVersion = $g.DriverVersion
        VRAM_MB = $vramMb
        CurrentMode = "$($g.CurrentHorizontalResolution)x$($g.CurrentVerticalResolution) @ $($g.CurrentRefreshRate)Hz"
        Status = $g.Status
    }
}
$report["GPUs"] = $gpuList
$report["IsHybridGPU"] = ($gpus.Count -gt 1)

# 7. Displays (EnumDisplayDevices / WindowsDisplayAPI)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class DisplayProbe {
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
}
"@ -ErrorAction SilentlyContinue

$displays = @()
$dd = New-Object DisplayProbe+DISPLAY_DEVICE
$dd.cb = [System.Runtime.InteropServices.Marshal]::SizeOf($dd)
$devNum = 0
while ([DisplayProbe]::EnumDisplayDevices($null, $devNum, [ref]$dd, 0)) {
    if (($dd.StateFlags -band 1) -ne 0) { # DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
        $mon = New-Object DisplayProbe+DISPLAY_DEVICE
        $mon.cb = [System.Runtime.InteropServices.Marshal]::SizeOf($mon)
        $monName = $dd.DeviceString
        if ([DisplayProbe]::EnumDisplayDevices($dd.DeviceName, 0, [ref]$mon, 0)) {
            if (![string]::IsNullOrWhiteSpace($mon.DeviceString)) {
                $monName = $mon.DeviceString
            }
        }
        $displays += [PSCustomObject]@{
            DeviceName = $dd.DeviceName
            MonitorName = $monName
            IsPrimary = (($dd.StateFlags -band 4) -ne 0)
        }
    }
    $devNum++
}
$report["Displays"] = $displays
$report["PrimaryResolution"] = "$([DisplayProbe]::GetSystemMetrics(0))x$([DisplayProbe]::GetSystemMetrics(1))"

# 8. Graphics Settings (HAGS, HDR, VRR)
# HAGS
$hagsKey = "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"
$hagsVal = (Get-ItemProperty $hagsKey -ErrorAction SilentlyContinue).HwSchMode
$report["HAGS_Enabled"] = ($hagsVal -eq 2)

# 9. Feature IDs check (38943831, 48433719)
$featRegPath = "HKLM:\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\4"
$feat38 = Test-Path "$featRegPath\38943831"
$feat48 = Test-Path "$featRegPath\48433719"
$report["Feature_38943831_Configured"] = $feat38
$report["Feature_48433719_Configured"] = $feat48

# 10. Lockscreen SystemData access
$sid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
$systemDataUser = "C:\ProgramData\Microsoft\Windows\SystemData\$sid\ReadOnly"
$canAccessSystemData = Test-Path "C:\ProgramData\Microsoft\Windows\SystemData"
$report["CurrentUserSID"] = $sid
$report["SystemData_DirectRead"] = $canAccessSystemData
$report["SystemData_UserPath"] = $systemDataUser

# Output JSON and formatted summary
$report | ConvertTo-Json -Depth 5
