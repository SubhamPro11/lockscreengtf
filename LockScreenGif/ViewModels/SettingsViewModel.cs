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
            WallpaperStartupService.SetStartupEnabled(value);
            OnPropertyChanged(nameof(IsRunAtStartup));
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
