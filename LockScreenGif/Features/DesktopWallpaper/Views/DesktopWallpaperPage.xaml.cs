using LockscreenGif.Features.DesktopWallpaper.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LockscreenGif.Features.DesktopWallpaper.Views;

public sealed partial class DesktopWallpaperPage : Page
{
    public DesktopWallpaperViewModel ViewModel { get; }

    public DesktopWallpaperPage()
    {
        ViewModel = App.GetService<DesktopWallpaperViewModel>();
        InitializeComponent();
    }
}

