namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Scaling and positioning modes for wallpaper rendering.
/// </summary>
public enum WallpaperFitMode
{
    Fill, // Crop edges to preserve aspect ratio and cover the screen
    Fit, // Letterbox or pillarbox to show entire content
    Stretch, // Stretch to fill window ignoring original aspect ratio
    Center, // Original unscaled size centered on screen
}
