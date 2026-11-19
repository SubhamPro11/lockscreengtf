using System.IO;
using LockscreenGif.Contracts.Services;
using LockscreenGif.Helpers;
using Microsoft.UI.Xaml;

namespace LockscreenGif.Services;

public class ThemeSelectorService : IThemeSelectorService
{
    private static readonly string ThemeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "theme.txt"
    );

    public ElementTheme Theme { get; set; } = ElementTheme.Default;

    public ThemeSelectorService() { }

    public async Task InitializeAsync()
    {
        Theme = await LoadThemeFromSettingsAsync();
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        Theme = theme;

        await SetRequestedThemeAsync();
        await SaveThemeInSettingsAsync(theme);
    }

    public async Task SetRequestedThemeAsync()
    {
        if (App.MainWindow.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = Theme;

            TitleBarHelper.UpdateTitleBar(Theme);
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        }

        await Task.CompletedTask;
    }

    private Task<ElementTheme> LoadThemeFromSettingsAsync()
    {
        try
        {
            if (File.Exists(ThemeFilePath))
            {
                var text = File.ReadAllText(ThemeFilePath);
                if (Enum.TryParse<ElementTheme>(text.Trim(), out var theme))
                {
                    return Task.FromResult(theme);
                }
            }
        }
        catch { }

        return Task.FromResult(ElementTheme.Default);
    }

    private Task SaveThemeInSettingsAsync(ElementTheme theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFilePath)!);
            File.WriteAllText(ThemeFilePath, theme.ToString());
        }
        catch { }

        return Task.CompletedTask;
    }
}
