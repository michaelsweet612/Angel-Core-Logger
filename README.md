# Angel-Core-Logger

A friendly, 100% local and private Windows desktop application powered by a live **PowerShell Deep Background Telemetry Engine** that reveals everything happening on your computer—including hidden background activity most Windows users never see.

---

## What It Shows You (Visible & Hidden Background Activity)

- **Hidden Background Process Spawns (`HIDDEN SPAWN` / `HIDDEN NET BG`)**:
  - Detects processes launching silently with no taskbar window (`svchost`, `dllhost`, `conhost`, `rundll32`, `wmiprvse`, `MoUsoCoreWorker`, `CompatTelRunner`, updaters, and background helpers).
  - Reveals the **Parent Process chain** (which process spawned it) and the **full Command-Line arguments**, plus a plain-English explanation of what the component does.
- **Silent Windows Scheduled Tasks (`SCHED TASK` / `BG TASKS`)**:
  - Watches Windows Task Scheduler in real time and alerts you whenever a background task triggers or completes behind the scenes.
- **Background Network & Listening Ports (`BG NETWORK` / `INTERNET` / `PORT OPENED`)**:
  - Shows which hidden or visible processes are talking to external servers (`HTTPS 443`, `HTTP 80`, `DNS 53`, etc.) or opening local listening ports.
- **DNS Lookups & Hidden Telemetry Queries (`DNS LOOKUP` / `BG TELEMETRY`)**:
  - Logs every domain resolved in the Windows DNS cache and highlights background diagnostic/telemetry endpoints.
- **Startup Persistence & Dropped Files (`STARTUP MOD` / `FILE DROPPED`)**:
  - Monitors `HKCU\...\Run` startup registrations and watches `%TEMP%` and `Downloads` for newly dropped `.exe`, `.dll`, `.ps1`, `.bat`, or `.msi` files.
- **Foreground Apps, Active Browser/Window Tabs & Windows Services (`APP OPENED`, `ACTIVE TAB`, `WINDOWS SVC`)**:
  - Tracks opened/closed desktop apps, active window title switches, and Windows Service state changes.

---

## Friendly Dark Desktop UI

- **Category Filter Dropdown (`All activity ⌄`)**: Instantly filter between *All activity*, *Hidden background*, *Network & connections*, *DNS & websites*, *Apps & windows*, *Tasks & services*, and *Security & startup*.
- **Live Search Box**: Filter the activity stream in real time by process name, domain, PID, port, or command-line flag.
- **`+ Deep Background Scan` Button**: Runs an on-demand PowerShell X-Ray scan of hidden windowless processes holding active internet connections, running scheduled tasks, cached DNS domains, and registered Windows startup items.
- **Bottom Privacy & Control Bar**:
  - **Mask IPs (`###`)**: ON by default — masks IPv4 and IPv6 digits with `#` in both the live window and saved log files.
  - **Deep Hidden Mode**: ON by default — toggles hidden background process, telemetry, and scheduled task events.
  - **Startup**: ON by default — registers `AngelSystemLoggerPro.exe` in Windows Startup (`Task Manager → Startup apps`).
  - **Live / Pause**: Pause the visual feed at any time to inspect events without losing background file logs.

---

## Quick Start

1. **Launch the Desktop App**: Double-click **`AngelSystemLoggerPro.exe`**.
2. **Local Logs Folder**: Click **`Open Log Folder`** inside the window (or browse to `Documents\LocalActivityLogs\ActivityLog_YYYY-MM-DD.log`).
3. **Rebuild Anytime**:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Exe.ps1
   ```
