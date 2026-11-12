using LockscreenGif.Contracts.Services;
using LockscreenGif.Services.Analytics;
using LockscreenGif.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LockscreenGif.Views;

public sealed partial class ShellPage : Page
{
    private readonly INavigationService _navigationService;
    private bool _synchronizingSelection;
    private bool _analyticsReady;

    public Frame NavigationFrame => ContentFrame;

    public ShellPage()
    {
        InitializeComponent();
        _analytics = App.GetService<AnalyticsService>();
        _navigationService = App.GetService<INavigationService>();
        _navigationService.Frame = ContentFrame;

        Loaded += ShellPage_Loaded;
        ActualThemeChanged += ShellPage_ActualThemeChanged;

#if DEBUG
        var galleryItem = new NavigationViewItem
        {
            Content = "Glass Gallery",
            Tag = "GlassGallery",
            Icon = new FontIcon { Glyph = "\uE790" },
        };
        Navigation.MenuItems.Add(galleryItem);
#endif
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_synchronizingSelection || _navigationService is null)
        {
            return;
        }

#if DEBUG
        if (args.SelectedItem is NavigationViewItem item && (string?)item.Tag == "GlassGallery")
        {
            ContentFrame.Navigate(typeof(GlassGalleryPage));
            return;
        }
#endif

        var viewModel =
            ReferenceEquals(args.SelectedItem, SettingsItem) ? typeof(SettingsViewModel)
            : ReferenceEquals(args.SelectedItem, DiagnosticsItem) ? typeof(DiagnosticsViewModel)
            : ReferenceEquals(args.SelectedItem, WallpaperItem)
                ? typeof(LockscreenGif.Features.DesktopWallpaper.ViewModels.DesktopWallpaperViewModel)
            : typeof(MainViewModel);
        _navigationService.NavigateTo(viewModel.FullName!, clearNavigation: true);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs args)
    {
        if (args.SourcePageType == typeof(SettingsPage))
        {
            AnalyticsNotice.IsOpen = false;
        }
        _synchronizingSelection = true;
#if DEBUG
        if (args.SourcePageType == typeof(GlassGalleryPage))
        {
            Navigation.SelectedItem = Navigation
                .MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(i => (string?)i.Tag == "GlassGallery");
            _synchronizingSelection = false;
            return;
        }
#endif
        Navigation.SelectedItem =
            args.SourcePageType == typeof(SettingsPage) ? SettingsItem
            : args.SourcePageType == typeof(DiagnosticsPage) ? DiagnosticsItem
            : args.SourcePageType == typeof(LockscreenGif.Features.DesktopWallpaper.Views.DesktopWallpaperPage) ? WallpaperItem
            : LockscreenItem;
        _synchronizingSelection = false;
        if (_analyticsReady)
        {
            TrackCurrentPage();
        }
    }

    public void StartPageAnalytics()
    {
        _analyticsReady = true;
        TrackCurrentPage();
    }

    private void TrackCurrentPage() =>
        _analytics?.Track(
            AnalyticsEvent.PageViewed,
            new()
            {
                Page =
                    ContentFrame.Content is SettingsPage ? AnalyticsPage.Settings
                    : ContentFrame.Content is DiagnosticsPage ? AnalyticsPage.Diagnostics
                    : AnalyticsPage.Lockscreen,
            }
        );

    private void ShellPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        App.AppTitlebar = AppTitleBar;
        App.MainWindow.SetTitleBar(AppTitleBar);
        Helpers.TitleBarHelper.ApplySystemThemeToCaptionButtons();
    }

    private void ShellPage_ActualThemeChanged(Microsoft.UI.Xaml.FrameworkElement sender, object args)
    {
        Helpers.TitleBarHelper.ApplySystemThemeToCaptionButtons();
    }
}
