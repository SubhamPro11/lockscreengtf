using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LockscreenGif.Features.DesktopWallpaper.Models;
using LockscreenGif.Features.DesktopWallpaper.Services.Media;
using Microsoft.Web.WebView2.Core;

namespace LockscreenGif.Features.DesktopWallpaper.Services.Renderers;

/// <summary>
/// Renderer using Microsoft Edge WebView2 (Chromium) for high-performance rendering of
/// web wallpapers, WebGL/shaders, HTML5 videos (MP4, WebM, animated WebP/GIF), and images.
/// </summary>
public sealed class WebView2WallpaperRenderer : IWallpaperRenderer
{
    private static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LockscreenGif",
        "WallpaperWebView2Data"
    );

    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private nint _hostHwnd;
    private bool _isPlaying;
    private WallpaperItem? _currentItem;
    private bool _isDisposed;

    public bool IsPlaying => _isPlaying;
    public nint ContentHwnd => _hostHwnd;

    public async Task InitializeAsync(nint hostHwnd, int left, int top, int width, int height)
    {
        _hostHwnd = hostHwnd;

        Directory.CreateDirectory(UserDataFolder);

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding"
        };

        _environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            UserDataFolder,
            options
        );

        var winRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hostHwnd);
        _controller = await _environment.CreateCoreWebView2ControllerAsync(winRef);
        _controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        _controller.Bounds = new Windows.Foundation.Rect(left, top, width, height);
        _controller.IsVisible = true;

        var settings = _controller.CoreWebView2.Settings;
        settings.IsScriptEnabled = true;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreDevToolsEnabled = false;
    }

    public async Task LoadAsync(WallpaperItem item)
    {
        if (_controller == null)
        {
            throw new InvalidOperationException("WebView2 controller not initialized.");
        }

        _currentItem = item;
        var source = item.SourcePathOrUrl;

        if (item.Type == WallpaperType.Web)
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _controller.CoreWebView2.Navigate(source);
            }
            else if (File.Exists(source))
            {
                var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? string.Empty;
                var fileName = Path.GetFileName(source);

                _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "wallpaper.local",
                    folder,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                _controller.CoreWebView2.Navigate($"https://wallpaper.local/{fileName}");
            }
            else
            {
                throw new FileNotFoundException($"Web wallpaper file not found: {source}");
            }
        }
        else if (item.Type == WallpaperType.Image)
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Image wallpaper file not found: {source}");
            }

            var folder = Path.GetDirectoryName(Path.GetFullPath(source)) ?? string.Empty;
            var fileName = Path.GetFileName(source);

            _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "image.local",
                folder,
                CoreWebView2HostResourceAccessKind.Allow
            );

            var fitCss = GetFitCss(item.FitMode);
            var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                * { margin: 0; padding: 0; box-sizing: border-box; overflow: hidden; }
                html, body { width: 100%; height: 100%; background: #000; }
                img {
                  width: 100vw;
                  height: 100vh;
                  object-fit: {{fitCss}};
                  display: block;
                }
              </style>
            </head>
            <body>
              <img id="media" src="https://image.local/{{fileName}}" alt="Wallpaper" />
              <script>
                window.setFitMode = function(mode) {
                  const media = document.getElementById('media');
                  if (!media) return;
                  if (mode === 'Fill') media.style.objectFit = 'cover';
                  else if (mode === 'Fit') media.style.objectFit = 'contain';
                  else if (mode === 'Stretch') media.style.objectFit = 'fill';
                  else media.style.objectFit = 'none';
                };
              </script>
            </body>
            </html>
            """;

            _controller.CoreWebView2.NavigateToString(html);
        }
        else // WallpaperType.Video (mp4, webm, mkv, avi, mov, gif, animated webp)
        {
            var playbackSource = await WallpaperMediaHelper.PrepareMediaForPlaybackAsync(source);
            if (!File.Exists(playbackSource))
            {
                throw new FileNotFoundException($"Media file not found: {playbackSource}");
            }

            var folder = Path.GetDirectoryName(Path.GetFullPath(playbackSource)) ?? string.Empty;
            var fileName = Path.GetFileName(playbackSource);

            _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "video.local",
                folder,
                CoreWebView2HostResourceAccessKind.Allow
            );

            var fitCss = GetFitCss(item.FitMode);
            var isMutedAttr = item.IsMuted ? "muted" : string.Empty;
            var isLoopAttr = item.Loop ? "loop" : string.Empty;
            var volumeVal = item.Volume.ToString("F2", CultureInfo.InvariantCulture);
            var speedVal = item.PlaybackSpeed.ToString("F2", CultureInfo.InvariantCulture);

            var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                * { margin: 0; padding: 0; box-sizing: border-box; overflow: hidden; }
                html, body { width: 100%; height: 100%; background: #000; }
                video {
                  width: 100vw;
                  height: 100vh;
                  object-fit: {{fitCss}};
                  display: block;
                }
              </style>
            </head>
            <body>
              <video id="media" src="https://video.local/{{fileName}}" autoplay {{isLoopAttr}} {{isMutedAttr}} playsinline></video>
              <script>
                const media = document.getElementById('media');
                if (media) {
                  media.volume = {{volumeVal}};
                  media.playbackRate = {{speedVal}};
                }

                window.setVolume = function(v) { if (media) media.volume = v; };
                window.setMuted = function(m) { if (media) media.muted = m; };
                window.setPlaybackRate = function(r) { if (media) media.playbackRate = r; };
                window.setFitMode = function(mode) {
                  if (!media) return;
                  if (mode === 'Fill') media.style.objectFit = 'cover';
                  else if (mode === 'Fit') media.style.objectFit = 'contain';
                  else if (mode === 'Stretch') media.style.objectFit = 'fill';
                  else media.style.objectFit = 'none';
                };
                window.playMedia = function() { if (media && media.play) media.play(); };
                window.pauseMedia = function() { if (media && media.pause) media.pause(); };
              </script>
            </body>
            </html>
            """;

            _controller.CoreWebView2.NavigateToString(html);
        }

        _isPlaying = true;
    }

    public Task SetBoundsAsync(int left, int top, int width, int height)
    {
        if (_controller != null)
        {
            _controller.Bounds = new Windows.Foundation.Rect(left, top, width, height);
        }
        return Task.CompletedTask;
    }

    public async Task PauseAsync()
    {
        if (_controller != null)
        {
            _isPlaying = false;
            try
            {
                await _controller.CoreWebView2.ExecuteScriptAsync("if (window.pauseMedia) { window.pauseMedia(); }");
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task ResumeAsync()
    {
        if (_controller != null)
        {
            _isPlaying = true;
            try
            {
                await _controller.CoreWebView2.ExecuteScriptAsync("if (window.playMedia) { window.playMedia(); }");
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task SetVolumeAsync(double volume)
    {
        if (_controller != null)
        {
            var clamped = Math.Clamp(volume, 0.0, 1.0);
            try
            {
                var script = $"if (window.setVolume) {{ window.setVolume({clamped.ToString(CultureInfo.InvariantCulture)}); }}";
                await _controller.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task SetMutedAsync(bool muted)
    {
        if (_controller != null)
        {
            try
            {
                var script = $"if (window.setMuted) {{ window.setMuted({(muted ? "true" : "false")}); }}";
                await _controller.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task SetPlaybackRateAsync(double rate)
    {
        if (_controller != null)
        {
            var clamped = Math.Clamp(rate, 0.25, 4.0);
            try
            {
                var script = $"if (window.setPlaybackRate) {{ window.setPlaybackRate({clamped.ToString(CultureInfo.InvariantCulture)}); }}";
                await _controller.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public async Task SetFitModeAsync(WallpaperFitMode fitMode)
    {
        if (_controller != null)
        {
            try
            {
                var script = $"if (window.setFitMode) {{ window.setFitMode(\"{fitMode}\"); }}";
                await _controller.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public Task PostWebMessageAsJsonAsync(string json)
    {
        if (_controller != null)
        {
            try
            {
                _controller.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch
            {
                // Best effort
            }
        }
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        _isPlaying = false;
        if (_controller != null)
        {
            _controller.Close();
            _controller = null;
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        await CloseAsync();
    }

    private static string GetFitCss(WallpaperFitMode mode) => mode switch
    {
        WallpaperFitMode.Fit => "contain",
        WallpaperFitMode.Stretch => "fill",
        WallpaperFitMode.Center => "none",
        _ => "cover"
    };
}
