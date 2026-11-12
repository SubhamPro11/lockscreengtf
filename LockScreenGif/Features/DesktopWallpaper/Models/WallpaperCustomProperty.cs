using System.Collections.Generic;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Describes a configurable setting exposed by a web wallpaper via its wallpaper.json descriptor.
/// </summary>
public sealed class WallpaperCustomProperty
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text"; // "slider", "boolean", "color", "select", "text"
    public string Value { get; set; } = string.Empty;
    public double Min { get; set; }
    public double Max { get; set; } = 100.0;
    public double Step { get; set; } = 1.0;
    public List<string> Options { get; set; } = [];
}

/// <summary>
/// Root schema for wallpaper.json descriptor files.
/// </summary>
public sealed class WallpaperDescriptor
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Type { get; set; } = "web";
    public List<WallpaperCustomProperty> Properties { get; set; } = [];
}

