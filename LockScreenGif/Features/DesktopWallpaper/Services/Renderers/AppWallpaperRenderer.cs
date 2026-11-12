using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

/// <summary>
/// Renderer for executing a user-chosen .exe application and embedding its window
/// as a live wallpaper behind the desktop icons.
/// </summary>
public sealed class AppWallpaperRenderer : IWallpaperRenderer
{
    private Process? _process;
    private nint _appHwnd;
    private nint _hostHwnd;
    private bool _isPlaying;
    private bool _isDisposed;
    private int _width;
    private int _height;

    public bool IsPlaying => _isPlaying;
    public nint ContentHwnd => _appHwnd != 0 ? _appHwnd : _hostHwnd;

    public Task InitializeAsync(nint hostHwnd, int left, int top, int width, int height)
    {
        _hostHwnd = hostHwnd;
        _width = width;
        _height = height;
        return Task.CompletedTask;
    }

    public async Task LoadAsync(WallpaperItem item)
    {
        var exePath = item.SourcePathOrUrl;
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Executable file not found: {exePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? string.Empty,
            UseShellExecute = true
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to launch process: {exePath}");

        // Wait for main window handle
        var waited = 0;
        while (_process.MainWindowHandle == 0 && !_process.HasExited && waited < 5000)
        {
            await Task.Delay(100);
            waited += 100;
            _process.Refresh();
        }

        _appHwnd = _process.MainWindowHandle;
        if (_appHwnd == 0)
        {
            throw new TimeoutException("Timed out waiting for application window handle.");
        }

        // Strip window caption and borders to make it a seamless child window
        var style = DesktopNativeMethods.GetWindowLongPtr(_appHwnd, DesktopNativeMethods.GWL_STYLE);
        var newStyle = (style.ToInt64() & ~DesktopNativeMethods.WS_CAPTION & ~DesktopNativeMethods.WS_THICKFRAME) | DesktopNativeMethods.WS_CHILD;
        DesktopNativeMethods.SetWindowLongPtr(_appHwnd, DesktopNativeMethods.GWL_STYLE, new nint(newStyle));

        // Reparent to the wallpaper host window
        DesktopNativeMethods.SetParent(_appHwnd, _hostHwnd);
        DesktopNativeMethods.SetWindowPos(
            _appHwnd,
            DesktopNativeMethods.HWND_TOP,
            0,
            0,
            _width,
            _height,
            DesktopNativeMethods.SWP_SHOWWINDOW | DesktopNativeMethods.SWP_FRAMECHANGED
        );

        _isPlaying = true;
    }

    public Task SetBoundsAsync(int left, int top, int width, int height)
    {
        _width = width;
        _height = height;
        if (_appHwnd != 0)
        {
            DesktopNativeMethods.SetWindowPos(
                _appHwnd,
                DesktopNativeMethods.HWND_TOP,
                0,
                0,
                width,
                height,
                DesktopNativeMethods.SWP_SHOWWINDOW | DesktopNativeMethods.SWP_NOACTIVATE
            );
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (_appHwnd != 0)
        {
            _isPlaying = false;
            DesktopNativeMethods.ShowWindow(_appHwnd, 0 /* SW_HIDE */);
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_appHwnd != 0)
        {
            _isPlaying = true;
            DesktopNativeMethods.ShowWindow(_appHwnd, 5 /* SW_SHOW */);
        }
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume) => Task.CompletedTask;
    public Task SetMutedAsync(bool muted) => Task.CompletedTask;
    public Task SetPlaybackRateAsync(double rate) => Task.CompletedTask;
    public Task SetFitModeAsync(WallpaperFitMode fitMode) => Task.CompletedTask;
    public Task PostWebMessageAsJsonAsync(string json) => Task.CompletedTask;

    public Task CloseAsync()
    {
        _isPlaying = false;
        _appHwnd = 0;

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.CloseMainWindow();
                if (!_process.WaitForExit(1000))
                {
                    _process.Kill(true);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error terminating wallpaper process: {ex.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        await CloseAsync();
    }
}

