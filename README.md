# LockscreenGif

[![Build & Release](https://github.com/SubhamPro11/lockscreengtf/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/SubhamPro11/lockscreengtf/actions/workflows/dotnet-desktop.yml)
[![C# formatting](https://github.com/SubhamPro11/lockscreengtf/actions/workflows/format.yml/badge.svg)](https://github.com/SubhamPro11/lockscreengtf/actions/workflows/format.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%2024H2%2B%20(x64)-0078D4.svg?logo=windows&logoColor=white)](https://github.com/SubhamPro11/lockscreengtf)
[![Runtime](https://img.shields.io/badge/.NET-10%20Desktop%20Runtime-512BD4.svg?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![Latest Release](https://img.shields.io/github/v/release/SubhamPro11/lockscreengtf?label=Release&color=success)](https://github.com/SubhamPro11/lockscreengtf/releases)

A modern Windows 11 (24H2+) desktop personalization suite that brings smooth video loops and animated GIFs to both your **Windows Lock Screen** and **Live Desktop Wallpaper**, crafted with a fluid **Liquid Glass** WinUI 3 interface.

**Maintained & developed by [SubhamPro11](https://github.com/SubhamPro11)** · **[Official Website](https://lockscreen-gtf-web.morbius.workers.dev)** · **[GitHub Releases](https://github.com/SubhamPro11/lockscreengtf/releases)**

---

## Highlights

* 🔒 **Animated Lock Screen**: Converts videos and GIFs into native Windows lock-screen cache animations. Once applied, the app exits completely with **zero ongoing CPU or RAM overhead** while the Windows Desktop Compositor plays the loop natively.
* 🖥️ **Live Desktop Wallpaper Engine**: Runs videos (`.mp4`, `.webm`, `.avi`, `.mov`, `.mkv`), GIFs, and interactive web applications (HTML5/Canvas/WebGL) directly behind Windows desktop icons using hardware-accelerated rendering.
* ⚡ **Smart Power & Performance Rules**: Automatic pause when running on battery saver, when fullscreen games or applications are active, or when windows fully cover the desktop.
* 🎵 **Audio & Music Reactivity**: Full playback volume and mute controls, paired with live WASAPI audio loopback spectrum broadcasting for audio-reactive web wallpapers.
* 🛡️ **Fail-Safe Restoration**: Automatically backs up your active Windows wallpaper before starting and restores it cleanly upon stopping, exiting, or Explorer restart.
* 💎 **Windows 11 Fluent & Liquid Glass UI**: Responsive, centered design system ($\le 1000$px) with Mica backdrop, Segoe UI Variable typography, and dynamic Light/Dark/High Contrast support.

---

## Feature Tour

### 1. Lockscreen Tab
* **Video & GIF Conversion**: Load high-resolution clips or animated GIFs.
* **Frame-Accurate Video Timeline**: Precise start/end trimming, live scrubbing, and frame rate adjustments.
* **Studio-Quality Processing**: Integrated [FFmpeg](https://ffmpeg.org/) and [Gifski](https://gif.ski/) pipeline generating color-quantized, high-fps animations tailored to your display's resolution.
* **Safe One-Time Elevation**: Transparent pre-elevation dialog before writing to the protected Windows system cache (`SystemData`) via an isolated helper service.
* **Live Smoke Test**: Trigger a 5-second simulated lock screen test directly inside the session without manually locking or logging out.
* **One-Click Restoration**: Revert back to the Windows default static lock screen anytime with full cleanup of cached animated frames.

### 2. Desktop Wallpaper Tab
* **Now Playing Header**: Instant visibility into the current wallpaper title, active engine state badge (Playing, Paused, Stopped), live CPU/RAM resource metrics, and quick control buttons (Pause/Resume, Stop).
* **Multi-Format Support**:
  * **Video**: `.mp4`, `.webm`, `.mkv`, `.avi`, `.mov`, `.gif`, `.webp` (GPU hardware decoded).
  * **Web**: `.html`, web URLs, WebGL, Canvas, and custom shaders via Microsoft Edge WebView2 Evergreen.
  * **Image**: High-resolution `.png`, `.jpg`, `.jpeg`, `.bmp` with multi-monitor scaling.
* **Layout Modes**: Span across all displays, duplicate on each monitor, or target a specific monitor with per-monitor DPI awareness.
* **Intelligent Safeguards**: Automatic recovery on `explorer.exe` crash/restart (`TaskbarCreated`) and seamless re-alignment on display topology changes (`WM_DISPLAYCHANGE`).
* **System Tray Integration**: Background tray icon with quick controls (Pause/Resume, Next, Open App, Exit).
* **Developer Guide**: See [docs/DESKTOP_WALLPAPER.md](docs/DESKTOP_WALLPAPER.md) for custom `wallpaper.json` schemas and the WASAPI audio-reactive API.

### 3. Diagnostics Tab
* **System Prerequisites**: Real-time validation of Windows build compatibility, administrative rights, Windows API status, and cache folder permissions with a calm status indicator.
* **ETW Event Tracing**: In-app Event Tracing for Windows capture to diagnose compositor behavior.
* **Sanitized Report Export**: One-click generation of sanitized diagnostic ZIP packages for issue reporting without exposing private personal information.

### 4. Settings Tab
* **Lock Screen Behavior**: Granular control over the Windows lock-screen API preference.
* **Windows Startup**: Optional toggle to launch the wallpaper engine automatically at Windows login.
* **Diagnostic Logs**: Live log viewer and one-click ZIP export for application troubleshooting.
* **Privacy & Telemetry**: Transparent usage statistics (PostHog EU) with an immediate opt-out toggle. Zero filenames, media assets, or personal paths are ever collected. See [docs/analytics.md](docs/analytics.md).
* **Safe Clean Removal**: Destructive action button with confirmation dialog to purge all cached animations and reset system configurations.

---

## System Requirements

| Component | Minimum Requirement | Recommended |
|---|---|---|
| **Operating System** | Windows 11 24H2 (Build 26100+), 64-bit | Windows 11 24H2+ (x64) |
| **Processor Architecture** | x64 (AMD64 / Intel 64) | x64 multi-core processor |
| **Runtime** | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64) | .NET 10 Desktop Runtime (x64) |
| **Windows App SDK** | 1.8+ (included in package) | Bundled |
| **Web Engine** | Microsoft Edge WebView2 Evergreen | Bundled with Windows 11 |

---

## Installation

Download the latest release from the [GitHub Releases](https://github.com/SubhamPro11/lockscreengtf/releases) page:

* **MSI Installer (`LockscreenGif-*.msi`)**: Recommended. Includes Start menu integration and automatic update readiness.
* **Portable Archive (`LockscreenGif-*-win-x64.zip`)**: Portable zero-install package. Extract and run `LockscreenGif.exe`.

---

## Building from Source

### Prerequisites
* [Windows 11 SDK (10.0.26100.0)](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/)
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* PowerShell 7+ (`pwsh`)

### Build Steps
1. **Clone the repository**:
   ```powershell
   git clone https://github.com/SubhamPro11/lockscreengtf.git
   cd lockscreengtf
   ```

2. **Restore tools and packages**:
   ```powershell
   dotnet tool restore
   dotnet restore
   ```

3. **Build the application**:
   ```powershell
   dotnet build LockScreenGif/LockscreenGif.csproj -c Release -p:Platform=x64
   ```

4. **Run all regression test suites**:
   ```powershell
   Get-ChildItem Tests -Recurse -Filter *.csproj | ForEach-Object {
       dotnet run --project $_.FullName -c Release
   }
   ```

5. **Format code with CSharpier**:
   ```powershell
   dotnet csharpier format .
   ```

6. **Publish folder package**:
   ```powershell
   cd LockScreenGif
   dotnet publish -c Release -p:PublishProfile=FolderProfile.pubxml -p:Platform=x64
   ```

---

## Diagnostics & Privacy

* **Application Logs**: Access **Settings > Logs** to inspect live logs or export recent session diagnostics as a ZIP.
* **Sanitized Diagnostics**: Access **Diagnostics > Export Sanitized Report** to create a shareable archive containing system prerequisites, ETW traces, and cache status with all personal data stripped.
* **Telemetry**: Anonymous telemetry tracks feature usage and error exceptions via PostHog (EU cluster). No file names, media paths, or screenshots are ever transmitted. Telemetry can be toggled off at any time under **Settings > Share anonymous usage analytics**. See [docs/analytics.md](docs/analytics.md).

---

## Lineage & Attribution

LockscreenGif is actively developed and maintained by **Subham Kumar ([SubhamPro11](https://github.com/SubhamPro11))**.

The project originated as an enhanced fork of the original [LockscreenGif by Leapward-Koex](https://github.com/Leapward-Koex/LockscreenGif), which is licensed under the MIT License. The architectural pattern for elevated lock-screen cache manipulation is preserved and credited.

Full third-party notices, license texts, and source availability information are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and inside the app via **Settings > About > Open-source licenses**.

---

## License

This project is licensed under the [MIT License](LICENSE.txt).

```text
Copyright (c) 2026 Subham Kumar (SubhamPro11)
Copyright (c) 2026 Leapward-Koex
```
