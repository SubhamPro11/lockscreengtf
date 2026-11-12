using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LockscreenGif.Features.DesktopWallpaper.Services.System;

/// <summary>
/// Manages running the wallpaper engine automatically at Windows user login.
/// </summary>
public static class WallpaperStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "LockscreenGif";

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppValueName) != null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to read startup registry key: {ex.Message}");
            return false;
        }
    }

    public static bool SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null)
            {
                return false;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    key.SetValue(AppValueName, $"\"{exePath}\" --autostart");
                    Logger.Info("Registered LockscreenGif to run at startup.");
                    return true;
                }
            }
            else
            {
                key.DeleteValue(AppValueName, false);
                Logger.Info("Removed LockscreenGif from startup.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to update startup registry setting", ex);
        }

        return false;
    }
}

