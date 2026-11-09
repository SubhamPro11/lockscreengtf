using System.Runtime.InteropServices;

namespace LockscreenGif.Services;

public sealed record EnvironmentValidationResult(
    bool IsSupportedOS,
    bool IsSupportedArchitecture,
    IReadOnlyList<string> MissingComponents,
    string? PrimaryWarning
)
{
    public bool CanOperate => IsSupportedOS && IsSupportedArchitecture && MissingComponents.Count == 0;
}

public static class EnvironmentValidator
{
    public static readonly Version MinimumRequiredBuild = new(10, 0, 26100);

    public static EnvironmentValidationResult Validate()
    {
        var currentVersion = Environment.OSVersion.Version;
        var isSupportedOS = currentVersion >= MinimumRequiredBuild;
        var isX64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        var missing = new List<string>();

        var ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Vendor", "FFMPEG", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
        {
            missing.Add("FFmpeg (Vendor/FFMPEG/ffmpeg.exe)");
        }

        var gifskiPath = Path.Combine(AppContext.BaseDirectory, "Vendor", "gifski", "gifski.dll");
        if (!File.Exists(gifskiPath))
        {
            missing.Add("Gifski (Vendor/gifski/gifski.dll)");
        }

        var helperPath = Path.Combine(AppContext.BaseDirectory, "Helpers", "Privileged", "LockscreenGif.Privileged.Helper.exe");
        if (!File.Exists(helperPath))
        {
            missing.Add("Privileged Helper (Helpers/Privileged/LockscreenGif.Privileged.Helper.exe)");
        }

        string? warning = null;
        if (!isSupportedOS)
        {
            warning = $"Windows 11 24H2 (Build 26100) or later is required. Current: {currentVersion}.";
        }
        else if (!isX64)
        {
            warning = $"x64 architecture is required. Current: {RuntimeInformation.ProcessArchitecture}.";
        }
        else if (missing.Count > 0)
        {
            warning = $"Missing required component(s): {string.Join(", ", missing)}. Check antivirus quarantine or reinstall.";
        }

        return new EnvironmentValidationResult(isSupportedOS, isX64, missing, warning);
    }
}
