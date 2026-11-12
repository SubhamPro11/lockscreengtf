using System;
using System.IO;

namespace LockscreenGif.Features.DesktopWallpaper.Models;

/// <summary>
/// Model describing a wallpaper instance, its source, and playback properties.
/// </summary>
public sealed class WallpaperItem
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
    public bool IsAdvancedExecutable { get; set; }

    public static WallpaperItem FromPathOrUrl(string pathOrUrl, string? title = null)
    {
        return new WallpaperItem
        {
            SourcePathOrUrl = pathOrUrl,
            Title = !string.IsNullOrWhiteSpace(title)
                ? title
                : (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : Path.GetFileName(pathOrUrl)),
            Type = DetectType(pathOrUrl)
        };
    }

    public static WallpaperType DetectType(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return WallpaperType.Video;
        }

        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return WallpaperType.Web;
        }

        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => WallpaperType.Web,
            ".png" or ".jpg" or ".jpeg" or ".bmp" => WallpaperType.Image,
            ".exe" => WallpaperType.Application,
            _ => WallpaperType.Video // .mp4, .webm, .gif, .mkv, .avi, .mov, .webp
        };
    }

    public static bool IsSupportedFormat(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return false;
        }

        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov" or ".gif" or ".webp"
                   or ".html" or ".htm" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".exe";
    }
}

