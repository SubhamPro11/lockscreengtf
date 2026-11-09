# LockscreenGif

A modern Windows 11 (24H2+) utility that turns GIFs and video loops (`.gif`, `.mp4`, `.webm`, `.avi`, `.mov`, `.mkv`) into smooth animated lock screens, styled with a fluid **Liquid Glass** visual interface.

**Maintained & developed by [SubhamPro11](https://github.com/SubhamPro11)** · **[Website](https://lockscreen-gtf-web.morbius.workers.dev)** · **[Releases](https://github.com/SubhamPro11/lockscreengtf/releases)**

---

## Features

- **Liquid Glass Design Language**: Softly translucent, layered surfaces with specular edge highlights, concentric radii, and full dynamic adaptation across Windows Light, Dark, and High Contrast themes.
- **Video & GIF Conversion**: Direct input of high-resolution video clips or animated GIFs. Frame-accurate start and end boundary trimming with real-time frame previews.
- **Studio-Quality Conversion Engine**: Integrated [FFmpeg](https://ffmpeg.org/) and [Gifski](https://gif.ski/) pipeline producing high-fps, dithered GIF animations matched to your monitor's display resolution.
- **Safe One-Time Elevation**: Transparent pre-elevation security dialog explaining the system cache write process before standard Windows User Account Control (UAC) elevation.
- **Live Lock Screen Smoke Test**: Trigger a 5-second live test simulation directly from the app to verify animation smoothness without manually logging out.
- **One-Click Restoration**: Revert back to the Windows default static lock screen anytime with full cleanup of cached animated frames.
- **Comprehensive Diagnostics**: In-app ETW tracing, lock screen cache permission verification, and sanitized ZIP export for bug reporting.

---

## Lineage & Attribution

LockscreenGif is actively developed and maintained by **Subham Kumar ([SubhamPro11](https://github.com/SubhamPro11))**.

The project originated as an enhanced fork of the original [LockscreenGif by Leapward-Koex](https://github.com/Leapward-Koex/LockscreenGif), which is licensed under the MIT License. The original architectural concept for the elevated lock-screen cache writer is preserved and credited. Full third-party notices, license texts, and source availability information are documented in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and inside the app via **Settings > About > Open-source licenses**.

---

## System Requirements

- **Operating System**: Windows 11 24H2 (Build 26100 or later), 64-bit (x64).
- **Runtime**: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- **Windows App SDK**: 1.8+ (bundled with the application).

---

## Installation

Download the latest release from the [GitHub Releases](https://github.com/SubhamPro11/lockscreengtf/releases) page:
- **`LockscreenGif-*.msi`**: Recommended Windows Installer package with Start menu integration.
- **`LockscreenGif-*-win-x64.zip`**: Portable application archive.

---

## Building from Source

1. Clone the repository:
   ```powershell
   git clone https://github.com/SubhamPro11/lockscreengtf.git
   cd lockscreengtf
   ```
2. Restore tools and dependencies:
   ```powershell
   dotnet tool restore
   dotnet restore
   ```
3. Build the application in Release mode:
   ```powershell
   dotnet build LockScreenGif/LockscreenGif.csproj -c Release -p:Platform=x64
   ```
4. Run regression check suite:
   ```powershell
   dotnet run --project Tests/MainFlow.Tests/MainFlow.Tests.csproj -c Release
   ```
5. Format C# sources:
   ```powershell
   ./scripts/Format-CSharp.ps1
   ```

---

## Diagnostics & Privacy

- **Logs**: Open **Settings > Logs** to inspect active logs or export all recent application logs as a ZIP file.
- **Diagnostic Reports**: Access **Diagnostics > Export report** to generate a sanitized diagnostic bundle containing system health checks, ETW session traces, and environment verification.
- **Analytics**: Usage analytics (event outcomes, feature usage) are sent via PostHog in the EU to help improve reliability. No filenames, media, screenshots, or personal paths are ever collected. Analytics can be disabled anytime under **Settings > Analytics**. See [docs/analytics.md](docs/analytics.md).

---

## License

This project is licensed under the [MIT License](LICENSE.txt).

```
Copyright (c) 2026 Subham Kumar (SubhamPro11)
Copyright (c) 2026 Leapward-Koex
```
