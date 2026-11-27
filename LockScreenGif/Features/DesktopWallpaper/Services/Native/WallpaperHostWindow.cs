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
    /// Creates the host window as a top-level popup suitable for WebView2 initialization.
    /// </summary>
    public bool Create(int left, int top, int width, int height)
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
                DesktopNativeMethods.WS_POPUP
                    | DesktopNativeMethods.WS_CLIPCHILDREN
                    | DesktopNativeMethods.WS_CLIPSIBLINGS
                    | DesktopNativeMethods.WS_VISIBLE,
                left,
                top,
                width,
                height,
                0,
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

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to create wallpaper host window", ex);
            Destroy();
            return false;
        }
    }

    /// <summary>
    /// Attaches the initialized host window behind desktop icons.
    /// </summary>
    public bool AttachToDesktop(DesktopAttachTarget target, int left, int top, int width, int height)
    {
        if (_hwnd == 0 || target == null || target.ParentHwnd == 0)
        {
            return false;
        }

        try
        {
            var insertAfter = target.InsertAfterHwnd != 0 ? target.InsertAfterHwnd : DesktopNativeMethods.HWND_BOTTOM;
            var previousParent = DesktopNativeMethods.SetParent(_hwnd, target.ParentHwnd);
            var setParentErr = Marshal.GetLastWin32Error();
            DesktopAttachHelper.LogAttach(
                $"AttachToDesktop: chosenParent=0x{target.ParentHwnd:X8} ({target.Description}), insertAfter=0x{insertAfter:X}, SetParent={previousParent:X8}, SetParentLastError={setParentErr}"
            );

            if (previousParent == 0 && setParentErr != 0)
            {
                return false;
            }

            const uint wsBorder = 0x00800000;
            var style = (long)DesktopNativeMethods.GetWindowLongPtr(_hwnd, DesktopNativeMethods.GWL_STYLE);
            style &= ~(
                (long)DesktopNativeMethods.WS_POPUP
                | DesktopNativeMethods.WS_CAPTION
                | DesktopNativeMethods.WS_THICKFRAME
                | DesktopNativeMethods.WS_MINIMIZEBOX
                | DesktopNativeMethods.WS_MAXIMIZEBOX
                | wsBorder
            );
            style |=
                DesktopNativeMethods.WS_CHILD
                | DesktopNativeMethods.WS_VISIBLE
                | DesktopNativeMethods.WS_CLIPCHILDREN
                | DesktopNativeMethods.WS_CLIPSIBLINGS;
            DesktopNativeMethods.SetWindowLongPtr(_hwnd, DesktopNativeMethods.GWL_STYLE, new nint(style));

            var exStyle = (long)DesktopNativeMethods.GetWindowLongPtr(_hwnd, DesktopNativeMethods.GWL_EXSTYLE);
            exStyle |= DesktopNativeMethods.WS_EX_TOOLWINDOW | DesktopNativeMethods.WS_EX_NOACTIVATE;
            DesktopNativeMethods.SetWindowLongPtr(_hwnd, DesktopNativeMethods.GWL_EXSTYLE, new nint(exStyle));

            // Convert screen coordinates to parent window client coordinates (handles negative multi-monitor coordinates)
            var pt = new DesktopNativeMethods.POINT { X = left, Y = top };
            DesktopNativeMethods.ScreenToClient(target.ParentHwnd, ref pt);

            var swpOk = DesktopNativeMethods.SetWindowPos(
                _hwnd,
                insertAfter,
                pt.X,
                pt.Y,
                width,
                height,
                DesktopNativeMethods.SWP_NOACTIVATE | DesktopNativeMethods.SWP_SHOWWINDOW | DesktopNativeMethods.SWP_FRAMECHANGED
            );
            var swpErr = Marshal.GetLastWin32Error();

            DesktopNativeMethods.ShowWindow(_hwnd, DesktopNativeMethods.SW_SHOWNOACTIVATE);

            DesktopNativeMethods.GetWindowRect(_hwnd, out var finalRect);
            var isVisible = DesktopNativeMethods.IsWindowVisible(_hwnd);
            var dpi = DesktopNativeMethods.GetDpiForWindow(_hwnd);
            DesktopAttachHelper.LogAttach(
                $"AttachToDesktop result: SetWindowPos={swpOk} (lastError={swpErr}), clientOrigin=({pt.X},{pt.Y}), finalRect=({finalRect.Left},{finalRect.Top},{finalRect.Right},{finalRect.Bottom}), visible={isVisible}, dpi={dpi}"
            );
            return swpOk && isVisible;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to dock wallpaper host window to desktop", ex);
            return false;
        }
    }

    /// <summary>
    /// Creates the window and attaches it behind the desktop icons.
    /// </summary>
    public bool CreateAndAttach(DesktopAttachTarget target, int left, int top, int width, int height)
    {
        if (!Create(left, top, width, height))
        {
            return false;
        }
        return AttachToDesktop(target, left, top, width, height);
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
                var hdc = DesktopNativeMethods.BeginPaint(hWnd, out var ps);
                if (hdc != 0 && DesktopNativeMethods.GetClientRect(hWnd, out var rect))
                {
                    var hBrush = DesktopNativeMethods.CreateSolidBrush(_backgroundColor);
                    if (hBrush != 0)
                    {
                        DesktopNativeMethods.FillRect(hdc, ref rect, hBrush);
                        DesktopNativeMethods.DeleteObject(hBrush);
                    }
                }
                DesktopNativeMethods.EndPaint(hWnd, ref ps);
                return 0;

            case DesktopNativeMethods.WM_DESTROY:
                _hwnd = 0;
                return 0;

            default:
                return DesktopNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
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
