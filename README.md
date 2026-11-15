# Angel-Core-Logger

A 100% local, privacy-first Windows desktop application (`AngelSystemLoggerPro.exe`) powered by a live **PowerShell Deep Telemetry Engine** that exposes hidden background processes, network sockets, scheduled tasks, startup persistence, and microphone/webcam/location hardware access in real time.

---

## Key Capabilities (v3.0)

### 1. Live Activity Stream & Event Inspector (`Live Feed`)
- **Clickable Category Filter Cards**: Filter between **All Events**, **Hidden BG**, **Network**, **DNS & Web**, **Apps & Tabs**, and **Privacy & Sys** with matching live counters.
- **Right-Side Event Inspector**: Click any activity card to examine its verified publisher badge (`✓ Signed Publisher` / `⚠ Unsigned`), **Parent → Child process tree**, executable path, remote endpoint / hostname, and full command line (with automatic **UTF-16LE `-EncodedCommand` Base64 decoding**).
- **Quick Actions**: **Open File Folder**, **Copy Report**, **Mute Process**, or **End Process** directly from the Inspector or Hidden Process bar.
- **Live Title-Bar Vitals**: Displays real-time **CPU %**, **RAM %**, **Mic status**, **Webcam status**, and **Standard / Admin Elevated** mode.

### 2. Windowless Background X-Ray (`Hidden BG`)
- Inventories every process running without a visible taskbar window.
- Maps `svchost.exe` hosts to their active **Windows Service** names (`WinDefend`, `wuauserv`, `BITS`, `DiagTrack`, etc.).
- Verifies digital signatures quickly via embedded PE certificates and Windows Authenticode catalogs, surfacing unsigned or network-active background processes first with checkboxes to show **Only with external network** or **Hide Microsoft processes**.

### 3. External Network, Listening Ports & Hardware Privacy (`Net & Privacy`)
- **Outbound Sockets (`Outbound`)**: Parses live external `ESTABLISHED` / `SYN_SENT` connections with reverse DNS & network provider identification (`Cloudflare`, `Microsoft / Azure`, `Google LLC`, `AWS / CloudFront`, `Akamai`, `Fastly`, `GitHub`, `Valve / Steam`) and port meaning (`HTTPS 443`, `HTTP 80`, `DNS 53`, `RDP 3389`, etc.).
- **Open Listening Ports (`Listening Ports`)**: Lists every process bound locally waiting for incoming traffic.
- **Hardware Privacy Monitor (`Mic / Cam / Loc`)**: Polls Windows `CapabilityAccessManager\ConsentStore` for **Microphone**, **Webcam**, and **Location** access—raising immediate alerts and live header indicators whenever any app activates or touches hardware sensors.

### 4. Persistence & Integrity Watcher (`Startup & Tasks`)
- **Startup Apps**: Audits `HKCU` and `HKLM` `Run` / `RunOnce` entries plus live alerts whenever a program adds a new Windows auto-start entry.
- **Running Tasks**: Highlights active **Windows Scheduled Tasks** executing silently behind the scenes (`Active`, triggers, descriptions).
- **Temp / Drops**: Monitors `%TEMP%` and `Downloads` during the session for newly written executables and scripts (`.exe`, `.dll`, `.ps1`, `.bat`, `.cmd`, `.msi`, `.vbs`, `.scr`, `.tmp`).

---

## Controls, Tray & Privacy

- **100% In-Memory Monitoring (Zero Disk Logs)**: Captured activity stays strictly in memory inside the window and is never automatically saved to log files or `Documents` folders. Closing or clearing the feed leaves zero telemetry history on disk.
- **Mask IPs (`###`)**: Dynamically masks or reveals IPv4 and IPv6 addresses across the live feed, Inspector, Hidden BG, Network tabs, and manual snapshot exports.
- **Deep Hidden**: Toggles deep telemetry events (windowless background spawns, scheduled tasks, diagnostics, listening ports).
- **Startup**: Registers or removes `AngelSystemLoggerPro.exe` from Windows Startup (`HKCU\...\Run`).
- **System Tray (`NotifyIcon`)**: Minimize or close to the tray while keeping background monitoring active; double-click the tray icon to bring the window back or right-click for **Run Deep X-Ray Scan**, **Pause/Resume Monitoring**, and **Clear In-Memory Feed**.
- **Settings (`⚙ Settings`)**: Configure start minimized, tray behavior, muted process exclusions, optional manual **Save Snapshot As... (.txt)** dialog, and **Restart as Administrator** for protected system command lines.

---

## Quick Start & Build

1. **Run Directly**: Double-click **`AngelSystemLoggerPro.exe`** (automatically extracts the bundled telemetry script if run standalone).
2. **Command-Line Flags**:
   - `--minimized` / `--tray` — launch hidden in the System Tray.
   - `--install-startup` / `--remove-startup` — manage the Windows Startup registry key.
3. **Rebuild from Source** (uses Windows built-in `.NET Framework v4.0` C# compiler — zero external SDKs required):
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Exe.ps1
   ```

---

## Project Structure

- `Start-LocalLogger.ps1` — PowerShell real-time system telemetry collector & standalone CLI monitor.
- `src/Program.cs` — Single-instance launcher, named-pipe window wakeup, and CLI switch handler.
- `src/MainWindow.cs` — WPF dark desktop window, tray icon controller, live event feed, and Inspector views.
- `src/Models.cs` — Persistent settings and observable telemetry view models.
- `src/Helpers.cs` — Async reverse DNS resolver, IP privacy masker, CPU/RAM counters, and process/startup utilities.
- `src/SettingsDialog.cs` — Modal preferences, muted-process editor, admin elevation, and report exporter.
- `Build-Exe.ps1` — Generates `.ico` artwork, embeds `Start-LocalLogger.ps1`, and compiles `AngelSystemLoggerPro.exe`.
