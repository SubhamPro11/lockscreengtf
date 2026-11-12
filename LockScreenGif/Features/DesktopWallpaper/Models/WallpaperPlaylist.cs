using System;
using System.Collections.Generic;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

public enum PlaylistRotationMode
{
    Timer,
    OnLogin,
    TimeOfDay
}

/// <summary>
/// Model for automatic playlist rotation of wallpapers.
/// </summary>
public sealed class WallpaperPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Main Playlist";
    public List<string> ItemIds { get; set; } = [];
    public PlaylistRotationMode Mode { get; set; } = PlaylistRotationMode.Timer;
    public int IntervalMinutes { get; set; } = 30;
    public bool IsEnabled { get; set; }
    public int CurrentIndex { get; set; }
    public DateTimeOffset LastRotated { get; set; } = DateTimeOffset.MinValue;
}

