using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Library;

/// <summary>
/// Contract for managing wallpaper library entries, persistent settings, playlists, and custom properties.
/// </summary>
public interface IWallpaperLibraryService
{
    IReadOnlyList<WallpaperLibraryEntry> Items { get; }
    WallpaperPlaylist Playlist { get; }
    WallpaperPersistentSettings Settings { get; }

    Task InitializeAsync();
    Task<WallpaperLibraryEntry> AddAsync(string pathOrUrl, string? customTitle = null, List<string>? tags = null);
    Task<bool> RemoveAsync(string id);
    Task ToggleFavoriteAsync(string id);
    Task UpdateAsync(WallpaperLibraryEntry entry);
    Task<WallpaperDescriptor?> ReadDescriptorAsync(string pathOrFolder);
    Task SaveCustomPropertyAsync(string wallpaperId, string propertyId, string value);
    Task SetMonitorSelectionAsync(string deviceName, string wallpaperId);
    Task<string?> GetMonitorSelectionAsync(string deviceName);
    Task RotatePlaylistNextAsync();
    Task SaveSettingsAsync();

    event EventHandler? LibraryChanged;
    event EventHandler<WallpaperLibraryEntry>? ActiveWallpaperChanged;
}

