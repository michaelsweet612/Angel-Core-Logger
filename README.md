# Angel-Core-Logger

A basic little thing to help you log everything and activities on your computer completely 100 percent privacy and locally.

---

## Features

- **100% Local & Private**: No cloud sync, no external telemetry—all logs stay on your machine under `Documents\LocalActivityLogs\ActivityLog_YYYY-MM-DD.log`.
- **IP Address Privacy Filter (ON by Default)**: Automatically masks IPv4 and IPv6 addresses with `#` (`###.###.###.###`) in both the live console and saved log files. Toggleable live at any time.
- **Standalone Windows Startup `.exe`**:
  - Runs directly from [`AngelSystemLoggerPro.exe`](AngelSystemLoggerPro.exe) with a custom icon and embedded script.
  - Registers natively in Windows Startup (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) so it appears in **Task Manager → Startup apps** and **Windows Settings → Apps → Startup**.
- **Real-Time Activity Monitoring**:
  1. **Opened & Closed Applications** (`[APP OPENED]` / `[APP CLOSED]`)
  2. **Active Window & Browser Tab Changes** (`[ACTIVE TAB]`)
  3. **Website DNS Lookups** (`[WEBSITE]`)
  4. **Outbound Internet Connections** (`[INTERNET]`)
  5. **Windows Service State Changes** (`[SERVICE]`)

---

## Quick Start

### Option 1: Run the Standalone Executable
Double-click **`AngelSystemLoggerPro.exe`**.

Command-line options for Windows Startup registration:
```powershell
# Register in Windows Startup Apps
.\AngelSystemLoggerPro.exe --install-startup

# Remove from Windows Startup Apps
.\AngelSystemLoggerPro.exe --remove-startup
```

### Option 2: Run the PowerShell Script Directly
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start-LocalLogger.ps1
```

---

## Live Keyboard Controls

While the logger window is active, press any of these keys to toggle settings immediately:

| Key | Action |
| :---: | :--- |
| **`I`** | Toggle **IP Address Privacy Filter** (`ON` masks digits with `#` / `OFF` reveals raw IPs) |
| **`U`** | Toggle **Windows Startup Registration** (`ON` / `OFF` in Task Manager Startup Apps) |
| **`A`** | Toggle **Administration Mode** (`OFF` by default for standard user mode) |
| **`F`** | Toggle **Local File Logging** (`ON` saves to `Documents\LocalActivityLogs` / `OFF` console only) |
| **`1`** | Toggle **Application Open/Close** logging |
| **`2`** | Toggle **Window & Browser Tab Title** logging |
| **`3`** | Toggle **Website DNS Lookup** logging |
| **`4`** | Toggle **External Internet Connection** logging |
| **`5`** | Toggle **Windows Service** logging |
| **`Ctrl+C`** | Stop the logger |

---

## Rebuilding the Executable

If you customize [`Start-LocalLogger.ps1`](Start-LocalLogger.ps1) or [`AngelSystemLoggerPro.cs`](AngelSystemLoggerPro.cs), rebuild the standalone `.exe` and icon with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Exe.ps1
```

---

## Repository Files

- [`AngelSystemLoggerPro.exe`](AngelSystemLoggerPro.exe) — Standalone Windows executable with embedded script and icon.
- [`Start-LocalLogger.ps1`](Start-LocalLogger.ps1) — Core PowerShell activity monitoring and logging engine.
- [`AngelSystemLoggerPro.cs`](AngelSystemLoggerPro.cs) — Native C# wrapper and Windows Startup registry manager.
- [`Build-Exe.ps1`](Build-Exe.ps1) — Build script that generates the icon and compiles `AngelSystemLoggerPro.exe`.
- [`AngelSystemLoggerPro.ico`](AngelSystemLoggerPro.ico) — Application icon.
