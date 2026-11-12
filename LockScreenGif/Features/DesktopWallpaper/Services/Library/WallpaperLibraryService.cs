using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Media;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Library;

/// <summary>
/// Service managing the desktop wallpaper library, playlist rotation, and persistent settings.
/// </summary>
public sealed class WallpaperLibraryService : IWallpaperLibraryService, IDisposable
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif"
    );

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "WallpaperSettings.json");

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer _playlistTimer;
    private WallpaperPersistentSettings _settings = new();
    private bool _initialized;
    private bool _disposed;

    public IReadOnlyList<WallpaperLibraryEntry> Items => _settings.Library.AsReadOnly();
    public WallpaperPlaylist Playlist => _settings.Playlist;
    public WallpaperPersistentSettings Settings => _settings;

    public event EventHandler? LibraryChanged;
    public event EventHandler<WallpaperLibraryEntry>? ActiveWallpaperChanged;

    public WallpaperLibraryService()
    {
        _playlistTimer = new Timer(OnPlaylistTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _lock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            if (File.Exists(SettingsFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<WallpaperPersistentSettings>(json);
                    if (loaded != null)
                    {
                        _settings = loaded;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to load wallpaper settings from disk: {ex.Message}");
                    _settings = new WallpaperPersistentSettings();
                }
            }

            _initialized = true;
            UpdatePlaylistTimer();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<WallpaperLibraryEntry> AddAsync(string pathOrUrl, string? customTitle = null, List<string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        await InitializeAsync();

        var type = WallpaperItem.DetectType(pathOrUrl);
        var title = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle
            : (pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pathOrUrl : Path.GetFileNameWithoutExtension(pathOrUrl));

        string? thumbnail = null;
        if (File.Exists(pathOrUrl))
        {
            thumbnail = await WallpaperMediaHelper.GenerateThumbnailAsync(pathOrUrl);
        }

        var entry = new WallpaperLibraryEntry
        {
            Title = title,
            SourcePathOrUrl = pathOrUrl,
            Type = type,
            ThumbnailPath = thumbnail,
            Tags = tags ?? [],
        };

        // Check for descriptor in folder
        var descriptor = await ReadDescriptorAsync(pathOrUrl);
        if (descriptor != null)
        {
            if (!string.IsNullOrWhiteSpace(descriptor.Title))
            {
                entry.Title = descriptor.Title;
            }
            foreach (var prop in descriptor.Properties)
            {
                if (!string.IsNullOrWhiteSpace(prop.Id) && !string.IsNullOrWhiteSpace(prop.Value))
                {
                    entry.CustomProperties[prop.Id] = prop.Value;
                }
            }
        }

        await _lock.WaitAsync();
        try
        {
            _settings.Library.Insert(0, entry);
            await SaveSettingsInternalAsync();
        }
        finally
        {
            _lock.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public async Task<bool> RemoveAsync(string id)
    {
        await InitializeAsync();
        var removed = false;

        await _lock.WaitAsync();
        try
        {
            var idx = _settings.Library.FindIndex(x => x.Id == id);
            if (idx >= 0)
            {
                _settings.Library.RemoveAt(idx);
                _settings.Playlist.ItemIds.Remove(id);
                removed = true;
                await SaveSettingsInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }

        if (removed)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    public async Task ToggleFavoriteAsync(string id)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            var item = _settings.Library.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.IsFavorite = !item.IsFavorite;
                await SaveSettingsInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateAsync(WallpaperLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await InitializeAsync();

        await _lock.WaitAsync();
        try
        {
            var idx = _settings.Library.FindIndex(x => x.Id == entry.Id);
            if (idx >= 0)
            {
                _settings.Library[idx] = entry;
                await SaveSettingsInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<WallpaperDescriptor?> ReadDescriptorAsync(string pathOrFolder)
    {
        try
        {
            string? dir = null;
            if (Directory.Exists(pathOrFolder))
            {
                dir = pathOrFolder;
            }
            else if (File.Exists(pathOrFolder))
            {
                dir = Path.GetDirectoryName(pathOrFolder);
            }

            if (dir != null)
            {
                var descriptorPath = Path.Combine(dir, "wallpaper.json");
                if (File.Exists(descriptorPath))
                {
                    var json = File.ReadAllText(descriptorPath);
                    var descriptor = JsonSerializer.Deserialize<WallpaperDescriptor>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    return Task.FromResult(descriptor);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to read wallpaper descriptor: {ex.Message}");
        }

        return Task.FromResult<WallpaperDescriptor?>(null);
    }

    public async Task SaveCustomPropertyAsync(string wallpaperId, string propertyId, string value)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            var item = _settings.Library.FirstOrDefault(x => x.Id == wallpaperId);
            if (item != null)
            {
                item.CustomProperties[propertyId] = value;
                await SaveSettingsInternalAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetMonitorSelectionAsync(string deviceName, string wallpaperId)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            _settings.MonitorSelections[deviceName] = wallpaperId;
            _settings.LastActiveWallpaperId = wallpaperId;
            await SaveSettingsInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetMonitorSelectionAsync(string deviceName)
    {
        await InitializeAsync();
        await _lock.WaitAsync();
        try
        {
            _settings.MonitorSelections.TryGetValue(deviceName, out var id);
            return id;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RotatePlaylistNextAsync()
    {
        await InitializeAsync();
        WallpaperLibraryEntry? nextItem = null;

        await _lock.WaitAsync();
        try
        {
            var playlist = _settings.Playlist;
            if (!playlist.IsEnabled || playlist.ItemIds.Count == 0)
            {
                return;
            }

            playlist.CurrentIndex = (playlist.CurrentIndex + 1) % playlist.ItemIds.Count;
            var nextId = playlist.ItemIds[playlist.CurrentIndex];
            nextItem = _settings.Library.FirstOrDefault(x => x.Id == nextId);
            playlist.LastRotated = DateTimeOffset.UtcNow;
            await SaveSettingsInternalAsync();
        }
        finally
        {
            _lock.Release();
        }

        if (nextItem != null)
        {
            ActiveWallpaperChanged?.Invoke(this, nextItem);
        }
    }

    public async Task SaveSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await SaveSettingsInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveSettingsInternalAsync()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            var tempFile = SettingsFile + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, SettingsFile, true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save wallpaper settings to disk", ex);
        }
    }

    private void UpdatePlaylistTimer()
    {
        if (_settings.Playlist.IsEnabled && _settings.Playlist.IntervalMinutes > 0)
        {
            var interval = TimeSpan.FromMinutes(_settings.Playlist.IntervalMinutes);
            _playlistTimer.Change(interval, interval);
        }
        else
        {
            _playlistTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnPlaylistTimerTick(object? state)
    {
        _ = RotatePlaylistNextAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _playlistTimer.Dispose();
        _lock.Dispose();
    }
}
