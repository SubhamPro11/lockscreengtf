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

    bool IsRunning { get; }

    Task<bool> StartAsync();
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
    void RefreshMonitors();

    event EventHandler<WallpaperEngineStatus>? StatusChanged;
}

