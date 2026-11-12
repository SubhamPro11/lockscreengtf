namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Multi-monitor layout mode for desktop wallpaper.
/// </summary>
public enum WallpaperLayoutMode
{
    /// <summary>
    /// Display wallpaper on a chosen single monitor.
    /// </summary>
    SingleMonitor,

    /// <summary>
    /// Span a single wallpaper across all connected displays.
    /// </summary>
    SpanAllMonitors,

    /// <summary>
    /// Duplicate the wallpaper on each connected display.
    /// </summary>
    Duplicate
}

