using System.Text.Json;

namespace LockscreenGif.Services;

public sealed record RecentFileEntry(string Path, string FileName, bool IsVideo, DateTimeOffset LastUsed);

public sealed class RecentFilesService
{
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "recent_files.json"
    );

    private readonly object _lock = new();

    public IReadOnlyList<RecentFileEntry> GetRecentFiles()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(StoragePath))
                {
                    return [];
                }

                var json = File.ReadAllText(StoragePath);
                var items = JsonSerializer.Deserialize<List<RecentFileEntry>>(json);
                if (items is null)
                {
                    return [];
                }

                // Filter out non-existent files
                return items.Where(i => File.Exists(i.Path)).Take(5).ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    public void AddRecentFile(string path, bool isVideo)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(StoragePath);
                if (dir is not null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var current = GetRecentFiles().Where(f => !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
                var fileName = Path.GetFileName(path);
                current.Insert(0, new RecentFileEntry(path, fileName, isVideo, DateTimeOffset.UtcNow));

                var trimmed = current.Take(5).ToList();
                var json = JsonSerializer.Serialize(trimmed);
                File.WriteAllText(StoragePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save recent file", ex);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(StoragePath))
                {
                    File.Delete(StoragePath);
                }
            }
            catch { }
        }
    }
}

