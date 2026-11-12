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

    public static string NormalizePathOrUrl(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return string.Empty;
        }

        pathOrUrl = pathOrUrl.Trim();

        // Guard against accidentally prepended https:// to Windows drive paths like https://C:\...
        if (
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        )
        {
            var schemeLen = pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8 : 7;
            var afterScheme = pathOrUrl[schemeLen..];
            if (afterScheme.Length >= 2 && char.IsLetter(afterScheme[0]) && afterScheme[1] == ':')
            {
                return afterScheme;
            }
        }

        return pathOrUrl;
    }

    public static WallpaperItem FromPathOrUrl(string pathOrUrl, string? title = null)
    {
        var normalized = NormalizePathOrUrl(pathOrUrl);
        return new WallpaperItem
        {
            SourcePathOrUrl = normalized,
            Title = !string.IsNullOrWhiteSpace(title)
                ? title
                : (normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? normalized : Path.GetFileName(normalized)),
            Type = DetectType(normalized),
        };
    }

    public static WallpaperType DetectType(string pathOrUrl)
    {
        pathOrUrl = NormalizePathOrUrl(pathOrUrl);
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return WallpaperType.Video;
        }

        if (
            pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return WallpaperType.Web;
        }

        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => WallpaperType.Web,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => WallpaperType.Image,
            ".exe" => WallpaperType.Application,
            _ => WallpaperType.Video, // .mp4, .webm, .mkv, .avi, .mov
        };
    }

    public static bool IsSupportedFormat(string pathOrUrl)
    {
        pathOrUrl = NormalizePathOrUrl(pathOrUrl);
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return false;
        }

        if (
            pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext
            is ".mp4"
                or ".webm"
                or ".mkv"
                or ".avi"
                or ".mov"
                or ".gif"
                or ".webp"
                or ".html"
                or ".htm"
                or ".png"
                or ".jpg"
                or ".jpeg"
                or ".bmp"
                or ".exe";
    }
}
