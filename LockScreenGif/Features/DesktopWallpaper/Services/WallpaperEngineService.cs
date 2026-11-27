using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Player;
using LockscreenGif.Features.DesktopWallpaper.Services.Audio;
using LockscreenGif.Features.DesktopWallpaper.Services.Input;
using LockscreenGif.Features.DesktopWallpaper.Services.Media;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;
using LockscreenGif.Features.DesktopWallpaper.Services.Performance;

namespace LockscreenGif.Features.DesktopWallpaper.Services;

/// <summary>
/// Core desktop wallpaper engine service.
/// Coordinates the isolated player process (LockscreenGif.exe --wallpaper-player)
/// across single and multi-monitor setups, ensuring zero UI thread overhead,
/// uninterrupted playback when the main window closes, and clean restoration.
/// </summary>
public sealed class WallpaperEngineService : IWallpaperEngineService, IDisposable
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly SemaphoreSlim _ipcGate = new(1, 1);
    private readonly DesktopInputForwarder _inputForwarder = new();

    private Process? _playerProcess;

    private readonly string _pidFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "wallpaper_player.pid"
    );

    private WallpaperEngineStatus _status = WallpaperEngineStatus.Stopped;
    private string? _currentErrorMessage;
    private string? _originalWallpaperPath;
    private string? _activeTargetDescription;
    private bool _originalWallpaperRecorded;
    private List<DisplayMonitor> _monitors = [];
    private DisplayMonitor? _selectedMonitor;
    private WallpaperLayoutMode _layoutMode = WallpaperLayoutMode.SpanAllMonitors;
    private WallpaperItem? _currentWallpaper;
    private bool _allowAdvancedExecutables;
    private bool _disposed;

    public WallpaperEngineStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    public string? CurrentErrorMessage => _currentErrorMessage;
    public string? OriginalWallpaperPath => _originalWallpaperPath;
    public string? ActiveTargetDescription => _activeTargetDescription;

    public IReadOnlyList<DisplayMonitor> Monitors
    {
        get
        {
            lock (_lock)
            {
                return _monitors.AsReadOnly();
            }
        }
    }

    public DisplayMonitor? SelectedMonitor
    {
        get => _selectedMonitor;
        set => _selectedMonitor = value;
    }

    public WallpaperLayoutMode LayoutMode
    {
        get => _layoutMode;
        set => _layoutMode = value;
    }

    public WallpaperItem? CurrentWallpaper => _currentWallpaper;

    public bool AllowAdvancedExecutables
    {
        get => _allowAdvancedExecutables;
        set => _allowAdvancedExecutables = value;
    }

    private readonly WallpaperPerformanceMonitor _performanceMonitor;
    public WallpaperPerformanceMonitor PerformanceMonitor => _performanceMonitor;

    private readonly WasapiAudioCaptureService _audioCaptureService = new();
    private bool _isAudioReactiveEnabled;

    public bool IsAudioReactiveEnabled
    {
        get => _isAudioReactiveEnabled;
        set
        {
            _isAudioReactiveEnabled = value;
            if (value && IsRunning)
            {
                _audioCaptureService.Enable();
            }
            else
            {
                _audioCaptureService.Disable();
            }
        }
    }

    public bool IsRunning => Status == WallpaperEngineStatus.Running || Status == WallpaperEngineStatus.Paused;

    public event EventHandler<WallpaperEngineStatus>? StatusChanged;

    public WallpaperEngineService()
    {
        _performanceMonitor = new WallpaperPerformanceMonitor(this);
        _audioCaptureService.SpectrumReady += OnAudioSpectrumReady;
        WallpaperMediaHelper.CleanupStaleJobDirectories();
        RefreshMonitors();
        CheckExistingPlayerProcess();
    }

    private static string? ReadPidFileText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }

    private void CheckExistingPlayerProcess()
    {
        try
        {
            var content = ReadPidFileText(_pidFilePath);
            if (!string.IsNullOrEmpty(content) && int.TryParse(content, out var pid))
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited && proc.ProcessName.Contains("LockscreenGif", StringComparison.OrdinalIgnoreCase))
                    {
                        _playerProcess = proc;
                        Status = WallpaperEngineStatus.Running;
                        _performanceMonitor.Start();
                        Logger.Info($"Detected active background wallpaper player process (PID {pid}).");
                    }
                }
                catch (ArgumentException)
                {
                    // Stale PID file
                    File.Delete(_pidFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inspect existing player process: {ex.Message}");
        }
    }

    private async void OnAudioSpectrumReady(object? sender, AudioSpectrumData data)
    {
        if (!_isAudioReactiveEnabled || !IsRunning || _ipcGate.CurrentCount == 0)
        {
            return;
        }

        try
        {
            var json =
                $"{{\"type\":\"audioSpectrum\",\"peak\":{data.Peak.ToString("F3", CultureInfo.InvariantCulture)},\"bands\":[{string.Join(",", data.Bands.Select(b => b.ToString("F3", CultureInfo.InvariantCulture)))}]}}";
            await SendCommandAsync($"WEB_MSG {json}", timeoutMs: 500);
        }
        catch { }
    }

    public void RefreshMonitors()
    {
        lock (_lock)
        {
            _monitors = DesktopAttachHelper.GetMonitors().ToList();
            if (_selectedMonitor == null || !_monitors.Any(m => m.DeviceName == _selectedMonitor.DeviceName))
            {
                _selectedMonitor = _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.FirstOrDefault();
            }
        }
    }

    public async Task<bool> StartAsync()
    {
        if (CurrentWallpaper != null)
        {
            return await ApplyWallpaperAsync(CurrentWallpaper);
        }

        return false;
    }

    public async Task<bool> ApplyWallpaperAsync(WallpaperItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Type == WallpaperType.Application && !AllowAdvancedExecutables)
        {
            throw new InvalidOperationException(
                "Running executable wallpapers is restricted. Enable the 'Advanced: Allow Executables' setting first."
            );
        }

        await _renderGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _currentWallpaper = item;
            Status = WallpaperEngineStatus.Starting;
            _currentErrorMessage = null;

            var effectiveSource = item.SourcePathOrUrl;
            if (item.Type != WallpaperType.Application)
            {
                effectiveSource = await WallpaperMediaHelper.CopySourceToWallpaperCacheAsync(item.SourcePathOrUrl, cancellationToken);
            }

            if (!_originalWallpaperRecorded)
            {
                _originalWallpaperPath = DesktopAttachHelper.GetCurrentWallpaperPath();
                _originalWallpaperRecorded = true;
                Logger.Info($"Recorded original wallpaper path: {_originalWallpaperPath ?? "(none)"}");
            }

            var target = DesktopAttachHelper.FindDesktopHost();
            if (target == null || target.ParentHwnd == 0)
            {
                _currentErrorMessage = "Unable to locate or initialize the Windows desktop background window hierarchy (Progman/WorkerW).";
                Logger.Error(_currentErrorMessage);
                Status = WallpaperEngineStatus.Error;
                return false;
            }

            _activeTargetDescription = target.Description;

            // If an isolated player is already alive, send LOAD command over synchronized named pipe
            if (IsPlayerProcessAlive())
            {
                Logger.Info($"Sending LOAD command to existing player process: {effectiveSource}");
                var response = await SendCommandAsync($"LOAD {effectiveSource}", timeoutMs: 5000, cancellationToken);
                if (response != null && !response.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    await SendCommandAsync($"VOLUME {item.Volume.ToString("F2", CultureInfo.InvariantCulture)}", 3000, cancellationToken);
                    await SendCommandAsync($"MUTE {(item.IsMuted ? "true" : "false")}", 3000, cancellationToken);
                    await SendCommandAsync($"LOOP {(item.Loop ? "true" : "false")}", 3000, cancellationToken);
                    await SendCommandAsync($"FIT {item.FitMode}", 3000, cancellationToken);
                    await SendCommandAsync(
                        $"SPEED {item.PlaybackSpeed.ToString("F2", CultureInfo.InvariantCulture)}",
                        3000,
                        cancellationToken
                    );

                    Status = WallpaperEngineStatus.Running;
                    _performanceMonitor.Start();
                    return true;
                }
            }

            // Otherwise, spawn new isolated player process
            Logger.Info($"Launching isolated wallpaper player process for: {effectiveSource}");
            await TerminateExistingPlayerAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                _currentErrorMessage = "Unable to locate LockscreenGif application executable.";
                Logger.Error(_currentErrorMessage);
                Status = WallpaperEngineStatus.Error;
                return false;
            }

            var args =
                $"--wallpaper-player --path \"{effectiveSource}\" "
                + $"--volume {item.Volume.ToString("F2", CultureInfo.InvariantCulture)} "
                + $"--muted {(item.IsMuted ? "true" : "false")} "
                + $"--loop {(item.Loop ? "true" : "false")} "
                + $"--fit {item.FitMode} "
                + $"--speed {item.PlaybackSpeed.ToString("F2", CultureInfo.InvariantCulture)} "
                + $"--layout {_layoutMode}";

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            };

            var proc = Process.Start(psi);
            if (proc == null)
            {
                _currentErrorMessage = "Failed to launch wallpaper player process.";
                Logger.Error(_currentErrorMessage);
                Status = WallpaperEngineStatus.Error;
                return false;
            }

            _playerProcess = proc;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var stdout = proc.StandardOutput;
            string? readyLine = null;
            while (!linkedCts.IsCancellationRequested)
            {
                var line = await stdout.ReadLineAsync(linkedCts.Token);
                if (line == null)
                {
                    break;
                }
                line = line.Trim();
                if (line.Equals("READY", StringComparison.OrdinalIgnoreCase))
                {
                    readyLine = line;
                    break;
                }
                if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    _currentErrorMessage = line.Length > 5 ? line[5..].Trim() : line;
                    Logger.Error($"Player reported error: {_currentErrorMessage}");
                    Status = WallpaperEngineStatus.Error;
                    return false;
                }
            }

            if (readyLine == null)
            {
                _currentErrorMessage = proc.HasExited
                    ? $"Wallpaper player process exited prematurely with code {proc.ExitCode}."
                    : "Timed out waiting for wallpaper player process to initialize.";
                Logger.Error(_currentErrorMessage);
                Status = WallpaperEngineStatus.Error;
                return false;
            }

            Status = WallpaperEngineStatus.Running;
            _performanceMonitor.Start();
            Logger.Info($"Wallpaper '{item.Title}' applied and playing via isolated background process.");
            return true;
        }
        catch (OperationCanceledException)
        {
            await TerminateExistingPlayerAsync();
            Status = WallpaperEngineStatus.Stopped;
            throw;
        }
        catch (IOException ex)
        {
            _currentErrorMessage =
                "Could not read or access the wallpaper file because it is in use by another program. Close any program using the file and try again.";
            Logger.Error(_currentErrorMessage, ex);
            Status = WallpaperEngineStatus.Error;
            return false;
        }
        catch (Exception ex)
        {
            _currentErrorMessage = $"Failed to render wallpaper: {ex.Message}";
            Logger.Error(_currentErrorMessage, ex);
            Status = WallpaperEngineStatus.Error;
            return false;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private bool IsPlayerProcessAlive()
    {
        if (_playerProcess != null && !_playerProcess.HasExited)
        {
            return true;
        }

        try
        {
            var content = ReadPidFileText(_pidFilePath);
            if (!string.IsNullOrEmpty(content) && int.TryParse(content, out var pid))
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited && proc.ProcessName.Contains("LockscreenGif", StringComparison.OrdinalIgnoreCase))
                    {
                        _playerProcess = proc;
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    try
                    {
                        File.Delete(_pidFilePath);
                    }
                    catch { }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        await _ipcGate.WaitAsync(cancellationToken);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                WallpaperPlayerProcess.NamedPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await pipe.ConnectAsync(linkedCts.Token);

            await using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);

            await writer.WriteLineAsync(command.AsMemory(), linkedCts.Token);
            var response = await reader.ReadLineAsync(linkedCts.Token);
            return response ?? "OK";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warn($"Timed out sending command '{command}' via named pipe.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send command via named pipe: {ex.Message}");
            return null;
        }
        finally
        {
            _ipcGate.Release();
        }
    }

    public async Task RestartAsync()
    {
        if (_currentWallpaper == null)
        {
            return;
        }

        var item = _currentWallpaper;
        await StopAsync();
        await Task.Delay(300);
        await ApplyWallpaperAsync(item);
    }

    public async Task StopAsync()
    {
        if (Status == WallpaperEngineStatus.Stopped && !IsPlayerProcessAlive())
        {
            return;
        }

        try
        {
            Logger.Info("Stopping wallpaper engine and terminating isolated player process...");
            _audioCaptureService.Disable();
            _performanceMonitor.Stop();
            _inputForwarder.Disable();

            await SendCommandAsync("CLOSE", timeoutMs: 1000);
            await TerminateExistingPlayerAsync();

            if (_originalWallpaperRecorded)
            {
                DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
            }

            Status = WallpaperEngineStatus.Stopped;
            _currentErrorMessage = null;
            Logger.Info("Wallpaper engine stopped cleanly and original wallpaper restored.");
        }
        catch (Exception ex)
        {
            Logger.Error("Error stopping wallpaper engine", ex);
            Status = WallpaperEngineStatus.Error;
        }
    }

    private async Task TerminateExistingPlayerAsync()
    {
        try
        {
            if (_playerProcess != null)
            {
                if (!_playerProcess.HasExited)
                {
                    using var waitCts = new CancellationTokenSource(1000);
                    try
                    {
                        await _playerProcess.WaitForExitAsync(waitCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _playerProcess.Kill(true);
                    }
                }
                _playerProcess.Dispose();
                _playerProcess = null;
            }

            var content = ReadPidFileText(_pidFilePath);
            if (!string.IsNullOrEmpty(content) && int.TryParse(content, out var pid))
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                    }
                }
                catch
                {
                    // Already exited
                }
            }
            if (File.Exists(_pidFilePath))
            {
                File.Delete(_pidFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to cleanly terminate existing player process: {ex.Message}");
        }
    }

    public async Task PauseAsync()
    {
        if (Status != WallpaperEngineStatus.Running)
        {
            return;
        }

        Logger.Info("Pausing wallpaper playback...");
        await SendCommandAsync("PAUSE");
        Status = WallpaperEngineStatus.Paused;
    }

    public async Task ResumeAsync()
    {
        if (Status != WallpaperEngineStatus.Paused)
        {
            return;
        }

        Logger.Info("Resuming wallpaper playback...");
        await SendCommandAsync("RESUME");
        Status = WallpaperEngineStatus.Running;
    }

    public async Task SetVolumeAsync(double volume)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.Volume = volume;
        }
        await SendCommandAsync($"VOLUME {(volume * 100.0).ToString("F1", CultureInfo.InvariantCulture)}");
    }

    public async Task SetMutedAsync(bool muted)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.IsMuted = muted;
        }
        await SendCommandAsync($"MUTE {(muted ? "true" : "false")}");
    }

    public async Task SetPlaybackRateAsync(double rate)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.PlaybackSpeed = rate;
        }
        await SendCommandAsync($"SPEED {rate.ToString("F2", CultureInfo.InvariantCulture)}");
    }

    public async Task SetFitModeAsync(WallpaperFitMode fitMode)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.FitMode = fitMode;
        }
        await SendCommandAsync($"FIT {fitMode}");
    }

    public Task SetForwardInputAsync(bool forwardInput)
    {
        if (_currentWallpaper != null)
        {
            _currentWallpaper.ForwardInput = forwardInput;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _performanceMonitor.Dispose();
        _audioCaptureService.Dispose();
        _inputForwarder.Dispose();

        // Note: Closing the main app does NOT kill the player process.
        // As per the Lively Wallpaper model, the wallpaper keeps playing!
        _renderGate.Dispose();
        _ipcGate.Dispose();
    }
}
