namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Snapshot of live CPU, memory, and graphics resource usage for active wallpaper playback.
/// </summary>
public readonly record struct WallpaperResourceUsage(double CpuPercent, long MemoryMegabytes, string GpuDescription);
