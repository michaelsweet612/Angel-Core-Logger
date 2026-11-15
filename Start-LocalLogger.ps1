<#
.SYNOPSIS
    Angel Core Logger - PowerShell Deep Background & System Activity Engine
.DESCRIPTION
    Captures visible activity AND hidden Windows background activity in real time:
      1. Hidden Background Process Spawns (Parent chain, decoded CommandLine, Publisher Signature, Hosted Services)
      2. Hardware Privacy Access (Microphone, Webcam & Location via Windows ConsentStore)
      3. Outbound Network Connections & Newly Opened Listening Ports (fast netstat + DNS mapping)
      4. Background DNS Lookups & Telemetry Domains
      5. Silent Windows Scheduled Tasks & Windows Service State Transitions
      6. Persistence Watchers (HKCU/HKLM Run, Startup Folder) & Temp/Downloads Executable Drops
      7. Foreground Apps & Active Window / Browser Tab Switches
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
# 1. CONFIGURATION, PATHS & LIVE SETTINGS SYNC
# =========================================================================
$AppDataDir      = Join-Path $env:LOCALAPPDATA "AngelSystemLoggerPro"
$SettingsPath    = Join-Path $AppDataDir "settings.json"
$ScanTriggerPath = Join-Path $AppDataDir "scan.trigger"
if (-not (Test-Path $AppDataDir)) {
    New-Item -ItemType Directory -Path $AppDataDir -Force | Out-Null
}

$Settings = [ordered]@{
    FilterIPAddresses      = (-not $NoFilterIP.IsPresent)
    DeepBackgroundMode     = (-not $NoDeepBackground.IsPresent)
    UseAdminFeatures       = $AdminMode.IsPresent
    EnableFileLogging      = (-not $NoFileLog.IsPresent)
    LogDirectory           = Join-Path $env:USERPROFILE "Documents\LocalActivityLogs"
    LogFilePrefix          = "ActivityLog"
    PollIntervalMs         = $PollMs
    ExcludedProcesses      = @("AngelSystemLoggerPro", "Start-LocalLogger")
}

$script:LastSettingsWriteUtc = [DateTime]::MinValue

function Sync-ExternalSettings {
    if (Test-Path $SettingsPath) {
        try {
            $fi = Get-Item $SettingsPath -ErrorAction Stop
            if ($fi.LastWriteTimeUtc -ne $script:LastSettingsWriteUtc) {
                $script:LastSettingsWriteUtc = $fi.LastWriteTimeUtc
                $raw = Get-Content $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($null -ne $raw) {
                    if ($null -ne $raw.MaskIp)          { $Settings.FilterIPAddresses  = [bool]$raw.MaskIp }
                    if ($null -ne $raw.DeepHiddenMode)  { $Settings.DeepBackgroundMode = [bool]$raw.DeepHiddenMode }
                    if ($null -ne $raw.AutoSaveLogs)    { $Settings.EnableFileLogging  = [bool]$raw.AutoSaveLogs }
                    if ($null -ne $raw.ExcludedProcesses) {
                        $merged = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
                        $merged.Add("AngelSystemLoggerPro") | Out-Null
                        $merged.Add("Start-LocalLogger") | Out-Null
                        foreach ($item in $raw.ExcludedProcesses) {
                            if (-not [string]::IsNullOrWhiteSpace([string]$item)) {
                                $merged.Add(([string]$item).Trim() -replace '\.exe$','') | Out-Null
                            }
                        }
                        $Settings.ExcludedProcesses = @($merged)
                    }
                }
            }
        } catch { }
    }
}

Sync-ExternalSettings

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
    $Masked = [regex]::Replace($Masked, '(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F:]+', {
        param($Match)
        return ($Match.Value -replace '[0-9a-fA-F]', '#')
    })
    return $Masked
}

# =========================================================================
# 2. PROCESS ENCYCLOPEDIA, SIGNATURE VERIFIER & DECODERS
# =========================================================================
$ProcessExplanations = @{
    "svchost"                   = "Windows Service Host (runs core background system services & networking)"
    "dllhost"                   = "COM Surrogate (hosts background DLL plugins outside of main programs)"
    "conhost"                   = "Console Window Host (attached whenever a command-line tool or script runs)"
    "rundll32"                  = "Windows DLL Runner (executes exported functions inside system DLL libraries)"
    "wmiprvse"                  = "WMI Provider Host (handles background system hardware and OS queries)"
    "taskhostw"                 = "Windows Task Host (executes scheduled background tasks & DLL maintenance)"
    "backgroundTaskHost"        = "UWP Background Task Host (runs background tasks for Store apps)"
    "RuntimeBroker"             = "Runtime Broker (brokers security & file permissions for Store/UWP apps)"
    "SearchProtocolHost"        = "Windows Search Protocol Host (crawls local files for search indexing)"
    "SearchFilterHost"          = "Windows Search Content Filter (reads file text in background for indexing)"
    "SearchIndexer"             = "Windows Search Indexer (maintains the local Windows Search index database)"
    "MoUsoCoreWorker"           = "Windows Update USO Worker (silently checks, downloads, and stages updates)"
    "UsoClient"                 = "Update Session Orchestrator Client (triggers background Windows Update scans)"
    "TiWorker"                  = "Windows Modules Installer Worker (applies Windows updates & servicing)"
    "TrustedInstaller"          = "Windows Module Installer (owns and updates protected Windows OS files)"
    "CompatTelRunner"           = "Microsoft Compatibility Telemetry (collects upgrade & telemetry diagnostics)"
    "audiodg"                   = "Windows Audio Device Graph Isolation (processes audio DSP & effects)"
    "smartscreen"               = "Microsoft Defender SmartScreen (checks reputation of downloaded apps/URLs)"
    "MpCmdRun"                  = "Microsoft Defender CLI (updates virus definitions or triggers background scan)"
    "MsMpEng"                   = "Microsoft Defender Antivirus Engine (real-time malware protection service)"
    "NisSrv"                    = "Microsoft Defender Network Inspection (monitors network traffic patterns)"
    "SecurityHealthService"     = "Windows Security Health Service (monitors Defender, firewall, and device health)"
    "sppsvc"                    = "Software Protection Platform (periodically verifies Windows & Office licensing)"
    "WmiApSrv"                  = "WMI Performance Adapter (collects system performance counter data)"
    "consent"                   = "Windows UAC Prompt (verifies User Account Control administrator elevation)"
    "powershell"                = "Windows PowerShell (automation, scripting & administration shell)"
    "pwsh"                      = "PowerShell Core (cross-platform automation & scripting shell)"
    "cmd"                       = "Windows Command Prompt (executes batch scripts or shell commands)"
    "wscript"                   = "Windows Script Host (executes VBScript / JScript silently in background)"
    "cscript"                   = "Console Script Host (executes command-line VBScript / JScript)"
    "mshta"                     = "Microsoft HTML Application Host (runs .hta / inline scripts)"
    "schtasks"                  = "Task Scheduler CLI (creates, edits, or triggers scheduled tasks)"
    "bitsadmin"                 = "BITS Transfer CLI (manages silent background file downloads/uploads)"
    "msiexec"                   = "Windows Installer (installs, repairs, or updates .msi software packages)"
    "WerFault"                  = "Windows Error Reporting (captures crash dumps and sends error diagnostics)"
    "wermgr"                    = "Windows Error Reporting Manager (uploads queued crash/diagnostic reports)"
    "sihost"                    = "Shell Infrastructure Host (manages Start menu, taskbar & shell visuals)"
    "ctfmon"                    = "CTF Loader (manages keyboard layout, IME & text input services)"
    "fontdrvhost"               = "Usermode Font Driver Host (isolates font rendering for security)"
    "dwm"                       = "Desktop Window Manager (renders hardware-accelerated Windows desktop)"
    "spoolsv"                   = "Print Spooler Service (manages local & network print queues)"
    "dasHost"                   = "Device Association Host (pairs Bluetooth, wireless & PnP devices)"
    "AggregatorHost"            = "Windows Telemetry / Usage Aggregator Host"
    "ShellExperienceHost"       = "Windows Shell Experience Host (Start menu, Action Center & flyouts)"
    "StartMenuExperienceHost"   = "Windows Start Menu Host"
    "Widgets"                   = "Windows Widgets Panel"
    "WidgetService"             = "Windows Widgets Background Data Service"
    "PhoneExperienceHost"       = "Microsoft Phone Link (syncs notifications & calls with phone)"
    "OneDrive"                  = "Microsoft OneDrive (syncs local folders with cloud storage)"
    "FileCoAuth"                = "OneDrive File Co-Authoring Helper"
    "GoogleUpdater"             = "Google Updater (checks and applies silent Chrome/Google updates)"
    "updater"                   = "Background Software Updater (silently checks for software updates)"
    "msedgewebview2"            = "Microsoft Edge WebView2 (embedded Chromium browser engine inside apps)"
    "steamwebhelper"            = "Steam Embedded Browser & Background Helper"
    "nvcontainer"               = "NVIDIA Container (manages GPU driver telemetry, overlay & updates)"
    "Aggregator"                = "Background Telemetry / System Aggregator"
    "csrss"                     = "Client/Server Runtime Subsystem (critical core Windows session process)"
    "lsass"                     = "Local Security Authority (handles Windows logins, credentials & tokens)"
    "services"                  = "Windows Service Control Manager (starts, stops & hosts Windows services)"
    "wininit"                   = "Windows Start-Up Application (initializes core system user-mode services)"
    "smss"                      = "Windows Session Manager Subsystem (creates user sessions at boot)"
}

$script:SignatureCache = @{}
$script:PidMetaCache   = @{}
function Get-PublisherInfo ([string]$ExePath, [string]$FallbackName = "") {
    if ([string]::IsNullOrWhiteSpace($ExePath)) {
        $clean = ($FallbackName -replace '\.exe$','').Trim()
        if ($clean -and -not ($ProcessExplanations.ContainsKey($clean) -or $clean -in @("System","Registry","Idle","Memory Compression","dwm","audiodg","fontdrvhost","SecurityHealthService","NisSrv","MsMpEng","SearchHost"))) {
            return @{ Publisher = "Protected / System Process"; Signed = $true; IsMicrosoft = $false }
        }
        return @{ Publisher = "Protected / System"; Signed = $true; IsMicrosoft = $true }
    }
    if ($script:SignatureCache.ContainsKey($ExePath)) {
        return $script:SignatureCache[$ExePath]
    }
    $info = @{ Publisher = "Unsigned / Unverified"; Signed = $false; IsMicrosoft = $false }
    try {
        if (Test-Path -LiteralPath $ExePath) {
            $subj = $null
            $verified = $false
            try {
                $cert = [System.Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($ExePath)
                if ($cert) {
                    $subj = $cert.Subject
                    $verified = $true
                }
            } catch {
                # Catalog-signed Windows binaries (System32/WinSxS) don't embed certs in PE; verify via Get-AuthenticodeSignature
                $sig = Get-AuthenticodeSignature -LiteralPath $ExePath -ErrorAction SilentlyContinue
                if ($sig -and $sig.Status -eq 'Valid' -and $sig.SignerCertificate) {
                    $subj = $sig.SignerCertificate.Subject
                    $verified = $true
                } elseif ($sig) {
                    $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
                    $comp = if ($fvi.CompanyName) { " ($($fvi.CompanyName))" } else { "" }
                    $info = @{ Publisher = "Unverified: $($sig.Status)$comp"; Signed = $false; IsMicrosoft = $false }
                }
            }

            if ($verified -and $subj) {
                $pub = $subj
                if ($subj -match 'O="([^"]+)"') { $pub = $Matches[1] }
                elseif ($subj -match 'O=([^,]+)') { $pub = $Matches[1].Trim() }
                elseif ($subj -match 'CN="([^"]+)"') { $pub = $Matches[1] }
                elseif ($subj -match 'CN=([^,]+)') { $pub = $Matches[1].Trim() }

                $isMs = ($subj -match 'Microsoft')
                $info = @{ Publisher = $pub; Signed = $true; IsMicrosoft = $isMs }
            }
        }
    } catch { }
    $script:SignatureCache[$ExePath] = $info
    return $info
}

function Get-ProcessPathAndTrust ([int]$TargetPid, [string]$ProcName) {
    $clean = ($ProcName -replace '\.exe$','').Trim()
    $key = "$TargetPid-$clean"
    if ($TargetPid -gt 0 -and $script:PidMetaCache.ContainsKey($key)) {
        return $script:PidMetaCache[$key]
    }
    $exePath = ""
    if ($TargetPid -gt 4) {
        try {
            $proc = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
            if ($proc -and $proc.Path) { $exePath = [string]$proc.Path }
        } catch { }
    }
    $pubInfo = Get-PublisherInfo -ExePath $exePath -FallbackName $clean
    $meta = @{ Path = $exePath; Publisher = $pubInfo.Publisher; Signed = $pubInfo.Signed }
    if ($TargetPid -gt 0) {
        $script:PidMetaCache[$key] = $meta
        if ($script:PidMetaCache.Count -gt 3000) { $script:PidMetaCache.Clear() }
    }
    return $meta
}

function Get-ProcessFriendlyDescription ([string]$ProcName, [bool]$IsHidden, [string]$ServiceDesc) {
    $Clean = ($ProcName -replace '\.exe$','').Trim()
    if (-not [string]::IsNullOrWhiteSpace($ServiceDesc)) {
        return "Hosts Windows Service(s): $ServiceDesc"
    }
    if ($ProcessExplanations.ContainsKey($Clean)) {
        return $ProcessExplanations[$Clean]
    }
    if ($IsHidden) {
        return "Windowless background process running silently without a taskbar window"
    }
    return "Desktop application process"
}

function Expand-DecodedCommandLine ([string]$CmdLine) {
    if ([string]::IsNullOrWhiteSpace($CmdLine)) { return "" }
    $trimmed = $CmdLine.Trim()
    if ($trimmed -match '(?i)(?:^|\s)-(?:enc|encodedcommand|en|ec)\s+([A-Za-z0-9+/=]{12,})') {
        $b64 = $Matches[1]
        try {
            $bytes = [System.Convert]::FromBase64String($b64)
            $decoded = [System.Text.Encoding]::Unicode.GetString($bytes).Trim()
            if (-not [string]::IsNullOrWhiteSpace($decoded)) {
                if ($decoded.Length -gt 240) { $decoded = $decoded.Substring(0, 240) + "..." }
                return "$trimmed  [Decoded Base64: $decoded]"
            }
        } catch { }
    }
    return $trimmed
}

function Get-PortMeaning ([int]$Port) {
    switch ($Port) {
        21   { return "FTP File Transfer (Port 21)" }
        22   { return "SSH Remote Shell (Port 22)" }
        23   { return "Telnet Unencrypted Shell (Port 23)" }
        25   { return "SMTP Mail (Port 25)" }
        53   { return "DNS Lookup (Port 53)" }
        80   { return "HTTP Unencrypted Web (Port 80)" }
        123  { return "NTP Time Sync (Port 123)" }
        443  { return "HTTPS Encrypted Traffic (Port 443)" }
        445  { return "SMB Windows File Sharing (Port 445)" }
        587  { return "SMTP Secure Mail (Port 587)" }
        853  { return "DNS-over-TLS (Port 853)" }
        1433 { return "SQL Server (Port 1433)" }
        3389 { return "Remote Desktop RDP (Port 3389)" }
        5228 { return "Push Notification Channel (Port 5228)" }
        5985 { return "WinRM Remote PowerShell HTTP (Port 5985)" }
        5986 { return "WinRM Remote PowerShell HTTPS (Port 5986)" }
        8080 { return "Alt HTTP / Proxy (Port 8080)" }
        8443 { return "Alt HTTPS (Port 8443)" }
        default { return "TCP Port $Port" }
    }
}

# =========================================================================
# 3. FAST NETWORK SNAPSHOT PARSER (netstat -ano)
# =========================================================================
function Get-FastNetSnapshot {
    $established = New-Object System.Collections.ArrayList
    $listening   = New-Object System.Collections.ArrayList

    $rawLines = & netstat.exe -ano 2>$null
    foreach ($line in $rawLines) {
        $t = $line.Trim()
        if ($t -match '^(TCP)\s+(\S+)\s+(\S+)\s+(ESTABLISHED|SYN_SENT|LISTENING)\s+(\d+)$') {
            $localStr  = $Matches[2]
            $remoteStr = $Matches[3]
            $state     = $Matches[4]
            $ownerPid  = [int]$Matches[5]

            $localIp = $localStr; $localPort = 0
            if ($localStr -match '^\[([^\]]+)\]:(\d+)$') { $localIp = $Matches[1]; $localPort = [int]$Matches[2] }
            elseif ($localStr -match '^(.+):(\d+)$') { $localIp = $Matches[1]; $localPort = [int]$Matches[2] }

            $remIp = $remoteStr; $remPort = 0
            if ($remoteStr -match '^\[([^\]]+)\]:(\d+)$') { $remIp = $Matches[1]; $remPort = [int]$Matches[2] }
            elseif ($remoteStr -match '^(.+):(\d+)$') { $remIp = $Matches[1]; $remPort = [int]$Matches[2] }

            if ($state -eq 'LISTENING') {
                [void]$listening.Add(@{
                    Pid = $ownerPid; LocalAddress = $localIp; LocalPort = $localPort; LocalEndpoint = $localStr
                })
            } else {
                [void]$established.Add(@{
                    Pid = $ownerPid; LocalAddress = $localIp; LocalPort = $localPort; LocalEndpoint = $localStr
                    RemoteAddress = $remIp; RemotePort = $remPort; RemoteEndpoint = $remoteStr; State = $state
                })
            }
        }
    }
    return @{ Established = $established; Listening = $listening }
}

# =========================================================================
# 4. EVENT EMITTER (RAW JSON TO GUI, PRIVACY-MASKED TO LOCAL DISK LOG)
# =========================================================================
function Emit-ActivityEvent {
    param(
        [string]$Category,        # HIDDEN, NETWORK, DNS, APP, WINDOW, PRIVACY, TASK, SERVICE, SECURITY, SYSTEM
        [string]$Badge,
        [string]$Title,
        [string]$Subtitle,
        [string]$Details,
        [string]$Severity = "Info",
        [int]$ProcessId = 0,
        [string]$ProcessName = "",
        [string]$ProcessPath = "",
        [string]$ParentChain = "",
        [string]$CommandLine = "",
        [string]$Publisher = "",
        [bool]$IsSigned = $true,
        [string]$RemoteIp = "",
        [int]$RemotePort = 0,
        [string]$ResolvedHost = ""
    )

    $Now = Get-Date
    $TimeFull = $Now.ToString("yyyy-MM-dd HH:mm:ss")
    $TimeShort = $Now.ToString("HH:mm:ss")

    if ($Settings.EnableFileLogging) {
        $LogTitle    = Format-IPFilter $Title
        $LogSubtitle = Format-IPFilter $Subtitle
        $LogDetails  = Format-IPFilter $Details
        $PaddedBadge = $Badge.PadRight(13)
        $LogLine = "[$TimeFull] [$PaddedBadge] $LogTitle | $LogSubtitle $(if ($LogDetails) { "($LogDetails)" })"
        Add-Content -Path (Get-LogFilePath) -Value $LogLine -Encoding UTF8 -ErrorAction SilentlyContinue
    }

    if ($GuiStream) {
        # Send UNMASKED values to GUI so the interactive Mask IPs switch can reveal/mask live
        $Payload = @{
            time         = $TimeShort
            fullTime     = $TimeFull
            category     = $Category
            badge        = $Badge
            title        = $Title
            subtitle     = $Subtitle
            details      = $Details
            severity     = $Severity
            pid          = $ProcessId
            processName  = $ProcessName
            processPath  = $ProcessPath
            parentChain  = $ParentChain
            commandLine  = $CommandLine
            publisher    = $Publisher
            isSigned     = $IsSigned
            remoteIp     = $RemoteIp
            remotePort   = $RemotePort
            resolvedHost = $ResolvedHost
        } | ConvertTo-Json -Compress
        [Console]::Out.WriteLine("ANGEL_EVENT|$Payload")
        [Console]::Out.Flush()
    } else {
        $SafeTitle    = Format-IPFilter $Title
        $SafeSubtitle = Format-IPFilter $Subtitle
        $Color = switch ($Category) {
            "HIDDEN"   { "Yellow" }
            "APP"      { "Green" }
            "WINDOW"   { "Cyan" }
            "NETWORK"  { "DarkYellow" }
            "DNS"      { "Magenta" }
            "PRIVACY"  { "Red" }
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
# 5. HARDWARE PRIVACY MONITOR (MIC, WEBCAM, LOCATION via ConsentStore)
# =========================================================================
$script:ConsentState = @{}
function Get-HardwarePrivacyEntries ([switch]$EmitChanges) {
    $entries = New-Object System.Collections.ArrayList
    $caps = @(
        @{ Key = "microphone"; Label = "Microphone" },
        @{ Key = "webcam";     Label = "Webcam" },
        @{ Key = "location";   Label = "Location" }
    )

    foreach ($cap in $caps) {
        $basePath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\$($cap.Key)"
        if (-not (Test-Path $basePath)) { continue }

        $subItems = New-Object System.Collections.ArrayList
        foreach ($item in (Get-ChildItem $basePath -ErrorAction SilentlyContinue)) {
            if ($item.PSChildName -eq "NonPackaged") {
                foreach ($np in (Get-ChildItem $item.PSPath -ErrorAction SilentlyContinue)) {
                    [void]$subItems.Add(@{ Item = $np; IsNonPackaged = $true })
                }
            } else {
                [void]$subItems.Add(@{ Item = $item; IsNonPackaged = $false })
            }
        }

        foreach ($entry in $subItems) {
            $regItem = $entry.Item
            $props = Get-ItemProperty $regItem.PSPath -ErrorAction SilentlyContinue
            if (-not $props) { continue }

            $startFt = 0L; $stopFt = 0L
            try { if ($props.LastUsedTimeStart) { $startFt = [long]$props.LastUsedTimeStart } } catch {}
            try { if ($props.LastUsedTimeStop)  { $stopFt  = [long]$props.LastUsedTimeStop  } } catch {}
            if ($startFt -le 0) { continue }

            $rawName = $regItem.PSChildName
            $fullPath = ""
            $appName = $rawName
            if ($entry.IsNonPackaged) {
                $fullPath = $rawName -replace '#', '\'
                $appName  = [System.IO.Path]::GetFileName($fullPath)
            } else {
                $appName = ($rawName -split '_')[0]
            }

            $isActiveNow = ($stopFt -eq 0 -or $stopFt -lt $startFt)
            $startTimeStr = [DateTime]::FromFileTime($startFt).ToString("yyyy-MM-dd HH:mm:ss")
            $stopTimeStr  = if ($isActiveNow) { "ACTIVE RIGHT NOW" } else { [DateTime]::FromFileTime($stopFt).ToString("yyyy-MM-dd HH:mm:ss") }
            $durationStr  = if ($isActiveNow) {
                "In use right now"
            } else {
                $span = [DateTime]::FromFileTime($stopFt) - [DateTime]::FromFileTime($startFt)
                if ($span.TotalSeconds -lt 1) { "< 1s" }
                elseif ($span.TotalMinutes -lt 1) { "$([math]::Round($span.TotalSeconds))s" }
                else { "$([math]::Round($span.TotalMinutes, 1))m" }
            }

            $stateKey = "$($cap.Key)|$rawName"
            $prev = $script:ConsentState[$stateKey]
            $script:ConsentState[$stateKey] = @{ Start = $startFt; Stop = $stopFt; Active = $isActiveNow }

            if ($EmitChanges -and $prev) {
                $privPub = if ($entry.IsNonPackaged -and $fullPath) { Get-PublisherInfo -ExePath $fullPath -FallbackName $appName } else { @{ Publisher = "Microsoft Store / UWP App"; Signed = $true } }
                if (-not $prev.Active -and $isActiveNow) {
                    $badge = switch ($cap.Key) { "microphone" { "MIC ACTIVE" } "webcam" { "CAM ACTIVE" } default { "LOC ACTIVE" } }
                    Emit-ActivityEvent -Category "PRIVACY" -Badge $badge `
                        -Title "$appName started using your $($cap.Label)" `
                        -Subtitle "Windows recorded active $($cap.Label) hardware access ($appName)" `
                        -Details "Started: $startTimeStr | Path: $(if ($fullPath) { $fullPath } else { $rawName })" `
                        -Severity "Warning" `
                        -ProcessName $appName -ProcessPath $fullPath `
                        -Publisher $privPub.Publisher -IsSigned $privPub.Signed
                } elseif ($prev.Start -ne $startFt -and -not $isActiveNow) {
                    $badge = switch ($cap.Key) { "microphone" { "MIC ACCESS" } "webcam" { "CAM ACCESS" } default { "LOC ACCESS" } }
                    Emit-ActivityEvent -Category "PRIVACY" -Badge $badge `
                        -Title "$appName accessed your $($cap.Label) ($durationStr)" `
                        -Subtitle "Brief or completed $($cap.Label) access detected in Windows privacy registry" `
                        -Details "Window: $startTimeStr -> $stopTimeStr | Path: $(if ($fullPath) { $fullPath } else { $rawName })" `
                        -Severity "Warning" `
                        -ProcessName $appName -ProcessPath $fullPath `
                        -Publisher $privPub.Publisher -IsSigned $privPub.Signed
                } elseif ($prev.Active -and -not $isActiveNow) {
                    $badge = switch ($cap.Key) { "microphone" { "MIC STOPPED" } "webcam" { "CAM STOPPED" } default { "LOC STOPPED" } }
                    Emit-ActivityEvent -Category "PRIVACY" -Badge $badge `
                        -Title "$appName stopped using your $($cap.Label) ($durationStr)" `
                        -Subtitle "Hardware access ended cleanly" `
                        -Details "Ended: $stopTimeStr | Path: $(if ($fullPath) { $fullPath } else { $rawName })" `
                        -Severity "Info" `
                        -ProcessName $appName -ProcessPath $fullPath `
                        -Publisher $privPub.Publisher -IsSigned $privPub.Signed
                }
            } elseif ($EmitChanges -and -not $prev) {
                $privPub = if ($entry.IsNonPackaged -and $fullPath) { Get-PublisherInfo -ExePath $fullPath -FallbackName $appName } else { @{ Publisher = "Microsoft Store / UWP App"; Signed = $true } }
                $badge = switch ($cap.Key) { "microphone" { "MIC ACCESS" } "webcam" { "CAM ACCESS" } default { "LOC ACCESS" } }
                Emit-ActivityEvent -Category "PRIVACY" -Badge $badge `
                    -Title "$appName accessed your $($cap.Label)" `
                    -Subtitle "New application recorded accessing $($cap.Label)" `
                    -Details "Time: $startTimeStr | Status: $stopTimeStr | Path: $(if ($fullPath) { $fullPath } else { $rawName })" `
                    -Severity "Warning" `
                    -ProcessName $appName -ProcessPath $fullPath `
                    -Publisher $privPub.Publisher -IsSigned $privPub.Signed
            }

            [void]$entries.Add(@{
                capability = $cap.Label
                appName    = $appName
                appPath    = if ($fullPath) { $fullPath } else { $rawName }
                isActive   = $isActiveNow
                startTime  = $startTimeStr
                stopTime   = $stopTimeStr
                duration   = $durationStr
                sortTick   = $startFt
            })
        }
    }

    $sorted = @($entries | Sort-Object @{Expression={ $_.isActive }; Descending=$true}, @{Expression={ $_.sortTick }; Descending=$true} | Select-Object -First 35)
    return $sorted
}

# =========================================================================
# 6. SERVICE PID MAPPER & FULL STRUCTURED SNAPSHOT EMITTER
# =========================================================================
$script:ServicePidMap = @{}
function Refresh-ServicePidMap {
    $map = @{}
    foreach ($s in (Get-CimInstance Win32_Service -Filter "ProcessId > 0")) {
        $svcPid = [int]$s.ProcessId
        if (-not $map.ContainsKey($svcPid)) {
            $map[$svcPid] = New-Object System.Collections.ArrayList
        }
        [void]$map[$svcPid].Add("$($s.Name) ($($s.DisplayName))")
    }
    $script:ServicePidMap = $map
}

function Get-ServiceLabelForPid ([int]$TargetPid) {
    if ($script:ServicePidMap.ContainsKey($TargetPid)) {
        $list = $script:ServicePidMap[$TargetPid]
        if ($list.Count -le 2) { return ($list -join "; ") }
        return "$($list[0]); $($list[1]) (+$($list.Count - 2) more)"
    }
    return ""
}

$script:IpToDomainMap = @{}
function Update-DnsDomainMap {
    foreach ($d in (Get-DnsClientCache)) {
        if ($d.Entry -and $d.Data) {
            $script:IpToDomainMap[$d.Data] = $d.Entry
        }
    }
}

$script:SessionFileDrops = New-Object System.Collections.ArrayList

function Emit-FullSystemSnapshot {
    param(
        [hashtable]$ProcSnapshot,
        $NetSnap,
        $PrivacyEntries,
        $AllTasks
    )
    if (-not $GuiStream) { return }

    # Map CIM info for hidden processes
    $cimByPid = @{}
    foreach ($c in (Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine, WorkingSetSize)) {
        $cimByPid[[int]$c.ProcessId] = $c
    }

    # Count connections per PID
    $connByPid = @{}
    $connList = New-Object System.Collections.ArrayList
    foreach ($c in $NetSnap.Established) {
        if ($c.RemoteAddress -match '^(127\.|0\.|::1|fe80:|10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.|fc00:|fd00:)') {
            continue
        }
        if (-not $connByPid.ContainsKey($c.Pid)) { $connByPid[$c.Pid] = New-Object System.Collections.ArrayList }
        [void]$connByPid[$c.Pid].Add($c.RemoteEndpoint)

        $pObj = $ProcSnapshot[$c.Pid]
        $cimObj = $cimByPid[$c.Pid]
        $pName = if ($pObj) { $pObj.Name } elseif ($cimObj) { $cimObj.Name -replace '\.exe$','' } else { "System" }
        $pPath = if ($cimObj -and $cimObj.ExecutablePath) { $cimObj.ExecutablePath } else { "" }
        $pubInfo = Get-PublisherInfo $pPath
        $svcLabel = Get-ServiceLabelForPid $c.Pid
        $isHidden = ($null -eq $pObj -or $pObj.Handle -eq 0 -or [string]::IsNullOrWhiteSpace($pObj.Title))
        $resolvedDomain = if ($script:IpToDomainMap.ContainsKey($c.RemoteAddress)) { $script:IpToDomainMap[$c.RemoteAddress] } else { "" }

        [void]$connList.Add(@{
            pid           = $c.Pid
            processName   = "$pName.exe"
            serviceLabel  = $svcLabel
            localEndpoint = $c.LocalEndpoint
            remoteEndpoint= $c.RemoteEndpoint
            remoteIp      = $c.RemoteAddress
            remotePort    = $c.RemotePort
            state         = $c.State
            portMeaning   = (Get-PortMeaning $c.RemotePort)
            isHidden      = $isHidden
            publisher     = $pubInfo.Publisher
            isSigned      = $pubInfo.Signed
            resolvedHost  = $resolvedDomain
            processPath   = $pPath
        })
    }

    $listenList = New-Object System.Collections.ArrayList
    $listenSeenSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($l in $NetSnap.Listening) {
        $lk = "$($l.Pid)|$($l.LocalPort)|$($l.LocalAddress)"
        if (-not $listenSeenSet.Add($lk)) { continue }
        $pObj = $ProcSnapshot[$l.Pid]
        $cimObj = $cimByPid[$l.Pid]
        $pName = if ($pObj) { $pObj.Name } elseif ($cimObj) { $cimObj.Name -replace '\.exe$','' } else { "System" }
        $pPath = if ($cimObj -and $cimObj.ExecutablePath) { $cimObj.ExecutablePath } else { "" }
        $pubInfo = Get-PublisherInfo $pPath
        $svcLabel = Get-ServiceLabelForPid $l.Pid
        $isHidden = ($null -eq $pObj -or $pObj.Handle -eq 0 -or [string]::IsNullOrWhiteSpace($pObj.Title))

        [void]$listenList.Add(@{
            pid          = $l.Pid
            processName  = "$pName.exe"
            serviceLabel = $svcLabel
            localAddress = $l.LocalAddress
            localPort    = $l.LocalPort
            portMeaning  = (Get-PortMeaning $l.LocalPort)
            isHidden     = $isHidden
            publisher    = $pubInfo.Publisher
            isSigned     = $pubInfo.Signed
            processPath  = $pPath
        })
    }

    # Hidden background processes (windowless)
    $hiddenList = New-Object System.Collections.ArrayList
    foreach ($kv in $ProcSnapshot.GetEnumerator()) {
        $procPid = [int]$kv.Key
        $pInfo = $kv.Value
        $hasWindow = ($pInfo.Handle -ne 0 -and -not [string]::IsNullOrWhiteSpace($pInfo.Title))
        if ($hasWindow) { continue }

        $cimObj = $cimByPid[$procPid]
        $pPath = if ($cimObj -and $cimObj.ExecutablePath) { $cimObj.ExecutablePath } else { "" }
        $pubInfo = Get-PublisherInfo $pPath
        $svcLabel = Get-ServiceLabelForPid $procPid

        $parentPid = if ($cimObj) { [int]$cimObj.ParentProcessId } else { 0 }
        $parentName = if ($parentPid -and $ProcSnapshot.ContainsKey($parentPid)) {
            "$($ProcSnapshot[$parentPid].Name).exe"
        } elseif ($parentPid -and $cimByPid.ContainsKey($parentPid)) {
            $cimByPid[$parentPid].Name
        } else { "System" }

        $cmd = if ($cimObj -and $cimObj.CommandLine) {
            Expand-DecodedCommandLine $cimObj.CommandLine
        } elseif (-not [string]::IsNullOrWhiteSpace($pPath)) {
            $pPath
        } elseif (-not [string]::IsNullOrWhiteSpace($svcLabel)) {
            "Protected Windows Service Host ($svcLabel)"
        } else {
            "Protected System Process (Run as Admin to read full command line)"
        }

        $memMb = if ($cimObj -and $cimObj.WorkingSetSize) { [math]::Round($cimObj.WorkingSetSize / 1MB, 1) } else { 0.0 }
        $eps = if ($connByPid.ContainsKey($procPid)) { @($connByPid[$procPid] | Select-Object -Unique) } else { @() }

        [void]$hiddenList.Add(@{
            pid          = $procPid
            processName  = "$($pInfo.Name).exe"
            parentName   = "$parentName (PID $parentPid)"
            serviceLabel = $svcLabel
            role         = (Get-ProcessFriendlyDescription -ProcName $pInfo.Name -IsHidden $true -ServiceDesc $svcLabel)
            publisher    = $pubInfo.Publisher
            isSigned     = $pubInfo.Signed
            isMicrosoft  = $pubInfo.IsMicrosoft
            memoryMb     = $memMb
            connCount    = $eps.Count
            endpoints    = ($eps | Select-Object -First 4) -join ", "
            processPath  = $pPath
            commandLine  = $cmd
            sortScore    = ($eps.Count * 1000) + $(if (-not $pubInfo.Signed) { 500 } else { 0 }) + $(if (-not $pubInfo.IsMicrosoft) { 100 } else { 0 }) + $memMb
        })
    }

    $sortedHidden = @($hiddenList | Sort-Object @{Expression={ $_.sortScore }; Descending=$true} | Select-Object -First 85)

    # Startup items (HKCU Run, HKLM Run, Startup Folder)
    $startupItems = New-Object System.Collections.ArrayList
    $regLocations = @(
        @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"; Label = "HKCU Run" },
        @{ Path = "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"; Label = "HKCU RunOnce" },
        @{ Path = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"; Label = "HKLM Run" },
        @{ Path = "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"; Label = "HKLM WOW64" }
    )
    foreach ($loc in $regLocations) {
        if (Test-Path $loc.Path) {
            $props = Get-ItemProperty $loc.Path
            foreach ($prop in $props.PSObject.Properties) {
                if ($prop.Name -notlike "PS*") {
                    $valStr = [string]$prop.Value
                    $exeGuess = ""
                    if ($valStr -match '^\s*"([^"]+)"') { $exeGuess = $Matches[1] }
                    elseif ($valStr -match '^\s*([^\s]+\.exe)') { $exeGuess = $Matches[1] }
                    $pubInfo = Get-PublisherInfo $exeGuess
                    [void]$startupItems.Add(@{
                        kind        = $loc.Label
                        name        = $prop.Name
                        command     = $valStr
                        publisher   = if ($exeGuess) { $pubInfo.Publisher } else { "Registry Command" }
                        isSigned    = $pubInfo.Signed
                        state       = "Enabled"
                        description = "Auto-starts at Windows login from $($loc.Label)"
                    })
                }
            }
        }
    }

    $startupDirs = @(
        @{ Path = [Environment]::GetFolderPath('Startup'); Label = "User Startup Folder" },
        @{ Path = [Environment]::GetFolderPath('CommonStartup'); Label = "All-Users Startup Folder" }
    )
    foreach ($sd in $startupDirs) {
        if ($sd.Path -and (Test-Path $sd.Path)) {
            foreach ($f in (Get-ChildItem $sd.Path -File -ErrorAction SilentlyContinue)) {
                if ($f.Name -eq "desktop.ini") { continue }
                [void]$startupItems.Add(@{
                    kind        = $sd.Label
                    name        = $f.Name
                    command     = $f.FullName
                    publisher   = "Startup Shortcut / Script"
                    isSigned    = $true
                    state       = "Enabled"
                    description = "Launches from $($sd.Label)"
                })
            }
        }
    }

    # Scheduled Tasks (Running or notable active non-disabled tasks)
    $taskRows = New-Object System.Collections.ArrayList
    $running = @($AllTasks | Where-Object { $_.State -eq 'Running' })
    foreach ($t in ($running | Select-Object -First 35)) {
        [void]$taskRows.Add(@{
            kind        = "Scheduled Task"
            name        = $t.TaskName
            command     = "$($t.TaskPath)$($t.TaskName)"
            publisher   = if ($t.Author) { $t.Author } else { "Windows Task Scheduler" }
            isSigned    = $true
            state       = "RUNNING NOW"
            description = if ($t.Description) { $t.Description } else { "Background task running via Windows Task Scheduler" }
        })
    }

    $snapshotObj = @{
        hiddenCount     = $hiddenList.Count
        hiddenProcesses = @($sortedHidden)
        connections     = @($connList | Select-Object -First 100)
        listeningPorts  = @($listenList | Sort-Object { $_.localPort } | Select-Object -First 80)
        privacyItems    = @($PrivacyEntries)
        startupItems    = @($startupItems)
        runningTasks    = @($taskRows)
        fileDrops       = @($script:SessionFileDrops)
    }

    $json = $snapshotObj | ConvertTo-Json -Depth 5 -Compress
    [Console]::Out.WriteLine("ANGEL_SNAPSHOT|$json")
    [Console]::Out.Flush()
}

# =========================================================================
# 7. FAST INITIALIZATION (< 0.5s to ENGINE READY, then async deep baseline)
# =========================================================================
$SelfPid = $PID
$ParentSelfPid = [int](Get-CimInstance Win32_Process -Filter "ProcessId = $PID").ParentProcessId

$Procs = @{}
foreach ($p in (Get-Process)) {
    $Procs[$p.Id] = @{
        Name   = $p.ProcessName
        Title  = $p.MainWindowTitle
        Handle = $p.MainWindowHandle
    }
}

$InitialNet = Get-FastNetSnapshot
$NetSeen = @{}
$ListenSeen = @{}
foreach ($c in $InitialNet.Established) {
    $NetSeen["$($c.Pid)-$($c.RemoteEndpoint)"] = $true
}
foreach ($l in $InitialNet.Listening) {
    $ListenSeen["$($l.Pid)-$($l.LocalEndpoint)"] = $true
}

$WatchDirs = @(
    $env:TEMP,
    (Join-Path $env:USERPROFILE "Downloads")
) | Where-Object { Test-Path $_ }

$SeenFiles = @{}
foreach ($dir in $WatchDirs) {
    foreach ($f in (Get-ChildItem -Path $dir -File -ErrorAction SilentlyContinue)) {
        $SeenFiles[$f.FullName] = $true
    }
}

$StartupSnapshot = @{}
foreach ($rk in @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce")) {
    if (Test-Path $rk) {
        $Props = Get-ItemProperty -Path $rk
        foreach ($p in $Props.PSObject.Properties) {
            if ($p.Name -notlike "PS*") {
                $StartupSnapshot["$rk|$($p.Name)"] = [string]$p.Value
            }
        }
    }
}

$InitialPrivacy = Get-HardwarePrivacyEntries
$IsElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Emit-ActivityEvent -Category "SYSTEM" -Badge "ENGINE READY" `
    -Title "PowerShell Deep Telemetry Engine online ($($Procs.Count) active processes indexed)" `
    -Subtitle "Monitoring windowless background spawns, Mic/Webcam/Location privacy, sockets, DNS, tasks & persistence ($(if ($IsElevated) { 'Elevated Admin' } else { 'Standard Mode' }))." `
    -Details "Local Log: $(Get-LogFilePath)" `
    -Severity "Success" `
    -Publisher "Angel Core Logger"

# Populate heavy baseline maps immediately after ENGINE READY is already visible in the UI
Refresh-ServicePidMap
Update-DnsDomainMap

$Services = @{}
foreach ($s in (Get-Service)) {
    $Services[$s.Name] = @{
        Status      = [string]$s.Status
        DisplayName = $s.DisplayName
    }
}

$AllTasks = @(Get-ScheduledTask)
$Tasks = @{}
foreach ($t in $AllTasks) {
    $Tasks["$($t.TaskPath)$($t.TaskName)"] = [string]$t.State
}

$DnsSeen = @{}
foreach ($d in (Get-DnsClientCache)) {
    if ($d.Entry) { $DnsSeen[$d.Entry] = $true }
}

$RunningTasksNow = @($AllTasks | Where-Object { $_.State -eq 'Running' })
if ($RunningTasksNow.Count -gt 0) {
    $SampleNames = ($RunningTasksNow | Select-Object -First 4 -ExpandProperty TaskName) -join ", "
    Emit-ActivityEvent -Category "TASK" -Badge "BG TASKS" `
        -Title "$($RunningTasksNow.Count) Windows Scheduled Tasks are running silently right now" `
        -Subtitle "Active background tasks include: $SampleNames (check 'Startup & Tasks' tab for full details)" `
        -Details "Task Scheduler baseline scan" `
        -Severity "Hidden" `
        -Publisher "Windows Scheduler" -IsSigned $true
}

# Emit initial snapshot so all 4 tabs populate right away
Emit-FullSystemSnapshot -ProcSnapshot $Procs -NetSnap $InitialNet -PrivacyEntries $InitialPrivacy -AllTasks $AllTasks

# =========================================================================
# 8. FAST REAL-TIME MONITORING LOOP
# =========================================================================
$LoopCounter = 0

while ($true) {
    $LoopCounter++
    Sync-ExternalSettings

    # Check if GUI requested an immediate Deep X-Ray snapshot
    $ForceSnapshot = $false
    if (Test-Path $ScanTriggerPath) {
        Remove-Item $ScanTriggerPath -Force -ErrorAction SilentlyContinue
        $ForceSnapshot = $true
        Refresh-ServicePidMap
        Update-DnsDomainMap
        $AllTasks = @(Get-ScheduledTask)
    }

    # --- A. Process Spawns, Hidden Background Processes & Active Window Titles ---
    $CurrentProcs = @{}
    $NewPids = New-Object System.Collections.Generic.List[int]

    foreach ($p in (Get-Process)) {
        if ($p.Id -eq $SelfPid -or $p.Id -eq $ParentSelfPid) { continue }
        if ($Settings.ExcludedProcesses -contains $p.ProcessName) { continue }

        $CurrentProcs[$p.Id] = @{
            Name   = $p.ProcessName
            Title  = $p.MainWindowTitle
            Handle = $p.MainWindowHandle
        }

        if (-not $Procs.ContainsKey($p.Id)) {
            $NewPids.Add($p.Id)
        }
        elseif ($p.MainWindowTitle -and $Procs[$p.Id].Title -ne $p.MainWindowTitle) {
            $wMeta = Get-ProcessPathAndTrust $p.Id $p.ProcessName
            Emit-ActivityEvent -Category "WINDOW" -Badge "ACTIVE TAB" `
                -Title "$($p.ProcessName).exe switched to '$($p.MainWindowTitle)'" `
                -Subtitle "Active window or browser tab title changed" `
                -Details "PID: $($p.Id) | Process: $($p.ProcessName).exe" `
                -Severity "Info" `
                -ProcessId $p.Id -ProcessName "$($p.ProcessName).exe" -ProcessPath $wMeta.Path `
                -Publisher $wMeta.Publisher -IsSigned $wMeta.Signed
        }
    }

    if ($NewPids.Count -gt 0) {
        $CimMap = @{}
        foreach ($cimObj in (Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine)) {
            $CimMap[[int]$cimObj.ProcessId] = $cimObj
        }
        if ($NewPids | Where-Object { $CurrentProcs[$_].Name -eq "svchost" }) {
            Refresh-ServicePidMap
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
                "System"
            }

            $exePath = if ($cim -and $cim.ExecutablePath) { $cim.ExecutablePath } else { "" }
            $pubInfo = Get-PublisherInfo -ExePath $exePath -FallbackName $pName
            $script:PidMetaCache["$newId-$pName"] = @{ Path = $exePath; Publisher = $pubInfo.Publisher; Signed = $pubInfo.Signed }
            $svcLabel = Get-ServiceLabelForPid $newId
            $cmdLine = if ($cim -and $cim.CommandLine) {
                Expand-DecodedCommandLine $cim.CommandLine
            } elseif ($exePath) {
                $exePath
            } elseif ($svcLabel) {
                "Protected Windows Service Host ($svcLabel)"
            } else {
                "Protected system process (Run as Admin for command line)"
            }

            $friendlyDesc = Get-ProcessFriendlyDescription -ProcName $pName -IsHidden (-not $hasWindow) -ServiceDesc $svcLabel
            $parentChain = "$parentName (PID $parentPid) -> $pName.exe (PID $newId)"
            $isScriptHost = ($pName -in @("powershell", "pwsh", "cmd", "wscript", "cscript", "mshta", "rundll32", "schtasks", "bitsadmin"))

            if ($hasWindow) {
                Emit-ActivityEvent -Category "APP" -Badge "APP OPENED" `
                    -Title "$pName.exe opened ('$($pInfo.Title)')" `
                    -Subtitle "$friendlyDesc — launched by $parentName" `
                    -Details "Publisher: $($pubInfo.Publisher) | PID: $newId | Parent: $parentName" `
                    -Severity "Success" `
                    -ProcessId $newId -ProcessName "$pName.exe" -ProcessPath $exePath `
                    -ParentChain $parentChain -CommandLine $cmdLine `
                    -Publisher $pubInfo.Publisher -IsSigned $pubInfo.Signed
            } elseif ($Settings.DeepBackgroundMode) {
                $badge = if ($isScriptHost) { "SILENT SCRIPT" } else { "HIDDEN SPAWN" }
                $sev   = if ($isScriptHost -or -not $pubInfo.Signed) { "Warning" } else { "Hidden" }
                $svcTag = if ($svcLabel) { " [$svcLabel]" } else { "" }
                Emit-ActivityEvent -Category "HIDDEN" -Badge $badge `
                    -Title "Background process $pName.exe$svcTag started silently by $parentName" `
                    -Subtitle "$friendlyDesc (no window shown)" `
                    -Details "Publisher: $($pubInfo.Publisher) | PID: $newId | Parent: $parentName (PID $parentPid)" `
                    -Severity $sev `
                    -ProcessId $newId -ProcessName "$pName.exe" -ProcessPath $exePath `
                    -ParentChain $parentChain -CommandLine $cmdLine `
                    -Publisher $pubInfo.Publisher -IsSigned $pubInfo.Signed
            }
        }
    }

    foreach ($oldId in $Procs.Keys) {
        if (-not $CurrentProcs.ContainsKey($oldId)) {
            $oldProc = $Procs[$oldId]
            if ($Settings.ExcludedProcesses -contains $oldProc.Name) { continue }
            $exitKey = "$oldId-$($oldProc.Name)"
            $exitMeta = if ($script:PidMetaCache.ContainsKey($exitKey)) { $script:PidMetaCache[$exitKey] } else { Get-PublisherInfo -ExePath "" -FallbackName $oldProc.Name }
            $wasVisible = ($oldProc.Handle -ne 0 -and -not [string]::IsNullOrWhiteSpace($oldProc.Title))
            if ($wasVisible) {
                Emit-ActivityEvent -Category "APP" -Badge "APP CLOSED" `
                    -Title "$($oldProc.Name).exe closed" `
                    -Subtitle "Application exited (window: '$($oldProc.Title)')" `
                    -Details "PID: $oldId" `
                    -Severity "Info" `
                    -ProcessId $oldId -ProcessName "$($oldProc.Name).exe" -ProcessPath $(if ($exitMeta.Path) { $exitMeta.Path } else { "" }) `
                    -Publisher $exitMeta.Publisher -IsSigned $exitMeta.Signed
            } elseif ($Settings.DeepBackgroundMode -and $oldProc.Name -notin @("conhost", "backgroundTaskHost")) {
                Emit-ActivityEvent -Category "HIDDEN" -Badge "BG EXIT" `
                    -Title "Background process $($oldProc.Name).exe exited" `
                    -Subtitle (Get-ProcessFriendlyDescription -ProcName $oldProc.Name -IsHidden $true -ServiceDesc "") `
                    -Details "PID: $oldId" `
                    -Severity "Info" `
                    -ProcessId $oldId -ProcessName "$($oldProc.Name).exe" -ProcessPath $(if ($exitMeta.Path) { $exitMeta.Path } else { "" }) `
                    -Publisher $exitMeta.Publisher -IsSigned $exitMeta.Signed
            }
        }
    }
    $Procs = $CurrentProcs

    # --- B. DNS Cache Lookups & Telemetry Domains ---
    foreach ($d in (Get-DnsClientCache)) {
        if ($d.Data -and $d.Entry) { $script:IpToDomainMap[$d.Data] = $d.Entry }
        if ($d.Entry -and -not $DnsSeen.ContainsKey($d.Entry)) {
            $DnsSeen[$d.Entry] = $true
            if ($d.Entry -notin @("localhost", "wpad")) {
                $isTelemetry = ($d.Entry -match "telemetry|vortex|settings-win|data\.microsoft|events\.data|aria|metrics|analytics|crashlytics|diag")
                $badge = if ($isTelemetry) { "BG TELEMETRY" } else { "DNS LOOKUP" }
                $sub = if ($isTelemetry) {
                    "Background diagnostic or telemetry domain resolved silently"
                } else {
                    "Domain resolved$(if ($d.Data) { " to $($d.Data)" })"
                }
                Emit-ActivityEvent -Category "DNS" -Badge $badge `
                    -Title "Resolved domain: $($d.Entry)" `
                    -Subtitle $sub `
                    -Details "Record: $($d.Data) | TTL: $($d.TimeToLive)s" `
                    -Severity $(if ($isTelemetry) { "Warning" } else { "Accent" }) `
                    -RemoteIp $d.Data -ResolvedHost $d.Entry
            }
        }
    }

    # --- C. Fast Network Connections & Open Listening Ports ---
    $NetSnap = Get-FastNetSnapshot
    foreach ($c in $NetSnap.Established) {
        if ($c.Pid -eq $SelfPid -or $c.Pid -eq $ParentSelfPid) { continue }
        if ($c.RemoteAddress -notmatch "^(127\.|0\.|::1|fe80:|10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.|fc00:|fd00:)") {
            $Key = "$($c.Pid)-$($c.RemoteEndpoint)"
            if (-not $NetSeen.ContainsKey($Key)) {
                $NetSeen[$Key] = $true
                $procInfo = $CurrentProcs[$c.Pid]
                $appName = if ($procInfo) { $procInfo.Name } else { (Get-Process -Id $c.Pid).ProcessName }
                if (-not $appName) { $appName = "System" }
                if ($Settings.ExcludedProcesses -contains $appName) { continue }

                $isHiddenApp = ($null -eq $procInfo -or $procInfo.Handle -eq 0 -or [string]::IsNullOrWhiteSpace($procInfo.Title))
                $svcLabel = Get-ServiceLabelForPid $c.Pid
                $svcNote = if ($svcLabel) { " [$svcLabel]" } else { "" }
                $portDesc = Get-PortMeaning $c.RemotePort
                $knownHost = if ($script:IpToDomainMap.ContainsKey($c.RemoteAddress)) { $script:IpToDomainMap[$c.RemoteAddress] } else { "" }

                $visibilityNote = if ($isHiddenApp) { "Windowless background process" } else { "Active desktop app" }
                $netMeta = Get-ProcessPathAndTrust $c.Pid $appName
                $netChain = if ($svcLabel) { "Windows Service -> $appName.exe [$svcLabel] (PID $($c.Pid))" } else { "$appName.exe (PID $($c.Pid))" }
                Emit-ActivityEvent -Category "NETWORK" -Badge $(if ($isHiddenApp) { "BG NETWORK" } else { "INTERNET" }) `
                    -Title "$appName.exe$svcNote connected to $($c.RemoteEndpoint)" `
                    -Subtitle "$visibilityNote via $portDesc" `
                    -Details "PID: $($c.Pid) | Local: $($c.LocalEndpoint) -> Remote: $($c.RemoteEndpoint) ($($c.State))" `
                    -Severity $(if ($isHiddenApp) { "Hidden" } else { "Info" }) `
                    -ProcessId $c.Pid -ProcessName "$appName.exe" -ProcessPath $netMeta.Path `
                    -ParentChain $netChain -Publisher $netMeta.Publisher -IsSigned $netMeta.Signed `
                    -RemoteIp $c.RemoteAddress -RemotePort $c.RemotePort -ResolvedHost $knownHost
            }
        }
    }

    if ($Settings.DeepBackgroundMode) {
        foreach ($l in $NetSnap.Listening) {
            if ($l.Pid -eq $SelfPid -or $l.Pid -eq $ParentSelfPid) { continue }
            $LKey = "$($l.Pid)-$($l.LocalEndpoint)"
            if (-not $ListenSeen.ContainsKey($LKey)) {
                $ListenSeen[$LKey] = $true
                $procInfo = $CurrentProcs[$l.Pid]
                $appName = if ($procInfo) { $procInfo.Name } else { (Get-Process -Id $l.Pid).ProcessName }
                if (-not $appName) { $appName = "System" }
                if ($Settings.ExcludedProcesses -contains $appName) { continue }

                $portMeta = Get-ProcessPathAndTrust $l.Pid $appName
                $pSvcLabel = Get-ServiceLabelForPid $l.Pid
                $portChain = if ($pSvcLabel) { "Windows Service -> $appName.exe [$pSvcLabel] (PID $($l.Pid))" } else { "$appName.exe (PID $($l.Pid))" }
                Emit-ActivityEvent -Category "NETWORK" -Badge "PORT OPENED" `
                    -Title "$appName.exe opened listening port $($l.LocalPort) on $($l.LocalAddress)" `
                    -Subtitle "Process started listening for incoming connections ($(Get-PortMeaning $l.LocalPort))" `
                    -Details "PID: $($l.Pid) | Bind: $($l.LocalEndpoint)" `
                    -Severity "Warning" `
                    -ProcessId $l.Pid -ProcessName "$appName.exe" -ProcessPath $portMeta.Path `
                    -ParentChain $portChain -Publisher $portMeta.Publisher -IsSigned $portMeta.Signed -RemotePort $l.LocalPort
            }
        }
    }

    # --- D. Hardware Privacy Monitor (Mic / Webcam / Location) every 2 cycles (~1.8s) ---
    $PrivacyNow = $null
    if ($LoopCounter % 2 -eq 0 -or $ForceSnapshot) {
        $PrivacyNow = Get-HardwarePrivacyEntries -EmitChanges
    }

    # --- E. Windows Service Transitions every 3 cycles ---
    if ($LoopCounter % 3 -eq 0 -or $ForceSnapshot) {
        $svcChanged = $false
        foreach ($s in (Get-Service)) {
            $statusStr = [string]$s.Status
            if ($Services.ContainsKey($s.Name) -and $Services[$s.Name].Status -ne $statusStr) {
                $oldStatus = $Services[$s.Name].Status
                $Services[$s.Name].Status = $statusStr
                $svcChanged = $true
                Emit-ActivityEvent -Category "SERVICE" -Badge "WINDOWS SVC" `
                    -Title "Service '$($s.DisplayName)' changed from $oldStatus to $statusStr" `
                    -Subtitle "Background Windows Service ($($s.Name)) transitioned state" `
                    -Details "Service: $($s.Name) | State: $oldStatus -> $statusStr" `
                    -Severity "Accent"
            }
        }
        if ($svcChanged) { Refresh-ServicePidMap }
    }

    # --- F. Persistence & Dropped Executables every 4 cycles (~3.6s) ---
    if ($Settings.DeepBackgroundMode -and ($LoopCounter % 4 -eq 0 -or $ForceSnapshot)) {
        foreach ($dir in $WatchDirs) {
            foreach ($f in (Get-ChildItem -Path $dir -File -ErrorAction SilentlyContinue)) {
                if (-not $SeenFiles.ContainsKey($f.FullName)) {
                    $SeenFiles[$f.FullName] = $true
                    if ($f.Extension -match "^\.(exe|dll|ps1|bat|cmd|msi|vbs|scr|tmp)$") {
                        $sizeKb = [math]::Round($f.Length / 1KB, 1)
                        $dropNote = "$($f.Name) ($sizeKb KB) created in $($f.DirectoryName) at $(Get-Date -Format 'HH:mm:ss')"
                        [void]$script:SessionFileDrops.Add(@{
                            name = $f.Name; path = $f.FullName; sizeKb = $sizeKb; time = (Get-Date -Format 'HH:mm:ss')
                        })
                        $dropPub = Get-PublisherInfo -ExePath $f.FullName
                        Emit-ActivityEvent -Category "SECURITY" -Badge "FILE DROPPED" `
                            -Title "New $($f.Extension) file created in $(Split-Path $dir -Leaf): $($f.Name)" `
                            -Subtitle "A background process or download wrote an executable/script file ($sizeKb KB)" `
                            -Details "Path: $($f.FullName) | Size: $sizeKb KB" `
                            -Severity "Warning" `
                            -ProcessPath $f.FullName -Publisher $dropPub.Publisher -IsSigned $dropPub.Signed
                    }
                }
            }
        }

        foreach ($rk in @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce")) {
            if (Test-Path $rk) {
                $Props = Get-ItemProperty -Path $rk
                foreach ($p in $Props.PSObject.Properties) {
                    if ($p.Name -notlike "PS*") {
                        $sKey = "$rk|$($p.Name)"
                        $val = [string]$p.Value
                        if (-not $StartupSnapshot.ContainsKey($sKey)) {
                            $StartupSnapshot[$sKey] = $val
                            Emit-ActivityEvent -Category "SECURITY" -Badge "STARTUP MOD" `
                                -Title "New Windows Startup entry added: '$($p.Name)'" `
                                -Subtitle "A program registered itself to auto-start at Windows sign-in" `
                                -Details "Registry: $rk\$($p.Name) = $val" `
                                -Severity "Warning" `
                                -CommandLine $val
                        }
                    }
                }
            }
        }
    }

    # --- G. Scheduled Tasks Check & Full Snapshot Refresh every 8 cycles (~7.2s) or on demand ---
    if ($ForceSnapshot -or ($LoopCounter % 8 -eq 0)) {
        if ($Settings.DeepBackgroundMode) {
            $AllTasks = @(Get-ScheduledTask)
            foreach ($t in $AllTasks) {
                $tKey = "$($t.TaskPath)$($t.TaskName)"
                $tState = [string]$t.State
                if ($Tasks.ContainsKey($tKey) -and $Tasks[$tKey] -ne $tState) {
                    $oldState = $Tasks[$tKey]
                    $Tasks[$tKey] = $tState
                    $taskPub = if ($t.Author) { $t.Author } else { "Windows Scheduler" }
                    if ($tState -eq "Running") {
                        Emit-ActivityEvent -Category "TASK" -Badge "SCHED TASK" `
                            -Title "Scheduled Task '$($t.TaskName)' started silently in background" `
                            -Subtitle "Triggered automatically by Windows Task Scheduler ($($t.TaskPath))" `
                            -Details "Task: $tKey | Author: $($t.Author) | $($t.Description)" `
                            -Severity "Hidden" -Publisher $taskPub -IsSigned $true
                    } else {
                        Emit-ActivityEvent -Category "TASK" -Badge "TASK DONE" `
                            -Title "Scheduled Task '$($t.TaskName)' finished ($tState)" `
                            -Subtitle "Background scheduled task completed" `
                            -Details "Task: $tKey | State: $oldState -> $tState" `
                            -Severity "Info" -Publisher $taskPub -IsSigned $true
                    }
                }
            }
        }
        if ($null -eq $PrivacyNow) { $PrivacyNow = Get-HardwarePrivacyEntries }
        Emit-FullSystemSnapshot -ProcSnapshot $Procs -NetSnap $NetSnap -PrivacyEntries $PrivacyNow -AllTasks $AllTasks
    }

    Start-Sleep -Milliseconds $Settings.PollIntervalMs
}
