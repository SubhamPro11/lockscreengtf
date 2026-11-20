using System;
using LockscreenGif.Services.Analytics;
using LockscreenGif.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LockscreenGif.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.GetService<SettingsViewModel>();
    public string AppVersion
    {
        get
        {
            var raw = LockscreenGif.Helpers.BuildInfo.ShortVersion;
            var dashIdx = raw.IndexOf('-');
            if (dashIdx > 0)
            {
                raw = raw[..dashIdx];
            }
            return $"Version {raw}";
        }
    }
    private AnalyticsService? _analytics;
    private bool _updatingToggle;

    public SettingsPage()
    {
        InitializeComponent();
        _analytics = App.GetService<AnalyticsService>();
        RefreshPreference();
        InitializeWindowsImageFeatureSettings();
        UpdateThemeButtons(ViewModel.ElementTheme);
    }

    private async void ThemeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag && Enum.TryParse<ElementTheme>(tag, out var theme))
        {
            await ViewModel.SetThemeAsync(theme);
            UpdateThemeButtons(theme);
        }
    }

    private void UpdateThemeButtons(ElementTheme currentTheme)
    {
        ApplyThemeButtonState(ThemeLightButton, currentTheme == ElementTheme.Light);
        ApplyThemeButtonState(ThemeDarkButton, currentTheme == ElementTheme.Dark);
        ApplyThemeButtonState(ThemeDefaultButton, currentTheme == ElementTheme.Default);
    }

    private static void ApplyThemeButtonState(Button? button, bool isSelected)
    {
        if (button == null)
        {
            return;
        }

        button.Style = (Style)Application.Current.Resources[isSelected ? "MaterialFilledButtonStyle" : "MaterialTextButtonStyle"];
    }

    private void RefreshPreference()
    {
        _updatingToggle = true;
        AnalyticsToggle.IsOn = _analytics?.IsEnabled == true;
        AnalyticsToggle.IsEnabled = _analytics?.IsConfigured == true;
        _updatingToggle = false;
        if (_analytics?.IsConfigured != true)
        {
            AnalyticsStatus.Title = "Analytics is not configured in this build";
            AnalyticsStatus.IsOpen = true;
        }
    }

    private void AnalyticsToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_updatingToggle || _analytics is null)
        {
            return;
        }

        AnalyticsSettingsError.IsOpen = !_analytics.SetEnabled(AnalyticsToggle.IsOn);
        RefreshPreference();
    }

    private async void ViewLicenses_Click(object sender, RoutedEventArgs args)
    {
        if (XamlRoot is not null)
        {
            await Dialogs.LicensesDialog.ShowAsync(XamlRoot);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusInfoBar.IsOpen = false;
        try
        {
            await Task.Delay(800);
            var channel = ReleaseChannelComboBox.SelectedIndex == 1 ? "Beta" : "Stable";
            UpdateStatusInfoBar.Title = "You are on the latest version";
            UpdateStatusInfoBar.Message = $"LockscreenGif v{AppVersion} is currently up to date on the {channel} channel.";
            UpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
            UpdateStatusInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            UpdateStatusInfoBar.Title = "Update check failed";
            UpdateStatusInfoBar.Message = ex.Message;
            UpdateStatusInfoBar.Severity = InfoBarSeverity.Error;
            UpdateStatusInfoBar.IsOpen = true;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }
}
