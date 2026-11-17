using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Media;
using LockscreenGif.Features.DesktopWallpaper.Services.Native;
using LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

namespace LockscreenGif.Features.DesktopWallpaper.Player;

/// <summary>
/// Standalone, isolated wallpaper player process.
/// Owns the video window, hardware-accelerated decoding/rendering, desktop WorkerW attachment,
/// and responds to IPC commands over redirected stdin/stdout or named pipes.
/// Runs on a dedicated Single-Threaded Apartment (STA) thread with a native Win32 message loop
/// to ensure WebView2 (Chromium) COM compatibility.
/// </summary>
public static class WallpaperPlayerProcess
{
    private static readonly object _stateLock = new();
    private static readonly List<WallpaperHostWindow> _hostWindows = [];
    private static readonly List<IWallpaperRenderer> _renderers = [];

    private static string? _mediaPath;
    private static WallpaperType _wallpaperType = WallpaperType.Video;
    private static WallpaperLayoutMode _layoutMode = WallpaperLayoutMode.SpanAllMonitors;
    private static WallpaperFitMode _fitMode = WallpaperFitMode.Fill;
    private static double _volume = 0.5;
    private static bool _isMuted = true;
    private static bool _loop = true;
    private static double _playbackSpeed = 1.0;

    private static bool _isPlaying;
    private static bool _isPaused;
    private static bool _isAutoPaused;
    private static bool _isExiting;
    private static string? _originalWallpaperPath;
    private static uint _msgThreadId;
    private static nint _msgHwnd;
    private static Timer? _perfRuleTimer;
    private static StaSynchronizationContext? _syncContext;

    // Retain static delegate reference to prevent garbage collection crash in Win32 wndproc
    private static readonly DesktopNativeMethods.WndProc _wndProcDelegate = MessageWindowProc;

    public const string NamedPipeName = "LockscreenGif_WallpaperPlayer_Pipe";
    private static readonly string PidFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "wallpaper_player.pid"
    );

    /// <summary>
    /// Custom SynchronizationContext that marshals all async continuations back onto the STA message loop thread.
    /// This is strictly required by Microsoft Edge WebView2 COM interfaces.
    /// </summary>
    internal sealed class StaSynchronizationContext : SynchronizationContext
    {
        public const uint WM_DISPATCH = 0x8000 + 42; // WM_APP + 42
        private readonly nint _hwnd;
        private readonly int _staThreadId;
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public StaSynchronizationContext(nint hwnd)
        {
            _hwnd = hwnd;
            _staThreadId = Environment.CurrentManagedThreadId;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            if (_hwnd != 0)
            {
                DesktopNativeMethods.PostMessage(_hwnd, WM_DISPATCH, 0, 0);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == _staThreadId)
            {
                d(state);
                return;
            }

            using var mres = new ManualResetEventSlim(false);
            Exception? caught = null;

            Post(
                _ =>
                {
                    try
                    {
                        d(state);
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }
                    finally
                    {
                        mres.Set();
                    }
                },
                null
            );

            mres.Wait();

            if (caught != null)
            {
                throw new TargetInvocationException(caught);
            }
        }

        public Task InvokeAsync(Func<Task> asyncFunc)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        await asyncFunc();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                null
            );
            return tcs.Task;
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR in STA dispatch: {ex.Message}");
                    Console.Out.Flush();
                }
            }
        }
    }

    /// <summary>
    /// Ensures the calling thread is attached to the interactive WinSta0\Default desktop.
    /// </summary>
    private static void EnsureDefaultDesktop()
    {
        try
        {
            var hDesk = DesktopNativeMethods.OpenDesktop("Default", 0, false, 0x01FF);
            if (hDesk != 0)
            {
                DesktopNativeMethods.SetThreadDesktop(hDesk);
            }
        }
        catch { }
    }

    /// <summary>
    /// Entry point for the isolated player process (--wallpaper-player).
    /// </summary>
    public static void Run(string[] args)
    {
        try
        {
            EnsureDefaultDesktop();

            _msgThreadId = DesktopNativeMethods.GetCurrentThreadId();
            _originalWallpaperPath = DesktopAttachHelper.GetCurrentWallpaperPath();
            WritePidFile();

            ParseArguments(args);

            // Register message window for Explorer and Display topology events
            CreateMessageWindow();

            // Install STA synchronization context so all async continuations remain on the STA thread
            _syncContext = new StaSynchronizationContext(_msgHwnd);
            SynchronizationContext.SetSynchronizationContext(_syncContext);

            // Setup input listener on stdin
            _ = Task.Run(ListenStdinAsync);

            // Setup named pipe server listener
            _ = Task.Run(ListenNamedPipeAsync);

            // Setup auto-pause rule monitor (every 1.5 seconds)
            _perfRuleTimer = new Timer(EvaluatePerformanceRules, null, 1500, 1500);

            // If an initial media path was supplied in args, post load immediately to the STA thread
            if (!string.IsNullOrWhiteSpace(_mediaPath))
            {
                _syncContext.Post(
                    async _ =>
                    {
                        await LoadMediaAsync(_mediaPath);
                    },
                    null
                );
            }

            // Enter Win32 message pump
            RunMessageLoop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR FATAL: {ex.Message}");
            Console.Out.Flush();
        }
        finally
        {
            Cleanup();
        }
    }

    private static void ParseArguments(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (i + 1 < args.Length)
            {
                if (string.Equals(arg, "--path", StringComparison.OrdinalIgnoreCase))
                {
                    _mediaPath = args[++i];
                }
                else if (string.Equals(arg, "--layout", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<WallpaperLayoutMode>(args[++i], true, out var layout))
                    {
                        _layoutMode = layout;
                    }
                }
                else if (string.Equals(arg, "--volume", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var vol))
                    {
                        _volume = Math.Clamp(vol, 0.0, 1.0);
                    }
                }
                else if (string.Equals(arg, "--muted", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(args[++i], out var muted))
                    {
                        _isMuted = muted;
                    }
                }
                else if (string.Equals(arg, "--loop", StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(args[++i], out var loop))
                    {
                        _loop = loop;
                    }
                }
                else if (string.Equals(arg, "--fit", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<WallpaperFitMode>(args[++i], true, out var fit))
                    {
                        _fitMode = fit;
                    }
                }
                else if (string.Equals(arg, "--speed", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                    {
                        _playbackSpeed = Math.Clamp(speed, 0.25, 4.0);
                    }
                }
            }
        }
    }

    private static void WritePidFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PidFilePath)!);
            File.WriteAllText(PidFilePath, Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
            // Ignore failure writing PID file
        }
    }

    private static void DeletePidFile()
    {
        try
        {
            if (File.Exists(PidFilePath))
            {
                File.Delete(PidFilePath);
            }
        }
        catch
        {
            // Ignore
        }
    }

    private static async Task LoadMediaAsync(string path)
    {
        lock (_stateLock)
        {
            if (_isExiting)
            {
                return;
            }
        }

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("ERROR Missing media path.");
                Console.Out.Flush();
                return;
            }

            var isWeb =
                path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

            var isImage =
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);

            _wallpaperType = isWeb ? WallpaperType.Web : (isImage ? WallpaperType.Image : WallpaperType.Video);
            _mediaPath = path;

            // Ensure desktop host is located and initialized
            var hostTarget = DesktopAttachHelper.FindDesktopHost();
            if (hostTarget == null || hostTarget.ParentHwnd == 0)
            {
                Console.WriteLine("ERROR Unable to find or spawn WorkerW desktop host layer.");
                Console.Out.Flush();
                return;
            }

            // Tear down existing renderers
            await CleanRenderersAsync();
            CleanHostWindows();

            // Re-enumerate monitors
            var monitors = DesktopAttachHelper.GetMonitors();

            if (_layoutMode == WallpaperLayoutMode.SpanAllMonitors || monitors.Count == 0)
            {
                var (left, top, width, height) = DesktopAttachHelper.GetVirtualScreenBounds();
                var hostWin = new WallpaperHostWindow();
                if (!hostWin.Create(left, top, width, height))
                {
                    Console.WriteLine("ERROR Failed to create host window.");
                    Console.Out.Flush();
                    return;
                }
                _hostWindows.Add(hostWin);
            }
            else // Duplicate or Single
            {
                foreach (var monitor in monitors)
                {
                    var hostWin = new WallpaperHostWindow();
                    if (hostWin.Create(monitor.Left, monitor.Top, monitor.Width, monitor.Height))
                    {
                        _hostWindows.Add(hostWin);
                    }
                    if (_layoutMode == WallpaperLayoutMode.SingleMonitor)
                    {
                        break;
                    }
                }
            }

            var item = new WallpaperItem
            {
                SourcePathOrUrl = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Type = _wallpaperType,
                FitMode = _fitMode,
                Volume = _volume,
                IsMuted = _isMuted,
                Loop = _loop,
                PlaybackSpeed = _playbackSpeed,
            };

            for (var i = 0; i < _hostWindows.Count; i++)
            {
                var hostWin = _hostWindows[i];
                DesktopNativeMethods.GetClientRect(hostWin.Hwnd, out var rc);
                var width = rc.Right - rc.Left;
                var height = rc.Bottom - rc.Top;

                var renderer = new WebView2WallpaperRenderer();
                await renderer.InitializeAsync(hostWin.Hwnd, 0, 0, width, height);
                await renderer.LoadAsync(item);
                await renderer.SetFitModeAsync(_fitMode);
                await renderer.SetVolumeAsync(_volume);
                await renderer.SetMutedAsync(_isMuted);
                await renderer.SetPlaybackRateAsync(_playbackSpeed);

                _renderers.Add(renderer);

                // Dock into desktop layer behind icons
                if (_layoutMode == WallpaperLayoutMode.SpanAllMonitors || monitors.Count == 0)
                {
                    var (left, top, spanW, spanH) = DesktopAttachHelper.GetVirtualScreenBounds();
                    hostWin.AttachToDesktop(hostTarget, left, top, spanW, spanH);
                }
                else
                {
                    var monitor = monitors[Math.Min(i, monitors.Count - 1)];
                    hostWin.AttachToDesktop(hostTarget, monitor.Left, monitor.Top, monitor.Width, monitor.Height);
                }
            }

            _isPlaying = true;
            _isPaused = false;
            _isAutoPaused = false;

            Console.WriteLine("READY");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR Failed to load media: {ex}");
            Console.Out.Flush();
        }
    }

    private static async Task ProcessCommandAsync(string commandLine, TextWriter responseWriter)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        var parts = commandLine.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var verb = parts[0].ToUpperInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (verb)
        {
            case "LOAD":
                await LoadMediaAsync(arg);
                await responseWriter.WriteLineAsync("OK LOAD");
                break;

            case "PAUSE":
                await PauseAsync();
                await responseWriter.WriteLineAsync("OK PAUSE");
                break;

            case "RESUME":
                await ResumeAsync();
                await responseWriter.WriteLineAsync("OK RESUME");
                break;

            case "VOLUME":
                if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol))
                {
                    _volume = Math.Clamp(vol > 1.0 ? vol / 100.0 : vol, 0.0, 1.0);
                    foreach (var r in _renderers)
                    {
                        await r.SetVolumeAsync(_volume);
                    }
                    await responseWriter.WriteLineAsync("OK VOLUME");
                }
                else
                {
                    await responseWriter.WriteLineAsync("ERROR Invalid volume parameter");
                }
                break;

            case "MUTE":
                if (bool.TryParse(arg, out var muted))
                {
                    _isMuted = muted;
                    foreach (var r in _renderers)
                    {
                        await r.SetMutedAsync(_isMuted);
                    }
                    await responseWriter.WriteLineAsync("OK MUTE");
                }
                else
                {
                    await responseWriter.WriteLineAsync("ERROR Invalid mute parameter");
                }
                break;

            case "LOOP":
                if (bool.TryParse(arg, out var loop))
                {
                    _loop = loop;
                    await responseWriter.WriteLineAsync("OK LOOP");
                }
                break;

            case "FIT":
                if (Enum.TryParse<WallpaperFitMode>(arg, true, out var fit))
                {
                    _fitMode = fit;
                    foreach (var r in _renderers)
                    {
                        await r.SetFitModeAsync(_fitMode);
                    }
                    await responseWriter.WriteLineAsync("OK FIT");
                }
                break;

            case "SPEED":
                if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                {
                    _playbackSpeed = Math.Clamp(speed, 0.25, 4.0);
                    foreach (var r in _renderers)
                    {
                        await r.SetPlaybackRateAsync(_playbackSpeed);
                    }
                    await responseWriter.WriteLineAsync("OK SPEED");
                }
                break;

            case "WEB_MSG":
                foreach (var r in _renderers)
                {
                    if (r is WebView2WallpaperRenderer webRenderer)
                    {
                        await webRenderer.PostWebMessageAsJsonAsync(arg);
                    }
                }
                await responseWriter.WriteLineAsync("OK WEB_MSG");
                break;

            case "STATUS":
                var status = _isPlaying ? (_isPaused || _isAutoPaused ? "PAUSED" : "PLAYING") : "STOPPED";
                await responseWriter.WriteLineAsync($"STATUS {status} {_mediaPath ?? ""}");
                break;

            case "PING":
                await responseWriter.WriteLineAsync("PONG");
                break;

            case "CLOSE":
                await responseWriter.WriteLineAsync("OK CLOSE");
                await responseWriter.FlushAsync();
                RequestExit();
                break;

            default:
                await responseWriter.WriteLineAsync($"ERROR Unknown command: {verb}");
                break;
        }

        await responseWriter.FlushAsync();
    }

    private static async Task PauseAsync()
    {
        _isPaused = true;
        foreach (var r in _renderers)
        {
            await r.PauseAsync();
        }
    }

    private static async Task ResumeAsync()
    {
        _isPaused = false;
        foreach (var r in _renderers)
        {
            await r.ResumeAsync();
        }
    }

    private static async Task ListenStdinAsync()
    {
        try
        {
            using var reader = Console.In;
            while (!_isExiting)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    // Stdin closed. Wallpaper keeps playing.
                    break;
                }
                if (_syncContext != null)
                {
                    await _syncContext.InvokeAsync(async () =>
                    {
                        await ProcessCommandAsync(line, Console.Out);
                    });
                }
            }
        }
        catch
        {
            // Ignore stdin read errors
        }
    }

    private static async Task ListenNamedPipeAsync()
    {
        while (!_isExiting)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    NamedPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                await pipe.WaitForConnectionAsync();

                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

                while (pipe.IsConnected && !_isExiting)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }
                    if (_syncContext != null)
                    {
                        await _syncContext.InvokeAsync(async () =>
                        {
                            await ProcessCommandAsync(line, writer);
                        });
                    }
                }
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    private static void EvaluatePerformanceRules(object? state)
    {
        if (!_isPlaying || _isPaused || _isExiting)
        {
            return;
        }

        var shouldPause = false;

        // 1. RDP Remote Desktop Session
        if (DesktopNativeMethods.GetSystemMetrics(DesktopNativeMethods.SM_REMOTESESSION) != 0)
        {
            shouldPause = true;
        }

        // 2. Battery / Power Saver
        if (!shouldPause && DesktopNativeMethods.GetSystemPowerStatus(out var powerStatus))
        {
            if (powerStatus.SystemStatusFlag == 1) // Battery Saver
            {
                shouldPause = true;
            }
        }

        // 3. Fullscreen Application or Game
        if (!shouldPause && IsForegroundAppFullscreen())
        {
            shouldPause = true;
        }

        if (shouldPause && !_isAutoPaused)
        {
            _isAutoPaused = true;
            _syncContext?.Post(
                async _ =>
                {
                    foreach (var r in _renderers)
                    {
                        await r.PauseAsync();
                    }
                },
                null
            );
        }
        else if (!shouldPause && _isAutoPaused)
        {
            _isAutoPaused = false;
            _syncContext?.Post(
                async _ =>
                {
                    foreach (var r in _renderers)
                    {
                        await r.ResumeAsync();
                    }
                },
                null
            );
        }
    }

    private static bool IsForegroundAppFullscreen()
    {
        var fgWnd = DesktopNativeMethods.GetForegroundWindow();
        if (fgWnd == 0)
        {
            return false;
        }

        DesktopNativeMethods.GetWindowThreadProcessId(fgWnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (!DesktopNativeMethods.GetWindowRect(fgWnd, out var rcWin))
        {
            return false;
        }

        var hMon = DesktopNativeMethods.MonitorFromWindow(
            fgWnd,
            2 /* MONITOR_DEFAULTTONEAREST */
        );
        if (hMon == 0)
        {
            return false;
        }

        var mi = new DesktopNativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<DesktopNativeMethods.MONITORINFOEX>() };
        if (DesktopNativeMethods.GetMonitorInfo(hMon, ref mi))
        {
            var isCovering =
                rcWin.Left <= mi.rcMonitor.Left
                && rcWin.Top <= mi.rcMonitor.Top
                && rcWin.Right >= mi.rcMonitor.Right
                && rcWin.Bottom >= mi.rcMonitor.Bottom;

            if (isCovering)
            {
                var style = (long)DesktopNativeMethods.GetWindowLongPtr(fgWnd, DesktopNativeMethods.GWL_STYLE);
                var isBorderless =
                    (
                        style & 0x00C00000 /* WS_CAPTION */
                    ) != 0x00C00000;
                return isBorderless;
            }
        }

        return false;
    }

    private static void CreateMessageWindow()
    {
        var className = "LockscreenGif_PlayerMsgWnd_" + Guid.NewGuid().ToString("N")[..8];
        var hInstance = DesktopNativeMethods.GetModuleHandle(null);

        var wndClass = new DesktopNativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(DesktopNativeMethods.WNDCLASSEX)),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className,
        };

        DesktopNativeMethods.RegisterClassEx(ref wndClass);

        _msgHwnd = DesktopNativeMethods.CreateWindowEx(
            0,
            className,
            "LockscreenGif Player Message Receiver",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            hInstance,
            0
        );
    }

    private static readonly uint _wmTaskbarCreated = DesktopNativeMethods.RegisterWindowMessage("TaskbarCreated");

    private static nint MessageWindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == StaSynchronizationContext.WM_DISPATCH)
        {
            _syncContext?.Drain();
            return 0;
        }

        if (msg == _wmTaskbarCreated && _wmTaskbarCreated != 0)
        {
            // Explorer restarted: Re-attach to new WorkerW on the STA thread
            _syncContext?.Post(
                async _ =>
                {
                    await Task.Delay(1000);
                    if (!string.IsNullOrWhiteSpace(_mediaPath))
                    {
                        await LoadMediaAsync(_mediaPath);
                    }
                },
                null
            );
            return 0;
        }

        if (
            msg == 0x007E /* WM_DISPLAYCHANGE */
            || msg == 0x02E0 /* WM_DPICHANGED */
        )
        {
            // Display configuration changed: Refresh layout on the STA thread
            _syncContext?.Post(
                async _ =>
                {
                    await Task.Delay(500);
                    if (!string.IsNullOrWhiteSpace(_mediaPath))
                    {
                        await LoadMediaAsync(_mediaPath);
                    }
                },
                null
            );
            return 0;
        }

        return DesktopNativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void RunMessageLoop()
    {
        while (!_isExiting && DesktopNativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            DesktopNativeMethods.TranslateMessage(ref msg);
            DesktopNativeMethods.DispatchMessage(ref msg);
        }
    }

    private static void RequestExit()
    {
        lock (_stateLock)
        {
            _isExiting = true;
        }

        if (_msgThreadId != 0)
        {
            DesktopNativeMethods.PostThreadMessage(
                _msgThreadId,
                0x0012 /* WM_QUIT */
                ,
                0,
                0
            );
        }
    }

    private static async Task CleanRenderersAsync()
    {
        foreach (var r in _renderers)
        {
            try
            {
                await r.CloseAsync();
                await r.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
        }
        _renderers.Clear();
    }

    private static void CleanHostWindows()
    {
        foreach (var w in _hostWindows)
        {
            w.Dispose();
        }
        _hostWindows.Clear();
    }

    private static void Cleanup()
    {
        _perfRuleTimer?.Dispose();
        _perfRuleTimer = null;

        CleanRenderersAsync().GetAwaiter().GetResult();
        CleanHostWindows();

        if (_originalWallpaperPath != null)
        {
            DesktopAttachHelper.RestoreWallpaper(_originalWallpaperPath);
        }

        DeletePidFile();

        if (_msgHwnd != 0)
        {
            DesktopNativeMethods.DestroyWindow(_msgHwnd);
            _msgHwnd = 0;
        }
    }
}
