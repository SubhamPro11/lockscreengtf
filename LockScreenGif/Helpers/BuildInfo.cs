using System.IO;
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

    public static string ShortCommit
    {
        get
        {
            var raw = Version;
            var plusIdx = raw.IndexOf('+');
            if (plusIdx >= 0 && plusIdx + 1 < raw.Length)
            {
                var commit = raw[(plusIdx + 1)..];
                return commit.Length > 7 ? commit[..7] : commit;
            }
            return "local";
        }
    }

    public static string GitBranch
    {
        get
        {
            try
            {
                var meta = typeof(BuildInfo)
                    .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "GitBranch")
                    ?.Value;
                if (!string.IsNullOrWhiteSpace(meta))
                {
                    return meta;
                }

                var baseDir = AppContext.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    var headFile = Path.Combine(dir.FullName, ".git", "HEAD");
                    if (File.Exists(headFile))
                    {
                        var head = File.ReadAllText(headFile).Trim();
                        if (head.StartsWith("ref: refs/heads/"))
                        {
                            return head["ref: refs/heads/".Length..];
                        }
                        return head.Length > 7 ? head[..7] : head;
                    }
                    dir = dir.Parent;
                }
            }
            catch { }
            return "chore/stale-ui-and-slim";
        }
    }

    public static string BuildConfiguration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    public static string BuildTime
    {
        get
        {
            try
            {
                var loc = typeof(BuildInfo).Assembly.Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                {
                    return File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
            catch { }
            return "Local";
        }
    }

    public static string ExecutablePath => Environment.ProcessPath ?? AppContext.BaseDirectory;

    public static string BuildStamp =>
        $"LockscreenGif {ShortVersion}\n"
        + $"Commit: {ShortCommit}\n"
        + $"Branch: {GitBranch}\n"
        + $"Config: {BuildConfiguration}\n"
        + $"Built: {BuildTime}\n"
        + $"Path: {ExecutablePath}";

    public static string ShortDisplayVersion => $"Version {ShortVersion}";

    public static string DisplayVersion => $"Lockscreen Gif {Version}";

    public static bool IsGitHubActionsBuild { get; } =
        typeof(BuildInfo)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(attribute => attribute.Key == "GitHubActionsBuild" && attribute.Value == "true");
}
