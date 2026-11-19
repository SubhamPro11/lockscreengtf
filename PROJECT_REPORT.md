# LockscreenGif - Comprehensive Project Report

## Project Overview

**LockscreenGif** is a next-generation Windows 11 application that enables animated lock screens and live desktop wallpapers. It converts videos and GIFs into Windows-native lock-screen cache animations (rendered by `dwm.exe` with **0% ongoing CPU/RAM**) and provides a full-featured live desktop wallpaper engine with video loops, interactive web applications (HTML5/WebGL), and audio-reactive visuals.

**Author:** Subham Kumar (SubhamPro11)  
**License:** MIT  
**Platform:** Windows 11 24H2+ (Build 26100+), x64 only  
**Framework:** .NET 10, Windows App SDK 1.8+, WinUI 3  
**Runtime:** Microsoft Edge WebView2 Evergreen

---

## Architecture

### Solution Structure (4 Projects)

| Project | Type | Purpose |
|---------|------|---------|
| `LockscreenGif` | WinExe (WinUI 3) | Main application - UI, services, diagnostics, lockscreen pipeline |
| `Logger` | Library | Shared logging infrastructure |
| `LockscreenGif.Privileged.Contracts` | Library | Shared IPC contracts, interfaces, Windows feature models |
| `LockscreenGif.Privileged.Helper` | WinExe (Console) | Elevated helper for cache permission repair, ETW tracing, Windows feature management |

### Key Design Principles

1. **Privilege Separation** - Main app runs unelevated; only the standalone `LockscreenGif.Privileged.Helper.exe` requests elevation for cache writes, ETW tracing, and Windows feature configuration
2. **Safe IPC** - Named pipe with parent PID verification, monotonic request IDs, versioned protocol
3. **Diagnostics-First** - Comprehensive ETW tracing, cache verification, and exportable diagnostic reports
4. **Privacy-Conscious Analytics** - Opt-in PostHog telemetry (EU cluster), no paths/media/PII, bounded queue, fire-and-forget delivery
5. **Testable Architecture** - All core logic covered by regression tests with synthetic fixtures

---

## Core Features

### 1. Animated Lock Screen (Native Windows Integration)

**Flow:** Choose → Edit → Set → Applied  
- **Video sources:** MP4, WebM, MKV, AVI, MOV (GPU hardware decoding via Media Foundation)
- **GIF sources:** Animated GIF, animated WebP
- **Pipeline:** FFmpeg (frame extraction/trim) → PNG sequence → Gifski (high-quality GIF encoding with variable frame delays)
- **Application:** Atomic replacement of Windows lock-screen cache files (`LockScreen.jpg`, `LockScreen_*_notdimmed.jpg`) under `C:\ProgramData\Microsoft\Windows\SystemData\<SID>\ReadOnly\`
- **Windows API option:** Optional `LockScreen.SetImageFileAsync` call before cache replacement
- **Zero runtime cost:** After apply, app exits; animation rendered by `dwm.exe`

### 2. Live Desktop Wallpaper Engine

**Architecture:**
- **Windows 10/11 pre-24H2:** Creates `WorkerW` sibling via `Progman` message `0x052C`
- **Windows 11 24H2+:** Attaches host window as child of `Progman` at `HWND_BOTTOM`
- **Fail-safe:** Backs up `SPI_GETDESKWALLPAPER`, restores on stop/explorer restart/shutdown
- **Resilience:** Listens for `TaskbarCreated` (explorer restart) and `WM_DISPLAYCHANGE`

**Supported Media:**
| Type | Formats | Engine |
|------|---------|--------|
| Video/Loop | MP4, WebM, MKV, AVI, MOV, GIF, WebP | WebView2 (Chromium GPU) |
| Web Wallpaper | HTML, HTTPS URLs | WebView2 (WebGL, Canvas, WASM) |
| Static Image | PNG, JPG, BMP | WebView2 |
| Application | EXE (advanced) | Win32 Job Object containment |

**Smart Performance Rules (750ms poll):**
1. Fullscreen app/game focused → pause
2. Desktop completely occluded → pause
3. Battery/Battery Saver → pause
4. PC locked/display off → pause
5. FPS throttling: 60/30/15 FPS

**System Tray:** Pause/Resume, Next wallpaper, Open app, Exit; autostart via `HKCU\Run` with `--autostart`

**Dual Sync:** "Apply to Both" applies same media to desktop wallpaper AND lock screen independently

### 3. Video Editing & Trim Pipeline

**Frame-accurate editing:**
- `VideoFrameService.IndexAsync` uses FFmpeg `showinfo` for presentation-order timestamps (integer PTS + stream timebase)
- Trim interval: `[startFrame, endFrame)` - exclusive end boundary
- Export: FFmpeg `trim=start_frame=N:end_frame=M` → timestamp normalization → optional frame reduction → scaling → PNG output
- Gifski receives presentation timestamps, preserves variable frame delays (hundredths-second precision)
- Preview: Decodes neighborhood, caches up to 32 PNGs; thumbnails decoded separately

**UI Controls:**
- Filmstrip timeline with drag handles, ±1 frame buttons, keyboard arrow stepping
- Time input: seconds, `mm:ss.fff`, `h:mm:ss.fff` (snaps to frame boundaries)
- Play/pause loops selection; playhead follows media clock via render transforms
- Output settings: Resolution, Frame Rate (Original or target), Quality slider (70-100)

---

## Lockscreen Apply Pipeline (`LockscreenApplyPipeline`)

```csharp
ApplyAsync(sourcePath, useWindowsApi, progress, cancellationToken)
```

**Stages:**
1. **Source Verification** - Open GIF, validate signature (GIF87a/89a), compute SHA-256, read logical screen dimensions
2. **Windows Image Feature Snapshot** - Read feature ID 38943831 state (runtime + registry override)
3. **Optional Windows API** - `LockScreen.SetImageFileAsync` if enabled; waits for completion even on cancellation
4. **Cache Discovery** - `CacheLayout.FindDestinationsAsync` finds `LockScreen*` folders under user's `ReadOnly` cache
5. **Atomic Copy & Verify** - `VerifiedCacheWriter.WriteAsync` copies to each destination, verifies byte-for-byte match
6. **Result Classification** - Success / Partial / Failed / Cancelled with actionable recovery messages

**Failure Categories:**
- `CacheInaccessible` - Access denied (permission repair attempted)
- `CacheMissing` - Required cache directory absent
- `NoDestinations` - Cache found but no valid destinations
- `InvalidSource` / `SourceReadFailed` - Not a valid GIF
- `WindowsApiFailed` - OS image-setting call failed
- `CopyFailed` / `VerificationFailed` - Write/verify issues

---

## Privileged Helper & IPC

### Helper Executable (`LockscreenGif.Privileged.Helper.exe`)

**Launch:** Main app spawns with 3 args: `pipeName`, `parentPID`, `userSID`  
**Lifetime:** 6-minute watchdog; cancels if parent exits or memory > 256 MiB  
**Authentication:** Verifies parent process PID, executable path, user SID via named pipe impersonation

### Commands (via `PipeProtocol`)

| Command | Purpose | Validation |
|---------|---------|------------|
| `Grant` | Repair cache permissions (takeown + icacls) | Path under user's cache root, no reparse points |
| `StartTrace` | Begin ETW tracing for diagnostic test | Scope cache directory matches helper root |
| `ReadTrace` | Drain collected trace batches | Requires active collector |
| `StopTrace` | Stop ETW collector | Graceful shutdown |
| `EnableWindowsImageFeatureById` | Enable feature via `RtlSetFeatureConfigurations` | Positive feature ID only |
| `DisableWindowsImageFeatureById` | Disable feature via `RtlSetFeatureConfigurations` | Positive feature ID only |

### Cache Permission Repair (`CacheAccessRepair`)

1. **Validate** - Path under `ReadOnly\LockScreen*\LockScreen.jpg` or `*_notdimmed.jpg`; no reparse points
2. **Grant Parent Metadata** - ACL API adds `ReadAttributes` on SID parent (preserves inheritance)
3. **Fallback** - If parent ACL inaccessible: `takeown /a` → retry ACL grant
4. **Target Repair** - `icacls /grant *SID:RX` (read) or `:M` (modify); fallback to `takeown /a` + retry

---

## Diagnostics & ETW Tracing

### Diagnostic Test Flow

1. **Setup** - Select source (current GIF or bundled reference), toggle Windows API step
2. **Start Test** - Requests elevation → spawns helper → begins ETW trace
3. **Baseline** - Hash current cache files, read Windows image feature state
4. **Apply** - Runs `LockscreenApplyPipeline` with progress reporting
5. **Lock Cycle** - 5-second countdown → `LockWorkStation()` → monitor unlock
6. **Post-Unlock Observation** - 30s monitoring for external reads/writes
7. **Trace Stop** - 5s drain deadline → forced stop if needed → shutdown evidence
8. **Findings** - Verified copies, external access, lock/unlock notifications, feature state
9. **Export** - ZIP with `summary.md`, `session.json`, `events.jsonl`, redacted logs

### ETW Collection (`EtwFileCollector`)

- **Keywords:** Process, Thread, DiskFileIO, FileIO, FileIOInit (no stacks/sampling/registry/network)
- **Limits:** 32 MiB buffer, 4096 queued ops, 10k retained ops / 16 MiB data, 512 per-file aggregates
- **Correlation:** Process/thread generations, file mappings, IRPs link starts/completions
- **Shutdown Evidence:** Native stop result/duration, consumer return, disposal, progress snapshots
- **Memory Guard:** 256 MiB helper private memory limit stops tracing

### Cache Verification (`CacheCollector`, `CacheFileReader`)

- Watcher hints + bounded reconciliation
- Stable SHA-256 hashes
- Read provenance tracking (app/helper vs external)
- Only independent application reads *after* verification pass "applied GIF access" finding

---

## Windows Image Feature (ID 38943831)

**Purpose:** Controls image-loading path affecting static GIF playback on Windows 11 25H2+  
**Target State:** Disabled (1)  
**Registry Path:** `HKLM\SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\2920700556`
- `EnabledState` = 1 (Disabled) or 2 (Enabled)
- `EnabledStateOptions` = 0

**Reader:** `RtlQueryFeatureConfiguration` (ntdll) - queries runtime + registry override independently  
**Writer:** `RtlSetFeatureConfigurations` via elevated helper  
**UI:** Settings page - edit ID, reset to default, enable/disable buttons with reboot guidance  
**Apply Behavior:** Records read-only snapshot at apply time; **never changes the feature**

---

## Analytics & Telemetry

**Endpoint:** PostHog EU (`https://eu.i.posthog.com`)  
**Tokens:** Production (GitHub Actions builds) vs Development (local builds) via `GitHubActionsBuild` assembly marker  
**Opt-in:** First launch notice; stored in `%LOCALAPPDATA%\LockscreenGif\analytics.json`  
**Queue:** 64 events max; 3s HTTP deadline; fire-and-forget; drops on pressure/offline  
**No PII:** No paths, filenames, media, hashes, usernames, SIDs, exception messages, stack traces

### Events (19 types)

| Event | Key Properties |
|-------|----------------|
| `app_opened` | Windows edition/build/locale, architecture, elevation, remote session |
| `gif_selected` / `video_load_completed` | `media_load_stage`, `playback_available`, `error_hresult` |
| `gif_generation_completed` | `failure_stage`, `extracted_frame_count`, `encoding_duration_ms` |
| `lockscreen_apply_completed` | `lockscreen_source`, `target_count`, `verified_count`, `api_requested`, `apply_failure_reason` |
| `diagnostic_test_requested` / `diagnostic_report_export_completed` | `uses_reference_gif`, outcome |
| `$exception` | Fingerprinted by boundary, exception family, native code |

**Error Tracking:** PostHog manual `$exception` with sanitized context (no raw messages/stack traces)

---

## Video Editing Pipeline Details

### Frame Indexing (`VideoFrameService`)
- Decodes first video stream once via FFmpeg `showinfo`
- Records presentation-order frame timestamps (PTS + timebase)
- Supports fractional/variable frame rates
- Final frame duration from decoded data, nominal FPS fallback

### Export Pipeline
```
FFmpeg: trim=start_frame=S:end_frame=E → setpts=PTS-STARTPTS → fps=N (optional) → scale=W:H → PNG
Gifski: PNG sequence + presentation timestamps → GIF (hundredths-second delay precision)
```

### Preview System
- Paused previews: Same ordinal frame selection as export (not media player seek)
- Decodes neighborhood, caches ≤32 PNGs
- Live seek shown while exact still loads
- Debounced/cancelled superseded requests
- Thumbnails: Separate decode, black+spinner overlay during load

---

## Desktop Wallpaper Engine Details

### Web Wallpaper Custom Properties (`wallpaper.json`)
```json
{
  "properties": [
    { "id": "accentColor", "type": "color", "default": "#00FFAA" },
    { "id": "gridSpeed", "type": "slider", "min": 1, "max": 10, "step": 0.5 },
    { "id": "showStars", "type": "boolean" },
    { "id": "themeMode", "type": "choice", "options": ["A", "B", "C"] }
  ]
}
```
Injected via `window.onWallpaperPropertyChanged(propertyId, value)`

### Audio-Reactive API (WASAPI Loopback)
- 16-band normalized FFT every 20ms via `chrome.webview.postMessage`
- `peak`: 0.0-1.0, `bands[0-15]`: bass to treble (~20Hz - ~20kHz)

---

## Testing & Quality

### Test Projects (11)
| Test Project | Focus |
|--------------|-------|
| `Analytics.Tests` | Fake HTTP transport, event schema, opt-out, error tracking |
| `ApplyPipeline.Tests` | Cache discovery, copy/verify, failure classification, elevation mocking |
| `Diagnostics.Tests` | GIF inspector, reference generator, analyzer, report writer, ZIP export |
| `MainFlow.Tests` | Navigation, readiness, stale tokens, outcome classification |
| `PickedFileWriter.Tests` | File save, overwrite, provider completion |
| `Prerequisites.Tests` | Picture mode detection, cache check, feature state evaluation |
| `ProcessTracing.Tests` | ETW correlation, identity mapping, shutdown evidence |
| `Session.Tests` | Window lifecycle, activation, unlock notifications |
| `VideoEditing.Tests` | Frame counts, byte matches, boundaries, VFR, previews, thumbnails |
| `WindowLifecycle.Tests` | Player disposal, close behavior, navigation |
| `WindowsImageFeature.Tests` | Feature read/evaluate, registry encoding, override persistence |

### Native Fixture Tests (Opt-in, Require Elevation)
- `Run-NativePermissions.ps1` - Real ACLs, `takeown`/`icacls`, backup token metadata inspection
- `Run-NativeTracing.ps1` - Collector mode (ETW open/read/write), Transport mode (helper IPC)
- `Test-PrivilegedPackaging.ps1` - MSI component/file/feature inventory verification

### Code Quality
- **Formatter:** CSharpier (`.editorconfig`, 140-col, IDE0011 braces)
- **Command:** `dotnet tool restore && ./scripts/Format-CSharp.ps1` (check: `-Check`)
- **Build:** `dotnet build LockScreenGif/LockscreenGif.csproj -c Release -p:Platform=x64`
- **Test:** `Get-ChildItem Tests -Recurse -Filter *.csproj | ForEach-Object { dotnet run --project $_.FullName -c Release }`

---

## Configuration Files

### `LockScreenGif/appsettings.json`
```json
{
  "LocalSettingsOptions": { "ApplicationDataFolder": "LockscreenGif/ApplicationData", "LocalSettingsFile": "LocalSettings.json" },
  "Analytics": { "Host": "https://eu.i.posthog.com", "ProductionProjectToken": "...", "DevelopmentProjectToken": "..." }
}
```

### `LockscreenGif.csproj` Key Settings
- Target: `net10.0-windows10.0.26100.0`, x64 only
- WinUI 3, Windows App SDK 1.8.260101001, Self-contained
- Package refs: CommunityToolkit.Mvvm, FFMpegCore, Gifski.Net, Microsoft.Extensions.Hosting, WindowsDisplayAPI, WinUIEx
- Vendor binaries: `ffmpeg.exe`, `gifski.dll` copied to output
- Privileged helper built into `Helpers/Privileged/` for publish

---

## Documentation Index

| Document | Purpose |
|----------|---------|
| `README.md` | User-facing overview, quick start, installation, building |
| `docs/diagnostics.md` | Diagnostic test flow, evidence interpretation, code boundaries, acceptance checks |
| `docs/analytics.md` | Event schema, properties, error tracking, dashboard recipes, delivery |
| `docs/windows-image-feature.md` | Feature ID 38943831, registry encoding, explicit controls, diagnostic evidence |
| `docs/cache-access-report.md` | PowerShell script for cache permission evidence collection |
| `docs/video-editing.md` | Frame-based editing flow, pipeline, validation, manual UI checks |
| `docs/DESKTOP_WALLPAPER.md` | Wallpaper engine architecture, media types, web properties, audio API, performance rules |
| `docs/code-style.md` | CSharpier configuration, formatting rules |
| `AGENTS.md` | Repository guidance for AI assistants |
| `CONTRIBUTING.md` | Code style, test requirements, PR guidelines |

---

## Build & Release

### Prerequisites
- Windows 11 SDK 10.0.26100.0
- .NET 10 SDK
- PowerShell 7+

### Build Commands
```powershell
dotnet tool restore
dotnet restore
dotnet build LockScreenGif/LockscreenGif.csproj -c Release -p:Platform=x64
# Run all tests
Get-ChildItem Tests -Recurse -Filter *.csproj | ForEach-Object { dotnet run --project $_.FullName -c Release }
# Publish unpackaged
cd LockScreenGif
dotnet publish -c Release -p:PublishProfile=FolderProfile.pubxml -p:Platform=x64
```

### Release Artifacts (GitHub Actions)
- MSI Installer (`LockscreenGif-*-x64.msi`)
- Portable ZIP (`LockscreenGif-*-win-x64.zip`)

---

## Security & Privacy Summary

| Aspect | Implementation |
|--------|----------------|
| **Privilege Boundary** | Unelevated main app; standalone helper for cache writes, ETW, feature config |
| **IPC Security** | Named pipe + parent PID + executable path + user SID verification |
| **Cache Scope** | Strict path validation: only `ReadOnly\LockScreen*\LockScreen.jpg` / `*_notdimmed.jpg` |
| **Reparse Points** | Rejected at all validation layers |
| **Analytics** | Opt-in, EU PostHog, no PII, bounded queue, fire-and-forget |
| **Diagnostics Export** | User-initiated only, redacted paths/SIDs, no media/screenshots |
| **Helper Memory** | 256 MiB limit triggers trace stop |
| **Helper Lifetime** | 6-min watchdog, parent process monitoring |

---

## Key Implementation Files

### Lockscreen Apply
- `Services/Lockscreen/LockscreenApplyPipeline.cs` - Core apply logic
- `Services/Lockscreen/VerifiedCacheWriter.cs` - Atomic copy + hash verification
- `Services/Lockscreen/CacheLayout.cs` - Cache destination discovery
- `Services/Lockscreen/CachePermissions.cs` - Access denied detection

### Privileged Helper
- `LockscreenGif.Privileged.Helper/Program.cs` - IPC server, command dispatch
- `LockscreenGif.Privileged.Helper/CacheAccessRepair.cs` - Permission repair logic
- `LockscreenGif.Privileged.Helper/Tracing/EtwFileCollector.cs` - ETW session management
- `LockscreenGif.Privileged.Contracts/WindowsImageFeature.cs` - Feature read/evaluate

### Diagnostics
- `Services/Diagnostics/DiagnosticsSessionService.cs` - Test coordination
- `Services/Diagnostics/DiagnosticRecorder.cs` - In-memory evidence
- `Services/Diagnostics/DiagnosticAnalyzer.cs` - Findings generation
- `Services/Diagnostics/DiagnosticReportWriter.cs` - ZIP export

### Video Editing
- `Services/VideoFrameService.Metadata.cs` - FFmpeg indexing
- `CustomControls/VideoTimeline.xaml/cs` - Timeline control
- `Views/MainPage.PlaybackLifetime.cs` - Media player management

### Desktop Wallpaper
- `Services/Wallpaper/` - Engine, WebView2 host, performance monitor
- `Services/Audio/` - WASAPI loopback capture

---

## Version & Dependencies

**App Version:** 3.0.0-local (set at build)  
**Key Dependencies:**
- .NET 10.0.2
- Windows App SDK 1.8.260101001
- CommunityToolkit.Mvvm 8.4.0
- FFMpegCore 5.4.0 (bundled `ffmpeg.exe`)
- Gifski.Net 1.2.0 (bundled `gifski.dll`)
- Microsoft.Extensions.Hosting 10.0.2
- Microsoft.WindowsAppSDK 1.8.260101001
- Microsoft.Xaml.Behaviors.WinUI.Managed 3.0.1
- System.DirectoryServices.AccountManagement 10.0.2
- WindowsDisplayAPI 1.3.0.13
- WinUIEx 2.9.0

---

*Report generated from repository analysis on 2026-11-18*