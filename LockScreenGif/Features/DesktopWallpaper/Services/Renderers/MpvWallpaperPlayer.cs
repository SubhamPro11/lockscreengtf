using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

/// <summary>
/// Optional second wallpaper engine for files WebView2 cannot play well (AVI, WMV, FLV, exotic MKV).
/// Runs a separate, unmodified mpv.exe that renders into the existing wallpaper host window (--wid) and is
/// controlled over mpv's JSON IPC named pipe. Original code, no Lively source.
/// Desktop Wallpaper only. Never touches the lockscreen system.
/// </summary>
public sealed class MpvWallpaperPlayer : IWallpaperRenderer
{
    private readonly string _mpvExe;
    private readonly string _pipeName = "lsgif-mpv-" + Guid.NewGuid().ToString("N");
    private Process? _process;
    private bool _stopping;
    private nint _hostHwnd;
    private bool _isPlaying;

    /// <summary>Raised if mpv exits on its own (crash, killed). Not raised for a requested stop.</summary>
    public event Action? Exited;

    public MpvWallpaperPlayer(string? mpvExePath = null) => _mpvExe = mpvExePath ?? DefaultPath;

    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, "Wallpaper", "mpv", "mpv.exe");

    public static bool IsAvailable(string? mpvExePath = null) => File.Exists(mpvExePath ?? DefaultPath);

    public bool HasExited => _process is null || _process.HasExited;
    public bool IsPlaying => _isPlaying && !HasExited;
    public nint ContentHwnd => _hostHwnd;

    public Task InitializeAsync(nint hostHwnd, int left, int top, int width, int height)
    {
        _hostHwnd = hostHwnd;
        return Task.CompletedTask;
    }

    public async Task LoadAsync(WallpaperItem item)
    {
        if (_hostHwnd == 0)
        {
            throw new InvalidOperationException("Host window handle is not initialized.");
        }

        await StartAsync(
            item.SourcePathOrUrl,
            _hostHwnd,
            fill: item.FitMode == WallpaperFitMode.Fill,
            audio: !item.IsMuted,
            volume: (int)Math.Round(item.Volume * 100)
        );
        await SetPlaybackRateAsync(item.PlaybackSpeed);
    }

    public Task SetBoundsAsync(int left, int top, int width, int height) => Task.CompletedTask;

    /// <summary>Starts playback inside <paramref name="hostHwnd"/> (your already-attached wallpaper host window).</summary>
    public async Task StartAsync(string file, nint hostHwnd, bool fill, bool audio, int volume = 50, CancellationToken ct = default)
    {
        if (_process is not null)
        {
            throw new InvalidOperationException("Player already started.");
        }
        if (!File.Exists(_mpvExe))
        {
            throw new FileNotFoundException("mpv.exe was not found.", _mpvExe);
        }
        if (!File.Exists(file))
        {
            throw new FileNotFoundException("The wallpaper file was not found.", file);
        }

        _hostHwnd = hostHwnd;
        _stopping = false;

        var psi = new ProcessStartInfo(_mpvExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_mpvExe)!,
        };

        void Arg(string a) => psi.ArgumentList.Add(a);

        Arg("--no-config"); // ignore any user mpv.conf
        Arg($"--wid={hostHwnd.ToInt64()}"); // render inside our wallpaper window
        Arg(@"--input-ipc-server=\\.\pipe\" + _pipeName);
        Arg("--loop-file=inf");
        Arg("--keep-open=yes");
        Arg("--hwdec=auto-safe"); // GPU decoding when safe
        Arg("--no-terminal");
        Arg("--really-quiet");
        Arg("--osc=no");
        Arg("--osd-level=0");
        Arg("--no-input-default-bindings");
        Arg("--input-vo-keyboard=no");
        Arg("--input-cursor=no");
        Arg("--cursor-autohide=no");

        if (fill)
        {
            Arg("--panscan=1.0"); // crop to fill instead of letterbox
        }

        if (audio)
        {
            Arg($"--volume={Math.Clamp(volume, 0, 100)}");
        }
        else
        {
            Arg("--aid=no");
        }

        Arg("--");
        Arg(file);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            _isPlaying = false;
            if (!_stopping)
            {
                Exited?.Invoke();
            }
        };

        process.Start();
        _process = process;

        // If this app's wallpaper process dies, Windows kills mpv too (no orphan mpv.exe left running).
        KillOnCloseJob.Add(process);

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            /* ignore */
        }

        await WaitUntilReadyAsync(ct);
        _isPlaying = true;
    }

    public async Task PauseAsync()
    {
        _isPlaying = false;
        await SendAsync("{\"command\":[\"set_property\",\"pause\",true]}");
    }

    public async Task ResumeAsync()
    {
        await SendAsync("{\"command\":[\"set_property\",\"pause\",false]}");
        _isPlaying = !HasExited;
    }

    public Task SetVolumeAsync(double volume) =>
        SendAsync(
            $"{{\"command\":[\"set_property\",\"volume\",{Math.Clamp((int)Math.Round(volume * 100), 0, 100).ToString(CultureInfo.InvariantCulture)}]}}"
        );

    public Task SetMutedAsync(bool muted) =>
        SendAsync(muted ? "{\"command\":[\"set_property\",\"aid\",\"no\"]}" : "{\"command\":[\"set_property\",\"aid\",\"auto\"]}");

    public Task SetPlaybackRateAsync(double rate) =>
        SendAsync(
            $"{{\"command\":[\"set_property\",\"speed\",{Math.Clamp(rate, 0.25, 4.0).ToString("F2", CultureInfo.InvariantCulture)}]}}"
        );

    public Task SetFitModeAsync(WallpaperFitMode fitMode) =>
        SendAsync($"{{\"command\":[\"set_property\",\"panscan\",{(fitMode == WallpaperFitMode.Fill ? "1.0" : "0.0")}]}}");

    public Task PostWebMessageAsJsonAsync(string json) => Task.CompletedTask;

    public async Task StopAsync()
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        _stopping = true;
        _isPlaying = false;
        _process = null;

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    await SendInternalAsync(process, "{\"command\":[\"quit\"]}", CancellationToken.None);
                }
                catch
                {
                    /* pipe gone */
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }
        catch (InvalidOperationException)
        {
            /* already exited */
        }
        finally
        {
            try
            {
                process.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    public Task CloseAsync() => StopAsync();

    public async ValueTask DisposeAsync() => await StopAsync();

    // ---------------------------------------------------------------- internals

    private async Task WaitUntilReadyAsync(CancellationToken ct)
    {
        // mpv opens its IPC pipe once it has started. Wait for it, but fail fast if mpv dies.
        var deadline = DateTime.UtcNow.AddSeconds(8);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_process is null || _process.HasExited)
            {
                throw new InvalidOperationException("mpv exited during startup. The file may be unsupported.");
            }

            try
            {
                await using var probe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await probe.ConnectAsync(250, ct);
                return;
            }
            catch (TimeoutException)
            {
                /* not ready yet */
            }
            catch (IOException)
            {
                /* not ready yet */
            }
        }

        await StopAsync();
        throw new TimeoutException("Timed out waiting for mpv to start.");
    }

    private Task SendAsync(string json, CancellationToken ct = default)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            return Task.CompletedTask;
        }
        return SendInternalAsync(process, json, ct);
    }

    private async Task SendInternalAsync(Process process, string json, CancellationToken ct)
    {
        if (process.HasExited)
        {
            return;
        }

        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1000, ct);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), ct);
        await pipe.FlushAsync(ct);
    }
}

/// <summary>Windows job object with KILL_ON_JOB_CLOSE: child processes die when this process dies.</summary>
internal static class KillOnCloseJob
{
    private static readonly nint Job = Create();

    public static void Add(Process process)
    {
        if (Job != 0)
        {
            AssignProcessToJobObject(Job, process.Handle);
        }
    }

    private static nint Create()
    {
        var job = CreateJobObject(0, null);
        if (job == 0)
        {
            return 0;
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(
                job,
                9 /* JobObjectExtendedLimitInformation */
                ,
                ptr,
                (uint)size
            );
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return job; // handle stays open for the life of the process
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint size);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
}

/// <summary>Picks which engine plays a file. WebView2 stays the default; mpv covers what it cannot.</summary>
public static class WallpaperEngineSelector
{
    private static readonly HashSet<string> MpvOnlyExtensions = new(StringComparer.OrdinalIgnoreCase) { ".avi", ".wmv", ".flv" };

    public static bool ShouldUseMpv(string file, bool preferMpv, bool webViewFailed, string? mpvExePath = null)
    {
        if (!MpvWallpaperPlayer.IsAvailable(mpvExePath))
        {
            return false; // mpv not installed: keep current behavior
        }

        return preferMpv || webViewFailed || MpvOnlyExtensions.Contains(Path.GetExtension(file));
    }
}
