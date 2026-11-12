using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services;
using Microsoft.UI.Dispatching;

namespace LockscreenGif.Features.DesktopWallpaper.ViewModels;

/// <summary>
/// ViewModel for the Desktop Wallpaper page supporting video, web, image, and executable wallpapers.
/// </summary>
public sealed class DesktopWallpaperViewModel : ObservableObject
{
    private readonly IWallpaperEngineService _engineService;
    private readonly DispatcherQueue _dispatcherQueue;

    private string _statusText = "Stopped";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanPauseResume));
                OnPropertyChanged(nameof(StartButtonVisibility));
                OnPropertyChanged(nameof(StopButtonVisibility));
            }
        }
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseResumeButtonText));
                OnPropertyChanged(nameof(PauseResumeGlyph));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanPauseResume));
                OnPropertyChanged(nameof(StartButtonVisibility));
                OnPropertyChanged(nameof(StopButtonVisibility));
            }
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    private string? _originalWallpaperPath;
    public string? OriginalWallpaperPath
    {
        get => _originalWallpaperPath;
        set => SetProperty(ref _originalWallpaperPath, value);
    }

    private string? _activeTargetDescription;
    public string? ActiveTargetDescription
    {
        get => _activeTargetDescription;
        set => SetProperty(ref _activeTargetDescription, value);
    }

    private DisplayMonitor? _selectedMonitor;
    public DisplayMonitor? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (SetProperty(ref _selectedMonitor, value))
            {
                _engineService.SelectedMonitor = value;
            }
        }
    }

    private int _layoutModeIndex;
    public int LayoutModeIndex
    {
        get => _layoutModeIndex;
        set
        {
            if (SetProperty(ref _layoutModeIndex, value))
            {
                UpdateEngineLayoutMode();
            }
        }
    }

    private string _sourceInput = string.Empty;
    public string SourceInput
    {
        get => _sourceInput;
        set => SetProperty(ref _sourceInput, value);
    }

    private string _wallpaperTitle = "No wallpaper selected";
    public string WallpaperTitle
    {
        get => _wallpaperTitle;
        set => SetProperty(ref _wallpaperTitle, value);
    }

    private string _wallpaperTypeDescription = "None";
    public string WallpaperTypeDescription
    {
        get => _wallpaperTypeDescription;
        set => SetProperty(ref _wallpaperTypeDescription, value);
    }

    private bool _isMediaLoaded;
    public bool IsMediaLoaded
    {
        get => _isMediaLoaded;
        set
        {
            if (SetProperty(ref _isMediaLoaded, value))
            {
                OnPropertyChanged(nameof(CanApply));
            }
        }
    }

    private bool _isVideo;
    public bool IsVideo
    {
        get => _isVideo;
        set => SetProperty(ref _isVideo, value);
    }

    private bool _isWeb;
    public bool IsWeb
    {
        get => _isWeb;
        set => SetProperty(ref _isWeb, value);
    }

    private bool _isApplication;
    public bool IsApplication
    {
        get => _isApplication;
        set => SetProperty(ref _isApplication, value);
    }

    private int _fitModeIndex;
    public int FitModeIndex
    {
        get => _fitModeIndex;
        set
        {
            if (SetProperty(ref _fitModeIndex, value))
            {
                var mode = value switch
                {
                    1 => WallpaperFitMode.Fit,
                    2 => WallpaperFitMode.Stretch,
                    3 => WallpaperFitMode.Center,
                    _ => WallpaperFitMode.Fill
                };
                _ = _engineService.SetFitModeAsync(mode);
            }
        }
    }

    private double _volume = 50.0;
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _ = _engineService.SetVolumeAsync(value / 100.0);
            }
        }
    }

    private bool _isMuted = true;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _ = _engineService.SetMutedAsync(value);
            }
        }
    }

    private int _speedIndex = 1; // 1.0x
    public int SpeedIndex
    {
        get => _speedIndex;
        set
        {
            if (SetProperty(ref _speedIndex, value))
            {
                var speed = value switch
                {
                    0 => 0.5,
                    2 => 1.5,
                    3 => 2.0,
                    _ => 1.0
                };
                _ = _engineService.SetPlaybackRateAsync(speed);
            }
        }
    }

    private bool _loop = true;
    public bool Loop
    {
        get => _loop;
        set => SetProperty(ref _loop, value);
    }

    private bool _forwardInput;
    public bool ForwardInput
    {
        get => _forwardInput;
        set
        {
            if (SetProperty(ref _forwardInput, value))
            {
                _ = _engineService.SetForwardInputAsync(value);
            }
        }
    }

    private bool _allowAdvancedExecutables;
    public bool AllowAdvancedExecutables
    {
        get => _allowAdvancedExecutables;
        set
        {
            if (SetProperty(ref _allowAdvancedExecutables, value))
            {
                _engineService.AllowAdvancedExecutables = value;
            }
        }
    }

    public ObservableCollection<DisplayMonitor> Monitors { get; } = [];

    public bool CanStart => !IsRunning && !IsBusy;
    public bool CanStop => IsRunning && !IsBusy;
    public bool CanPauseResume => IsRunning && !IsBusy;
    public bool CanApply => IsMediaLoaded && !IsBusy;

    public string PauseResumeButtonText => IsPaused ? "Resume" : "Pause";
    public string PauseResumeGlyph => IsPaused ? "\uE768" : "\uE769";

    public Microsoft.UI.Xaml.Visibility StartButtonVisibility =>
        IsRunning ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility StopButtonVisibility =>
        IsRunning ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand TogglePauseCommand { get; }
    public IAsyncRelayCommand ApplyWallpaperCommand { get; }
    public IRelayCommand RefreshMonitorsCommand { get; }

    public DesktopWallpaperViewModel(IWallpaperEngineService engineService)
    {
        _engineService = engineService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        TogglePauseCommand = new AsyncRelayCommand(TogglePauseAsync, () => CanPauseResume);
        ApplyWallpaperCommand = new AsyncRelayCommand(ApplyWallpaperAsync, () => CanApply);
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors);

        _engineService.StatusChanged += EngineService_StatusChanged;
        RefreshFromEngine();
    }

    private void EngineService_StatusChanged(object? sender, WallpaperEngineStatus status)
    {
        _dispatcherQueue.TryEnqueue(RefreshFromEngine);
    }

    public void RefreshFromEngine()
    {
        IsRunning = _engineService.IsRunning;
        IsPaused = _engineService.Status == WallpaperEngineStatus.Paused;

        StatusText = _engineService.Status switch
        {
            WallpaperEngineStatus.Running => "Running",
            WallpaperEngineStatus.Starting => "Starting…",
            WallpaperEngineStatus.Paused => "Paused",
            WallpaperEngineStatus.Error => "Attachment error",
            _ => "Stopped"
        };

        ErrorMessage = _engineService.CurrentErrorMessage;
        HasError = !string.IsNullOrWhiteSpace(ErrorMessage);
        OriginalWallpaperPath = _engineService.OriginalWallpaperPath;
        ActiveTargetDescription = _engineService.ActiveTargetDescription;

        Monitors.Clear();
        foreach (var monitor in _engineService.Monitors)
        {
            Monitors.Add(monitor);
        }

        SelectedMonitor = _engineService.SelectedMonitor;
        LayoutModeIndex = _engineService.LayoutMode switch
        {
            WallpaperLayoutMode.SingleMonitor => 1,
            WallpaperLayoutMode.Duplicate => 2,
            _ => 0
        };

        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        ApplyWallpaperCommand.NotifyCanExecuteChanged();

        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPauseResume));
        OnPropertyChanged(nameof(StartButtonVisibility));
        OnPropertyChanged(nameof(StopButtonVisibility));
    }

    public void LoadSource(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return;
        }

        SourceInput = pathOrUrl;
        var type = WallpaperItem.DetectType(pathOrUrl);

        IsVideo = type == WallpaperType.Video;
        IsWeb = type == WallpaperType.Web;
        IsApplication = type == WallpaperType.Application;

        WallpaperTypeDescription = type switch
        {
            WallpaperType.Video => "Video / Animated Loop",
            WallpaperType.Web => "Web Page / WebGL / Interactive",
            WallpaperType.Image => "Static Image",
            WallpaperType.Application => "Executable Application (.exe)",
            _ => "Media"
        };

        WallpaperTitle = pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : Path.GetFileName(pathOrUrl);

        IsMediaLoaded = true;
        ApplyWallpaperCommand.NotifyCanExecuteChanged();

        // If the engine is already running, seamlessly apply the newly selected wallpaper
        if (IsRunning)
        {
            _ = ApplyWallpaperAsync();
        }
    }

    public async Task ApplyWallpaperAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceInput))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        HasError = false;

        try
        {
            UpdateEngineLayoutMode();
            _engineService.SelectedMonitor = SelectedMonitor;

            var type = WallpaperItem.DetectType(SourceInput);
            var fitMode = FitModeIndex switch
            {
                1 => WallpaperFitMode.Fit,
                2 => WallpaperFitMode.Stretch,
                3 => WallpaperFitMode.Center,
                _ => WallpaperFitMode.Fill
            };

            var speed = SpeedIndex switch
            {
                0 => 0.5,
                2 => 1.5,
                3 => 2.0,
                _ => 1.0
            };

            var item = new WallpaperItem
            {
                SourcePathOrUrl = SourceInput,
                Title = WallpaperTitle,
                Type = type,
                FitMode = fitMode,
                Volume = Volume / 100.0,
                IsMuted = IsMuted,
                PlaybackSpeed = speed,
                Loop = Loop,
                ForwardInput = ForwardInput,
                IsAdvancedExecutable = type == WallpaperType.Application
            };

            var success = await _engineService.ApplyWallpaperAsync(item);
            if (!success)
            {
                ErrorMessage = _engineService.CurrentErrorMessage ?? "Failed to apply wallpaper.";
                HasError = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            RefreshFromEngine();
        }
    }

    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsMediaLoaded)
        {
            await ApplyWallpaperAsync();
            return;
        }

        IsBusy = true;
        try
        {
            UpdateEngineLayoutMode();
            _engineService.SelectedMonitor = SelectedMonitor;
            await _engineService.StartAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            RefreshFromEngine();
        }
    }

    private async Task StopAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _engineService.StopAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsBusy = false;
            RefreshFromEngine();
        }
    }

    private async Task TogglePauseAsync()
    {
        if (IsPaused)
        {
            await _engineService.ResumeAsync();
        }
        else
        {
            await _engineService.PauseAsync();
        }
        RefreshFromEngine();
    }

    private void RefreshMonitors()
    {
        _engineService.RefreshMonitors();
        RefreshFromEngine();
    }

    private void UpdateEngineLayoutMode()
    {
        _engineService.LayoutMode = LayoutModeIndex switch
        {
            1 => WallpaperLayoutMode.SingleMonitor,
            2 => WallpaperLayoutMode.Duplicate,
            _ => WallpaperLayoutMode.SpanAllMonitors
        };
    }
}
