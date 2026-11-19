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
    public bool IsAdaptiveFps => TargetFps == -1;
    public int EffectiveFps =>
        TargetFps switch
        {
            -1 => (PauseOnBattery || PauseInBatterySaver) ? 24 : 60,
            <= 0 => 60,
            _ => TargetFps,
        };
    public bool LowerDecodeQuality { get; set; }

    // Extended rules for smart power & process management
    public bool PauseWhenIdle { get; set; }
    public int IdleMinutes { get; set; } = 5;
    public System.Collections.Generic.List<string> PauseProcessNames { get; } =
    ["obs64", "obs32", "premiere", "photoshop", "afterfx", "blender"];
    public System.Collections.Generic.List<string> NeverPauseProcessNames { get; } = [];
}
