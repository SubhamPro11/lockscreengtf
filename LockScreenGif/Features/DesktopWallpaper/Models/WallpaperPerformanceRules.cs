namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// User-configurable rules for auto-pausing wallpaper and optimizing system resource usage.
/// </summary>
public sealed class WallpaperPerformanceRules
{
    public bool PauseWhenFullscreenAppActive { get; set; } = true;
    public bool PauseWhenDesktopCovered { get; set; } = true;
    public bool PauseOnBattery { get; set; }
    public bool PauseInBatterySaver { get; set; } = true;
    public bool PauseWhenLockedOrDisplayOff { get; set; } = true;
    public int TargetFps { get; set; } = 60;
    public bool LowerDecodeQuality { get; set; }
}

