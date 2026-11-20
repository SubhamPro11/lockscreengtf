# Tier 3 Slimming Proposals (S5)

This document contains architectural proposals for Tier 3 optimizations that touch frozen areas or require explicit user approval under Ground Rule #2.

---

## Proposal 1: FFmpeg Slimming (Custom Minimal Build)

### 1. Problem Statement
The current bundled `ffmpeg.exe` at `LockScreenGif/Vendor/FFMPEG/ffmpeg.exe` is **87,429,632 bytes (~83.38 MB)**. It is a full general-purpose static build containing hundreds of unused encoders, muxers, hardware decoders, and audio codecs.

### 2. LockscreenGif's Exact FFmpeg Requirements
Analysis of `VideoFrameService.cs` and `WallpaperMediaHelper.cs` shows LockscreenGif only uses FFmpeg for:
- **Demuxers:** `mov`, `mp4`, `matroska`, `webm`, `avi`.
- **Decoders:** `h264`, `hevc`, `vp8`, `vp9`, `av1`.
- **Filters:** `trim`, `showinfo`, `scale`, `fps`.
- **Encoders:** `png` (image2 muxer) to pipe still frames to Gifski.
- **Audio:** Zero audio encoding or processing is required.

### 3. Proposed Optimization
Replace the monolithic static build with a targeted, minimal FFmpeg binary built with:
```bash
./configure --disable-everything \
  --enable-decoder=h264,hevc,vp8,vp9,av1 \
  --enable-demuxer=mov,mp4,matroska,avi \
  --enable-parser=h264,hevc,vp8,vp9,av1 \
  --enable-filter=trim,showinfo,scale,fps,format \
  --enable-encoder=png \
  --enable-muxer=image2 \
  --disable-network --disable-autodetect --disable-debug
```

### 4. Impact & Safety
- **Estimated Size:** Decreases from **83.4 MB down to ~14–18 MB** (~65–70 MB disk reduction, ~28 MB MSI download reduction).
- **Validation:** Must pass all 32 tests in `VideoEditing.Tests` and `MainFlow.Tests`.
- **Status:** **PENDING USER APPROVAL** (Touches vendor binary).

---

## Proposal 2: Installer Layout Optimization (`Installer/LockscreenGif.aip`)

### 1. Problem Statement
`Installer/LockscreenGif.aip` tracks individual files by GUID. It currently packages:
- 3 legacy XBF files in `Styles_Dir`: `FontSizes.xbf`, `TextBlock.xbf`, `Thickness.xbf`.
- 3 DirectoryServices DLLs: `System.DirectoryServices.AccountManagement.dll`, `System.DirectoryServices.dll`, `System.DirectoryServices.Protocols.dll` (~2.8 MB).
- 2 Behavior DLLs: `Microsoft.Xaml.Interactivity.dll`, `Microsoft.Xaml.Interactions.dll` (~0.4 MB).

### 2. Evidence
- In S4, we proved that all keys in `FontSizes.xaml` and styles in `TextBlock.xaml` are 100% duplicate definitions of `InstrumentTheme.xaml`, and the 20 legacy margin keys in `Thickness.xaml` had 0 references in the entire project.
- Modern WinUI 3 views in LockscreenGif do not use XAML Behaviors or Active Directory account management.

### 3. Proposed Optimization
1. Remove `System.DirectoryServices.AccountManagement` and `Microsoft.Xaml.Behaviors.WinUI.Managed` from `LockScreenGif.csproj`.
2. Consolidate `App.xaml` to merge only `InstrumentTheme.xaml`. Delete `FontSizes.xaml`, `TextBlock.xaml`, and `Thickness.xaml`.
3. Update `Installer/LockscreenGif.aip` to remove the corresponding Component and File rows.

### 4. Impact & Safety
- **Estimated Size:** Eliminates ~3.2 MB of unneeded assemblies and 3 XBF files from the distribution package.
- **Performance Impact:** Eliminates 3 XML dictionary parse operations during `App` initialization on cold start.
- **Status:** **PENDING USER APPROVAL** (Touches frozen installer layout and .csproj dependencies).

---

## Proposal 3: Privileged Helper Distribution Pruning

### 1. Problem Statement
The elevated helper subsystem in `Helpers\Privileged\` bundles `Microsoft.Diagnostics.Tracing.TraceEvent.dll` (3.32 MB) and native architecture-specific ETW extraction binaries (`TraceEventNativeExtensions.dll` for both x64 and ARM64, ~2.2 MB total).

### 2. Evidence
LockscreenGif is published as `win-x64` (`net10.0-windows10.0.26100.0`, Platform: x64). The ARM64 native helper binaries are never executed on x64 target machines.

### 3. Proposed Optimization
Exclude `TraceEventNativeExtensions.ARM64.dll` from the x64 publish directory and installer manifest.

### 4. Impact & Safety
- **Estimated Size:** Eliminates ~1.1 MB from the distribution payload.
- **Status:** **PENDING USER APPROVAL**.
