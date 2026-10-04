# LockscreenGif

A modern Windows application to set your Windows lockscreen to an animated GIF or video.

Windows does not natively allow animated GIFs or videos on the lockscreen through standard Settings (even with file extension spoofing). **LockscreenGif** bypasses this limitation by modifying the Windows system lockscreen image cache to seamlessly display high-quality animated GIFs on your lockscreen.

---

## 🎬 Demos

### Video File Input Demo
https://github.com/user-attachments/assets/1dc4ec39-2f38-42c6-8216-3c911f6d8bc9

### GIF File Input Demo
https://github.com/Leapward-Koex/LockscreenGif/assets/30615050/7448e59f-9767-4509-8ce3-721cf1783faa

---

## ✨ Features

- **Animated Lockscreen:** Set any GIF or video file directly as your Windows lockscreen.
- **High-Quality Conversion:** Converts videos to high-quality GIFs using [FFmpeg](https://www.ffmpeg.org/) and [Gifski](https://gif.ski/).
- **Privileged Helper Service:** Safely modifies the Windows Lock Screen image cache without requiring full admin privileges on every run.
- **Trimming & Customization:** Built-in video editor to trim, adjust frame rate, resolution, and quality before applying.
- **Diagnostic Logging & Reporting:** Built-in log viewer and export tools for quick troubleshooting.

---

## 📋 System Requirements

- **Operating System:** Windows 11 24H2 (Build 26100) or later, 64-bit (x64).
- **Runtime:** Microsoft [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0).
  > **Note:** The desktop runtime is required. The MSI installer does not automatically install the runtime.
- **Permissions:** Administrator access is needed during setup to install the privileged cache helper service.

---

## 🚀 Installation Instructions

Choose one of the installation methods below:

### Method 1: Windows Installer (MSI) — Recommended

1. **Install Prerequisites:**
   - Download and install the **.NET 10 Desktop Runtime (x64)** from Microsoft's official site:
     👉 [Download .NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **Download the Installer:**
   - Go to the [Releases](https://github.com/SubhamPro11/lockscreengtf/releases) page.
   - Download the latest `LockscreenGif-<version>-x64.msi` (or `installLockscreenGif.msi`).
3. **Run the Installer:**
   - Double-click the downloaded `.msi` file.
   - Follow the on-screen setup wizard.
   - Accept the UAC prompt to allow installation of the app and privileged helper service.
4. **Launch the App:**
   - Open **LockscreenGif** from your Windows Start Menu or Desktop shortcut.

---

### Method 2: Portable Package (ZIP)

1. **Install Prerequisites:**
   - Ensure the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) is installed on your computer.
2. **Download the Archive:**
   - Go to [Releases](https://github.com/SubhamPro11/lockscreengtf/releases) and download `LockscreenGif-<version>-win-x64.zip`.
3. **Extract:**
   - Extract the contents of the ZIP archive to a folder of your choice (e.g., `C:\Tools\LockscreenGif`).
4. **Run:**
   - Launch `LockscreenGif.exe`.
   - On first run, grant administrator permissions when prompted so the helper service can configure the lockscreen cache.

---

### Method 3: Building from Source

If you want to build and customize the project locally:

#### Prerequisites
- Windows 11 x64 (Build 26100 or later)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (matches version specified in `global.json`)
- Visual Studio 2022 / 2025 (with *.NET Desktop Development* workload) or VS Code with C# Dev Kit
- PowerShell 7+ (pwsh)

#### Build Steps

1. **Clone the Repository:**
   ```powershell
   git clone https://github.com/SubhamPro11/lockscreengtf.git
   cd lockscreengtf
   ```

2. **Restore Dependencies & Tools:**
   ```powershell
   dotnet restore
   dotnet tool restore
   ```

3. **Build the Solution:**
   ```powershell
   dotnet build LockscreenGif.sln -c Release
   ```

4. **Publish the Desktop Application:**
   ```powershell
   dotnet publish LockScreenGif/LockscreenGif.csproj -c Release -p:PublishProfile=FolderProfile
   ```
   The compiled binaries will be output to `artifacts/app/` (or `LockScreenGif/bin/x64/Release/net10.0-windows10.0.26100.0/`).

5. **Run the Application:**
   ```powershell
   ./artifacts/app/LockscreenGif.exe
   ```

---

## 📖 How to Use

1. **Open LockscreenGif.**
2. Choose your input:
   - **GIF File:** Select an existing `.gif` image.
   - **Video File:** Select a video (`.mp4`, `.webm`, `.mov`, etc.). The app will launch the video editor to trim and convert your clip using FFmpeg and Gifski.
3. Click **Set Lockscreen**.
4. Lock your PC (<kbd>Win</kbd> + <kbd>L</kbd>) to preview your new animated lockscreen!

---

## 🛠️ Diagnostics & Troubleshooting

- **Permissions & Cache Error:** Windows lock screen cache is protected by system permissions. Make sure the Privileged Helper service is running.
- **Logs:** Open **Settings > Logs** to view current log files, or select **Save logs as ZIP…** to export logs.
- **Diagnostic Report:** Open **Diagnostics > Export report** to generate a sanitized diagnostic bundle for issue reporting. See [docs/diagnostics.md](docs/diagnostics.md).
- **Analytics:** Basic anonymous usage analytics can be toggled on/off under **Settings > Analytics**. See [docs/analytics.md](docs/analytics.md).

---

## 🧑‍💻 Contributing & Code Style

- Follow the code formatting guidelines in [docs/code-style.md](docs/code-style.md).
- To format C# files before committing, run:
  ```powershell
  dotnet tool restore
  ./scripts/Format-CSharp.ps1
  ```
- To verify formatting without modifying files:
  ```powershell
  ./scripts/Format-CSharp.ps1 -Check
  ```

---

## 📄 License

This project is licensed under the terms described in [LICENSE.txt](LICENSE.txt) and [LICENSE.rtf](LICENSE.rtf).
