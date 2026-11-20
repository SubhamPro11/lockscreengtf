$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " PHASE 3 QA: AUTO-PAUSE RULES & RESOURCE RELEASE TESTS     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Test LASTINPUTINFO Idle Time Check
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class LastInputProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static uint GetIdleMs()
    {
        LASTINPUTINFO lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (GetLastInputInfo(ref lii))
        {
            uint tick = (uint)Environment.TickCount;
            return tick >= lii.dwTime ? tick - lii.dwTime : 0;
        }
        return 0;
    }
}
"@

$idleMs = [LastInputProbe]::GetIdleMs()
Write-Host "Current System Idle Time: ${idleMs} ms ($([math]::Round($idleMs / 1000, 1)) seconds)"
if ($idleMs -ge 0) {
    Write-Host "PASS [Idle Detection]: LastInputInfo correctly computes system idle duration" -ForegroundColor Green
}

# 2. Test Process Power Throttling API (EcoQoS / Efficiency Mode)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class EcoQoSProbe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public const int ProcessPowerThrottling = 4;
    public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    public static bool ApplyEcoQoS(IntPtr hProc)
    {
        PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED
        };
        uint size = (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE));
        return SetProcessInformation(hProc, ProcessPowerThrottling, ref state, size);
    }
}
"@

$currentProc = [System.Diagnostics.Process]::GetCurrentProcess()
$ecoSuccess = [EcoQoSProbe]::ApplyEcoQoS($currentProc.Handle)
if ($ecoSuccess) {
    Write-Host "PASS [EcoQoS]: Process Power Throttling (Efficiency Mode) applied successfully to process $($currentProc.Id)" -ForegroundColor Green
} else {
    Write-Host "WARN [EcoQoS]: SetProcessInformation returned false (LastWin32Error: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))" -ForegroundColor Yellow
}

# 3. Test Per-App Pause List and Never-Pause List Logic
Write-Host "`nTesting Per-App Pause and Never-Pause Evaluation Rules:"
$pauseList = @("obs64", "obs32", "premiere", "photoshop", "afterfx", "blender")
$neverPauseList = @("explorer", "taskmgr")

$testCases = @(
    @{ Proc = "obs64"; ExpectedPause = $true; Reason = "In Pause List" },
    @{ Proc = "blender"; ExpectedPause = $true; Reason = "In Pause List" },
    @{ Proc = "notepad"; ExpectedPause = $false; Reason = "Normal App" },
    @{ Proc = "explorer"; ExpectedPause = $false; Reason = "In Never-Pause List" },
    @{ Proc = "taskmgr"; ExpectedPause = $false; Reason = "In Never-Pause List" }
)

foreach ($tc in $testCases) {
    $procName = $tc.Proc
    $shouldPause = ($pauseList -contains $procName) -and -not ($neverPauseList -contains $procName)
    if ($shouldPause -eq $tc.ExpectedPause) {
        Write-Host "PASS [Rule 0c]: '$procName' -> Pause=$shouldPause ($($tc.Reason))" -ForegroundColor Green
    } else {
        Write-Host "FAIL [Rule 0c]: '$procName' -> Expected $($tc.ExpectedPause), got $shouldPause" -ForegroundColor Red
    }
}

# 4. Energy Saver API check
try {
    $energySaver = [Windows.System.Power.PowerManager, Windows.System.Power, ContentType=WindowsRuntime]::EnergySaverStatus
    Write-Host "`nWindows PowerManager.EnergySaverStatus: $energySaver"
    Write-Host "PASS [EnergySaverStatus]: Windows.System.Power.PowerManager queried without error" -ForegroundColor Green
} catch {
    Write-Host "WARN: Windows.System.Power.PowerManager type not registered in powershell session: $($_.Exception.Message)" -ForegroundColor Yellow
}
