using System;
using System.Collections.Generic;
using System.Text;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Native;

/// <summary>
/// Result of finding the desktop wallpaper attachment host.
/// </summary>
internal sealed record DesktopAttachTarget(
    nint ParentHwnd,
    bool IsDirectProgmanParent,
    string Description
);

/// <summary>
/// Handles finding and preparing the desktop host window (Progman / WorkerW)
/// across Windows 10 and Windows 11 versions (including 24H2+).
/// </summary>
internal static class DesktopAttachHelper
{
    private const uint WM_SPAWN_WORKER = 0x052C;

    /// <summary>
    /// Finds the appropriate desktop parent window to attach the wallpaper to.
    /// Handles both classic WorkerW splitting (Win10 / early Win11) and direct Progman
    /// parenting (Win11 24H2+ where SHELLDLL_DefView remains under Progman).
    /// </summary>
    public static DesktopAttachTarget? FindDesktopHost()
    {
        // 1. Locate Progman
        var progman = DesktopNativeMethods.FindWindow("Progman", null);
        if (progman == 0)
        {
            progman = DesktopNativeMethods.GetShellWindow();
        }

        if (progman == 0)
        {
            return null;
        }

        // 2. Send 0x052C to Progman to prompt the shell to initialize/spawn the background layer.
        // Try with 0x0000000D (Windows 10/11) and fallback to 0x0.
        DesktopNativeMethods.SendMessageTimeout(
            progman,
            WM_SPAWN_WORKER,
            new nint(0x0000000D),
            0,
            0,
            1000,
            out _
        );

        // 3. Scan for a WorkerW window that hosts SHELLDLL_DefView.
        // If found, the sibling WorkerW behind it is our target (Classic Win10/11 path).
        nint workerWithDefView = 0;
        nint workerTarget = 0;

        DesktopNativeMethods.EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(256);
            DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
            if (string.Equals(sb.ToString(), "WorkerW", StringComparison.Ordinal))
            {
                var defView = DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                if (defView != 0)
                {
                    workerWithDefView = hwnd;
                }
            }
            return true;
        }, 0);

        if (workerWithDefView != 0)
        {
            // Find the WorkerW that sits directly behind the one hosting the icons
            DesktopNativeMethods.EnumWindows((hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "WorkerW", StringComparison.Ordinal))
                {
                    var defView = DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                    if (defView == 0 && hwnd != workerWithDefView)
                    {
                        workerTarget = hwnd;
                        return false; // Found our target!
                    }
                }
                return true;
            }, 0);

            if (workerTarget != 0)
            {
                return new DesktopAttachTarget(
                    workerTarget,
                    IsDirectProgmanParent: false,
                    "WorkerW background layer (Behind desktop icons)"
                );
            }
        }

        // 4. Windows 11 24H2+ path:
        // In Windows 11 24H2 (Build 26100+), SHELLDLL_DefView is directly inside Progman
        // and Progman itself acts as the parent container.
        var defViewInProgman = DesktopNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (defViewInProgman != 0)
        {
            return new DesktopAttachTarget(
                progman,
                IsDirectProgmanParent: true,
                "Progman container (Windows 11 24H2+ direct desktop parent)"
            );
        }

        // 5. Fallback: If Progman exists, use it directly
        return new DesktopAttachTarget(
            progman,
            IsDirectProgmanParent: true,
            "Progman fallback parent"
        );
    }

    /// <summary>
    /// Reads the active Windows wallpaper path from the system.
    /// </summary>
    public static string? GetCurrentWallpaperPath()
    {
        try
        {
            var sb = new StringBuilder(512);
            if (DesktopNativeMethods.SystemParametersInfo(DesktopNativeMethods.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            {
                var path = sb.ToString();
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to retrieve current wallpaper path", ex);
        }

        return null;
    }

    /// <summary>
    /// Restores the Windows desktop wallpaper and forces the shell to redraw.
    /// Never throws.
    /// </summary>
    public static void RestoreWallpaper(string? originalPath)
    {
        try
        {
            Logger.Info($"Restoring desktop wallpaper: {originalPath ?? "Default/Refresh"}");

            DesktopNativeMethods.SystemParametersInfo(
                DesktopNativeMethods.SPI_SETDESKWALLPAPER,
                0,
                originalPath,
                DesktopNativeMethods.SPIF_UPDATEINIFILE | DesktopNativeMethods.SPIF_SENDCHANGE
            );

            // Invalidate the desktop to clear any residual frames
            var progman = DesktopNativeMethods.FindWindow("Progman", null);
            if (progman != 0)
            {
                DesktopNativeMethods.RedrawWindow(
                    progman,
                    0,
                    0,
                    DesktopNativeMethods.RDW_INVALIDATE |
                    DesktopNativeMethods.RDW_ERASE |
                    DesktopNativeMethods.RDW_UPDATENOW |
                    DesktopNativeMethods.RDW_ALLCHILDREN
                );
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to restore desktop wallpaper", ex);
        }
    }

    /// <summary>
    /// Enumerates all active display monitors with per-monitor DPI awareness.
    /// </summary>
    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var monitors = new List<DisplayMonitor>();

        try
        {
            DesktopNativeMethods.EnumDisplayMonitors(0, 0, (nint hMon, nint hdc, ref DesktopNativeMethods.RECT rc, nint data) =>
            {
                var mi = new DesktopNativeMethods.MONITORINFOEX();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DesktopNativeMethods.MONITORINFOEX));
                if (DesktopNativeMethods.GetMonitorInfo(hMon, ref mi))
                {
                    var isPrimary = (mi.dwFlags & 1) != 0;
                    uint dpiX = 96, dpiY = 96;

                    try
                    {
                        DesktopNativeMethods.GetDpiForMonitor(hMon, 0 /* MDT_EFFECTIVE_DPI */, out dpiX, out dpiY);
                    }
                    catch
                    {
                        // Fall back to 96 DPI if GetDpiForMonitor is unavailable
                    }

                    var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                    monitors.Add(new DisplayMonitor(
                        Handle: hMon,
                        DeviceName: mi.szDevice ?? $"Display {monitors.Count + 1}",
                        Left: mi.rcMonitor.Left,
                        Top: mi.rcMonitor.Top,
                        Width: width,
                        Height: height,
                        IsPrimary: isPrimary,
                        DpiX: dpiX > 0 ? dpiX : 96,
                        DpiY: dpiY > 0 ? dpiY : 96
                    ));
                }
                return true;
            }, 0);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate display monitors", ex);
        }

        return monitors;
    }

    /// <summary>
    /// Gets the bounding rectangle of the virtual screen spanning all monitors.
    /// </summary>
    public static (int Left, int Top, int Width, int Height) GetVirtualScreenBounds()
    {
        var left = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_XVIRTUALSCREEN);
        var top = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_YVIRTUALSCREEN);
        var width = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_CXVIRTUALSCREEN);
        var height = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_CYVIRTUALSCREEN);

        return (left, top, width, height);
    }
}
