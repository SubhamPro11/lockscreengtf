using System;
using System.Collections.Generic;
using System.Text;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Native;

/// <summary>
/// Result of finding the desktop wallpaper attachment host.
/// </summary>
internal sealed record DesktopAttachTarget(nint ParentHwnd, bool IsDirectProgmanParent, string Description, nint InsertAfterHwnd = 0);

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
            LogAttach("FindDesktopHost: Progman handle not found (0x0).");
            return null;
        }

        LogAttach($"FindDesktopHost: Progman=0x{progman:X}");

        // 2. Prompt Progman to spawn the WorkerW background layer (0x052C)
        foreach (
            var (wp, lp) in new[]
            {
                (new nint(0x0D), nint.Zero),
                (new nint(0x0D), new nint(1)),
                (nint.Zero, nint.Zero),
                (nint.Zero, new nint(1)),
            }
        )
        {
            var sendOk = DesktopNativeMethods.SendMessageTimeout(progman, WM_SPAWN_WORKER, wp, lp, 0, 1000, out var sendResult);
            var sendErr = global::System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            LogAttach(
                $"SendMessageTimeout(0x052C, wParam=0x{wp:X}, lParam=0x{lp:X}) -> ok={sendOk}, result=0x{sendResult:X}, lastError={sendErr}"
            );
        }

        var progmanChildren = new List<string>();
        nint enumChild = 0;
        while ((enumChild = DesktopNativeMethods.FindWindowEx(progman, enumChild, null, null)) != 0)
        {
            var sbCls = new StringBuilder(256);
            DesktopNativeMethods.GetClassName(enumChild, sbCls, sbCls.Capacity);
            progmanChildren.Add($"0x{enumChild:X}:{sbCls}");
        }
        LogAttach($"Progman direct children ({progmanChildren.Count}): [{string.Join(", ", progmanChildren)}]");

        var directDefView = DesktopNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        nint topLevelDefViewWorkerW = 0;
        DesktopNativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                var sbCls = new StringBuilder(256);
                DesktopNativeMethods.GetClassName(hwnd, sbCls, sbCls.Capacity);
                if (
                    string.Equals(sbCls.ToString(), "WorkerW", StringComparison.Ordinal)
                    && DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) != 0
                )
                {
                    topLevelDefViewWorkerW = hwnd;
                    return false;
                }
                return true;
            },
            0
        );
        LogAttach(
            $"Layout probe: SHELLDLL_DefView direct child of Progman={(directDefView != 0)} (0x{directDefView:X}); top-level WorkerW containing SHELLDLL_DefView={(topLevelDefViewWorkerW != 0)} (0x{topLevelDefViewWorkerW:X})"
        );

        nint workerTarget = 0;
        nint defViewContainer = 0;

        // Try up to 5 attempts (Explorer can take 50-150ms to spawn and reorder the WorkerW window)
        for (var attempt = 0; attempt < 5; attempt++)
        {
            defViewContainer = 0;

            // Runtime layout detection (no build-number assumptions):
            // 1. Windows 11 24H2+ / 26H2 layout: SHELLDLL_DefView is a direct child of Progman (no top-level WorkerW).
            //    Attach wallpaper as a direct child of Progman positioned just BELOW SHELLDLL_DefView (above WorkerW).
            var defViewInProgman = DesktopNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
            if (defViewInProgman != 0)
            {
                LogAttach(
                    $"Detected Progman-hosted layout: attaching directly to Progman=0x{progman:X} below SHELLDLL_DefView=0x{defViewInProgman:X}"
                );
                return new DesktopAttachTarget(
                    progman,
                    IsDirectProgmanParent: true,
                    "Progman direct parent (below SHELLDLL_DefView)",
                    InsertAfterHwnd: defViewInProgman
                );
            }

            // 2. Classic layout (Windows 10 / pre-24H2): find top-level WorkerW hosting SHELLDLL_DefView
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

            if (defViewContainer != 0 && defViewContainer != progman)
            {
                // Top-level WorkerW placed immediately after defViewContainer in Z-order
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
            LogAttach($"Detected Classic WorkerW layout: parenting to top-level WorkerW=0x{workerTarget:X}");
            return new DesktopAttachTarget(
                workerTarget,
                IsDirectProgmanParent: false,
                "Classic WorkerW background layer (Behind desktop icons)",
                InsertAfterHwnd: DesktopNativeMethods.HWND_BOTTOM
            );
        }

        // Secondary fallback for Win11 24H2+: if WorkerW child wasn't spawned, Progman itself hosts behind SHELLDLL_DefView
        var defViewCheck = DesktopNativeMethods.FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        if (defViewCheck != 0)
        {
            return new DesktopAttachTarget(progman, IsDirectProgmanParent: true, "Progman direct parent (Behind desktop icons)");
        }

        // Tertiary fallback: Enumerate all visible, screen-sized top-level WorkerW windows
        DesktopNativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                if (!DesktopNativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }
                var sb = new StringBuilder(256);
                DesktopNativeMethods.GetClassName(hwnd, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "WorkerW", StringComparison.Ordinal))
                {
                    var defView = DesktopNativeMethods.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
                    if (defView == 0 && DesktopNativeMethods.GetWindowRect(hwnd, out var rc) && (rc.Right - rc.Left) > 500)
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

    internal static void LogAttach(string message)
    {
        Logger.Info($"[WallpaperAttach] {message}");
        try
        {
            var logDir = Logger.GetLogPath();
            if (!string.IsNullOrWhiteSpace(logDir))
            {
                global::System.IO.Directory.CreateDirectory(logDir);
                var attachLog = global::System.IO.Path.Combine(logDir, "wallpaper-attach.log");
                global::System.IO.File.AppendAllText(attachLog, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    [global::System.Runtime.InteropServices.ComImport]
    [global::System.Runtime.InteropServices.Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [global::System.Runtime.InteropServices.InterfaceType(global::System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper(
            [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)]
                string? monitorID,
            [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)] string wallpaper
        );
    }

    /// <summary>
    /// Fallback for GIFs/videos when live window attachment fails: sets a static frame via IDesktopWallpaper COM API.
    /// </summary>
    public static bool SetStaticWallpaperViaCom(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !global::System.IO.File.Exists(imagePath))
        {
            return false;
        }

        try
        {
            var clsid = new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
            var type = Type.GetTypeFromCLSID(clsid);
            if (type == null)
            {
                return false;
            }

            var instance = Activator.CreateInstance(type);
            if (instance is IDesktopWallpaper desktopWallpaper)
            {
                try
                {
                    desktopWallpaper.SetWallpaper(null, global::System.IO.Path.GetFullPath(imagePath));
                    LogAttach($"Applied static frame fallback via IDesktopWallpaper: {imagePath}");
                    return true;
                }
                finally
                {
                    global::System.Runtime.InteropServices.Marshal.ReleaseComObject(desktopWallpaper);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"IDesktopWallpaper static fallback failed: {ex.Message}");
        }

        return false;
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
