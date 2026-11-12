using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services;
using Microsoft.UI.Dispatching;

namespace LockscreenGif.Features.DesktopWallpaper.ViewModels;

/// <summary>
/// ViewModel for the Desktop Wallpaper page.
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
                OnPropertyChanged(nameof(StartButtonVisibility));
                OnPropertyChanged(nameof(StopButtonVisibility));
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

    public ObservableCollection<DisplayMonitor> Monitors { get; } = [];

    public bool CanStart => !IsRunning && !IsBusy;
    public bool CanStop => IsRunning && !IsBusy;

    public Microsoft.UI.Xaml.Visibility StartButtonVisibility =>
        IsRunning ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility StopButtonVisibility =>
        IsRunning ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand ToggleCommand { get; }
    public IRelayCommand RefreshMonitorsCommand { get; }

    public DesktopWallpaperViewModel(IWallpaperEngineService engineService)
    {
        _engineService = engineService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        ToggleCommand = new AsyncRelayCommand(ToggleAsync);
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
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(StartButtonVisibility));
        OnPropertyChanged(nameof(StopButtonVisibility));
    }

    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();

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
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();

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

    private async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
        }
        else
        {
            await StartAsync();
        }
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
