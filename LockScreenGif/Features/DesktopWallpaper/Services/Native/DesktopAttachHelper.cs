using System;
using System.Collections.Generic;
using System.Text;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Native;

/// <summary>
/// Result of finding the desktop wallpaper attachment host.
/// </summary>
internal sealed record DesktopAttachTarget(nint ParentHwnd, bool IsDirectProgmanParent, string Description);

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
            try
            {
                var hDesk = DesktopNativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
                if (hDesk != 0)
                {
                    DesktopNativeMethods.SetThreadDesktop(hDesk);
                    progman = DesktopNativeMethods.FindWindow("Progman", null);
                    if (progman == 0)
                    {
                        progman = DesktopNativeMethods.GetShellWindow();
                    }
                }
            }
            catch { }
        }

        if (progman == 0)
        {
            return null;
        }

        // 2. Prompt Progman to spawn the WorkerW background layer (0x052C)
        DesktopNativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKER, new nint(0x0000000D), 0, 0, 1000, out _);
        DesktopNativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKER, 0, 0, 0, 1000, out _);

        nint workerTarget = 0;
        nint defViewContainer = 0;

        // Try up to 5 attempts (Explorer can take 50-150ms to spawn and reorder the WorkerW window)
        for (var attempt = 0; attempt < 5; attempt++)
        {
            defViewContainer = 0;

            // First check if SHELLDLL_DefView is inside Progman directly
            var defViewInProgman = DesktopNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
            if (defViewInProgman != 0)
            {
                defViewContainer = progman;
            }
            else
            {
                // Otherwise find the WorkerW hosting SHELLDLL_DefView
                DesktopNativeMethods.EnumWindows(
                    (hwnd, _) =>
                    {
                        var sb = new StringBuilder(256);
                        DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                        if (string.Equals(sb.ToString(), "WorkerW", StringComparison.Ordinal))
                        {
                            var defView = DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                            if (defView != 0)
                            {
                                defViewContainer = hwnd;
                                return false;
                            }
                        }
                        return true;
                    },
                    0
                );
            }

            if (defViewContainer != 0)
            {
                // WorkerW placed immediately after defViewContainer in Z-order
                workerTarget = DesktopNativeMethods.FindWindowEx(0, defViewContainer, "WorkerW", null);
                if (workerTarget != 0)
                {
                    break;
                }
            }

            // Retry trigger if still not spawned
            DesktopNativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKER, 0, 0, 0, 500, out _);
            global::System.Threading.Thread.Sleep(60);
        }

        if (workerTarget != 0)
        {
            return new DesktopAttachTarget(workerTarget, IsDirectProgmanParent: false, "WorkerW background layer (Behind desktop icons)");
        }

        // Secondary fallback: Enumerate all WorkerW windows that do not host SHELLDLL_DefView
        DesktopNativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                var sb = new StringBuilder(256);
                DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "WorkerW", StringComparison.Ordinal))
                {
                    var defView = DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                    if (defView == 0 && hwnd != defViewContainer)
                    {
                        workerTarget = hwnd;
                        return false;
                    }
                }
                return true;
            },
            0
        );

        if (workerTarget != 0)
        {
            return new DesktopAttachTarget(workerTarget, IsDirectProgmanParent: false, "WorkerW background layer (Secondary enumeration)");
        }

        Logger.Warn("Could not find suitable WorkerW behind desktop icons.");
        return null;
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
                    DesktopNativeMethods.RDW_INVALIDATE
                        | DesktopNativeMethods.RDW_ERASE
                        | DesktopNativeMethods.RDW_UPDATENOW
                        | DesktopNativeMethods.RDW_ALLCHILDREN
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
            DesktopNativeMethods.EnumDisplayMonitors(
                0,
                0,
                (nint hMon, nint hdc, ref DesktopNativeMethods.RECT rc, nint data) =>
                {
                    var mi = new DesktopNativeMethods.MONITORINFOEX();
                    mi.cbSize = global::System.Runtime.InteropServices.Marshal.SizeOf(typeof(DesktopNativeMethods.MONITORINFOEX));
                    if (DesktopNativeMethods.GetMonitorInfo(hMon, ref mi))
                    {
                        var isPrimary = (mi.dwFlags & 1) != 0;
                        uint dpiX = 96,
                            dpiY = 96;

                        try
                        {
                            DesktopNativeMethods.GetDpiForMonitor(
                                hMon,
                                0 /* MDT_EFFECTIVE_DPI */
                                ,
                                out dpiX,
                                out dpiY
                            );
                        }
                        catch
                        {
                            // Fall back to 96 DPI if GetDpiForMonitor is unavailable
                        }

                        var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                        var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                        monitors.Add(
                            new DisplayMonitor(
                                Handle: hMon,
                                DeviceName: mi.szDevice ?? $"Display {monitors.Count + 1}",
                                Left: mi.rcMonitor.Left,
                                Top: mi.rcMonitor.Top,
                                Width: width,
                                Height: height,
                                IsPrimary: isPrimary,
                                DpiX: dpiX > 0 ? dpiX : 96,
                                DpiY: dpiY > 0 ? dpiY : 96
                            )
                        );
                    }
                    return true;
                },
                0
            );
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
