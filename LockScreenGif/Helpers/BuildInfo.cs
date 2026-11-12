using System.Reflection;

namespace LockscreenGif.Helpers;

public static class BuildInfo
{
    public static string Version { get; } =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static string ShortVersion
    {
        get
        {
            var raw = Version;
            var plusIdx = raw.IndexOf('+');
            if (plusIdx > 0)
            {
                raw = raw[..plusIdx];
            }
            return raw;
        }
    }

    public static string ShortDisplayVersion => $"Version {ShortVersion}";

    public static string DisplayVersion => $"Lockscreen Gif {Version}";

    public static bool IsGitHubActionsBuild { get; } =
        typeof(BuildInfo)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "GitHubActionsBuild" && attribute.Value == "true");
}
