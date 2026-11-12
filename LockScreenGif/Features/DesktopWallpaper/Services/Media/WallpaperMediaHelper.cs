using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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

    private static string? FindFfmpegPath()
    {
        var localFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Vendor", "FFMPEG", "ffmpeg.exe");
        if (File.Exists(localFfmpeg))
        {
            return localFfmpeg;
        }

        var parentFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Vendor", "FFMPEG", "ffmpeg.exe");
        if (File.Exists(parentFfmpeg))
        {
            return Path.GetFullPath(parentFfmpeg);
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
        return ext is ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv";
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
        var fastRemuxSuccess = await RunFfmpegAsync(ffmpeg, $"-y -i \"{inputFilePath}\" -c copy -movflags +faststart \"{tempOutputFile}\"");

        if (fastRemuxSuccess && File.Exists(tempOutputFile) && new FileInfo(tempOutputFile).Length > 1024)
        {
            File.Move(tempOutputFile, cachedMp4, true);
            Logger.Info($"Fast remux succeeded: {cachedMp4}");
            return cachedMp4;
        }

        // Fallback: Transcode video to H.264 and audio to AAC
        Logger.Info("Fast remux failed or codec incompatible; transcoding to H.264/AAC...");
        var transcodeSuccess = await RunFfmpegAsync(
            ffmpeg,
            $"-y -i \"{inputFilePath}\" -c:v libx264 -preset ultrafast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{tempOutputFile}\""
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
            $"-y -ss 00:00:01 -i \"{inputFilePath}\" -vframes 1 -q:v 2 -vf \"scale=480:-1\" \"{thumbPath}\""
        );

        return success && File.Exists(thumbPath) ? thumbPath : null;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpegExe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

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
