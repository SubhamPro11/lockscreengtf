using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Player;
using LockscreenGif.Features.DesktopWallpaper.Services.Audio;
using LockscreenGif.Features.DesktopWallpaper.Services.Input;
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
    private readonly DesktopInputForwarder _inputForwarder = new();

    private Process? _playerProcess;
    private StreamWriter? _playerStdin;
    private StreamReader? _playerStdout;

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
        RefreshMonitors();
        CheckExistingPlayerProcess();
    }

    private void CheckExistingPlayerProcess()
    {
        try
        {
            if (File.Exists(_pidFilePath))
            {
                var content = File.ReadAllText(_pidFilePath).Trim();
                if (int.TryParse(content, out var pid))
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
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inspect existing player process: {ex.Message}");
        }
    }

    private void OnAudioSpectrumReady(object? sender, AudioSpectrumData data)
    {
        if (!_isAudioReactiveEnabled || !IsRunning)
        {
            return;
        }

        var json =
            $"{{\"type\":\"audioSpectrum\",\"peak\":{data.Peak.ToString("F3", CultureInfo.InvariantCulture)},\"bands\":[{string.Join(",", data.Bands.Select(b => b.ToString("F3", CultureInfo.InvariantCulture)))}]}}";
        _ = SendCommandAsync($"WEB_MSG {json}", timeoutMs: 500);
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

    public async Task<bool> ApplyWallpaperAsync(WallpaperItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Type == WallpaperType.Application && !AllowAdvancedExecutables)
        {
            throw new InvalidOperationException(
                "Running executable wallpapers is restricted. Enable the 'Advanced: Allow Executables' setting first."
            );
        }

        _currentWallpaper = item;
        Status = WallpaperEngineStatus.Starting;
        _currentErrorMessage = null;

        try
        {
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

            // If an isolated player is already alive, send LOAD command
            if (IsPlayerProcessAlive())
            {
                Logger.Info($"Sending LOAD command to existing player process: {item.SourcePathOrUrl}");
                var response = await SendCommandAsync($"LOAD {item.SourcePathOrUrl}", timeoutMs: 5000);
                if (response != null && !response.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    await SendCommandAsync($"VOLUME {item.Volume.ToString("F2", CultureInfo.InvariantCulture)}");
                    await SendCommandAsync($"MUTE {(item.IsMuted ? "true" : "false")}");
                    await SendCommandAsync($"LOOP {(item.Loop ? "true" : "false")}");
                    await SendCommandAsync($"FIT {item.FitMode}");
                    await SendCommandAsync($"SPEED {item.PlaybackSpeed.ToString("F2", CultureInfo.InvariantCulture)}");

                    Status = WallpaperEngineStatus.Running;
                    _performanceMonitor.Start();
                    return true;
                }
            }

            // Otherwise, spawn new isolated player process
            Logger.Info($"Launching isolated wallpaper player process for: {item.SourcePathOrUrl}");
            await TerminateExistingPlayerAsync();

            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                _currentErrorMessage = "Unable to locate LockscreenGif application executable.";
                Logger.Error(_currentErrorMessage);
                Status = WallpaperEngineStatus.Error;
                return false;
            }

            var args =
                $"--wallpaper-player --path \"{item.SourcePathOrUrl}\" "
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
                RedirectStandardInput = true,
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
            _playerStdin = proc.StandardInput;
            _playerStdout = proc.StandardOutput;

            // Wait up to 15 seconds for READY notification
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string? readyLine = null;
            while (!cts.IsCancellationRequested)
            {
                var readTask = _playerStdout.ReadLineAsync(cts.Token).AsTask();
                var completedTask = await Task.WhenAny(readTask, Task.Delay(1000, cts.Token));
                if (completedTask == readTask)
                {
                    var line = await readTask;
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
        catch (Exception ex)
        {
            _currentErrorMessage = $"Failed to render wallpaper: {ex.Message}";
            Logger.Error(_currentErrorMessage, ex);
            Status = WallpaperEngineStatus.Error;
            return false;
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
            if (File.Exists(_pidFilePath))
            {
                var content = File.ReadAllText(_pidFilePath).Trim();
                if (int.TryParse(content, out var pid))
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
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private async Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
    {
        // 1. Stdin
        if (_playerStdin != null && _playerProcess != null && !_playerProcess.HasExited)
        {
            try
            {
                await _playerStdin.WriteLineAsync(command);
                await _playerStdin.FlushAsync();
                if (_playerStdout != null)
                {
                    var readTask = _playerStdout.ReadLineAsync();
                    if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) == readTask)
                    {
                        return await readTask;
                    }
                }
                return "OK";
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to send command via stdin: {ex.Message}");
            }
        }

        // 2. Named pipe fallback
        try
        {
            using var pipe = new NamedPipeClientStream(".", WallpaperPlayerProcess.NamedPipeName, PipeDirection.InOut);
            var connectTask = pipe.ConnectAsync(timeoutMs);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
            {
                using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);

                await writer.WriteLineAsync(command);
                var responseTask = reader.ReadLineAsync();
                if (await Task.WhenAny(responseTask, Task.Delay(timeoutMs)) == responseTask)
                {
                    return await responseTask;
                }
                return "OK";
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send command via named pipe: {ex.Message}");
        }

        return null;
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

    private Task TerminateExistingPlayerAsync()
    {
        try
        {
            if (_playerProcess != null && !_playerProcess.HasExited)
            {
                if (!_playerProcess.WaitForExit(1000))
                {
                    _playerProcess.Kill(true);
                }
                _playerProcess.Dispose();
                _playerProcess = null;
            }

            if (File.Exists(_pidFilePath))
            {
                var content = File.ReadAllText(_pidFilePath).Trim();
                if (int.TryParse(content, out var pid))
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
                File.Delete(_pidFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to cleanly terminate existing player process: {ex.Message}");
        }

        return Task.CompletedTask;
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
        _playerStdin?.Dispose();
        _playerStdout?.Dispose();
    }
}
