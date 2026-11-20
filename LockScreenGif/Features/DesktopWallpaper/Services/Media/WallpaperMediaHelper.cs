using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Media;

/// <summary>
/// Helper for media format validation, remuxing unsupported containers (MKV/AVI/MOV) via FFmpeg,
/// and thumbnail generation for the wallpaper library.
/// </summary>
public static class WallpaperMediaHelper
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "WallpaperCache"
    );

    internal static Func<string>? FfmpegPathResolver { get; set; }

    private static string? FindFfmpegPath()
    {
        if (FfmpegPathResolver != null)
        {
            var resolved = FfmpegPathResolver();
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Vendor", "FFMPEG", "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vendor", "FFMPEG", "ffmpeg.exe"),
            Path.Combine(Path.GetDirectoryName(typeof(WallpaperMediaHelper).Assembly.Location) ?? "", "Vendor", "FFMPEG", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "Vendor", "FFMPEG", "ffmpeg.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a file needs remuxing or conversion before playback in the Chromium/WebView2 engine.
    /// </summary>
    public static bool NeedsRemuxOrConversion(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".gif";
    }

    /// <summary>
    /// Remuxes or converts an unsupported container (e.g. MKV/AVI/MOV) into an MP4 loop file
    /// in the local wallpaper cache directory.
    /// </summary>
    public static async Task<string> PrepareMediaForPlaybackAsync(string inputFilePath)
    {
        if (!NeedsRemuxOrConversion(inputFilePath))
        {
            return inputFilePath;
        }

        var ffmpeg = FindFfmpegPath();
        if (ffmpeg == null)
        {
            Logger.Warn("FFmpeg executable not found; attempting direct playback of unsupported container.");
            return inputFilePath;
        }

        Directory.CreateDirectory(CacheDirectory);

        var fileHash = ComputeFileHash(inputFilePath);
        var cachedMp4 = Path.Combine(CacheDirectory, $"remux_{fileHash}.mp4");

        if (File.Exists(cachedMp4) && new FileInfo(cachedMp4).Length > 0)
        {
            return cachedMp4;
        }

        Logger.Info($"Remuxing/transcoding {inputFilePath} to {cachedMp4} using FFmpeg...");

        var tempOutputFile = cachedMp4 + ".tmp";
        if (File.Exists(tempOutputFile))
        {
            File.Delete(tempOutputFile);
        }

        // Try fast remux first (-c copy)
        var fastRemuxSuccess = await RunFfmpegAsync(
            ffmpeg,
            (string[])["-y", "-i", inputFilePath, "-c", "copy", "-movflags", "+faststart", tempOutputFile]
        );

        if (fastRemuxSuccess && File.Exists(tempOutputFile) && new FileInfo(tempOutputFile).Length > 1024)
        {
            File.Move(tempOutputFile, cachedMp4, true);
            Logger.Info($"Fast remux succeeded: {cachedMp4}");
            return cachedMp4;
        }

        // Fallback: Transcode video to H.264 (downscaling to display native resolution if higher) and audio to AAC
        Logger.Info("Fast remux failed or codec incompatible; transcoding to H.264/AAC with smart display scaling...");

        // Smart native display scaling: cap at monitor resolution so 4K videos on 1080p monitors don't waste 75% GPU/CPU
        var screenW = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_CXSCREEN);
        var screenH = DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_CYSCREEN);
        if (screenW <= 0)
        {
            screenW = 1920;
        }
        if (screenH <= 0)
        {
            screenH = 1080;
        }

        var scaleFilter =
            $"scale='min({screenW},iw)':'min({screenH},ih)':force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2";

        var transcodeSuccess = await RunFfmpegAsync(
            ffmpeg,
            (string[])
            [
                "-y",
                "-i",
                inputFilePath,
                "-c:v",
                "libx264",
                "-preset",
                "ultrafast",
                "-crf",
                "22",
                "-vf",
                scaleFilter,
                "-pix_fmt",
                "yuv420p",
                "-c:a",
                "aac",
                "-b:a",
                "192k",
                "-movflags",
                "+faststart",
                tempOutputFile,
            ]
        );

        if (transcodeSuccess && File.Exists(tempOutputFile))
        {
            File.Move(tempOutputFile, cachedMp4, true);
            Logger.Info($"Transcode succeeded: {cachedMp4}");
            return cachedMp4;
        }

        Logger.Warn("Transcode failed; falling back to original file.");
        return inputFilePath;
    }

    /// <summary>
    /// Generates a thumbnail image from a video or image wallpaper.
    /// </summary>
    public static async Task<string?> GenerateThumbnailAsync(string inputFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath) || !File.Exists(inputFilePath))
        {
            return null;
        }

        var ext = Path.GetExtension(inputFilePath).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
        {
            return inputFilePath;
        }

        var ffmpeg = FindFfmpegPath();
        if (ffmpeg == null)
        {
            return null;
        }

        Directory.CreateDirectory(CacheDirectory);
        var fileHash = ComputeFileHash(inputFilePath);
        var thumbPath = Path.Combine(CacheDirectory, $"thumb_{fileHash}.jpg");

        if (File.Exists(thumbPath))
        {
            return thumbPath;
        }

        var success = await RunFfmpegAsync(
            ffmpeg,
            (string[])
            [
                "-y",
                "-ss",
                "00:00:01",
                "-i",
                inputFilePath,
                "-vframes",
                "1",
                "-q:v",
                "2",
                "-vf",
                "scale=480:-1",
                thumbPath,
            ]
        );

        return success && File.Exists(thumbPath) ? thumbPath : null;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpegExe, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            foreach (var arg in arguments)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"FFmpeg invocation error: {ex.Message}");
            return false;
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var seed = $"{fileInfo.FullName}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return Convert.ToHexString(hash)[..16];
        }
        catch
        {
            return Guid.NewGuid().ToString("N")[..16];
        }
    }
}
