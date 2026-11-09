using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LockscreenGif.Views.Dialogs;

/// <summary>
/// Dialog displaying open-source software notices and upstream license credits.
/// </summary>
internal static class LicensesDialog
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 440,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 8),
        };

        var container = new StackPanel { Spacing = 14, MaxWidth = 520 };

        void AddLicenseCard(string name, string author, string licenseType, string notes, string? url = null)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
            };

            if (Application.Current.Resources.TryGetValue("GlassSurfaceSubtleBrush", out var bg) && bg is Brush bgb)
            {
                card.Background = bgb;
            }
            if (Application.Current.Resources.TryGetValue("GlassBorderRimBrush", out var border) && border is Brush bb)
            {
                card.BorderBrush = bb;
            }

            var cardStack = new StackPanel { Spacing = 6 };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
            };
            if (Application.Current.Resources.TryGetValue("GlassTextPrimaryBrush", out var fg) && fg is Brush fgb)
            {
                titleText.Foreground = fgb;
            }
            Grid.SetColumn(titleText, 0);
            headerGrid.Children.Add(titleText);

            var badge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                BorderThickness = new Thickness(1),
            };
            if (Application.Current.Resources.TryGetValue("GlassSurfaceElevatedBrush", out var bbg) && bbg is Brush bbgBrush)
            {
                badge.Background = bbgBrush;
            }
            if (Application.Current.Resources.TryGetValue("GlassBorderRimBrush", out var bbr) && bbr is Brush bbrBrush)
            {
                badge.BorderBrush = bbrBrush;
            }

            var badgeText = new TextBlock
            {
                Text = licenseType,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            if (Application.Current.Resources.TryGetValue("GlassAccentBrassBrush", out var ab) && ab is Brush abb)
            {
                badgeText.Foreground = abb;
            }
            badge.Child = badgeText;
            Grid.SetColumn(badge, 1);
            headerGrid.Children.Add(badge);

            cardStack.Children.Add(headerGrid);

            var authorText = new TextBlock { Text = author, FontSize = 12 };
            if (Application.Current.Resources.TryGetValue("GlassTextSecondaryBrush", out var sfg) && sfg is Brush sfgb)
            {
                authorText.Foreground = sfgb;
            }
            cardStack.Children.Add(authorText);

            var notesText = new TextBlock
            {
                Text = notes,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            if (Application.Current.Resources.TryGetValue("GlassTextSecondaryBrush", out var nfg) && nfg is Brush nfgb)
            {
                notesText.Foreground = nfgb;
            }
            cardStack.Children.Add(notesText);

            if (!string.IsNullOrEmpty(url))
            {
                var link = new HyperlinkButton
                {
                    Content = url,
                    NavigateUri = new Uri(url),
                    Padding = new Thickness(0),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                if (Application.Current.Resources.TryGetValue("GlassTextMonospaceBrush", out var mb) && mb is Brush mbb)
                {
                    link.Foreground = mbb;
                }
                cardStack.Children.Add(link);
            }

            card.Child = cardStack;
            container.Children.Add(card);
        }

        AddLicenseCard(
            "LockscreenGif (Upstream Project)",
            "Copyright (c) 2026 Leapward-Koex",
            "MIT License",
            "Original core architecture, privileged elevation concept, and lock-screen cache layout.",
            "https://github.com/Leapward-Koex/LockscreenGif"
        );

        AddLicenseCard(
            "FFmpeg",
            "Copyright (c) 2000-2025 the FFmpeg developers (gyan.dev build 7.1.1)",
            "GPLv3 / LGPLv3",
            "Command-line multimedia framework used as an external standalone process for video decoding and frame generation.",
            "https://ffmpeg.org"
        );

        AddLicenseCard(
            "Gifski",
            "Copyright (c) Kornel Lesiński / ImageOptim",
            "AGPLv3",
            "High-quality GIF encoder library invoked for optimal dithering and color quantization.",
            "https://github.com/ImageOptim/gifski"
        );

        AddLicenseCard(
            "Microsoft Windows App SDK & WinUI 3",
            "Copyright (c) Microsoft Corporation",
            "MIT License",
            "Native Windows application framework and user interface controls.",
            "https://github.com/microsoft/WindowsAppSDK"
        );

        AddLicenseCard(
            "CommunityToolkit.Mvvm",
            "Copyright (c) .NET Foundation and Contributors",
            "MIT License",
            "Modern, lightweight Model-View-ViewModel architecture toolkit.",
            "https://github.com/CommunityToolkit/dotnet"
        );

        AddLicenseCard(
            "WinUIEx",
            "Copyright (c) Morten Nielsen",
            "Apache 2.0",
            "Window manager and backdrop extensions for WinUI 3 applications.",
            "https://github.com/dotMorten/WinUIEx"
        );

        AddLicenseCard(
            "FFMpegCore & Gifski.Net",
            "Copyright (c) FFMpegCore / Gifski.Net Authors",
            "MIT License",
            ".NET bindings and process wrappers for multimedia utilities.",
            "https://github.com/rosenbjerg/FFMpegCore"
        );

        scrollViewer.Content = container;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Open-Source Licenses",
            Content = scrollViewer,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }
}
