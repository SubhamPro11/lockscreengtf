using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System.Power;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace LockscreenGif.Helpers;

public static class GlassFallbackManager
{
    private static WindowEx? _window;
    private static DispatcherQueue? _dispatcherQueue;
    private static UISettings? _uiSettings;
    private static AccessibilitySettings? _accessibilitySettings;
    private static bool _forceFallbackForTesting;
    private static bool _isWindowActive = true;

    public static bool ForceFallbackForTesting
    {
        get => _forceFallbackForTesting;
        set
        {
            if (_forceFallbackForTesting != value)
            {
                _forceFallbackForTesting = value;
                UpdateFallbackState();
            }
        }
    }

    public static bool IsWindowActive => _isWindowActive;

    public static bool IsTransparencyEnabled => _uiSettings?.AdvancedEffectsEnabled ?? true;
    public static bool AreAnimationsEnabled => _uiSettings?.AnimationsEnabled ?? true;
    public static bool IsBatterySaverActive => PowerManager.EnergySaverStatus == EnergySaverStatus.On;
    public static bool IsHighContrastActive => _accessibilitySettings?.HighContrast ?? false;

    public static bool IsFallbackActive =>
        _forceFallbackForTesting || !IsTransparencyEnabled || IsBatterySaverActive || IsHighContrastActive;

    public static event EventHandler<bool>? FallbackStateChanged;

    public static void Initialize(WindowEx window)
    {
        _window = window;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        try
        {
            _uiSettings = new UISettings();
            _uiSettings.AdvancedEffectsEnabledChanged += (s, e) => DispatchUpdate();
            _uiSettings.AnimationsEnabledChanged += (s, e) => DispatchUpdate();
        }
        catch
        {
            // Restricted environment fallback
        }

        try
        {
            _accessibilitySettings = new AccessibilitySettings();
            _accessibilitySettings.HighContrastChanged += (s, e) => DispatchUpdate();
        }
        catch { }

        try
        {
            PowerManager.EnergySaverStatusChanged += (s, e) => DispatchUpdate();
        }
        catch { }

        UpdateFallbackState();
    }

    public static void SetWindowActive(bool isActive)
    {
        if (_isWindowActive != isActive)
        {
            _isWindowActive = isActive;
            UpdateFallbackState();
        }
    }

    private static void DispatchUpdate()
    {
        _dispatcherQueue?.TryEnqueue(UpdateFallbackState);
    }

    public static void UpdateFallbackState()
    {
        if (_window == null)
        {
            return;
        }

        // Suspend blur composition when window is inactive to conserve GPU budget
        bool fallback = IsFallbackActive || !_isWindowActive;

        try
        {
            if (fallback)
            {
                _window.SystemBackdrop = null;
            }
            else
            {
                _window.SystemBackdrop = new MicaBackdrop();
            }
        }
        catch
        {
            // SystemBackdrop may be unsupported on specific hardware
        }

        FallbackStateChanged?.Invoke(null, IsFallbackActive);
    }

    public static void Shutdown()
    {
        _window = null;
        _dispatcherQueue = null;
    }
}
