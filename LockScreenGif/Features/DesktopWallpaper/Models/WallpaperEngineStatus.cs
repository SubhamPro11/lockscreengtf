namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Status of the desktop wallpaper engine.
/// </summary>
public enum WallpaperEngineStatus
{
    Stopped,
    Starting,
    Running,
    Paused,
    Error,
}
