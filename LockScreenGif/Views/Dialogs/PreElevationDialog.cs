using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LockscreenGif.Views.Dialogs;

/// <summary>
/// Pre-elevation explanatory dialog that informs the user why administrator
/// elevation is required before Windows User Account Control appears.
/// </summary>
internal static class PreElevationDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root)
    {
        var theme = (root.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default;

        var panel = new StackPanel { Spacing = 14 };

        var intro = new TextBlock
        {
            Text = "Applying this animation writes frames directly to the protected Windows lock-screen cache.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
        };
        panel.Children.Add(intro);

        var detailsBorder = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
        };

        if (Application.Current.Resources.TryGetValue("InstrumentSurfaceBrush", out var surface) && surface is Brush sb)
        {
            detailsBorder.Background = sb;
        }
        if (Application.Current.Resources.TryGetValue("InstrumentBorderBrush", out var border) && border is Brush bb)
        {
            detailsBorder.BorderBrush = bb;
        }

        var detailsStack = new StackPanel { Spacing = 10 };

        detailsStack.Children.Add(CreatePoint(
            "Target location",
            "%ProgramData%\\Microsoft\\Windows\\SystemData (protected lock screen cache)"
        ));

        detailsStack.Children.Add(CreatePoint(
            "One-time operation",
            "Zero background processes. Windows plays the loop natively with no ongoing CPU or battery overhead. The app can be closed once applied."
        ));

        detailsStack.Children.Add(CreatePoint(
            "Reversible anytime",
            "You can restore your default Windows lock screen at any time with a single click."
        ));

        detailsBorder.Child = detailsStack;
        panel.Children.Add(detailsBorder);

        var uacNote = new TextBlock
        {
            Text = "When you click 'Continue to UAC', Windows will display an elevation prompt. Select 'Yes' to authorize the write.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        };
        if (Application.Current.Resources.TryGetValue("InstrumentTextMutedBrush", out var muted) && muted is Brush mb)
        {
            uacNote.Foreground = mb;
        }
        panel.Children.Add(uacNote);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = "Administrator Permission Required",
            Content = panel,
            PrimaryButtonText = "Continue to UAC",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static StackPanel CreatePoint(string header, string body)
    {
        var item = new StackPanel { Spacing = 2 };

        var titleBlock = new TextBlock
        {
            Text = "• " + header,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };
        if (Application.Current.Resources.TryGetValue("InstrumentAccentBrassBrush", out var brass) && brass is Brush bb)
        {
            titleBlock.Foreground = bb;
        }

        var descBlock = new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(12, 0, 0, 0),
        };
        if (Application.Current.Resources.TryGetValue("InstrumentTextMutedBrush", out var muted) && muted is Brush mb)
        {
            descBlock.Foreground = mb;
        }

        item.Children.Add(titleBlock);
        item.Children.Add(descBlock);
        return item;
    }
}

