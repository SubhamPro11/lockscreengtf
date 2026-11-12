using System;
using System.Collections.Generic;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// A persistent entry in the desktop wallpaper library.
/// </summary>
public sealed class WallpaperLibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string SourcePathOrUrl { get; set; } = string.Empty;
    public WallpaperType Type { get; set; } = WallpaperType.Video;
    public WallpaperFitMode FitMode { get; set; } = WallpaperFitMode.Fill;
    public double Volume { get; set; } = 0.5;
    public bool IsMuted { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool Loop { get; set; } = true;
    public bool ForwardInput { get; set; }
    public string? ThumbnailPath { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsFavorite { get; set; }
    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
    public string TypeGlyph =>
        Type switch
        {
            WallpaperType.Video => "\uE714",
            WallpaperType.Web => "\uE774",
            WallpaperType.Image => "\uEB9F",
            _ => "\uE756",
        };

    public WallpaperItem ToWallpaperItem() =>
        new()
        {
            Id = Id,
            Title = Title,
            SourcePathOrUrl = SourcePathOrUrl,
            Type = Type,
            FitMode = FitMode,
            Volume = Volume,
            IsMuted = IsMuted,
            PlaybackSpeed = PlaybackSpeed,
            Loop = Loop,
            ForwardInput = ForwardInput,
            ThumbnailPath = ThumbnailPath,
            IsAdvancedExecutable = Type == WallpaperType.Application,
        };
}
