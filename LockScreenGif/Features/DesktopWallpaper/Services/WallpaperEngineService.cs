using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Input;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;
using LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

namespace LockscreenGif.Features.DesktopWallpaper.Services;

/// <summary>
/// Core desktop wallpaper engine service.
/// Renders wallpaper content (video, web, images, applications) behind desktop icons across
/// single and multi-monitor setups, handles dual Windows 10/11 24H2+ hierarchy, and ensures
/// original wallpaper restoration.
/// </summary>
public sealed class WallpaperEngineService : IWallpaperEngineService, IDisposable
{
    private readonly object _lock = new();
    private readonly List<WallpaperHostWindow> _hostWindows = [];
    private readonly List<IWallpaperRenderer> _renderers = [];
    private readonly DesktopInputForwarder _inputForwarder = new();

    private WallpaperEngineStatus _status = WallpaperEngineStatus.Stopped;
    private string? _currentErrorMessage;
    private string? _originalWallpaperPath;
    private string? _activeTargetDescription;
    private bool _originalWallpaperRecorded;
    private List<DisplayMonitor> _monitors = [];
    private DisplayMonitor? _selectedMonitor;
    private WallpaperLayoutMode _layoutMode = WallpaperLayoutMode.SpanAllMonitors;
    private WallpaperItem? _currentWallpaper;
    private bool _allowAdvancedExecutables;
    private bool _disposed;

    public WallpaperEngineStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    public string? CurrentErrorMessage => _currentErrorMessage;
    public string? OriginalWallpaperPath => _originalWallpaperPath;
    public string? ActiveTargetDescription => _activeTargetDescription;

    public IReadOnlyList<DisplayMonitor> Monitors
    {
        get
        {
            lock (_lock)
            {
                return _monitors.AsReadOnly();
            }
        }
    }

    public DisplayMonitor? SelectedMonitor
    {
        get => _selectedMonitor;
        set => _selectedMonitor = value;
    }

    public WallpaperLayoutMode LayoutMode
    {
        get => _layoutMode;
        set => _layoutMode = value;
    }

    public WallpaperItem? CurrentWallpaper => _currentWallpaper;

    public bool AllowAdvancedExecutables
    {
        get => _allowAdvancedExecutables;
        set => _allowAdvancedExecutables = value;
    }

    public bool IsRunning => Status == WallpaperEngineStatus.Running || Status == WallpaperEngineStatus.Paused;

    public event EventHandler<WallpaperEngineStatus>? StatusChanged;

    public WallpaperEngineService()
    {
        RefreshMonitors();
    }

    public void RefreshMonitors()
    {
        lock (_lock)
        {
            _monitors = DesktopAttachHelper.GetMonitors().ToList();
            if (_selectedMonitor == null || !_monitors.Any(m => m.DeviceName == _selectedMonitor.DeviceName))
            {
                _selectedMonitor = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();
            }
        }
    }

    public async Task<bool> StartAsync()
    {
        if (CurrentWallpaper != null)
        {
            return await ApplyWallpaperAsync(CurrentWallpaper);
        }

        return await InitializeEngineAsync();
    }

    public async Task<bool> ApplyWallpaperAsync(WallpaperItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Type == WallpaperType.Application && !AllowAdvancedExecutables)
        {
            throw new InvalidOperationException(
                "Running executable wallpapers is restricted. Enable the 'Advanced: Allow Executables' setting first."
            );
        }

        _currentWallpaper = item;

        var initialized = await InitializeEngineAsync();
        if (!initialized)
        {
            return false;
        }

        try
        {
            await CleanRenderersAsync();

            for (var i = 0; i < _hostWindows.Count; i++)
            {
                var hostWindow = _hostWindows[i];
                var (width, height) = GetWindowSize(hostWindow.Hwnd);

                IWallpaperRenderer renderer = item.Type == WallpaperType.Application
                    ? new AppWallpaperRenderer()
                    : new WebView2WallpaperRenderer();

                await renderer.InitializeAsync(hostWindow.Hwnd, 0, 0, width, height);
                await renderer.LoadAsync(item);
                await renderer.SetFitModeAsync(item.FitMode);
                await renderer.SetVolumeAsync(item.Volume);
                await renderer.SetMutedAsync(item.IsMuted);
                await renderer.SetPlaybackRateAsync(item.PlaybackSpeed);

                _renderers.Add(renderer);

                // Enable input forwarder on the primary renderer if requested
                if (i == 0 && renderer is WebView2WallpaperRenderer webViewRenderer)
                {
                    _inputForwarder.Attach(hostWindow.Hwnd, webViewRenderer);
                    if (item.ForwardInput)
                    {
                        _inputForwarder.Enable();
                    }
                    else
                    {
                        _inputForwarder.Disable();
                    }
                }
            }

            Status = WallpaperEngineStatus.Running;
            Logger.Info($"Wallpaper '{item.Title}' applied successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _currentErrorMessage = $"Failed to render wallpaper: {ex.Message}";
            Logger.Error(_currentErrorMessage, ex);
            Status = WallpaperEngineStatus.Error;
            return false;
        }
    }

    private Task<bool> InitializeEngineAsync()
    {
        lock (_lock)
        {
            if (IsRunning && _hostWindows.Count > 0)
            {
                return Task.FromResult(true);
            }

            Status = WallpaperEngineStatus.Starting;
            _currentErrorMessage = null;

            try
            {
                if (!_originalWallpaperRecorded)
                {
                    _originalWallpaperPath = DesktopAttachHelper.GetCurrentWallpaperPath();
                    _originalWallpaperRecorded = true;
                    Logger.Info($"Recorded original wallpaper path: {_originalWallpaperPath ?? "(none)"}");
                }

                var target = DesktopAttachHelper.FindDesktopHost();
                if (target == null || target.ParentHwnd == 0)
                {
                    _currentErrorMessage = "Unable to locate or initialize the Windows desktop background window hierarchy (Progman/WorkerW).";
                    Logger.Error(_currentErrorMessage);
                    Status = WallpaperEngineStatus.Error;
                    return Task.FromResult(false);
                }

                _activeTargetDescription = target.Description;
                Logger.Info($"Attaching wallpaper to {target.Description} (0x{target.ParentHwnd:X8})");

                RefreshMonitors();
                CleanHostWindows();

                if (_layoutMode == WallpaperLayoutMode.SpanAllMonitors || _monitors.Count == 0)
                {
                    var (left, top, width, height) = DesktopAttachHelper.GetVirtualScreenBounds();
                    var hostWindow = new WallpaperHostWindow();
                    if (!hostWindow.CreateAndAttach(target, left, top, width, height))
                    {
                        throw new InvalidOperationException("Failed to attach wallpaper host window to desktop hierarchy.");
                    }
                    _hostWindows.Add(hostWindow);
                }
                else if (_layoutMode == WallpaperLayoutMode.Duplicate)
                {
                    foreach (var monitor in _monitors)
                    {
                        var hostWindow = new WallpaperHostWindow();
                        if (hostWindow.CreateAndAttach(target, monitor.Left, monitor.Top, monitor.Width, monitor.Height))
                        {
                            _hostWindows.Add(hostWindow);
                        }
                    }
                }
                else // SingleMonitor
                {
                    var monitor = _selectedMonitor ?? _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();
                    if (monitor != null)
                    {
                        var hostWindow = new WallpaperHostWindow();
                        if (!hostWindow.CreateAndAttach(target, monitor.Left, monitor.Top, monitor.Width, monitor.Height))
                        {
                            throw new InvalidOperationException($"Failed to attach wallpaper host to monitor {monitor.DeviceName}.");
                        }
                        _hostWindows.Add(hostWindow);
                    }
                }

                Status = WallpaperEngineStatus.Running;
                Logger.Info($"Wallpaper engine host windows created: count={_hostWindows.Count}.");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _currentErrorMessage = $"Error starting wallpaper engine: {ex.Message}";
                Logger.Error("Wallpaper engine startup failed", ex);
                CleanHostWindows();
                DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
                Status = WallpaperEngineStatus.Error;
                return Task.FromResult(false);
            }
        }
    }

    public async Task StopAsync()
    {
        if (Status == WallpaperEngineStatus.Stopped)
        {
            return;
        }

        try
        {
            Logger.Info("Stopping wallpaper engine...");
            _inputForwarder.Disable();
            await CleanRenderersAsync();
            CleanHostWindows();

            if (_originalWallpaperRecorded)
            {
                DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
            }

            Status = WallpaperEngineStatus.Stopped;
            _currentErrorMessage = null;
            Logger.Info("Wallpaper engine stopped and original wallpaper restored.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error stopping wallpaper engine", ex);
            Status = WallpaperEngineStatus.Error;
        }
    }

    public async Task PauseAsync()
    {
        if (Status != WallpaperEngineStatus.Running)
        {
            return;
        }

        Logger.Info("Pausing wallpaper engine...");
        foreach (var renderer in _renderers)
        {
            await renderer.PauseAsync();
        }
        Status = WallpaperEngineStatus.Paused;
    }

    public async Task ResumeAsync()
    {
        if (Status != WallpaperEngineStatus.Paused)
        {
            return;
        }

        Logger.Info("Resuming wallpaper engine...");
        foreach (var renderer in _renderers)
        {
            await renderer.ResumeAsync();
        }
        Status = WallpaperEngineStatus.Running;
    }

    public async Task SetVolumeAsync(double volume)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.Volume = volume;
        }
        foreach (var renderer in _renderers)
        {
            await renderer.SetVolumeAsync(volume);
        }
    }

    public async Task SetMutedAsync(bool muted)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.IsMuted = muted;
        }
        foreach (var renderer in _renderers)
        {
            await renderer.SetMutedAsync(muted);
        }
    }

    public async Task SetPlaybackRateAsync(double rate)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.PlaybackSpeed = rate;
        }
        foreach (var renderer in _renderers)
        {
            await renderer.SetPlaybackRateAsync(rate);
        }
    }

    public async Task SetFitModeAsync(WallpaperFitMode fitMode)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.FitMode = fitMode;
        }
        foreach (var renderer in _renderers)
        {
            await renderer.SetFitModeAsync(fitMode);
        }
    }

    public Task SetForwardInputAsync(bool forwardInput)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.ForwardInput = forwardInput;
        }

        if (forwardInput && _renderers.OfType<WebView2WallpaperRenderer>().Any())
        {
            _inputForwarder.Enable();
        }
        else
        {
            _inputForwarder.Disable();
        }

        return Task.CompletedTask;
    }

    private void CleanHostWindows()
    {
        foreach (var win in _hostWindows)
        {
            win.Dispose();
        }
        _hostWindows.Clear();
    }

    private async Task CleanRenderersAsync()
    {
        foreach (var renderer in _renderers)
        {
            try
            {
                await renderer.CloseAsync();
                await renderer.DisposeAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error closing renderer: {ex.Message}");
            }
        }
        _renderers.Clear();
    }

    private static (int width, int height) GetWindowSize(nint hwnd)
    {
        if (hwnd != 0 && DesktopNativeMethods.GetClientRect(hwnd, out var rc))
        {
            return (rc.Right - rc.Left, rc.Bottom - rc.Top);
        }
        return (1920, 1080);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _inputForwarder.Dispose();
        CleanRenderersAsync().GetAwaiter().GetResult();
        CleanHostWindows();

        if (_originalWallpaperRecorded)
        {
            DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
        }
    }
}
