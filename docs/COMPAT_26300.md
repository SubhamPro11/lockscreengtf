# Windows 11 Build 26300 (26H2) Compatibility Report

**Target Machine:** ASUS TUF Gaming A15 (`FA506NFR_FA506NFR`)  
**OS Edition:** Windows 11 Home Single Language (`CoreSingleLanguage`), 64-bit  
**OS Version:** Version 26H2, OS Build `26300.9974`  
**Feature Experience Pack:** `1000.26100.385.0`  
**Hardware Profile:** AMD Ryzen 7 7435HS (8C/16T, 3.1 GHz, *No iGPU*), NVIDIA GeForce RTX 2050 (4 GB GDDR6), 16 GB DDR5 RAM, 1080p @ 144Hz (125% DPI)  
**Date Probed:** November 19, 2026  

---

## 1. Feature Compatibility Matrix

| Feature / Subsystem | Status on Build 26300 | Observed Behavior | Workaround / Architecture Implementation |
|---|---|---|---|
| **Desktop Layer Attach (WorkerW / Progman)** | Compatible (Dual-mode) | `Progman` can host `SHELLDLL_DefView` directly in 24H2/26H2 without always spawning a secondary `WorkerW`. | Build-aware attach strategy: attempt standard `0x052C` message on interactive desktop thread. If no `WorkerW` is spawned behind `SHELLDLL_DefView`, parent directly to `Progman` with `SetWindowPos(HWND_BOTTOM)` beneath icons. |
| **Feature ID 38943831 (Lock Screen Animated GIF)** | Supported & Verified | `RtlQueryFeatureConfiguration` returns `STATUS_SUCCESS` (`0x00000000`). Flags: `24` -> `RuntimeState = 1` (Disabled by default). | Configurable via `LockscreenGif.Privileged.Helper.exe` using `RtlSetFeatureConfigurations`. Persisted in registry. |
| **Feature ID 48433719 (Lock Screen Media)** | Supported & Verified | `RtlQueryFeatureConfiguration` returns `STATUS_SUCCESS` (`0x00000000`). Flags: `16` -> `RuntimeState = 1` (Disabled by default). | Both feature IDs exist on build 26300. UI is harmonized to expose `38943831` as primary with unified status messaging. |
| **Lock Screen Protected Cache** | Restricted (ACL Protected) | `C:\ProgramData\Microsoft\Windows\SystemData\<SID>\ReadOnly` allows user reads (`Read, Synchronize`). Parent directory returns `E_ACCESSDENIED` (`0x80070005`) for writes. | On-demand elevation via `LockscreenGif.Privileged.Helper.exe` with named pipe IPC (`LockscreenGif.Privileged.Contracts`). Preserves system ACLs. |
| **Windows API Lockscreen (`SetImageFileAsync`)** | Static Only | Windows API `LockScreen.SetImageFileAsync` extracts only the static first frame of animated GIFs and videos. | Used strictly as a fallback / restore mechanism. Animated lock screens use the verified protected cache mechanism. |
| **Edition Limits (Home Single Language)** | Verified No GPO / Sandbox | `gpedit.msc`, AppLocker, Windows Sandbox, and Hyper-V are unavailable on Home Single Language. | All policies enforced via user-scope registry (`HKCU`), app-level configuration (`lockscreen.json`), and Chromium process sandboxing. |
| **GPU Architecture (Hybrid iGPU vs dGPU)** | Single dGPU (No iGPU) | CPU is AMD Ryzen 7 7435HS, which lacks an integrated GPU. Discrete NVIDIA GeForce RTX 2050 handles all desktop & 3D rendering. | Power optimization relies on Windows EcoQoS (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`), `CoreWebView2.TrySuspendAsync()`, and frame rate throttling rather than GPU switching. |
| **Display Refresh Rate (144Hz)** | High Refresh Rate Active | Native panel runs at 144Hz refresh rate with 125% DPI scaling (1536×864 logical desktop). | Frame pacing options (15, 24, 30, 60, Match monitor at 144Hz, Adaptive). Auto-drops to 30/15 FPS on battery or high system load. |

---

## 2. In-Depth Probe Results

### 2.1 Native Feature ID Query via `ntdll.dll`
Using direct P/Invoke into `RtlQueryFeatureConfiguration`:
```
Feature ID 38943831:
  - NTSTATUS: 0x00000000 (Success)
  - ChangeTime: 0
  - Flags: 24 (RuntimeState: 1 = Disabled by default)

Feature ID 48433719:
  - NTSTATUS: 0x00000000 (Success)
  - ChangeTime: 0
  - Flags: 16 (RuntimeState: 1 = Disabled by default)
```
Both features are confirmed present in the kernel feature table of build 26300.9974.

### 2.2 Lock Screen Cache Access & Permissions
Probed using `scripts/Get-LockscreenAccessReport.ps1`:
- Current User SID: `S-1-5-21-2266382096-606964300-635372331-1001`
- `SystemData` base directory: `C:\ProgramData\Microsoft\Windows\SystemData`
- User ReadOnly cache: Accessible for reads without elevation.
- Writing to cache: Blocked by Windows SACL/DACL without `SeSecurityPrivilege` / Admin token.
- Elevation strategy: `LockscreenGif.Privileged.Helper.exe` launches with UAC prompt only during apply/restore actions.

### 2.3 Shell Attach & WorkerW Hierarchy
Probed on interactive desktop `"Default"`:
- `Progman` class `Progman` exists (HWND `0x000100AC`).
- Child `SHELLDLL_DefView` hosts desktop icons (`SysListView32`).
- When `0x052C` is posted, Windows 11 build 26300 creates `WorkerW` when desktop composition is engaged.
- Fallback verified: If `WorkerW` is unavailable, `SetParent(hostHwnd, progmanHwnd)` positions wallpaper directly behind desktop icon layer.
