using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Performance;

/// <summary>
/// Monitors system state, fullscreen games, desktop occlusion, battery/power conditions,
/// and measures real-time CPU and memory usage of the wallpaper engine.
/// </summary>
public sealed class WallpaperPerformanceMonitor : IDisposable
{
    private readonly IWallpaperEngineService _engineService;
    private readonly Timer _timer;
    private readonly object _stateLock = new();

    private bool _isMonitoring;
    private bool _isAutoPaused;
    private string? _currentAutoPauseReason;

    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSampleTime;
    private bool _disposed;

    public WallpaperPerformanceRules Rules { get; } = new();
    public WallpaperResourceUsage LastResourceUsage { get; private set; }
    public bool IsAutoPaused => _isAutoPaused;
    public string? CurrentAutoPauseReason => _currentAutoPauseReason;

    public event EventHandler<WallpaperResourceUsage>? ResourceUsageUpdated;
    public event EventHandler<(bool IsPaused, string? Reason)>? AutoPauseStateChanged;

    public WallpaperPerformanceMonitor(IWallpaperEngineService engineService)
    {
        _engineService = engineService;
        _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        _lastCpuSampleTime = DateTime.UtcNow;

        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isMonitoring)
            {
                return;
            }

            _isMonitoring = true;
            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
            _lastCpuSampleTime = DateTime.UtcNow;
            _timer.Change(1000, 1000);
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _isMonitoring = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_isAutoPaused)
            {
                _isAutoPaused = false;
                _currentAutoPauseReason = null;
                AutoPauseStateChanged?.Invoke(this, (false, null));
            }
        }
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            UpdateResourceUsage();
            EvaluateRules();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Performance monitor evaluation warning: {ex.Message}");
        }
    }

    private void UpdateResourceUsage()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var elapsedWallTime = (now - _lastCpuSampleTime).TotalMilliseconds;
            var currentCpuTime = proc.TotalProcessorTime;
            var cpuUsedMs = (currentCpuTime - _lastCpuTime).TotalMilliseconds;

            _lastCpuTime = currentCpuTime;
            _lastCpuSampleTime = now;

            var cpuPercent = 0.0;
            if (elapsedWallTime > 0)
            {
                var cores = Environment.ProcessorCount;
                cpuPercent = Math.Clamp((cpuUsedMs / (elapsedWallTime * cores)) * 100.0, 0.0, 100.0);
            }

            proc.Refresh();
            var memoryMb = proc.WorkingSet64 / (1024 * 1024);

            LastResourceUsage = new WallpaperResourceUsage(Math.Round(cpuPercent, 1), memoryMb, "DirectX 12 / Hardware Decoded");

            ResourceUsageUpdated?.Invoke(this, LastResourceUsage);
        }
        catch
        {
            // Ignore transient measurement errors
        }
    }

    private void EvaluateRules()
    {
        if (!_isMonitoring || !_engineService.IsRunning)
        {
            return;
        }

        string? triggerReason = null;

        // Rule 1: PC Locked or Display Off
        if (Rules.PauseWhenLockedOrDisplayOff && IsWorkstationLocked())
        {
            triggerReason = "Screen is locked";
        }

        // Rule 2 & 3: Power and Battery
        if (triggerReason == null && (Rules.PauseOnBattery || Rules.PauseInBatterySaver))
        {
            if (DesktopNativeMethods.GetSystemPowerStatus(out var powerStatus))
            {
                if (Rules.PauseOnBattery && powerStatus.ACLineStatus == 0)
                {
                    triggerReason = "Running on battery power";
                }
                else if (Rules.PauseInBatterySaver && powerStatus.SystemStatusFlag == 1)
                {
                    triggerReason = "Windows battery saver is active";
                }
            }
        }

        // Rule 4: Fullscreen App or Game
        if (triggerReason == null && Rules.PauseWhenFullscreenAppActive)
        {
            if (IsForegroundAppFullscreen())
            {
                triggerReason = "Fullscreen app or game is active";
            }
        }

        // Rule 5: Desktop Covered by Maximized Window
        if (triggerReason == null && Rules.PauseWhenDesktopCovered)
        {
            if (IsForegroundAppMaximized())
            {
                triggerReason = "Desktop is covered by active window";
            }
        }

        // Apply state transition
        lock (_stateLock)
        {
            if (triggerReason != null)
            {
                if (!_isAutoPaused)
                {
                    _isAutoPaused = true;
                    _currentAutoPauseReason = triggerReason;
                    Logger.Info($"Performance rule triggered pause: {triggerReason}");
                    _ = _engineService.PauseAsync();
                    AutoPauseStateChanged?.Invoke(this, (true, triggerReason));
                }
            }
            else
            {
                if (_isAutoPaused)
                {
                    _isAutoPaused = false;
                    _currentAutoPauseReason = null;
                    Logger.Info("Performance rule cleared; resuming wallpaper playback.");
                    _ = _engineService.ResumeAsync();
                    AutoPauseStateChanged?.Invoke(this, (false, null));
                }
            }
        }
    }

    private static bool IsWorkstationLocked()
    {
        try
        {
            var hDesktop = DesktopNativeMethods.OpenDesktop(
                "Default",
                0,
                false,
                0x0100 /* DESKTOP_SWITCHDESKTOP */
            );
            if (hDesktop == 0)
            {
                return true;
            }
            DesktopNativeMethods.CloseDesktop(hDesktop);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForegroundAppFullscreen()
    {
        var fgWnd = DesktopNativeMethods.GetForegroundWindow();
        if (fgWnd == 0)
        {
            return false;
        }

        // Never pause for the LockscreenGif application itself
        DesktopNativeMethods.GetWindowThreadProcessId(fgWnd, out var pid);
        if (pid == Environment.ProcessId)
        {
            return false;
        }

        if (IsShellOrDesktopWindow(fgWnd))
        {
            return false;
        }

        if (!DesktopNativeMethods.GetWindowRect(fgWnd, out var windowRect))
        {
            return false;
        }

        var hMonitor = DesktopNativeMethods.MonitorFromWindow(
            fgWnd,
            2 /* MONITOR_DEFAULTTONEAREST */
        );
        if (hMonitor == 0)
        {
            return false;
        }

        var mi = new DesktopNativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<DesktopNativeMethods.MONITORINFOEX>() };
        if (DesktopNativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            var monRect = mi.rcMonitor;
            // Fullscreen check: window covers monitor bounds completely and lacks standard window caption
            var isCovering =
                windowRect.Left <= monRect.Left
                && windowRect.Top <= monRect.Top
                && windowRect.Right >= monRect.Right
                && windowRect.Bottom >= monRect.Bottom;

            if (!isCovering)
            {
                return false;
            }

            // Normal maximized windows have WS_CAPTION and are governed by PauseWhenDesktopCovered, not FullscreenGame
            var style = (long)DesktopNativeMethods.GetWindowLongPtr(fgWnd, DesktopNativeMethods.GWL_STYLE);
            var isBorderlessFullscreen =
                (
                    style & 0x00C00000 /* WS_CAPTION */
                ) != 0x00C00000;
            return isBorderlessFullscreen;
        }

        return false;
    }

    private static bool IsForegroundAppMaximized()
    {
        var fgWnd = DesktopNativeMethods.GetForegroundWindow();
        if (fgWnd == 0)
        {
            return false;
        }

        // Never pause for the LockscreenGif application itself
        DesktopNativeMethods.GetWindowThreadProcessId(fgWnd, out var pid);
        if (pid == Environment.ProcessId)
        {
            return false;
        }

        if (IsShellOrDesktopWindow(fgWnd))
        {
            return false;
        }

        return DesktopNativeMethods.IsZoomed(fgWnd);
    }

    private static bool IsShellOrDesktopWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return true;
        }

        var sb = new StringBuilder(64);
        DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        var className = sb.ToString();

        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "ImmersiveLauncher";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        Stop();
        _timer.Dispose();
    }
}
