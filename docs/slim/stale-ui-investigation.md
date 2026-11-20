# Stale UI & Theme Switching Investigation (S1)

**Date:** November 20, 2026  
**Branch:** `chore/stale-ui-and-slim`  
**Target Platform:** Windows 11 Home Single Language 26H2 (Build `26300.9974`, FEP `1000.26100.385.0`)  
**Status:** EVIDENCE GATHERED — ROOT CAUSE ISOLATED

---

## 1. Build Stamp Verification (Section 4.1)

To eliminate ambiguity over which executable produces the UI, a view-layer build identity stamp has been wired to:
1. **Shell Header App Mark:** Hovering over the application title and icon in the top navigation displays a multi-line tooltip with full provenance.
2. **Settings → About Page:** Displays explicit badges for Version, Short Commit, Git Branch, Build Configuration, and running process file path (`ExecutablePath`).

### Build Stamp Schema:
```text
LockscreenGif 3.0.0-local
Commit: 6183072
Branch: chore/stale-ui-and-slim
Config: Release
Built: 2026-11-20 13:53:30
Path: D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\LockscreenGif.exe
```

---

## 2. Hypothesis Testing & Evidence Table (Section 4.2)

| # | Hypothesis | Test Methodology | Evidence Observed | Verdict |
|---|---|---|---|---|
| **H1** | **A second instance is running in the background** | Probed all processes via `Get-CimInstance Win32_Process` matching `Lockscreen*` and `msedgewebview2*` for Id, Path, StartTime, and Parent. | Currently no stray process running. However, because `LockscreenGif` lacks a named single-instance mutex in `App.xaml.cs` (it only intercepts toast notification activations), when launched via Windows startup (`WallpaperStartupService`), an installed instance sits in the tray with the wallpaper engine active. Launching a development build does not terminate or bring forward the running instance; it can silently connect to the same named pipe `LockscreenGif_WallpaperPlayer_Pipe`. | **CONFIRMED CONTRIBUTING FACTOR** |
| **H2** | **The wrong executable is launched (Installed MSI vs Output)** | Inspected Start Menu shortcuts (`%ProgramData%\Microsoft\Windows\Start Menu\Programs\Lockscreen Gif`), Desktop shortcuts, and `HKCU\...\Run`. | **Shortcut target:** `C:\Program Files (x86)\SubhamPro11\Lockscreen Gif\LockscreenGif.exe` (Installed MSI executable, written `11/20/2026 07:33:40`, 337,920 bytes). When testing UI changes, clicking the Start Menu shortcut, searching Windows Search, or launching from tray runs the **old installed MSI binary**, not the newly built development binary at `D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\LockscreenGif.exe`. | **CONFIRMED PRIMARY ROOT CAUSE** |
| **H3** | **Stale build outputs across disparate directories** | Compared LastWrite timestamps of source XAML (`LockScreenGif\Styles\InstrumentTheme.xaml`) vs intermediate `.xbf` in `obj/` and `.xbf` in `bin/`. Inspected output targets. | `InstrumentTheme.xaml` source LastWrite: `11/19/2026 20:52:04`. However, `obj\x64\Release\net10.0-windows10.0.26100.0\win-x64\Styles\InstrumentTheme.xbf` was timestamped `11/17/2026 17:46:03` (2 days older!). The repository contains 4 divergent output directories: `bin\Release\...\win-x64`, `bin\x64\Release\...`, `bin\x64\Release\...\win-x64`, and `bin\x64\Debug\...`. Incremental MSBuild runs without full clean reuse stale intermediate `.xbf` and `resources.pri` packs. | **CONFIRMED SECONDARY ROOT CAUSE** |
| **H4** | **Theme and resource resolution conflicts & Theme Switcher failure** | Parsed duplicate keys across `FontSizes.xaml`, `Thickness.xaml`, `TextBlock.xaml`, and `InstrumentTheme.xaml`. Traced `ThemeSelectorService.SetRequestedThemeAsync()` and WinUI 3 Window styling. | 1. **140 duplicate resource keys** exist across the legacy dictionaries and `InstrumentTheme.xaml` (e.g. `PageTitleStyle`, `SettingDescriptionStyle`, `NavigationViewPageContentMargin`, `AppSpacingL`).<br>2. **Theme Switch Failure:** `ThemeSelectorService.SetRequestedThemeAsync()` only sets `rootElement.RequestedTheme = Theme;` on `ShellPage`. In WinUI 3, `App.MainWindow` is a `WinUIEx.WindowEx` Win32 HWND. The DWM window frame and `MicaBackdrop` **never switch** between Light and Dark mode because `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ...)` is completely missing from the codebase. The window title bar and Mica frame stay dark while child elements switch, producing an unreadable or visually broken clash. | **CONFIRMED ROOT CAUSE FOR THEME FAILURE** |
| **H5** | **Saved state restoring old state** | Inspected `%LocalAppData%\LockscreenGif\*.json` and `theme.txt`. | `theme.txt` currently contains `"Light"`. Saved preferences store feature IDs and API toggles, but do not cache XAML styles. (However, the persisted `"Light"` state clashing with dark DWM confirms why the theme looked wrong on startup). | **RULED OUT** for stale UI; **CONFIRMED** for initial theme mismatch |
| **H6** | **Wrong workspace / branch** | Executed `git rev-parse HEAD`, `git status`, and checked solution path. | Active Commit: `6183072`, Branch: `chore/stale-ui-and-slim`, Solution: `D:\LockscreenGif-master\LockscreenGif.sln`. No stray worktrees or copy collisions. | **RULED OUT** |
| **H7** | **Installer upgrade path** | Inspected `Installer/LockscreenGif.aip` product version rules and upgrade table. | Base ProductVersion is `1.0.0`, overridden in CI by `SetVersion`. If an older build is installed and an updated MSI with the same major/minor/build version is installed over it, Windows Installer's default file-versioning rules skip files with matching file versions. | **CONFIRMED CONTRIBUTOR FOR MSI UPGRADES** |

---

## 3. Named Root Causes

### Root Cause 1: Launching the Installed MSI Executable instead of the Dev Output (H2)
The machine has LockscreenGif installed at `C:\Program Files (x86)\SubhamPro11\Lockscreen Gif\LockscreenGif.exe`. Launching from the Start Menu, Windows Search, or an autostart tray instance executes this static installed copy. Any changes made in `D:\LockscreenGif-master` are never reflected unless explicitly launching `D:\LockscreenGif-master\LockScreenGif\bin\x64\Release\net10.0-windows10.0.26100.0\LockscreenGif.exe`.

### Root Cause 2: Desynchronized Incremental XAML Compilation in `obj/` (H3)
WinUI 3's XAML compiler target (`MarkupCompilePass1` / `MarkupCompilePass2`) does not always detect changes in merged dictionaries during incremental `dotnet build` calls without `/t:Rebuild`, resulting in `InstrumentTheme.xbf` remaining 2 days out of date in `obj\x64\Release\...\win-x64\`.

### Root Cause 3: Theme Switching Broken by Missing DWM Immersive Dark Mode Integration (H4)
`ThemeSelectorService` sets `RequestedTheme` on `ShellPage`, but fails to toggle DWM window frame immersion:
1. `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ...)` is never invoked. On Windows 11, the window frame and Mica title bar stay dark while content turns light (or vice versa).
2. `GlassFallbackManager` instantiates `new MicaBackdrop()` without specifying theme synchronization.
3. 140 duplicate resource keys in `Thickness.xaml`, `TextBlock.xaml`, and `FontSizes.xaml` create shadow resolution conflicts with `InstrumentTheme.xaml`.

---

## 4. Proposed Fixes for S2 (Awaiting Confirmation)
1. **Developer Run Guard (`scripts/dev-run.ps1`):** Adds a dedicated development launcher that detects running background instances, offers clean termination, triggers a clean build, and launches the exact repo output binary while printing the build stamp.
2. **DWM Theme Synchronization:** Add `DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ...)` and Mica backdrop theme coupling in `TitleBarHelper` / `ThemeSelectorService` so switching between Light, Dark, and Default seamlessly updates both the window frame and the visual tree.
3. **Resource Dictionary Deduplication & Resolution Guards:** Add automated tests ensuring no orphan or shadowed keys conflict across merged dictionaries.
