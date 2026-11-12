using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;

namespace LockscreenGif.Features.DesktopWallpaper.Services;

/// <summary>
/// Core desktop wallpaper engine service.
/// Renders wallpaper windows behind desktop icons across single and multi-monitor setups,
/// detects Windows 10 / 11 24H2+ desktop hierarchy, and ensures original wallpaper restoration.
/// </summary>
public sealed class WallpaperEngineService : IWallpaperEngineService, IDisposable
{
    private readonly object _lock = new();
    private readonly List<WallpaperHostWindow> _hostWindows = [];
    private WallpaperEngineStatus _status = WallpaperEngineStatus.Stopped;
    private string? _currentErrorMessage;
    private string? _originalWallpaperPath;
    private string? _activeTargetDescription;
    private bool _originalWallpaperRecorded;
    private List<DisplayMonitor> _monitors = [];
    private DisplayMonitor? _selectedMonitor;
    private WallpaperLayoutMode _layoutMode = WallpaperLayoutMode.SpanAllMonitors;
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

    public Task<bool> StartAsync()
    {
        lock (_lock)
        {
            if (IsRunning)
            {
                return Task.FromResult(true);
            }

            Status = WallpaperEngineStatus.Starting;
            _currentErrorMessage = null;

            try
            {
                // Record the original wallpaper path before modifying desktop if not already saved
                if (!_originalWallpaperRecorded)
                {
                    _originalWallpaperPath = DesktopAttachHelper.GetCurrentWallpaperPath();
                    _originalWallpaperRecorded = true;
                    Logger.Info($"Recorded original wallpaper path: {_originalWallpaperPath ?? "(none)"}");
                }

                // Detect desktop host window (supporting Windows 10 and Windows 11 24H2+)
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
                    // Span all displays using virtual screen bounds
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
                    // Duplicate wallpaper across each monitor
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
                Logger.Info($"Wallpaper engine started successfully with {_hostWindows.Count} host window(s).");
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

    public Task StopAsync()
    {
        lock (_lock)
        {
            if (Status == WallpaperEngineStatus.Stopped)
            {
                return Task.CompletedTask;
            }

            try
            {
                Logger.Info("Stopping wallpaper engine...");
                CleanHostWindows();

                // Restore original wallpaper
                if (_originalWallpaperRecorded)
                {
                    DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
                }

                _activeTargetDescription = null;
                Status = WallpaperEngineStatus.Stopped;
            }
            catch (Exception ex)
            {
                Logger.Error("Error stopping wallpaper engine", ex);
                Status = WallpaperEngineStatus.Stopped;
            }

            return Task.CompletedTask;
        }
    }

    public Task PauseAsync()
    {
        lock (_lock)
        {
            if (Status == WallpaperEngineStatus.Running)
            {
                Status = WallpaperEngineStatus.Paused;
                Logger.Info("Wallpaper engine paused.");
            }
            return Task.CompletedTask;
        }
    }

    public Task ResumeAsync()
    {
        lock (_lock)
        {
            if (Status == WallpaperEngineStatus.Paused)
            {
                Status = WallpaperEngineStatus.Running;
                Logger.Info("Wallpaper engine resumed.");
            }
            return Task.CompletedTask;
        }
    }

    private void CleanHostWindows()
    {
        foreach (var window in _hostWindows)
        {
            try
            {
                window.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error("Error disposing wallpaper host window", ex);
            }
        }
        _hostWindows.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch { }
        }
        GC.SuppressFinalize(this);
    }
}

