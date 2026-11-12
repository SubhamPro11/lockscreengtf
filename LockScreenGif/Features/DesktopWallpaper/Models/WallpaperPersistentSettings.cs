using System.Collections.Generic;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Root persistent data model stored to disk in WallpaperSettings.json.
/// </summary>
public sealed class WallpaperPersistentSettings
{
    public List<WallpaperLibraryEntry> Library { get; set; } = [];
    public Dictionary<string, string> MonitorSelections { get; set; } = [];
    public WallpaperPlaylist Playlist { get; set; } = new();
    public WallpaperLayoutMode SavedLayoutMode { get; set; } = WallpaperLayoutMode.SpanAllMonitors;
    public WallpaperPerformanceRules PerformanceRules { get; set; } = new();
    public bool AllowAdvancedExecutables { get; set; }
    public string? LastActiveWallpaperId { get; set; }
}

