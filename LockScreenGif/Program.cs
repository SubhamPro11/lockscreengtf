using System;
using System.Linq;
using LockscreenGif.Features.DesktopWallpaper.Player;

namespace LockscreenGif;

/// <summary>
/// Application entry point allowing intercept of standalone wallpaper player mode.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--wallpaper-player", StringComparison.OrdinalIgnoreCase))
        {
            WallpaperPlayerProcess.Run(args.Skip(1).ToArray());
            return;
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start(
            (p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                );
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            }
        );
    }
}
