# Desktop Wallpaper Feature Specification & Developer Guide

LockscreenGif now includes an isolated, high-performance Desktop Wallpaper engine supporting live animated videos, interactive web applications (HTML5/Canvas/WebGL), static images, and application wallpapers running smoothly behind Windows 11 desktop icons.

---

## 1. Architecture & Windows 11 Compatibility

The engine renders wallpaper surfaces behind the desktop icon listview (`SHELLDLL_DefView`) by creating a dedicated Win32 child window (`WallpaperHostWindow`) and parenting it to the Windows desktop window hierarchy:

- **Windows 10 / Windows 11 (pre-24H2)**: Spawns the classic `WorkerW` sibling window using the documented `0x052C` message sent to `Progman`, placing the wallpaper surface behind `SHELLDLL_DefView`.
- **Windows 11 24H2+**: Detects when `SHELLDLL_DefView` is hosted directly inside `Progman` without a secondary `WorkerW`. The engine attaches the host window as a child of `Progman` and sets its Z-order to `HWND_BOTTOM`, keeping icons fully interactable above the wallpaper.
- **Fail-Safe Restoration**: The active system wallpaper path is read via `SystemParametersInfo(SPI_GETDESKWALLPAPER)` prior to attaching. On stopping, closing, or system shutdown, the user's original wallpaper is restored with `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`.
- **Explorer Crash & Topology Resilience**: Listens for the `TaskbarCreated` broadcast message to automatically recreate and re-attach wallpaper host surfaces if `explorer.exe` restarts. Also listens for `WM_DISPLAYCHANGE` to re-align multi-monitor bounds without requiring app restarts.

---

## 2. Supported Media Types

| Type | Formats / Extensions | Engine / Acceleration | Features |
|---|---|---|---|
| **Video & Loop** | `.mp4`, `.webm`, `.mkv`, `.avi`, `.mov`, `.gif`, `.webp` | Chromium GPU Acceleration / WebView2 | Mute, Volume, 0.5x–2.0x playback rate, Fit modes (`Fill`, `Fit`, `Stretch`, `Center`), seamless loop |
| **Web Wallpaper** | `.html`, `.htm`, HTTP/HTTPS URLs | Microsoft Edge WebView2 Evergreen | Mouse & keyboard interaction forwarding, WebGL, custom shaders, Canvas, local virtual host mapping (`wallpaper.local`) |
| **Static Image** | `.png`, `.jpg`, `.jpeg`, `.bmp` | WebView2 hardware rendering | High-DPI scaling, smooth fit modes |
| **Application** *(Advanced)* | User-chosen `.exe` | Win32 process containment (`SetParent`, Job Object) | Off by default; requires explicit security confirmation |

---

## 3. Custom Properties Format (`wallpaper.json`)

Web wallpapers can declare customizable user settings by including a `wallpaper.json` descriptor file alongside the `index.html` file in their folder.

### Example `wallpaper.json`:

```json
{
  "title": "Neon Grid Visualizer",
  "author": "Designer",
  "version": "1.0.0",
  "entry": "index.html",
  "thumbnail": "preview.png",
  "tags": ["audio", "cyberpunk", "grid"],
  "properties": [
    {
      "id": "accentColor",
      "name": "Grid Glow Color",
      "type": "color",
      "default": "#00FFAA"
    },
    {
      "id": "gridSpeed",
      "name": "Animation Speed",
      "type": "slider",
      "min": 1.0,
      "max": 10.0,
      "step": 0.5,
      "default": 3.0
    },
    {
      "id": "showStars",
      "name": "Background Starfield",
      "type": "boolean",
      "default": true
    },
    {
      "id": "themeMode",
      "name": "Color Scheme",
      "type": "choice",
      "options": ["Neon Blue", "Cyber Purple", "Retro Amber"],
      "default": "Neon Blue"
    }
  ]
}
```

### Supported Property Types:
- `slider`: Numeric slider with `min`, `max`, and `step`.
- `color`: Hex color string (`#RRGGBB`).
- `boolean`: Checkbox toggle (`true` / `false`).
- `choice`: Dropdown menu with a list of string `options`.

When a property is changed by the user in LockscreenGif, the value is transmitted into the web page in real time via WebView2 script injection or postMessage:
```javascript
window.onWallpaperPropertyChanged = function(propertyId, value) {
    if (propertyId === "accentColor") {
        document.documentElement.style.setProperty("--accent", value);
    }
};
```

---

## 4. Audio-Reactive JavaScript API (WASAPI Loopback)

When the **Audio-Reactive Spectrum** toggle is enabled, LockscreenGif captures live system audio output through Windows Core Audio WASAPI Loopback and broadcasts 16-band normalized FFT data to active web wallpapers.

### JSON Payload Schema:

Every 20 milliseconds, a JSON message is delivered to the web page via `chrome.webview.postMessage`:

```json
{
  "type": "audioSpectrum",
  "peak": 0.852,
  "bands": [
    0.12, 0.45, 0.82, 0.76, 0.61, 0.48, 0.35, 0.28,
    0.22, 0.19, 0.15, 0.12, 0.08, 0.05, 0.03, 0.01
  ]
}
```

- `peak`: Normalized peak audio volume between `0.0` (silence) and `1.0` (maximum output).
- `bands`: An array of 16 normalized floating-point values from lowest bass (band 0, ~20–60 Hz) to highest treble (band 15, ~16–20 kHz).

### Example Web Wallpaper Consumer:

```html
<!DOCTYPE html>
<html>
<head>
    <style>
        body { margin: 0; background: #0b0f19; overflow: hidden; display: flex; align-items: flex-end; justify-content: space-around; height: 100vh; }
        .bar { width: 4vw; background: linear-gradient(to top, #0078D4, #60CDFF); border-radius: 4px; transition: height 0.03s ease; }
    </style>
</head>
<body>
    <script>
        for (let i = 0; i < 16; i++) {
            const bar = document.createElement('div');
            bar.className = 'bar';
            bar.id = 'b' + i;
            bar.style.height = '10px';
            document.body.appendChild(bar);
        }

        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', function(event) {
                const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
                if (data.type === 'audioSpectrum') {
                    for (let i = 0; i < 16; i++) {
                        const height = Math.max(8, data.bands[i] * 80);
                        document.getElementById('b' + i).style.height = height + 'vh';
                    }
                }
            });
        }
    </script>
</body>
</html>
```

---

## 5. Smart Performance & Power Rules

To ensure zero impact on gaming and battery life, `WallpaperPerformanceMonitor` runs a lightweight polling loop (every 750ms) to enforce user-configurable rules:
1. **Fullscreen App / Game Focused**: Detects whether the active foreground window covers the primary monitor area, automatically pausing rendering.
2. **Desktop Occlusion**: Detects when desktop surface is completely hidden by maximized application windows.
3. **Battery & Battery Saver**: Uses Windows Power API (`GetSystemPowerStatus`) to pause wallpapers when running on battery power or in Windows Energy Saver mode.
4. **PC Locked or Display Off**: Uses native `OpenDesktop("Default")` security descriptor checks. When the PC is locked (Win+L / Winlogon active), the wallpaper immediately pauses.
5. **Frame Rate Throttling**: Restricts rendering to 60 FPS, 30 FPS, or 15 FPS for maximum energy efficiency.

---

## 6. System Tray & Startup

- **System Tray Icon**: Lives in the taskbar notification area (`Assets/app.ico`). Right-click menu provides **Pause / Resume Wallpaper**, **Next Wallpaper** (playlist skip), **Open LockscreenGif**, and **Exit**. Double-clicking restores the main window.
- **Run at Windows Startup**: Registers in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the `--autostart` flag. When triggered at login, the app initializes quietly in the background without popping open the window, restores the user's active wallpaper, and creates the tray icon.

---

## 7. Dual Sync (Desktop & Lock Screen)

Using the **Apply to Both** action in the UI, users can apply the same video or animated GIF as both their Desktop Wallpaper and their Windows 11 animated Lock Screen with one click.
- Calls `IWallpaperEngineService.ApplyWallpaperAsync` for the desktop.
- Delegates to the existing, unaltered `ILockscreenService.ApplyAsync(sourcePath, false)` pipeline for the lock screen.
- Keeps 100% feature isolation: failure on one surface never affects the other.

