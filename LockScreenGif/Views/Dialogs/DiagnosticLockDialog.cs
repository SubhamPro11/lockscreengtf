using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LockscreenGif.Views.Dialogs;

/// <summary>
/// A visible, cancellable countdown dialog with monospace readout.
/// The caller performs the lock after the dialog closes.
/// </summary>
internal sealed class DiagnosticLockDialog
{
    private readonly ContentDialog _dialog;
    private readonly DispatcherQueueTimer _timer;
    private readonly TextBlock _countdownBlock;
    private int _seconds = 5;
    private bool _expired;
    private bool _cancelled;
    private bool _opened;

    public DiagnosticLockDialog(XamlRoot root, DispatcherQueue dispatcher)
    {
        var contentStack = new StackPanel { Spacing = 14, MinWidth = 320 };

        _countdownBlock = new TextBlock
        {
            Text = "00:05",
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 4),
        };

        if (Application.Current.Resources.TryGetValue("InstrumentMonoFont", out var mono) && mono is FontFamily ff)
        {
            _countdownBlock.FontFamily = ff;
        }
        if (Application.Current.Resources.TryGetValue("InstrumentAccentBrassBrush", out var brass) && brass is Brush bb)
        {
            _countdownBlock.Foreground = bb;
        }

        var description = new TextBlock
        {
            Text =
                "Your PC will lock in 5 seconds so you can verify that Windows plays the animation on your lock screen.\n\nOnce locked, check the playback, then enter your PIN or password to return to LockscreenGif.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        contentStack.Children.Add(_countdownBlock);
        contentStack.Children.Add(description);

        _dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
            Title = "Lock Screen Verification",
            Content = contentStack,
            PrimaryButtonText = "Lock Screen Now",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
    }

    public async Task<bool> ShowAsync()
    {
        if (_cancelled)
        {
            return false;
        }

        _dialog.Opened += OnOpened;
        _dialog.Closing += OnClosing;
        _timer.Tick += OnTick;
        try
        {
            var result = await _dialog.ShowAsync();
            return !_cancelled && (_expired || result == ContentDialogResult.Primary);
        }
        finally
        {
            _timer.Stop();
            _opened = false;
            _timer.Tick -= OnTick;
            _dialog.Opened -= OnOpened;
            _dialog.Closing -= OnClosing;
        }
    }

    public void Cancel()
    {
        _cancelled = true;
        _timer.Stop();
        if (_opened)
        {
            _dialog.Hide();
        }
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _opened = true;
        if (_cancelled)
        {
            _dialog.Hide();
        }
        else
        {
            _timer.Start();
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        _timer.Stop();
        _opened = false;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_cancelled || !_opened)
        {
            return;
        }

        if (--_seconds > 0)
        {
            _countdownBlock.Text = $"00:0{_seconds}";
        }
        else
        {
            _countdownBlock.Text = "00:00";
            _timer.Stop();
            _expired = true;
            _dialog.Hide();
        }
    }
}
