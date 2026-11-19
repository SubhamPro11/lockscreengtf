using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI;

namespace LockscreenGif.Helpers;

public static class AmbientColorExtractor
{
    public static readonly Color DefaultPrimary = Color.FromArgb(0xFF, 0xFF, 0xA7, 0x9B); // Material 3 Coral Primary
    public static readonly Color DefaultSecondary = Color.FromArgb(0xFF, 0x5D, 0x3F, 0x3B); // Material 3 Secondary Container

    private static Color _currentPrimary = DefaultPrimary;
    private static Color _currentSecondary = DefaultSecondary;

    public static Color CurrentPrimary => _currentPrimary;
    public static Color CurrentSecondary => _currentSecondary;

    public static event EventHandler<(Color Primary, Color Secondary)>? ColorsChanged;

    public static void ResetToDefaults()
    {
        _currentPrimary = DefaultPrimary;
        _currentSecondary = DefaultSecondary;
        ColorsChanged?.Invoke(null, (_currentPrimary, _currentSecondary));
    }

    public static void UpdateFromSelection(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ResetToDefaults();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.PicturesView, 64, ThumbnailOptions.UseCurrentScale);

                if (thumbnail != null)
                {
                    var decoder = await BitmapDecoder.CreateAsync(thumbnail);
                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Rgba8,
                        BitmapAlphaMode.Premultiplied,
                        new BitmapTransform { ScaledWidth = 16, ScaledHeight = 16 },
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage
                    );

                    var bytes = pixelData.DetachPixelData();
                    var (p, s) = ComputeDominantColors(bytes);
                    _currentPrimary = p;
                    _currentSecondary = s;
                    ColorsChanged?.Invoke(null, (_currentPrimary, _currentSecondary));
                    return;
                }
            }
            catch
            {
                // Fallback to warm defaults on unsupported decoders or fast preview
            }

            ResetToDefaults();
        });
    }

    private static (Color Primary, Color Secondary) ComputeDominantColors(byte[] rgba)
    {
        long rSum1 = 0,
            gSum1 = 0,
            bSum1 = 0,
            count1 = 0;
        long rSum2 = 0,
            gSum2 = 0,
            bSum2 = 0,
            count2 = 0;

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i];
            byte g = rgba[i + 1];
            byte b = rgba[i + 2];
            byte a = rgba[i + 3];

            if (a < 64)
            {
                continue;
            }

            // Simple split by warm/cool hue to get 2 distinct dominant colors
            if (r >= b)
            {
                rSum1 += r;
                gSum1 += g;
                bSum1 += b;
                count1++;
            }
            else
            {
                rSum2 += r;
                gSum2 += g;
                bSum2 += b;
                count2++;
            }
        }

        Color c1 = count1 > 0 ? ClampLuminance((byte)(rSum1 / count1), (byte)(gSum1 / count1), (byte)(bSum1 / count1)) : DefaultPrimary;

        Color c2 = count2 > 0 ? ClampLuminance((byte)(rSum2 / count2), (byte)(gSum2 / count2), (byte)(bSum2 / count2)) : DefaultSecondary;

        return (c1, c2);
    }

    private static Color ClampLuminance(byte r, byte g, byte b)
    {
        // Keep colors warm and rich, but prevent ambient background from overpowering the dark projection booth
        double maxComp = Math.Max(r, Math.Max(g, b));
        if (maxComp > 220)
        {
            double scale = 220.0 / maxComp;
            r = (byte)(r * scale);
            g = (byte)(g * scale);
            b = (byte)(b * scale);
        }

        return Color.FromArgb(0xFF, r, g, b);
    }
}
