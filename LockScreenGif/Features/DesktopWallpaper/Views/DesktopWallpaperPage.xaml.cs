using System;
using System.IO;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LockscreenGif.Features.DesktopWallpaper.Views;

public sealed partial class DesktopWallpaperPage : Page
{
    public DesktopWallpaperViewModel ViewModel { get; }

    public DesktopWallpaperPage()
    {
        ViewModel = App.GetService<DesktopWallpaperViewModel>();
        InitializeComponent();
    }

    private async void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };

            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            // Supported formats: mp4, webm, mkv, avi, mov, gif, animated webp, html, htm, png, jpg, jpeg, bmp
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".webm");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".avi");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".html");
            picker.FileTypeFilter.Add(".htm");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");

            if (ViewModel.AllowAdvancedExecutables)
            {
                picker.FileTypeFilter.Add(".exe");
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ViewModel.LoadSource(file.Path);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("File picking failed", ex);
        }
    }

    private void LoadUrl_Click(object sender, RoutedEventArgs e)
    {
        var text = UrlInputBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                text = "https://" + text;
            }
            ViewModel.LoadSource(text);
        }
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop media or web file to set as wallpaper";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;

        if (Application.Current.Resources.TryGetValue("GlassBorderAccentBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Brush)brush;
        }
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e)
    {
        if (Application.Current.Resources.TryGetValue("GlassBorderSubtleBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Brush)brush;
        }
    }

    private async void DropTarget_Drop(object sender, DragEventArgs e)
    {
        if (Application.Current.Resources.TryGetValue("GlassBorderSubtleBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Brush)brush;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile file)
                {
                    ViewModel.LoadSource(file.Path);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Drop failed", ex);
            }
        }
    }

    private async void AdvancedExeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle)
        {
            return;
        }

        if (toggle.IsOn && !ViewModel.AllowAdvancedExecutables)
        {
            // Show security warning dialog
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Security Notice: Executable Wallpapers",
                Content = "Running third-party executables (.exe) as live desktop wallpapers executes native code with your current user privileges. Only run programs from trusted, verified developers.\n\nDo you want to enable this feature?",
                PrimaryButtonText = "Enable",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.AllowAdvancedExecutables = true;
            }
            else
            {
                toggle.IsOn = false;
                ViewModel.AllowAdvancedExecutables = false;
            }
        }
        else if (!toggle.IsOn && ViewModel.AllowAdvancedExecutables)
        {
            ViewModel.AllowAdvancedExecutables = false;
        }
    }
}
