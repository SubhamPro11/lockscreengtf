using System;
using System.IO;
using System.Runtime.InteropServices;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Library;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LockscreenGif.Features.DesktopWallpaper.Services.System;

/// <summary>
/// Manages the system notification tray icon and context menu for background wallpaper control.
/// </summary>
public sealed class WallpaperTrayService : IDisposable
{
    private const uint WM_TRAYICON = 0x8000 + 100; // WM_APP + 100
    private const uint TrayIconId = 1001;

    private readonly IWallpaperEngineService _engineService;
    private readonly IWallpaperLibraryService _libraryService;
    private readonly DesktopNativeMethods.WndProc _wndProcDelegate;

    private nint _messageHwnd;
    private bool _isIconVisible;
    private bool _disposed;

    public WallpaperTrayService(IWallpaperEngineService engineService, IWallpaperLibraryService libraryService)
    {
        _engineService = engineService;
        _libraryService = libraryService;
        _wndProcDelegate = TrayWindowProc;

        CreateMessageWindow();
        ShowTrayIcon();
    }

    private void CreateMessageWindow()
    {
        var className = "LockscreenGif_TrayWindow_" + Guid.NewGuid().ToString("N")[..8];
        var hInstance = DesktopNativeMethods.GetModuleHandle(null);

        var wndClass = new DesktopNativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(DesktopNativeMethods.WNDCLASSEX)),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className
        };

        DesktopNativeMethods.RegisterClassEx(ref wndClass);

        _messageHwnd = DesktopNativeMethods.CreateWindowEx(
            0,
            className,
            "LockscreenGif Tray Message Window",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            hInstance,
            0
        );
    }

    public void ShowTrayIcon()
    {
        if (_isIconVisible || _messageHwnd == 0)
        {
            return;
        }

        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
        nint hIcon = 0;
        if (File.Exists(iconPath))
        {
            // Load icon
            hIcon = ExtractIcon(iconPath);
        }

        var nid = new DesktopNativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf(typeof(DesktopNativeMethods.NOTIFYICONDATA)),
            hWnd = _messageHwnd,
            uID = TrayIconId,
            uFlags = DesktopNativeMethods.NIF_MESSAGE | DesktopNativeMethods.NIF_ICON | DesktopNativeMethods.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "LockscreenGif - Live Wallpaper"
        };

        _isIconVisible = DesktopNativeMethods.Shell_NotifyIcon(DesktopNativeMethods.NIM_ADD, ref nid);
    }

    public void HideTrayIcon()
    {
        if (!_isIconVisible || _messageHwnd == 0)
        {
            return;
        }

        var nid = new DesktopNativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf(typeof(DesktopNativeMethods.NOTIFYICONDATA)),
            hWnd = _messageHwnd,
            uID = TrayIconId
        };

        DesktopNativeMethods.Shell_NotifyIcon(DesktopNativeMethods.NIM_DELETE, ref nid);
        _isIconVisible = false;
    }

    private nint TrayWindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);

            if (mouseMsg == 0x0205 /* WM_RBUTTONUP */)
            {
                ShowContextMenu();
                return 0;
            }
            else if (mouseMsg == 0x0203 /* WM_LBUTTONDBLCLK */ || mouseMsg == 0x0202 /* WM_LBUTTONUP */)
            {
                RestoreMainWindow();
                return 0;
            }
        }

        return DesktopNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        DesktopNativeMethods.GetCursorPos(out var pt);

        var hMenu = DesktopNativeMethods.CreatePopupMenu();
        if (hMenu == 0)
        {
            return;
        }

        try
        {
            var isRunning = _engineService.IsRunning;
            var isPaused = _engineService.Status == WallpaperEngineStatus.Paused;

            var pauseResumeText = isPaused ? "Resume Wallpaper" : "Pause Wallpaper";
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_STRING, new nint(1), pauseResumeText);
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_STRING, new nint(2), "Next Wallpaper");
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_SEPARATOR, 0, string.Empty);
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_STRING, new nint(3), "Open LockscreenGif");
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_SEPARATOR, 0, string.Empty);
            DesktopNativeMethods.AppendMenu(hMenu, DesktopNativeMethods.MF_STRING, new nint(4), "Exit");

            DesktopNativeMethods.SetForegroundWindow(_messageHwnd);

            var cmd = DesktopNativeMethods.TrackPopupMenuEx(
                hMenu,
                DesktopNativeMethods.TPM_RETURNCMD | DesktopNativeMethods.TPM_RIGHTBUTTON,
                pt.X,
                pt.Y,
                _messageHwnd,
                0
            );

            switch (cmd)
            {
                case 1:
                    if (isPaused)
                    {
                        _ = _engineService.ResumeAsync();
                    }
                    else
                    {
                        _ = _engineService.PauseAsync();
                    }
                    break;

                case 2:
                    _ = _libraryService.RotatePlaylistNextAsync();
                    break;

                case 3:
                    RestoreMainWindow();
                    break;

                case 4:
                    HideTrayIcon();
                    Application.Current.Exit();
                    break;
            }
        }
        finally
        {
            DesktopNativeMethods.DestroyMenu(hMenu);
        }
    }

    public static void RestoreMainWindow()
    {
        try
        {
            var mainWin = App.MainWindow;
            if (mainWin != null)
            {
                var hwnd = WindowNative.GetWindowHandle(mainWin);
                if (hwnd != 0)
                {
                    DesktopNativeMethods.ShowWindow(hwnd, 9 /* SW_RESTORE */);
                    DesktopNativeMethods.SetForegroundWindow(hwnd);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to restore main window: {ex.Message}");
        }
    }

    private static nint ExtractIcon(string iconPath)
    {
        try
        {
            return ExtractIconW(DesktopNativeMethods.GetModuleHandle(null), iconPath, 0);
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint ExtractIconW(nint hInst, string lpszExeFileName, int nIconIndex);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        HideTrayIcon();
        if (_messageHwnd != 0)
        {
            DesktopNativeMethods.DestroyWindow(_messageHwnd);
            _messageHwnd = 0;
        }
    }
}

