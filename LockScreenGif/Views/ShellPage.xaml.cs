using System;
using System.Linq;
using LockscreenGif.Contracts.Services;
using LockscreenGif.Helpers;
using LockscreenGif.Services.Analytics;
using LockscreenGif.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace LockscreenGif.Views;

public sealed partial class ShellPage : Page
{
    private readonly INavigationService _navigationService;
    private bool _synchronizingSelection;
    private bool _analyticsReady;
    private Storyboard? _driftStoryboard;
    private bool _isAmbientLoaded;

    public Frame NavigationFrame => ContentFrame;

    public ShellPage()
    {
        InitializeComponent();
        _analytics = App.GetService<AnalyticsService>();
        _navigationService = App.GetService<INavigationService>();
        _navigationService.Frame = ContentFrame;

        Loaded += ShellPage_Loaded;
        Unloaded += ShellPage_Unloaded;
        ActualThemeChanged += ShellPage_ActualThemeChanged;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_synchronizingSelection || _navigationService is null)
        {
            return;
        }

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

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.AppTitlebar = AppTitleBar;
        App.MainWindow.SetTitleBar(AppTitleBar);
        UpdateCaptionButtonSpacer();
        if (App.MainWindow.AppWindow is { } appWindow)
        {
            appWindow.Changed += (_, args) =>
            {
                if (args.DidSizeChange)
                {
                    DispatcherQueue.TryEnqueue(UpdateCaptionButtonSpacer);
                }
            };
        }
        TitleBarHelper.ApplySystemThemeToCaptionButtons();

        if (!_isAmbientLoaded)
        {
            _isAmbientLoaded = true;
            SetupDriftAnimations();
            ApplyFallbackState(GlassFallbackManager.IsFallbackActive);
            UpdateAmbientColors(AmbientColorExtractor.CurrentPrimary, AmbientColorExtractor.CurrentSecondary);

            GlassFallbackManager.FallbackStateChanged += GlassFallbackManager_FallbackStateChanged;
            AmbientColorExtractor.ColorsChanged += AmbientColorExtractor_ColorsChanged;

            if (App.MainWindow != null)
            {
                App.MainWindow.Activated += MainWindow_Activated;
            }
        }
    }

    private void ShellPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isAmbientLoaded = false;
        GlassFallbackManager.FallbackStateChanged -= GlassFallbackManager_FallbackStateChanged;
        AmbientColorExtractor.ColorsChanged -= AmbientColorExtractor_ColorsChanged;

        if (App.MainWindow != null)
        {
            App.MainWindow.Activated -= MainWindow_Activated;
        }

        _driftStoryboard?.Stop();
        _driftStoryboard = null;
    }

    private void SetupDriftAnimations()
    {
        _driftStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        // Blob 1: ~40s drift cycle
        var anim1X = new DoubleAnimation
        {
            From = 0,
            To = 40,
            Duration = new Duration(TimeSpan.FromSeconds(20)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim1X, Blob1Transform);
        Storyboard.SetTargetProperty(anim1X, "X");
        _driftStoryboard.Children.Add(anim1X);

        var anim1Y = new DoubleAnimation
        {
            From = 0,
            To = -30,
            Duration = new Duration(TimeSpan.FromSeconds(18)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim1Y, Blob1Transform);
        Storyboard.SetTargetProperty(anim1Y, "Y");
        _driftStoryboard.Children.Add(anim1Y);

        // Blob 2: ~48s drift cycle
        var anim2X = new DoubleAnimation
        {
            From = 0,
            To = -45,
            Duration = new Duration(TimeSpan.FromSeconds(24)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim2X, Blob2Transform);
        Storyboard.SetTargetProperty(anim2X, "X");
        _driftStoryboard.Children.Add(anim2X);

        var anim2Y = new DoubleAnimation
        {
            From = 0,
            To = 25,
            Duration = new Duration(TimeSpan.FromSeconds(21)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim2Y, Blob2Transform);
        Storyboard.SetTargetProperty(anim2Y, "Y");
        _driftStoryboard.Children.Add(anim2Y);

        if (!GlassFallbackManager.IsFallbackActive && GlassFallbackManager.IsWindowActive)
        {
            _driftStoryboard.Begin();
        }
    }

    private void GlassFallbackManager_FallbackStateChanged(object? sender, bool isFallback)
    {
        DispatcherQueue.TryEnqueue(() => ApplyFallbackState(isFallback));
    }

    private void ApplyFallbackState(bool isFallback)
    {
        if (AmbientCanvas == null)
        {
            return;
        }

        if (isFallback)
        {
            AmbientCanvas.Visibility = Visibility.Collapsed;
            _driftStoryboard?.Pause();
        }
        else
        {
            AmbientCanvas.Visibility = Visibility.Visible;
            if (GlassFallbackManager.IsWindowActive)
            {
                _driftStoryboard?.Resume();
            }
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
        if (GlassFallbackManager.IsFallbackActive)
        {
            return;
        }

        if (isActive)
        {
            _driftStoryboard?.Resume();
        }
        else
        {
            _driftStoryboard?.Pause();
        }
    }

    private void AmbientColorExtractor_ColorsChanged(object? sender, (Color Primary, Color Secondary) colors)
    {
        DispatcherQueue.TryEnqueue(() => UpdateAmbientColors(colors.Primary, colors.Secondary));
    }

    private void UpdateAmbientColors(Color primary, Color secondary)
    {
        if (Blob1Stop0 == null || Blob2Stop0 == null)
        {
            return;
        }

        Blob1Stop0.Color = Color.FromArgb(0x18, primary.R, primary.G, primary.B);
        Blob1Stop1.Color = Color.FromArgb(0x0C, primary.R, primary.G, primary.B);

        Blob2Stop0.Color = Color.FromArgb(0x12, secondary.R, secondary.G, secondary.B);
        Blob2Stop1.Color = Color.FromArgb(0x06, secondary.R, secondary.G, secondary.B);
    }

    private void UpdateCaptionButtonSpacer()
    {
        if (App.MainWindow?.AppWindow?.TitleBar is { } titleBar && CaptionButtonSpacer != null)
        {
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var inset = titleBar.RightInset > 0 ? titleBar.RightInset / scale : 140;
            CaptionButtonSpacer.Width = Math.Max(48, inset);
        }
    }

    private void ShellPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft
            .UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl)
        {
            return;
        }

        var shift = Microsoft
            .UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Number1:
                Navigation.SelectedItem = LockscreenItem;
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Number2:
                Navigation.SelectedItem = WallpaperItem;
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Number3:
                Navigation.SelectedItem = DiagnosticsItem;
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Number4:
                Navigation.SelectedItem = SettingsItem;
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Tab:
                var items = new[] { LockscreenItem, WallpaperItem, DiagnosticsItem, SettingsItem };
                var index = Array.IndexOf(items, Navigation.SelectedItem);
                if (index >= 0)
                {
                    var nextIndex = shift ? (index - 1 + items.Length) % items.Length : (index + 1) % items.Length;
                    Navigation.SelectedItem = items[nextIndex];
                    e.Handled = true;
                }
                break;
        }
    }

    private void ShellPage_ActualThemeChanged(FrameworkElement sender, object args)
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons();
    }
}
