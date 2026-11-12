using CommunityToolkit.Mvvm.ComponentModel;
using LockscreenGif.Features.DesktopWallpaper.Services.System;
using LockscreenGif.Services;

namespace LockscreenGif.ViewModels;

public sealed class SettingsViewModel(LockscreenPreferences preferences) : ObservableObject
{
    public LockscreenPreferences Preferences { get; } = preferences;

    public bool IsRunAtStartup
    {
        get => WallpaperStartupService.IsStartupEnabled();
        set
        {
            WallpaperStartupService.SetStartupEnabled(value);
            OnPropertyChanged(nameof(IsRunAtStartup));
        }
    }
}
