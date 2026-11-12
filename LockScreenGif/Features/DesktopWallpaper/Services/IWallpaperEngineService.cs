using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services;

/// <summary>
/// Public interface for the Desktop Wallpaper Engine.
/// </summary>
public interface IWallpaperEngineService
{
    WallpaperEngineStatus Status { get; }
    string? CurrentErrorMessage { get; }
    string? OriginalWallpaperPath { get; }
    string? ActiveTargetDescription { get; }

    IReadOnlyList<DisplayMonitor> Monitors { get; }
    DisplayMonitor? SelectedMonitor { get; set; }
    WallpaperLayoutMode LayoutMode { get; set; }

    WallpaperItem? CurrentWallpaper { get; }
    bool AllowAdvancedExecutables { get; set; }
    LockscreenGif.Features.DesktopWallpaper.Services.Performance.WallpaperPerformanceMonitor PerformanceMonitor { get; }

    bool IsRunning { get; }

    Task<bool> StartAsync();
    Task<bool> ApplyWallpaperAsync(WallpaperItem item);
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task SetVolumeAsync(double volume);
    Task SetMutedAsync(bool muted);
    Task SetPlaybackRateAsync(double rate);
    Task SetFitModeAsync(WallpaperFitMode fitMode);
    Task SetForwardInputAsync(bool forwardInput);
    void RefreshMonitors();

    event EventHandler<WallpaperEngineStatus>? StatusChanged;
}
