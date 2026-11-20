using CommunityToolkit.Mvvm.ComponentModel;
using LockscreenGif.Contracts.Services;
using LockscreenGif.Features.DesktopWallpaper.Services.System;
using LockscreenGif.Services;
using Microsoft.UI.Xaml;

namespace LockscreenGif.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IThemeSelectorService _themeSelectorService;

    public LockscreenPreferences Preferences { get; }

    public ElementTheme ElementTheme => _themeSelectorService.Theme;

    public bool IsRunAtStartup
    {
        get => WallpaperStartupService.IsStartupEnabled();
        set
        {
            WallpaperStartupService.SetStartupEnabled(value, StartupDelaySeconds);
            OnPropertyChanged(nameof(IsRunAtStartup));
        }
    }

    public int StartupDelaySeconds
    {
        get => WallpaperStartupService.GetStartupDelaySeconds();
        set
        {
            if (WallpaperStartupService.IsStartupEnabled())
            {
                WallpaperStartupService.SetStartupEnabled(true, value);
            }
            OnPropertyChanged(nameof(StartupDelaySeconds));
            OnPropertyChanged(nameof(StartupDelayIndex));
        }
    }

    public int StartupDelayIndex
    {
        get => StartupDelaySeconds switch
        {
            5 => 1,
            10 => 2,
            30 => 3,
            _ => 0,
        };
        set
        {
            var seconds = value switch
            {
                1 => 5,
                2 => 10,
                3 => 30,
                _ => 0,
            };
            StartupDelaySeconds = seconds;
        }
    }

    public SettingsViewModel(LockscreenPreferences preferences, IThemeSelectorService themeSelectorService)
    {
        Preferences = preferences;
        _themeSelectorService = themeSelectorService;
    }

    public SettingsViewModel(LockscreenPreferences preferences)
        : this(preferences, App.GetService<IThemeSelectorService>()) { }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        await _themeSelectorService.SetThemeAsync(theme);
        OnPropertyChanged(nameof(ElementTheme));
    }
}
