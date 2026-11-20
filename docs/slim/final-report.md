# LockscreenGif UI Fix & Slimming Final Report (S0–S5)

**Date:** November 20, 2026  
**Branch:** `chore/stale-ui-and-slim`  
**Target Platform:** Windows 11 Home Single Language 26H2 (Build `26300.9974`)  
**Commit Range:** `5e90b9a`..`e922816`  

---

## 1. Executive Summary

This engagement achieved two objectives without breaking any feature or weakening any test:
1. **Identified and fixed the root causes of the "Stale UI" symptom** where running the application appeared to show the old interface, added a deterministic developer launcher (`scripts/dev-run.ps1`), implemented dynamic DWM title bar theme synchronization, fixed a Mica backdrop access violation, resolved a XAML tooltip parse crash, and added automated resource regression guards.
2. **Slimmed the repository footprint by over 4.03 MB (39 tracked files)**, eliminated 20 dead boilerplate tokens, removed duplicate XAML parsing overhead, audited all NuGet packages against the frozen installer layout, and provided three structured Tier 3 architectural proposals.

---

## 2. Before & After Metrics Comparison (S0 Baseline vs. S5 Final)

| Metric | S0 Baseline | S5 Final Result | Delta / Improvement |
|---|---|---|---|
| **Tracked Files in Git** | **440 files** | **401 files** | **-39 files (-8.9%)** |
| **Tracked Repo Size** | **90.97 MB** (95,393,446 B) | **87.13 MB** (91,363,382 B) | **-3.84 MB (-4,030,064 B)** |
| **Untracked Build Clutter** | **2.14 MB** (`build.log`) | **0 B** (Purged) | **-2.14 MB on disk** |
| **Automated Test Suites** | 11 / 11 test suites PASS | 11 / 11 test suites PASS | **100% PASS rate preserved** |
| **XAML Resource Guard** | N/A (unmonitored) | 251 keys audited, 0 orphans | **100% PASS (Added Guard)** |
| **C# Formatting Check** | FAILED in CI Run #115 | 275 files clean, 0 errors | **100% PASS in CI Run #116** |
| **Clean Build Time** | 19.71 seconds | 18.25 seconds | **~1.5s faster build** |
| **Cold Start to HWND** | ~4,500 ms (debug trace) | ~4,490 ms (HWND ready) | **Stable, crash-free** |
| **Active Working Set RAM** | ~150–160 MB | 157.39 MB | **Consistent with baseline** |

---

## 3. Stale UI: Root Causes & Permanent Solutions

### Root Cause 1: Launching Installed MSI Executable Instead of Development Artifacts
- **Evidence:** The target PC had LockscreenGif installed at `C:\Program Files (x86)\SubhamPro11\Lockscreen Gif\LockscreenGif.exe`. Windows Start Menu shortcuts and Windows search targeted this installed binary.
- **Fix:** Implemented runtime build stamp in `LockScreenGif\Helpers\BuildInfo.cs` (displayed in Settings → About and in the app mark tooltip) showing version, git commit, branch, configuration, and exact executable path. Created `scripts/dev-run.ps1` which automatically inspects and terminates any conflicting installed instances, builds via `FolderProfile.pubxml`, and executes the fresh development binary.

### Root Cause 2: Missing DWM Immersive Dark Mode Synchronization
- **Evidence:** When toggling themes in Settings, `ShellPage.RequestedTheme` changed, but the Win32 non-client window frame and Mica title bar remained dark because `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE)` was never called.
- **Fix:** Added P/Invoke `ApplyImmersiveDarkMode(hwnd, isDark)` in `LockScreenGif\Helpers\TitleBarHelper.cs` (supporting both DWM attribute 20 and pre-20H1 attribute 19). Now the native title bar, window borders, and caption buttons dynamically sync with Light, Dark, and System modes.

### Root Cause 3: Mica Backdrop Lifecycle Access Violation (`0xC0000005`)
- **Evidence:** `GlassFallbackManager.cs` called `_window.SystemBackdrop = new MicaBackdrop()` every time the window activated, corrupting the DirectComposition compositor surface.
- **Fix:** Guarded `SystemBackdrop` assignment to only instantiate when `SystemBackdrop == null`, preventing redundant re-allocation during window activation cycles.

### Root Cause 4: Missing `DefaultToolTipStyle` Resource Crash
- **Evidence:** `InstrumentTheme.xaml` contained `<Style TargetType="ToolTip" BasedOn="{StaticResource DefaultToolTipStyle}">`, which crashed at launch because `DefaultToolTipStyle` is not defined in WinUI 3.
- **Fix:** Removed the invalid `BasedOn` reference and created `scripts/test-xaml-resources.ps1` to prevent any orphan resource references from ever entering the repository.

---

## 4. Slimming Inventory & Rule-of-Three Verification

### Tier 1 (Safe Clutter - Completed in commit `4204642`)
1. **`Demos/Video Example.mp4` (3.21 MB):** Non-shipped sample video. Zero references in tests. Removed from git; updated `docs/video-editing.md` line 101.
2. **`docs/LockscreenAccess-normal-*.json` (67.5 KB):** One-off machine diagnostics dump accidentally committed during manual probing. Removed from git.
3. **`icon-source/` (585 KB, 37 files):** Unbundled source SVGs and 33 redundant duplicate PNG exports. Redirected `README.md` to official shipped asset `LockScreenGif/Assets/WindowIcon.png` and deleted folder.

### Tier 2 (Needs Evidence - Completed in commit `e922816`)
1. **XAML Dictionaries Audit:** Audited all 56 keys across `FontSizes.xaml`, `Thickness.xaml`, and `TextBlock.xaml`.
2. **Dead Margin Keys:** Removed 20 unreferenced legacy margin keys (`LargeTopMargin`, etc.) from `Thickness.xaml`.
3. **Frozen Installer Protection:** Confirmed that `Installer/LockscreenGif.aip` requires `FontSizes.xbf`, `TextBlock.xbf`, and `Thickness.xbf`. Kept those files compiling cleanly to guarantee zero installer regressions.
4. **NuGet Packages Audit:** Audited all 9 packages in `LockscreenGif.csproj` against the frozen installer layout.

### Tier 3 (Proposals for User Decision - Documented in `docs/slim/tier3-proposals.md`)
1. **Proposal 1 (FFmpeg Slimming):** Replace monolithic 83.4 MB FFmpeg build with a minimal custom build containing only required demuxers and decoders (~14–18 MB, saving **~65–70 MB**).
2. **Proposal 2 (Installer Layout Optimization):** Remove unused `System.DirectoryServices.*` and `Microsoft.Xaml.Interactivity.*` packages, consolidate XAML into `InstrumentTheme.xaml`, and update `LockscreenGif.aip` (saving **~3.2 MB** and speeding up startup).
3. **Proposal 3 (Privileged Helper Pruning):** Exclude unused ARM64 native ETW binaries from x64 distributions (saving **~1.1 MB**).

---

## 5. Verification of the Feature Contract

All 40+ features specified in Section 8 of the baseline contract were verified:
- Solution compiles with 0 errors across all projects.
- All 11 automated test suites passed 100%.
- CSharpier check passes 100% across all 275 files.
- XAML resource regression guard passes 100%.
- Interactive UI verified running from `artifacts/app/LockscreenGif.exe` with dynamic theme switching and build stamp.
