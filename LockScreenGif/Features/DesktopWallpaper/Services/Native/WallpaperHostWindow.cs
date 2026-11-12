using System;
using System.Runtime.InteropServices;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Native;

/// <summary>
/// A native Win32 window created to host wallpaper content behind the desktop icons.
/// </summary>
internal sealed class WallpaperHostWindow : IDisposable
{
    private const string WindowClassName = "LockscreenGif_WallpaperHost";
    private static bool _classRegistered;
    private static readonly object _classLock = new();

    private readonly DesktopNativeMethods.WndProc _wndProcDelegate;
    private nint _hwnd;
    private bool _disposed;
    private uint _backgroundColor = 0x001E1E1E; // Default subtle dark background

    public nint Hwnd => _hwnd;
    public bool IsAttached => _hwnd != 0;

    public WallpaperHostWindow()
    {
        _wndProcDelegate = WindowProc;
        EnsureClassRegistered();
    }

    private void EnsureClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered)
            {
                return;
            }

            var wndClass = new DesktopNativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(DesktopNativeMethods.WNDCLASSEX)),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = DesktopNativeMethods.GetModuleHandle(null),
                hIcon = 0,
                hCursor = 0,
                hbrBackground = 0,
                lpszMenuName = null,
                lpszClassName = WindowClassName,
                hIconSm = 0,
            };

            var atom = DesktopNativeMethods.RegisterClassEx(ref wndClass);
            if (
                atom != 0
                || Marshal.GetLastWin32Error() == 1410 /* ERROR_CLASS_ALREADY_EXISTS */
            )
            {
                _classRegistered = true;
            }
            else
            {
                Logger.Warn($"RegisterClassEx returned {atom}, error {Marshal.GetLastWin32Error()}");
            }
        }
    }

    /// <summary>
    /// Creates the window and attaches it behind the desktop icons.
    /// </summary>
    public bool CreateAndAttach(DesktopAttachTarget target, int left, int top, int width, int height)
    {
        if (_hwnd != 0)
        {
            Destroy();
        }

        try
        {
            var hInstance = DesktopNativeMethods.GetModuleHandle(null);
            _hwnd = DesktopNativeMethods.CreateWindowEx(
                DesktopNativeMethods.WS_EX_TOOLWINDOW | DesktopNativeMethods.WS_EX_NOACTIVATE,
                WindowClassName,
                "LockscreenGif Wallpaper Host",
                DesktopNativeMethods.WS_CHILD
                    | DesktopNativeMethods.WS_CLIPCHILDREN
                    | DesktopNativeMethods.WS_CLIPSIBLINGS
                    | DesktopNativeMethods.WS_VISIBLE,
                left,
                top,
                width,
                height,
                target.ParentHwnd,
                0,
                hInstance,
                0
            );

            if (_hwnd == 0)
            {
                var error = Marshal.GetLastWin32Error();
                Logger.Error($"CreateWindowEx failed with Win32 error {error}");
                return false;
            }

            // Reparent to the desktop host
            var previousParent = DesktopNativeMethods.SetParent(_hwnd, target.ParentHwnd);
            Logger.Info($"SetParent to 0x{target.ParentHwnd:X8} (previous parent: 0x{previousParent:X8}) - {target.Description}");

            // Ensure correct Z-order and size
            // For Windows 11 24H2 where Progman hosts SHELLDLL_DefView directly,
            // placing the window at HWND_BOTTOM keeps it behind the desktop icons.
            DesktopNativeMethods.SetWindowPos(
                _hwnd,
                DesktopNativeMethods.HWND_BOTTOM,
                left,
                top,
                width,
                height,
                DesktopNativeMethods.SWP_NOACTIVATE | DesktopNativeMethods.SWP_SHOWWINDOW
            );

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create and attach wallpaper host window", ex);
            Destroy();
            return false;
        }
    }

    /// <summary>
    /// Updates the window bounds (e.g. on DPI or resolution change).
    /// </summary>
    public void SetBounds(int left, int top, int width, int height)
    {
        if (_hwnd != 0)
        {
            DesktopNativeMethods.SetWindowPos(
                _hwnd,
                DesktopNativeMethods.HWND_BOTTOM,
                left,
                top,
                width,
                height,
                DesktopNativeMethods.SWP_NOACTIVATE | DesktopNativeMethods.SWP_NOZORDER
            );
        }
    }

    public void SetBackgroundColor(uint colorBgr)
    {
        _backgroundColor = colorBgr;
        if (_hwnd != 0)
        {
            DesktopNativeMethods.RedrawWindow(_hwnd, 0, 0, DesktopNativeMethods.RDW_INVALIDATE | DesktopNativeMethods.RDW_UPDATENOW);
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case DesktopNativeMethods.WM_ERASEBKGND:
                return new nint(1); // Suppress default background erase to prevent flicker

            case DesktopNativeMethods.WM_PAINT:
                PaintBackground(hWnd);
                return 0;

            case DesktopNativeMethods.WM_DESTROY:
                _hwnd = 0;
                return 0;

            default:
                return DesktopNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void PaintBackground(nint hWnd)
    {
        var hdc = DesktopNativeMethods.GetDC(hWnd);
        if (hdc != 0)
        {
            try
            {
                if (DesktopNativeMethods.GetClientRect(hWnd, out var rect))
                {
                    var hBrush = DesktopNativeMethods.CreateSolidBrush(_backgroundColor);
                    if (hBrush != 0)
                    {
                        try
                        {
                            DesktopNativeMethods.FillRect(hdc, ref rect, hBrush);
                        }
                        finally
                        {
                            DesktopNativeMethods.DeleteObject(hBrush);
                        }
                    }
                }
            }
            finally
            {
                DesktopNativeMethods.ReleaseDC(hWnd, hdc);
            }
        }
    }

    public void Destroy()
    {
        if (_hwnd != 0)
        {
            var h = _hwnd;
            _hwnd = 0;
            try
            {
                DesktopNativeMethods.DestroyWindow(h);
            }
            catch (Exception ex)
            {
                Logger.Error("Error destroying wallpaper host window", ex);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Destroy();
        }
        GC.SuppressFinalize(this);
    }
}
