# Design Note: Safe Single-Instance Behavior Proposal

**Status:** PROPOSAL ONLY — AWAITING REVIEW & APPROVAL  
**Target:** `LockScreenGif` Primary Process  
**Author:** Senior Windows Desktop Engineer  

---

## 1. Problem Statement
`LockscreenGif` currently has no single-instance mutex. If the app is already running in the background (e.g., in the notification tray from Windows startup), launching it from the Start Menu, a desktop shortcut, or a new build starts a second concurrent instance. The second instance attempts to listen on or connect to the same named pipes (`LockscreenGif_WallpaperPlayer_Pipe`), causing IPC collisions, dual tray icons, or visual confusion over which build is active.

---

## 2. Minimal Safe Implementation Architecture

### 2.1 Mechanism
1. **Per-Session Named Mutex:**
   Acquire a session-scoped named mutex early in `Program.Main` / `App()`:
   ```csharp
   private static Mutex? _instanceMutex;
   private const string MutexName = @"Local\LockscreenGif_SingleInstance_Mutex";
   ```
   *Why `Local\` instead of `Global\`?* `Local\` isolates the mutex to the interactive Windows terminal session, allowing Fast User Switching without cross-session permission denials or denial-of-service.

2. **Command-Line Bypasses (Critical):**
   The single-instance redirect MUST be bypassed for worker and helper roles:
   ```csharp
   if (args.Contains("--wallpaper-player") || args.Contains("--autostart-worker"))
   {
       // Do not acquire primary mutex; run dedicated worker pipeline
       return;
   }
   ```

3. **Activation Signal to Existing Instance:**
   If `_instanceMutex.WaitOne(0, false)` returns `false` (another instance exists):
   - Send a broadcast or direct message via `RegisterWindowMessage("LockscreenGif_ActivateWindow")` or lightweight local Named Pipe.
   - Primary instance responds by:
     ```csharp
     App.MainWindow.AppWindow.Show();
     App.MainWindow.Activate();
     Win32Helper.SetForegroundWindow(hwnd);
     ```
   - Secondary instance exits cleanly with code `0`.

---

## 3. Comprehensive Risk Assessment

| Area | Risk | Mitigation |
|---|---|---|
| **Wallpaper Player Process** | Secondary process launched with `--wallpaper-player` blocked from starting. | Explicit check for `--wallpaper-player` in command-line arguments BEFORE mutex acquisition. |
| **Tray-Only Execution** | Existing instance is hidden in tray; user double-clicks desktop shortcut expecting a window. | Message receiver must call `MainWindow.AppWindow.Show()` and restore window state (`ShowWindow(SW_RESTORE)`), not just activate. |
| **Privileged Helper IPC** | Concurrent UAC elevation during diagnostics conflicts with primary pipe. | The Privileged Helper is an isolated binary (`LockscreenGif.Privileged.Helper.exe`) with its own contract pipe, completely untouched by app mutex. |
| **Crash Recovery & Abandoned Mutex** | App crash leaves mutex in abandoned state. | Handle `AbandonedMutexException` as successful acquisition. |
| **Startup Delay / Autostart** | Startup entry with `--autostart` and `--delay` delays window creation. | Single-instance signal unhides window immediately even if delay timer was pending. |

---

## 4. Rollback Plan
If single-instance logic produces edge-case hangs or blocks worker processes, removing the 12-line check in `Program.Main` restores immediate multi-instance behavior with zero data loss.
