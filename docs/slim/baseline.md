# Baseline Metrics & Repository Inventory (S0)

**Date:** November 20, 2026  
**Branch:** `chore/stale-ui-and-slim`  
**Commit:** `6183072` (or current HEAD)  
**Target Platform:** Windows 11 Home Single Language 26H2 (Build `26300.9974`, FEP `1000.26100.385.0`)

---

## 1. Baseline Performance & Size Metrics

| Metric | Measured Baseline Value | Source / Notes |
|---|---|---|
| **Published Size (on disk)** | **159.7 MB** (167,448,987 bytes) | Release publish payload (`artifacts/app` uncompressed total: 254.5 MB) |
| **MSI Installer Size** | **~92.4 MB** compressed | Built via `Installer/LockscreenGif.aip` (Advanced Installer) |
| **Tracked Repository Size** | **90.93 MB** (95,342,846 bytes) | Sum of all 433 `git ls-files` tracked files |
| **Tracked File Count** | **433 files** | Git index tracked files |
| **`.git` Directory Size** | **153.84 MB** (161,311,698 bytes) | Local repository git history and packfiles |
| **Clean Build Time** | **19.71 seconds** | `dotnet build LockscreenGif.sln -c Release /p:Platform=x64 /m:1` |
| **Automated Test Count** | **11 / 11 test suites (100% PASS)** | 500+ individual test assertions across all test harnesses |
| **Cold Start Time to Tray** | **1,639 ms** | Measured to interactive tray state (Performance Audit baseline) |
| **Idle RAM (Working Set)** | **~75.4 MB** (Tray-only idle) / **148 MB** (UI open) | Tray working set without active wallpaper playback |

---

## 2. Feature-Preservation Contract (Section 8 Verification)

Every feature in the contract is protected against regressions during slimming. Verification methods are defined below:

| Feature Area | Feature Item | Verification Method / Command |
|---|---|---|
| **Build & Test** | Solution Compilation | `dotnet build LockscreenGif.sln -c Release /p:Platform=x64 /m:1` (0 warnings, 0 errors) |
| **Build & Test** | Regression Suites | `powershell scripts/run-all-tests.ps1` (11/11 suites PASS) |
| **Build & Test** | C# Formatting | `powershell scripts/Format-CSharp.ps1 -Check` & `dotnet csharpier check .` |
| **Lockscreen** | Choose Video File | Browse dialog opens, selects MP4/WebM/MOV/MKV/AVI, renders video frames in timeline |
| **Lockscreen** | Choose GIF File | Browse dialog selects animated GIF, loads direct via `LoadGifDirectAsync` |
| **Lockscreen** | Drag & Drop | Drag media file over window; drop triggers file load and decoding |
| **Lockscreen** | Recent Items | Recent files dropdown populates and reloads previously selected clips |
| **Lockscreen** | Trim Clip Range | Timeline sliders adjust start/end markers; frame preview updates in real time |
| **Lockscreen** | Preparing Overlay | Transcoding GIF generation displays progress bar and cancel button |
| **Lockscreen** | Apply Lockscreen | Clicking Apply triggers single UAC prompt, verifies cache variants, and commits |
| **Lockscreen** | Restore Windows Default | "Put the Windows lock screen back" restores default lockscreen image cleanly |
| **Lockscreen** | Prerequisite Checks | Prerequisites card checks registry overrides and build features accurately |
| **Desktop Wallpaper** | Choose File / Media | Selects video, GIF, or image; player process spawns and renders on desktop |
| **Desktop Wallpaper** | Apply to Desktop | Injects behind desktop icons via `Progman` / `WorkerW` without blocking icons |
| **Desktop Wallpaper** | Also Set as Lockscreen | Synchronizes selected wallpaper asset to Windows lock screen cache |
| **Desktop Wallpaper** | Web URL Load | Loads valid `https://` and `http://localhost:` URLs in sandboxed WebView2 |
| **Desktop Wallpaper** | Scaling Modes | Switches Fill, Fit, Stretch, Center, Tile, Span; adapts wallpaper canvas |
| **Desktop Wallpaper** | Audio Volume & Mute | Volume slider scales WASAPI volume; mute checkbox silences audio output |
| **Desktop Wallpaper** | Speed & Repetition | Dropdown switches playback speed (0.25x – 2.0x) and loop toggle |
| **Desktop Wallpaper** | All Pause Rules | Auto-pauses on fullscreen app, battery saver / Energy Saver, and display sleep |
| **Desktop Wallpaper** | Frame Rate Limit | Limits delivery to 15, 24, 30, 60 FPS or display refresh |
| **Desktop Wallpaper** | Lower Decode Quality | Toggles downsampled decode pipeline for reduced GPU memory footprint |
| **Desktop Wallpaper** | Multi-Monitor Layout | Detects all active monitors; allows per-display assignment or duplication |
| **Desktop Wallpaper** | Display Refresh | "Refresh" button rescans active display topology without restarting app |
| **Desktop Wallpaper** | Mouse Forwarding | Passes desktop icon click-through and mouse movements to interactive web wallpaper |
| **Desktop Wallpaper** | Audio Visualization | Captures loopback audio spectrum for reactive web wallpapers |
| **Desktop Wallpaper** | Executable Wallpapers | Validates Authenticode signature and `.exe` extension before execution |
| **Desktop Wallpaper** | Playlists | Cycles through queued media items based on timer or sequence |
| **Tray & Lifecycle** | Tray Icon Menu | Right-click tray shows Show, Pause, Mute, Exit options |
| **Tray & Lifecycle** | Run at Startup | Startup toggle writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with delay |
| **Tray & Lifecycle** | Shell Recovery | Wallpaper recovers seamlessly on `explorer.exe` restart or display change |
| **Diagnostics** | Diagnostic Presets | Selects test animation; runs verification suite under UAC helper |
| **Diagnostics** | Export ZIP Report | Packages sanitized environment logs and telemetry to `.zip` archive |
| **Settings** | Theme Switching | Toggles Light, Dark, System; updates application theme immediately |
| **Settings** | Lock Screen API Toggle | Switches between direct cache write and Windows API activation paths |
| **Settings** | Feature ID Save/Reset | Updates Feature IDs `38943831` / `48433719` with validation |
| **Settings** | Win11 Image Override | Toggles Windows 11 lock screen image feature override |
| **Settings** | Remove Animated Lockscreen | Restores system lockscreen defaults and purges cached assets |
| **Settings** | Logs Folder & ZIP | Opens log folder in Explorer and exports diagnostic archive |
| **Settings** | Analytics Toggle | Toggles opt-out; flushes queue and wipes identifiers on disable |
| **Settings** | About & Licenses | Displays version, links, and third-party open source licenses |
| **Settings** | Clean Settings Startup | Launches cleanly on empty `%LocalAppData%\LockscreenGif` settings directory |

---

## 3. Comprehensive Repository Inventory

### 3.1 Projects (15 total)
- **Application & Core:**
  1. `LockScreenGif\LockscreenGif.csproj` (`net10.0-windows10.0.26100.0`, WinUI 3 unpackaged desktop)
  2. `LockscreenGif.Privileged.Contracts\LockscreenGif.Privileged.Contracts.csproj` (`net10.0`, shared IPC contracts)
  3. `LockscreenGif.Privileged.Helper\LockscreenGif.Privileged.Helper.csproj` (`net10.0-windows`, elevated out-of-process helper)
  4. `Logger\Logger.csproj` (`net10.0`, structured logging library)
- **Test Projects (11 harnesses):**
  5. `Tests\Analytics.Tests\Analytics.Tests.csproj`
  6. `Tests\ApplyPipeline.Tests\ApplyPipeline.Tests.csproj`
  7. `Tests\Diagnostics.Tests\Diagnostics.Tests.csproj`
  8. `Tests\MainFlow.Tests\MainFlow.Tests.csproj`
  9. `Tests\PickedFileWriter.Tests\PickedFileWriter.Tests.csproj`
  10. `Tests\Prerequisites.Tests\Prerequisites.Tests.csproj`
  11. `Tests\ProcessTracing.Tests\ProcessTracing.Tests.csproj`
  12. `Tests\Session.Tests\Session.Tests.csproj`
  13. `Tests\VideoEditing.Tests\VideoEditing.Tests.csproj`
  14. `Tests\WindowLifecycle.Tests\WindowLifecycle.Tests.csproj`
  15. `Tests\WindowsImageFeature.Tests\WindowsImageFeature.Tests.csproj`

### 3.2 XAML Resource Dictionaries
- **Dictionaries Merged in `App.xaml`:**
  - `XamlControlsResources` (WinUI 3 standard library controls)
  - `/Styles/FontSizes.xaml` (Legacy font size constants)
  - `/Styles/Thickness.xaml` (Legacy margin & padding constants)
  - `/Styles/TextBlock.xaml` (Legacy text block styling)
  - `/Styles/InstrumentTheme.xaml` (Material 3 Dark/Light design tokens, styles, pill buttons, colors)
- **Component / Embedded Dictionaries & Controls:**
  - `LockScreenGif\CustomControls\VideoTimeline.xaml`

### 3.3 Views (Pages)
1. `LockScreenGif\Views\ShellPage.xaml` (Main navigation shell, NavigationView, window frame)
2. `LockScreenGif\Views\MainPage.xaml` (Lockscreen tab: file picker, video timeline, GIF conversion, apply)
3. `LockScreenGif\Features\DesktopWallpaper\Views\DesktopWallpaperPage.xaml` (Desktop wallpaper engine controls)
4. `LockScreenGif\Views\DiagnosticsPage.xaml` (Diagnostics, system probe, test animation runner)
5. `LockScreenGif\Views\SettingsPage.xaml` (Theme switcher, startup delay, lockscreen API, analytics, About)

### 3.4 Custom Controls
1. `LockScreenGif\CustomControls\VideoTimeline.xaml` / `VideoTimeline.xaml.cs`
2. `LockScreenGif\CustomControls\FrameHandle.cs`

### 3.5 Services
- **Contracts (`LockScreenGif\Contracts\Services\`):**
  - `IActivationService`, `IAppNotificationService`, `ILockscreenService`, `INavigationService`, `IPageService`, `IThemeSelectorService`
  - `IWallpaperEngineService`, `IWallpaperLibraryService`
- **Application Services (`LockScreenGif\Services\`):**
  - `ActivationService.cs`, `AppNotificationService.cs`, `DisplayService.cs`, `FfmpegService.cs`, `GifSkiService.cs`, `LockscreenService.cs`, `NavigationService.cs`, `PageService.cs`, `RecentFilesService.cs`, `TempDirectoryService.cs`, `ThemeSelectorService.cs`, `VideoFrameService.cs`, `VideoFrameService.Metadata.cs`
- **Analytics & Diagnostics (`LockScreenGif\Services\Analytics\` & `Diagnostics\`):**
  - `AnalyticsService.cs`, `DiagnosticsSessionService.cs`, `LockscreenVerificationService.cs`, `WindowsImageFeatureService.cs`
- **Desktop Wallpaper Services (`LockScreenGif\Features\DesktopWallpaper\Services\`):**
  - `WallpaperEngineService.cs`, `WasapiAudioCaptureService.cs`, `WallpaperLibraryService.cs`, `WallpaperStartupService.cs`, `WallpaperTrayService.cs`, `WallpaperMediaHelper.cs`, `AppWallpaperRenderer.cs`, `VideoWallpaperRenderer.cs`, `WebView2WallpaperRenderer.cs`

### 3.6 Assets
- **Application Icons & Branding (`LockScreenGif\Assets\`):**
  - `app.ico` (48,227 bytes)
  - `WindowIcon.ico` (48,227 bytes)
  - `WindowIcon.png` (25,544 bytes)
  - `LockScreenLogo.scale-200.png` (31,962 bytes)
  - `SplashScreen.scale-200.png` (7,700 bytes)
  - `Square150x150Logo.scale-200.png` (31,962 bytes)
  - `Square44x44Logo.scale-200.png` (6,329 bytes)
  - `Square44x44Logo.targetsize-24_altform-unplated.png` (938 bytes)
  - `StoreLogo.png` (3,099 bytes)
  - `Wide310x150Logo.scale-200.png` (3,204 bytes)
- **External Non-Shipped Assets / Clutter Candidates:**
  - `icon-source/` directory (40 files, ~2.8 MB, large raw SVG & PNG icon masters)
  - `Demos/Video Example.mp4` (3.24 MB tracked sample media)
  - `docs/preview.png` (866 KB documentation screenshot)

### 3.7 Vendor Binaries
1. `LockScreenGif\Vendor\FFMPEG\ffmpeg.exe` (83.38 MB / 87,429,632 bytes, static x64 FFmpeg build)
2. `LockScreenGif\Vendor\gifski\gifski.dll` (0.81 MB / 852,480 bytes, native x64 Rust dynamic library)

### 3.8 Scripts (17 total in `scripts/`)
1. `backup-system-state.ps1`
2. `env-probe.ps1`
3. `Format-CSharp.ps1`
4. `generate-qa-media.ps1`
5. `Generate-TestFixtures.ps1`
6. `Get-BuildVersion.ps1`
7. `Get-LockscreenAccessReport.ps1`
8. `power-probe.ps1`
9. `restore-system-state.ps1`
10. `run-all-tests.ps1`
11. `secret-scan.ps1`
12. `sig-check.ps1`
13. `test-desktop-attach-native.ps1`
14. `Test-PrivilegedPackaging.ps1`
15. `test-startup-delay.ps1`
16. `test-wallpaper-media-helper.ps1`
17. `verify-a11y-labels.ps1`

### 3.9 Documentation (12 docs in `docs/`)
1. `docs/analytics.md`
2. `docs/cache-access-report.md` (duplicate/overlapping candidate)
3. `docs/code-style.md`
4. `docs/COMPAT_26300.md`
5. `docs/DESKTOP_WALLPAPER.md`
6. `docs/diagnostics.md`
7. `docs/LockscreenAccess-normal-20261119-094044-e952ed69.json` (68 KB probe log candidate)
8. `docs/PERF_AUDIT.md`
9. `docs/preview.png`
10. `docs/QA_REPORT.md`
11. `docs/video-editing.md`
12. `docs/windows-image-feature.md`

### 3.10 NuGet Packages (10 packages across solution)
- `CommunityToolkit.Mvvm` 8.4.0 (LockScreenGif)
- `FFMpegCore` 5.4.0 (LockScreenGif)
- `Gifski.Net` 1.2.0 (LockScreenGif)
- `Microsoft.Diagnostics.Tracing.TraceEvent` [3.2.6] (LockscreenGif.Privileged.Helper)
- `Microsoft.Extensions.Hosting` 10.0.2 (LockScreenGif)
- `Microsoft.WindowsAppSDK` 1.8.260101001 (LockScreenGif)
- `Microsoft.Xaml.Behaviors.WinUI.Managed` 3.0.1 (LockScreenGif)
- `System.DirectoryServices.AccountManagement` 10.0.2 (LockScreenGif)
- `WindowsDisplayAPI` 1.3.0.13 (LockScreenGif)
- `WinUIEx` 2.9.0 (LockScreenGif)

---

## 4. Summary & Verification Status
- **Baseline established without modifying any source code or frozen files.**
- **All 11 test suites passing 100%.**
- Ready for S1 (Investigation into stale UI and theme switching).
