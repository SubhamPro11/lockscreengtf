# Changelog

All notable changes to LockscreenGif will be documented in this file.

## [3.0.58] - 2026-11-19

### Added
- **Windows 11 26H2 & Build 26300 Compatibility**:
  - Probed and verified native support for Windows Feature IDs `38943831` and `48433719` via `ntdll.dll` `RtlQueryFeatureConfiguration`.
  - Added dedicated `docs/COMPAT_26300.md` compatibility guide.
  - Added "Run compatibility check" in Diagnostics tab to re-evaluate system build, permissions, and feature IDs on demand.
  - Diagnostics report now enriches environment telemetry with OS build, Windows Feature Experience Pack version, edition, and active video decoder.
- **EcoQoS & Efficiency Mode**:
  - Background wallpaper player now automatically enables Windows 11 EcoQoS (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`) and runs in `BelowNormal` process priority class.
- **High-Refresh Frame Pacing & Adaptive Pacing**:
  - Added full frame pacing support with 6 selectable options: `15 FPS (Ultra Light)`, `24 FPS (Cinematic)`, `30 FPS (Standard)`, `60 FPS (Fluid)`, `Match monitor (144 Hz)`, and `Adaptive (Auto-throttle)`.
  - In Adaptive mode, frame rate automatically throttles down when running on battery or when system is under load.
- **Modern Energy Saver Integration**:
  - Updated auto-pause power monitoring from polling to event-driven notifications using `PowerManager.EnergySaverStatusChanged` and `PowerSupplyStatusChanged` with Win32 fallback.
  - Renamed "Battery saver" to "Energy Saver" to reflect Windows 11 terminology across desktops and laptops.
- **Web Wallpaper Sandboxing**:
  - Secured web wallpaper WebView2 renderer: blocked script dialogs (`alert`/`confirm`), disabled browser accelerator keys, disabled developer tools in production, and automatically blocks unauthorized popup windows.
- **Login Startup Delay**:
  - Added an 8-second background delay on login autostart so Windows shell and desktop icons load smoothly before wallpaper decoding spins up.

### Fixed
- **XamlParseException on Startup**: Resolved `TargetType` mismatch between `Button` style and `HyperlinkButton` by introducing `GlassHyperlinkButtonStyle`.
- **MeasureOverride Crash**: Fixed `0x80004005 (E_FAIL)` by restoring standard `ControlCornerRadius` and adding based-on styles.
- **Settings Feature ID Text**: Unified status and description text to eliminate conflicting feature ID references.
