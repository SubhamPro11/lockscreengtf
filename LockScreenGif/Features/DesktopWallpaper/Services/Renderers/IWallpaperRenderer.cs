using System;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

/// <summary>
/// Contract for rendering wallpaper content inside a host window.
/// </summary>
public interface IWallpaperRenderer : IAsyncDisposable
{
    bool IsPlaying { get; }
    nint ContentHwnd { get; }

    Task InitializeAsync(nint hostHwnd, int left, int top, int width, int height);
    Task LoadAsync(WallpaperItem item);
    Task SetBoundsAsync(int left, int top, int width, int height);
    Task PauseAsync();
    Task ResumeAsync();
    Task SetVolumeAsync(double volume);
    Task SetMutedAsync(bool muted);
    Task SetPlaybackRateAsync(double rate);
    Task SetFitModeAsync(WallpaperFitMode fitMode);
    Task CloseAsync();
}

