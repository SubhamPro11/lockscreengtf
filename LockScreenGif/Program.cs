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
        global::WinRT.ComWrappersSupport.InitializeComWrappers();

        if (args.Length > 0 && string.Equals(args[0], "--wallpaper-player", StringComparison.OrdinalIgnoreCase))
        {
            var playerArgs = args.Skip(1).ToArray();
            var playerThread = new global::System.Threading.Thread(() =>
            {
                try
                {
                    var hDesk = Features.DesktopWallpaper.Services.Native.DesktopNativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
                    if (hDesk != 0)
                    {
                        Features.DesktopWallpaper.Services.Native.DesktopNativeMethods.SetThreadDesktop(hDesk);
                    }
                }
                catch { }

                var dispatcherController = global::Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread();
                WallpaperPlayerProcess.Run(playerArgs);
            });
            playerThread.SetApartmentState(global::System.Threading.ApartmentState.STA);
            playerThread.Start();
            playerThread.Join();
            return;
        }

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
