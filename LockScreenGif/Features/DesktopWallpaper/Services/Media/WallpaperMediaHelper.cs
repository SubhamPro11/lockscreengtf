using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Media;

/// <summary>
/// Helper for media format validation, caching, remuxing unsupported containers (MKV/AVI/MOV) via FFmpeg,
/// and thumbnail generation for the wallpaper library.
/// </summary>
public static class WallpaperMediaHelper
{
    public static string WallpaperCacheDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LockscreenGif", "wallpaper-cache");

    private static string CacheDirectory => WallpaperCacheDirectory;

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
    /// Copies a local source file into %LocalAppData%\LockscreenGif\wallpaper-cache using read-only
    /// shared access so the user's original file is never locked during rendering or FFmpeg processing.
    /// </summary>
    public static async Task<string> CopySourceToWallpaperCacheAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (
            string.IsNullOrWhiteSpace(sourcePath)
            || sourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(sourcePath)
        )
        {
            return sourcePath;
        }

        var fullSource = Path.GetFullPath(sourcePath);
        var fullCacheDir = Path.GetFullPath(CacheDirectory);
        if (fullSource.StartsWith(fullCacheDir, StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        Directory.CreateDirectory(CacheDirectory);

        var fileHash = ComputeFileHash(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        var cachedPath = Path.Combine(CacheDirectory, $"src_{fileHash}{ext}");

        if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
        {
            return cachedPath;
        }

        var tempPath = $"{cachedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 81920, useAsync: true)
            )
            await using (
                var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true)
            )
            {
                await src.CopyToAsync(dst, cancellationToken);
            }

            File.Move(tempPath, cachedPath, overwrite: true);
            return cachedPath;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Remuxes or converts an unsupported container (e.g. MKV/AVI/MOV) into an MP4 loop file
    /// in the local wallpaper cache directory.
    /// </summary>
    public static async Task<string> PrepareMediaForPlaybackAsync(string inputFilePath, CancellationToken cancellationToken = default)
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

        var tempOutputFile = $"{cachedMp4}.{Guid.NewGuid():N}.tmp.mp4";
        try
        {
            // Try fast remux first (-c copy)
            var fastRemuxSuccess = await RunFfmpegAsync(
                ffmpeg,
                (string[])["-y", "-i", inputFilePath, "-c", "copy", "-movflags", "+faststart", tempOutputFile],
                cancellationToken
            );

            if (fastRemuxSuccess && File.Exists(tempOutputFile) && new FileInfo(tempOutputFile).Length > 1024)
            {
                File.Move(tempOutputFile, cachedMp4, true);
                Logger.Info($"Fast remux succeeded: {cachedMp4}");
                return cachedMp4;
            }

            // Fallback: Transcode video to H.264 (downscaling to display native resolution if higher) and audio to AAC
            Logger.Info("Fast remux failed or codec incompatible; transcoding to H.264/AAC with smart display scaling...");

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
                    ],
                cancellationToken
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
        finally
        {
            try
            {
                if (File.Exists(tempOutputFile))
                {
                    File.Delete(tempOutputFile);
                }
            }
            catch { }
        }
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
            (string[])["-y", "-ss", "00:00:01", "-i", inputFilePath, "-vframes", "1", "-q:v", "2", "-vf", "scale=480:-1", thumbPath]
        );

        return success && File.Exists(thumbPath) ? thumbPath : null;
    }

    private static async Task<bool> RunFfmpegAsync(string ffmpegExe, string[] arguments, CancellationToken cancellationToken = default)
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

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
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
