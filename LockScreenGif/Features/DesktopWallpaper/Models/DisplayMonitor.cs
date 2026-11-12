using System;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Information about a connected physical display monitor.
/// </summary>
public sealed record DisplayMonitor(
    nint Handle,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary,
    uint DpiX,
    uint DpiY
)
{
    public double ScaleFactor => DpiX > 0 ? (double)DpiX / 96.0 : 1.0;
    public int ScalePercentage => (int)Math.Round(ScaleFactor * 100);

    public string DisplayName =>
        $"{DeviceName} ({Width}×{Height}){(IsPrimary ? " · Primary" : "")} · {ScalePercentage}% scaling";

    public override string ToString() => DisplayName;
}

