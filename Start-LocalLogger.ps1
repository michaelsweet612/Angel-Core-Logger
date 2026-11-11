<#
.SYNOPSIS
    Angel Core Logger Pro - Deep Background & System Activity PowerShell Engine
.DESCRIPTION
    Uses PowerShell, CIM/WMI, NetTCPConnection, DnsClientCache, ScheduledTask, and Registry/File
    watchers to capture both visible user activity AND hidden background Windows activity that
    most users never see:
      1. Hidden Background Process Spawns (with Parent Process chain & full Command-Line arguments)
      2. Silent Windows Scheduled Tasks triggering in the background
      3. Outbound Network Connections & Newly Opened Listening Ports (with IP Privacy Masking)
      4. Background DNS Domain Resolutions & Telemetry Lookups
      5. Windows Services Starting / Stopping
      6. Temp / Downloads Executable Drops & Startup Persistence Changes
      7. Foreground Applications & Active Window/Browser Tab Switches

    Supports both:
      - GUI Stream Mode (-GuiStream): Streams JSON events to the Angel Core Logger desktop window
      - Interactive Console Mode: Runs directly in a PowerShell terminal
#>

param(
    [switch]$GuiStream,
    [switch]$NoFilterIP,
    [switch]$NoDeepBackground,
    [switch]$AdminMode,
    [switch]$NoFileLog,
    [int]$PollMs = 900
)

$ErrorActionPreference = "SilentlyContinue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# =========================================================================
# 1. SETTINGS & STATE
# =========================================================================
$Settings = [ordered]@{
    FilterIPAddresses      = (-not $NoFilterIP.IsPresent)
    DeepBackgroundMode     = (-not $NoDeepBackground.IsPresent)
    UseAdminFeatures       = $AdminMode.IsPresent
    EnableFileLogging      = (-not $NoFileLog.IsPresent)
    LogDirectory           = Join-Path $env:USERPROFILE "Documents\LocalActivityLogs"
    LogFilePrefix          = "ActivityLog"
    PollIntervalMs         = $PollMs
}

if ($Settings.EnableFileLogging -and -not (Test-Path $Settings.LogDirectory)) {
    New-Item -ItemType Directory -Path $Settings.LogDirectory -Force | Out-Null
}

function Get-LogFilePath {
    $DateStamp = Get-Date -Format "yyyy-MM-dd"
    return Join-Path $Settings.LogDirectory "$($Settings.LogFilePrefix)_$DateStamp.log"
}

function Format-IPFilter ([string]$Text) {
    if (-not $Settings.FilterIPAddresses -or [string]::IsNullOrEmpty($Text)) {
        return $Text
    }
    $Masked = [regex]::Replace($Text, '\b(?:\d{1,3}\.){3}\d{1,3}\b', {
        param($Match)
        return ($Match.Value -replace '\d', '#')
    })
    $Masked = [regex]::Replace($Masked, '\b(?:[0-9a-fA-F]{1,4}:){2,}[0-9a-fA-F:]+\b', {
        param($Match)
        return ($Match.Value -replace '[0-9a-fA-F]', '#')
    })
    return $Masked
}

# Friendly descriptions for common hidden Windows processes
$ProcessExplanations = @{
    "svchost"                = "Windows Service Host (runs background system services & network tasks)"
    "dllhost"                = "COM Surrogate (hosts background DLL components outside of main apps)"
    "conhost"                = "Console Window Host (spawned whenever a CLI or hidden script runs)"
    "rundll32"               = "Windows 32-bit DLL Runner (executes functions inside system DLLs)"
    "wmiprvse"               = "WMI Provider Host (executes system hardware/process queries in background)"
    "taskhostw"              = "Windows Task Host (runs background scheduled tasks & maintenance)"
    "backgroundTaskHost"     = "UWP Background Task Host (runs background tasks for Microsoft Store apps)"
    "RuntimeBroker"          = "Runtime Broker (manages permissions for Windows Store / UWP apps)"
    "SearchProtocolHost"     = "Windows Search Indexer Protocol Host (scans files in background)"
    "SearchFilterHost"       = "Windows Search Content Filter (extracts text from files for indexing)"
    "MoUsoCoreWorker"        = "Windows Update USO Worker (silently checks & prepares Windows updates)"
    "TiWorker"               = "Windows Modules Installer Worker (installs updates/servicing in background)"
    "TrustedInstaller"       = "Windows Module Installer Service (modifies protected system files)"
    "CompatTelRunner"        = "Microsoft Compatibility Telemetry (scans system for update telemetry)"
    "audiodg"                = "Windows Audio Device Graph Isolation (DSP & audio engine processing)"
    "smartscreen"            = "Windows Defender SmartScreen (checks downloaded files & app reputation)"
    "MpCmdRun"               = "Microsoft Defender CLI Utility (background signature update or scan)"
    "MsMpEng"                = "Microsoft Defender Antivirus Engine (real-time background scanner)"
    "sppsvc"                 = "Microsoft Software Protection Platform (verifies Windows licensing)"
    "WmiApSrv"               = "WMI Performance Adapter (collects performance counters)"
    " consent"               = "Windows UAC Elevation Prompt (User Account Control security check)"
    "powershell"             = "Windows PowerShell Engine (automation & system management script)"
    "pwsh"                   = "PowerShell Core Engine (automation & script execution)"
    "cmd"                    = "Windows Command Processor (batch or shell command execution)"
}

function Get-ProcessFriendlyDescription ([string]$ProcName, [bool]$IsHidden) {
    $Clean = $ProcName -replace '\.exe$',''
    if ($ProcessExplanations.ContainsKey($Clean)) {
        return $ProcessExplanations[$Clean]
    }
    if ($IsHidden) {
        return "Background process running without a visible taskbar window"
    }
    return "Desktop application process"
}

function Emit-ActivityEvent {
    param(
        [string]$Category,      # HIDDEN, APP, WINDOW, NETWORK, DNS, TASK, SERVICE, SECURITY, SYSTEM
        [string]$Badge,         # Short badge label
        [string]$Title,         # Friendly headline
        [string]$Subtitle,      # Plain-English explanation
        [string]$Details,       # Technical details (PID, Parent, CmdLine, IP/Port)
        [string]$Severity = "Info" # Info, Hidden, Warning, Success, Accent
    )

    $Now = Get-Date
    $TimeFull = $Now.ToString("yyyy-MM-dd HH:mm:ss")
    $TimeShort = $Now.ToString("HH:mm:ss")

    $SafeTitle    = Format-IPFilter $Title
    $SafeSubtitle = Format-IPFilter $Subtitle
    $SafeDetails  = Format-IPFilter $Details

    if ($Settings.EnableFileLogging) {
        $PaddedBadge = $Badge.PadRight(13)
        $LogLine = "[$TimeFull] [$PaddedBadge] $SafeTitle | $SafeSubtitle $(if ($SafeDetails) { "($SafeDetails)" })"
        Add-Content -Path (Get-LogFilePath) -Value $LogLine -Encoding UTF8 -ErrorAction SilentlyContinue
    }

    if ($GuiStream) {
        $Payload = @{
            time     = $TimeShort
            fullTime = $TimeFull
            category = $Category
            badge    = $Badge
            title    = $SafeTitle
            subtitle = $SafeSubtitle
            details  = $SafeDetails
            severity = $Severity
        } | ConvertTo-Json -Compress
        [Console]::Out.WriteLine("ANGEL_EVENT|$Payload")
        [Console]::Out.Flush()
    } else {
        $Color = switch ($Category) {
            "HIDDEN"   { "Yellow" }
            "APP"      { "Green" }
            "WINDOW"   { "Cyan" }
            "NETWORK"  { "DarkYellow" }
            "DNS"      { "Magenta" }
            "TASK"     { "Blue" }
            "SERVICE"  { "Blue" }
            "SECURITY" { "Red" }
            default    { "Gray" }
        }
        Write-Host "[$TimeShort] " -NoNewline -ForegroundColor DarkGray
        Write-Host "[$($Badge.PadRight(13))] " -NoNewline -ForegroundColor $Color
        Write-Host "$SafeTitle " -NoNewline -ForegroundColor White
        Write-Host "- $SafeSubtitle" -ForegroundColor Gray
    }
}

# =========================================================================
# 2. INITIAL BASELINE SNAPSHOT
# =========================================================================
$SelfPid = $PID
$ParentSelfPid = (Get-CimInstance Win32_Process -Filter "ProcessId = $PID").ParentProcessId

$Procs = @{}
Get-Process | ForEach-Object {
    $Procs[$_.Id] = @{
        Name   = $_.ProcessName
        Title  = $_.MainWindowTitle
        Handle = $_.MainWindowHandle
    }
}

$Services = @{}
Get-Service | ForEach-Object {
    $Services[$_.Name] = @{
        Status      = [string]$_.Status
        DisplayName = $_.DisplayName
    }
}

$Tasks = @{}
Get-ScheduledTask | ForEach-Object {
    $Tasks["$($_.TaskPath)$($_.TaskName)"] = [string]$_.State
}

$DnsSeen = @{}
Get-DnsClientCache | ForEach-Object {
    if ($_.Entry) { $DnsSeen[$_.Entry] = $true }
}

$NetSeen = @{}
$ListenSeen = @{}
Get-NetTCPConnection | ForEach-Object {
    if ($_.State -eq 'Established' -or $_.State -eq 'SynSent') {
        $NetSeen["$($_.OwningProcess)-$($_.RemoteAddress):$($_.RemotePort)"] = $true
    } elseif ($_.State -eq 'Listen') {
        $ListenSeen["$($_.OwningProcess)-$($_.LocalAddress):$($_.LocalPort)"] = $true
    }
}

# Watch Temp & Downloads for newly dropped executables/scripts
$WatchDirs = @(
    $env:TEMP,
    (Join-Path $env:USERPROFILE "Downloads")
) | Where-Object { Test-Path $_ }

$SeenFiles = @{}
foreach ($dir in $WatchDirs) {
    Get-ChildItem -Path $dir -File -ErrorAction SilentlyContinue | ForEach-Object {
        $SeenFiles[$_.FullName] = $true
    }
}

# Snapshot Startup Registry keys
$StartupSnapshot = @{}
$RunPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path $RunPath) {
    $Props = Get-ItemProperty -Path $RunPath
    foreach ($p in $Props.PSObject.Properties) {
        if ($p.Name -notlike "PS*") {
            $StartupSnapshot[$p.Name] = [string]$p.Value
        }
    }
}

Emit-ActivityEvent -Category "SYSTEM" -Badge "ENGINE READY" `
    -Title "PowerShell Deep Activity Engine connected ($($Procs.Count) active processes, $($Services.Count) services indexed)" `
    -Subtitle "Monitoring visible apps, hidden background spawns, scheduled tasks, DNS lookups, network sockets, and startup items." `
    -Details "Log File: $(Get-LogFilePath) | IP Privacy Mask: $($Settings.FilterIPAddresses) | Deep Background: $($Settings.DeepBackgroundMode)" `
    -Severity "Success"

# Emit initial summary of notable hidden background processes already running
$HiddenNetCount = 0
foreach ($c in (Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue)) {
    if ($c.RemoteAddress -notmatch "^(127\.|0\.|::1|fe80:)") {
        $HiddenNetCount++
    }
}
$RunningTasks = @(Get-ScheduledTask | Where-Object { $_.State -eq 'Running' })
if ($RunningTasks.Count -gt 0) {
    $TaskNames = ($RunningTasks | Select-Object -First 4 -ExpandProperty TaskName) -join ", "
    Emit-ActivityEvent -Category "TASK" -Badge "BG TASKS" `
        -Title "$($RunningTasks.Count) Windows Scheduled Tasks are actively running in the background right now" `
        -Subtitle "Active tasks include: $TaskNames" `
        -Details "Baseline scan of Windows Task Scheduler" `
        -Severity "Hidden"
}

$LoopCounter = 0

# =========================================================================
# 3. LIVE MONITORING LOOP
# =========================================================================
while ($true) {
    $LoopCounter++

    # --- A. Process Spawns, Hidden Background Processes & Active Window Titles ---
    $CurrentProcs = @{}
    $NewPids = New-Object System.Collections.Generic.List[int]

    foreach ($p in (Get-Process)) {
        if ($p.Id -eq $SelfPid -or $p.Id -eq $ParentSelfPid) { continue }
        if ($p.ProcessName -in @("AngelSystemLoggerPro", "Start-LocalLogger")) { continue }

        $CurrentProcs[$p.Id] = @{
            Name   = $p.ProcessName
            Title  = $p.MainWindowTitle
            Handle = $p.MainWindowHandle
        }

        if (-not $Procs.ContainsKey($p.Id)) {
            $NewPids.Add($p.Id)
        }
        elseif ($p.MainWindowTitle -and $Procs[$p.Id].Title -ne $p.MainWindowTitle) {
            Emit-ActivityEvent -Category "WINDOW" -Badge "ACTIVE TAB" `
                -Title "$($p.ProcessName).exe switched active window to '$($p.MainWindowTitle)'" `
                -Subtitle "Foreground window or browser tab title changed" `
                -Details "PID: $($p.Id) | Process: $($p.ProcessName).exe" `
                -Severity "Info"
        }
    }

    # Enrich new processes with CIM parent process + command line details
    if ($NewPids.Count -gt 0) {
        $CimMap = @{}
        Get-CimInstance Win32_Process | ForEach-Object {
            $CimMap[[int]$_.ProcessId] = $_
        }

        foreach ($newId in $NewPids) {
            $pInfo = $CurrentProcs[$newId]
            $pName = $pInfo.Name
            $hasWindow = ($pInfo.Handle -ne 0 -and -not [string]::IsNullOrWhiteSpace($pInfo.Title))
            $cim = $CimMap[$newId]

            $parentPid = if ($cim) { [int]$cim.ParentProcessId } else { 0 }
            $parentName = if ($parentPid -and $CurrentProcs.ContainsKey($parentPid)) {
                "$($CurrentProcs[$parentPid].Name).exe"
            } elseif ($parentPid -and $Procs.ContainsKey($parentPid)) {
                "$($Procs[$parentPid].Name).exe"
            } elseif ($cim -and $CimMap.ContainsKey($parentPid)) {
                $CimMap[$parentPid].Name
            } else {
                "System / Explorer"
            }

            $cmdLine = if ($cim -and $cim.CommandLine) { $cim.CommandLine.Trim() } else { "$pName.exe" }
            if ($cmdLine.Length -gt 220) { $cmdLine = $cmdLine.Substring(0, 220) + "..." }

            $friendlyDesc = Get-ProcessFriendlyDescription -ProcName $pName -IsHidden (-not $hasWindow)

            if ($hasWindow) {
                Emit-ActivityEvent -Category "APP" -Badge "APP OPENED" `
                    -Title "$pName.exe launched ('$($pInfo.Title)')" `
                    -Subtitle "$friendlyDesc — started by $parentName" `
                    -Details "PID: $newId | Parent: $parentName (PID $parentPid) | Cmd: $cmdLine" `
                    -Severity "Success"
            } elseif ($Settings.DeepBackgroundMode) {
                Emit-ActivityEvent -Category "HIDDEN" -Badge "HIDDEN SPAWN" `
                    -Title "Silent background process $pName.exe started by $parentName" `
                    -Subtitle "$friendlyDesc (no visible window shown to user)" `
                    -Details "PID: $newId | Spawned by: $parentName (PID $parentPid) | Cmd: $cmdLine" `
                    -Severity "Hidden"
            }
        }
    }

    # Detect closed processes
    foreach ($oldId in $Procs.Keys) {
        if (-not $CurrentProcs.ContainsKey($oldId)) {
            $oldProc = $Procs[$oldId]
            $wasVisible = ($oldProc.Handle -ne 0 -and -not [string]::IsNullOrWhiteSpace($oldProc.Title))
            if ($wasVisible) {
                Emit-ActivityEvent -Category "APP" -Badge "APP CLOSED" `
                    -Title "$($oldProc.Name).exe closed" `
                    -Subtitle "Application exited (last window: '$($oldProc.Title)')" `
                    -Details "PID: $oldId" `
                    -Severity "Info"
            } elseif ($Settings.DeepBackgroundMode -and $oldProc.Name -notin @("conhost", "backgroundTaskHost")) {
                Emit-ActivityEvent -Category "HIDDEN" -Badge "BG EXIT" `
                    -Title "Background process $($oldProc.Name).exe finished and exited" `
                    -Subtitle (Get-ProcessFriendlyDescription -ProcName $oldProc.Name -IsHidden $true) `
                    -Details "PID: $oldId" `
                    -Severity "Info"
            }
        }
    }
    $Procs = $CurrentProcs

    # --- B. DNS Domain Lookups & Hidden Telemetry Queries ---
    foreach ($d in (Get-DnsClientCache)) {
        if ($d.Entry -and -not $DnsSeen.ContainsKey($d.Entry)) {
            $DnsSeen[$d.Entry] = $true
            if ($d.Entry -notin @("localhost", "wpad")) {
                $isTelemetry = ($d.Entry -match "telemetry|vortex|settings-win|data\.microsoft|events\.data|aria|metrics|analytics|crashlytics|diag")
                $badge = if ($isTelemetry) { "BG TELEMETRY" } else { "DNS LOOKUP" }
                $sub = if ($isTelemetry) {
                    "Background telemetry or diagnostic domain resolved silently"
                } else {
                    "Domain name resolved to IP $(if ($d.Data) { $d.Data } else { 'address' })"
                }
                Emit-ActivityEvent -Category "DNS" -Badge $badge `
                    -Title "Resolved domain: $($d.Entry)" `
                    -Subtitle $sub `
                    -Details "Record Data: $($d.Data) | TTL: $($d.TimeToLive)s" `
                    -Severity $(if ($isTelemetry) { "Warning" } else { "Accent" })
            }
        }
    }

    # --- C. Outbound Connections & New Listening Ports ---
    foreach ($c in (Get-NetTCPConnection)) {
        if ($c.OwningProcess -eq $SelfPid -or $c.OwningProcess -eq $ParentSelfPid) { continue }

        if ($c.State -eq 'Established' -or $c.State -eq 'SynSent') {
            if ($c.RemoteAddress -notmatch "^(127\.|0\.|::1|fe80:)") {
                $Key = "$($c.OwningProcess)-$($c.RemoteAddress):$($c.RemotePort)"
                if (-not $NetSeen.ContainsKey($Key)) {
                    $NetSeen[$Key] = $true
                    $procInfo = $CurrentProcs[$c.OwningProcess]
                    $appName = if ($procInfo) { $procInfo.Name } else { (Get-Process -Id $c.OwningProcess).ProcessName }
                    if (-not $appName) { $appName = "System/Kernel (PID $($c.OwningProcess))" }

                    $isHiddenApp = ($null -eq $procInfo -or [string]::IsNullOrWhiteSpace($procInfo.Title))
                    $portDesc = switch ($c.RemotePort) {
                        443  { "HTTPS Encrypted Traffic (Port 443)" }
                        80   { "HTTP Unencrypted Web (Port 80)" }
                        53   { "DNS Resolution (Port 53)" }
                        853  { "DNS-over-TLS (Port 853)" }
                        5228 { "Push Notification Channel (Port 5228)" }
                        default { "TCP Port $($c.RemotePort)" }
                    }

                    $visibilityNote = if ($isHiddenApp) { "Hidden/background process" } else { "Active application" }
                    Emit-ActivityEvent -Category "NETWORK" -Badge $(if ($isHiddenApp) { "BG NETWORK" } else { "INTERNET" }) `
                        -Title "$appName.exe connected to $($c.RemoteAddress):$($c.RemotePort)" `
                        -Subtitle "$visibilityNote communicating externally via $portDesc" `
                        -Details "PID: $($c.OwningProcess) | Local: $($c.LocalAddress):$($c.LocalPort) -> Remote: $($c.RemoteAddress):$($c.RemotePort) ($($c.State))" `
                        -Severity $(if ($isHiddenApp) { "Hidden" } else { "Info" })
                }
            }
        }
        elseif ($Settings.DeepBackgroundMode -and $c.State -eq 'Listen') {
            $LKey = "$($c.OwningProcess)-$($c.LocalAddress):$($c.LocalPort)"
            if (-not $ListenSeen.ContainsKey($LKey)) {
                $ListenSeen[$LKey] = $true
                $procInfo = $CurrentProcs[$c.OwningProcess]
                $appName = if ($procInfo) { $procInfo.Name } else { (Get-Process -Id $c.OwningProcess).ProcessName }
                if (-not $appName) { $appName = "PID $($c.OwningProcess)" }

                Emit-ActivityEvent -Category "NETWORK" -Badge "PORT OPENED" `
                    -Title "$appName.exe opened listening port $($c.LocalPort) on $($c.LocalAddress)" `
                    -Subtitle "Process is now listening for incoming network connections on port $($c.LocalPort)" `
                    -Details "PID: $($c.OwningProcess) | Bind Address: $($c.LocalAddress):$($c.LocalPort)" `
                    -Severity "Warning"
            }
        }
    }

    # --- D. Windows Services State Changes ---
    foreach ($s in (Get-Service)) {
        $statusStr = [string]$s.Status
        if ($Services.ContainsKey($s.Name) -and $Services[$s.Name].Status -ne $statusStr) {
            $oldStatus = $Services[$s.Name].Status
            $Services[$s.Name].Status = $statusStr
            Emit-ActivityEvent -Category "SERVICE" -Badge "WINDOWS SVC" `
                -Title "Service '$($s.DisplayName)' changed from $oldStatus to $statusStr" `
                -Subtitle "Background Windows Service ($($s.Name)) transitioned state" `
                -Details "Service Name: $($s.Name) | State: $oldStatus -> $statusStr" `
                -Severity "Accent"
        }
    }

    # --- E. Periodic Deep Checks (Scheduled Tasks, Startup Registry & Temp Files) every ~3 cycles ---
    if ($Settings.DeepBackgroundMode -and ($LoopCounter % 3 -eq 0)) {
        foreach ($t in (Get-ScheduledTask)) {
            $tKey = "$($t.TaskPath)$($t.TaskName)"
            $tState = [string]$t.State
            if ($Tasks.ContainsKey($tKey) -and $Tasks[$tKey] -ne $tState) {
                $oldState = $Tasks[$tKey]
                $Tasks[$tKey] = $tState
                if ($tState -eq "Running") {
                    Emit-ActivityEvent -Category "TASK" -Badge "SCHED TASK" `
                        -Title "Scheduled Task '$($t.TaskName)' started silently in the background" `
                        -Subtitle "Triggered automatically by Windows Task Scheduler ($($t.TaskPath))" `
                        -Details "Task Path: $tKey | State: $oldState -> $tState" `
                        -Severity "Hidden"
                } else {
                    Emit-ActivityEvent -Category "TASK" -Badge "TASK DONE" `
                        -Title "Scheduled Task '$($t.TaskName)' finished ($tState)" `
                        -Subtitle "Background scheduled task completed execution" `
                        -Details "Task Path: $tKey | State: $oldState -> $tState" `
                        -Severity "Info"
                }
            }
        }

        # Check for new files dropped in Temp or Downloads
        foreach ($dir in $WatchDirs) {
            foreach ($f in (Get-ChildItem -Path $dir -File -ErrorAction SilentlyContinue)) {
                if (-not $SeenFiles.ContainsKey($f.FullName)) {
                    $SeenFiles[$f.FullName] = $true
                    if ($f.Extension -match "^\.(exe|dll|ps1|bat|cmd|msi|vbs|scr|tmp)$") {
                        $sizeKb = [math]::Round($f.Length / 1KB, 1)
                        Emit-ActivityEvent -Category "SECURITY" -Badge "FILE DROPPED" `
                            -Title "New $($f.Extension) file created in $(Split-Path $dir -Leaf): $($f.Name)" `
                            -Subtitle "Background installer or process wrote a new file ($sizeKb KB)" `
                            -Details "Full Path: $($f.FullName) | Size: $sizeKb KB" `
                            -Severity "Warning"
                    }
                }
            }
        }

        # Check for Startup Registry modifications
        if (Test-Path $RunPath) {
            $Props = Get-ItemProperty -Path $RunPath
            foreach ($p in $Props.PSObject.Properties) {
                if ($p.Name -notlike "PS*") {
                    $val = [string]$p.Value
                    if (-not $StartupSnapshot.ContainsKey($p.Name)) {
                        $StartupSnapshot[$p.Name] = $val
                        Emit-ActivityEvent -Category "SECURITY" -Badge "STARTUP MOD" `
                            -Title "New Windows Startup entry registered: '$($p.Name)'" `
                            -Subtitle "A program registered itself to launch automatically when you sign into Windows" `
                            -Details "Registry: HKCU\...\Run\$($p.Name) = $val" `
                            -Severity "Warning"
                    }
                }
            }
        }
    }

    Start-Sleep -Milliseconds $Settings.PollIntervalMs
}
