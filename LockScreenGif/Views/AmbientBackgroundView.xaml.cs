using System;
using LockscreenGif.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace LockscreenGif.Views;

public sealed partial class AmbientBackgroundView : UserControl
{
    private Storyboard? _driftStoryboard;
    private bool _isLoaded;

    public AmbientBackgroundView()
    {
        InitializeComponent();
        Loaded += AmbientBackgroundView_Loaded;
        Unloaded += AmbientBackgroundView_Unloaded;
    }

    private void AmbientBackgroundView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }
        _isLoaded = true;

        SetupDriftAnimations();
        ApplyFallbackState(GlassFallbackManager.IsFallbackActive);
        UpdateColors(AmbientColorExtractor.CurrentPrimary, AmbientColorExtractor.CurrentSecondary);

        GlassFallbackManager.FallbackStateChanged += GlassFallbackManager_FallbackStateChanged;
        AmbientColorExtractor.ColorsChanged += AmbientColorExtractor_ColorsChanged;

        if (App.MainWindow != null)
        {
            App.MainWindow.Activated += MainWindow_Activated;
        }
    }

    private void AmbientBackgroundView_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
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
        _driftStoryboard = new Storyboard
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        // Blob 1: ~40s drift cycle
        var anim1X = new DoubleAnimation
        {
            From = 0,
            To = 40,
            Duration = new Duration(TimeSpan.FromSeconds(20)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
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
            EnableDependentAnimation = true
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
            EnableDependentAnimation = true
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
            EnableDependentAnimation = true
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
        DispatcherQueue.TryEnqueue(() => UpdateColors(colors.Primary, colors.Secondary));
    }

    private void UpdateColors(Color primary, Color secondary)
    {
        if (Blob1Stop0 == null || Blob2Stop0 == null)
        {
            return;
        }

        // Keep ambient alpha capped at 0x18 (~9.4%) and 0x12 (~7.0%) to strictly respect the 12% brightness ceiling
        Blob1Stop0.Color = Color.FromArgb(0x18, primary.R, primary.G, primary.B);
        Blob1Stop1.Color = Color.FromArgb(0x0C, primary.R, primary.G, primary.B);

        Blob2Stop0.Color = Color.FromArgb(0x12, secondary.R, secondary.G, secondary.B);
        Blob2Stop1.Color = Color.FromArgb(0x06, secondary.R, secondary.G, secondary.B);
    }
}
