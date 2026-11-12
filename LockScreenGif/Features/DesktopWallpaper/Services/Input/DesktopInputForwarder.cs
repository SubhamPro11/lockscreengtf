using System;
using System.Runtime.InteropServices;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;
using LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Input;

/// <summary>
/// Forwards low-level desktop mouse clicks and cursor movement to the active wallpaper renderer
/// when input interaction is enabled for web wallpapers.
/// </summary>
public sealed class DesktopInputForwarder : IDisposable
{
    private readonly DesktopNativeMethods.HookProc _hookProcDelegate;
    private nint _hookHandle;
    private nint _hostHwnd;
    private WebView2WallpaperRenderer? _renderer;
    private bool _isEnabled;
    private bool _disposed;

    public bool IsEnabled => _isEnabled;

    public DesktopInputForwarder()
    {
        _hookProcDelegate = HookCallback;
    }

    public void Attach(nint hostHwnd, WebView2WallpaperRenderer renderer)
    {
        _hostHwnd = hostHwnd;
        _renderer = renderer;
    }

    public void Enable()
    {
        if (_isEnabled || _hostHwnd == 0)
        {
            return;
        }

        var hMod = DesktopNativeMethods.GetModuleHandle(null);
        _hookHandle = DesktopNativeMethods.SetWindowsHookEx(DesktopNativeMethods.WH_MOUSE_LL, _hookProcDelegate, hMod, 0);
        _isEnabled = _hookHandle != 0;
    }

    public void Disable()
    {
        if (_hookHandle != 0)
        {
            DesktopNativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
        _isEnabled = false;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && _isEnabled && _renderer != null && _hostHwnd != 0)
        {
            var msg = (uint)wParam;
            var hookStruct = Marshal.PtrToStructure<DesktopNativeMethods.MSLLHOOKSTRUCT>(lParam);
            var pt = hookStruct.pt;

            var targetWnd = DesktopNativeMethods.WindowFromPoint(pt);
            if (targetWnd != 0)
            {
                var clientPt = pt;
                if (DesktopNativeMethods.ScreenToClient(_hostHwnd, ref clientPt))
                {
                    var lParamValue = (clientPt.Y << 16) | (clientPt.X & 0xFFFF);
                    DesktopNativeMethods.PostMessage(_hostHwnd, msg, wParam, new nint(lParamValue));
                }
            }
        }

        return DesktopNativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Disable();
            _renderer = null;
        }
    }
}
