namespace LockscreenGif.Services;

public enum CacheImpactLevel
{
    Safe,       // < 35 MB
    Moderate,   // 35 - 70 MB
    Heavy       // > 70 MB
}

public sealed record SizeEstimateResult(
    long EstimatedBytes,
    string FormattedSize,
    CacheImpactLevel ImpactLevel,
    string ImpactDescription
);

public static class SizeEstimator
{
    public static SizeEstimateResult EstimateGifSize(int frameCount, int width, int height, uint quality)
    {
        if (frameCount <= 0 || width <= 0 || height <= 0)
        {
            return new SizeEstimateResult(0, "0 KB", CacheImpactLevel.Safe, "No frames selected");
        }

        // Gifski color quantization estimation:
        // GIF uses 8-bit LZW compression per frame.
        // Higher quality retains more distinct colors and dithering patterns, reducing LZW run-length efficiency.
        // Base factor ranges from ~0.16 (q=70) to ~0.34 (q=100) bytes per pixel for typical video content.
        var qNorm = Math.Clamp((double)quality, 50, 100) / 100.0;
        var bytesPerPixel = 0.16 + (qNorm * 0.18);

        // First frame is a full keyframe; subsequent frames compress slightly better via frame diffing.
        var rawEstimated = width * height * frameCount * bytesPerPixel;

        // Add 5% overhead for GIF header, application extensions, and per-frame graphic control blocks
        var totalBytes = (long)(rawEstimated * 1.05);

        var formatted = FormatBytes(totalBytes);
        var totalMb = totalBytes / (1024.0 * 1024.0);

        CacheImpactLevel level;
        string description;

        if (totalMb > 70)
        {
            level = CacheImpactLevel.Heavy;
            description = "Heavy cache impact. Consider lowering resolution or frame rate to ensure smooth lock screen playback.";
        }
        else if (totalMb > 35)
        {
            level = CacheImpactLevel.Moderate;
            description = "Moderate cache size. Fully supported, but smaller loops render faster on lock screen wakeup.";
        }
        else
        {
            level = CacheImpactLevel.Safe;
            description = "Optimal size for seamless Windows 11 lock screen playback.";
        }

        return new SizeEstimateResult(totalBytes, formatted, level, description);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
