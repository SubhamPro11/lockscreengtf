using LockscreenGif.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LockscreenGif.Views;

public sealed partial class GlassGalleryPage : Page
{
    public GlassGalleryPage()
    {
        InitializeComponent();
        FallbackToggle.IsOn = GlassFallbackManager.ForceFallbackForTesting;
        UpdateStatus(GlassFallbackManager.IsFallbackActive);
        GlassFallbackManager.FallbackStateChanged += GlassFallbackManager_FallbackStateChanged;
        Unloaded += GlassGalleryPage_Unloaded;
    }

    private void GlassGalleryPage_Unloaded(object sender, RoutedEventArgs e)
    {
        GlassFallbackManager.FallbackStateChanged -= GlassFallbackManager_FallbackStateChanged;
    }

    private void GlassFallbackManager_FallbackStateChanged(object? sender, bool isFallback)
    {
        UpdateStatus(isFallback);
    }

    private void UpdateStatus(bool isFallback)
    {
        if (FallbackStatusText == null)
        {
            return;
        }

        if (isFallback)
        {
            FallbackStatusText.Text = "Solid Fallback Active (Opaque Surfaces)";
            FallbackStatusText.Foreground = (Brush)Application.Current.Resources["GlassStatusAlertBrush"];
        }
        else
        {
            FallbackStatusText.Text = "Translucent Glass (Backdrop Acrylic Active)";
            FallbackStatusText.Foreground = (Brush)Application.Current.Resources["GlassStatusSuccessBrush"];
        }
    }

    private void FallbackToggle_Toggled(object sender, RoutedEventArgs e)
    {
        GlassFallbackManager.ForceFallbackForTesting = FallbackToggle.IsOn;
    }

    private void ThemeDark_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme.Dark;
        }
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme.Light;
        }
    }

    private void ThemeDefault_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow.Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme.Default;
        }
    }
}
