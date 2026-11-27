using LockscreenGif.Helpers;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace LockscreenGif;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private UISettings settings;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();
        ExtendsContentIntoTitleBar = true;

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event

        Activated += MainWindow_Activated;
        SizeChanged += (_, _) => ClampWindowToWorkArea();
        Closed += (_, _) =>
        {
            GlassFallbackManager.Shutdown();
        };
        GlassFallbackManager.Initialize(this);
        ClampWindowToWorkArea();
    }

    private void ClampWindowToWorkArea()
    {
        try
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Nearest
            );
            if (displayArea is null)
            {
                return;
            }

            var work = displayArea.WorkArea;
            var size = AppWindow.Size;
            var pos = AppWindow.Position;

            var newW = Math.Min(size.Width, work.Width);
            var newH = Math.Min(size.Height, work.Height);
            if (newW != size.Width || newH != size.Height)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(newW, newH));
            }

            var newX = Math.Clamp(pos.X, work.X, Math.Max(work.X, work.X + work.Width - newW));
            var newY = Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - newH));
            if (newX != pos.X || newY != pos.Y)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(newX, newY));
            }
        }
        catch { }
    }

    private void MainWindow_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        GlassFallbackManager.SetWindowActive(args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated);
    }

    // this handles updating the caption button colors correctly when indows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }
}
