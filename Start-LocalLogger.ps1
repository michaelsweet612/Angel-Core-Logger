<#
.SYNOPSIS
    Angel System Logger Pro - Local System Activity Logger & Startup Monitor
.DESCRIPTION
    Monitors and logs local computer activity (Opened/Closed Apps, Active Window/Browser Tabs,
    DNS Website Lookups, External Internet Connections, and Windows Service state changes)
    both to the console and to a local log file.

    Includes configurable settings and live keyboard toggles for:
      - IP Address Filtering (ON by default -> masks IP digits with '#', OFF -> reveals raw IPs)
      - Administration Mode (OFF by default -> runs in standard user mode without admin actions)
      - Granular Exclusion Toggles (Apps, Window Titles, Websites/DNS, Internet, Services)
      - Windows Startup Registration (registers the EXE in Windows Startup / Task Manager Startup Apps)
#>

& {
    # =========================================================================
    # 1. USER SETTINGS & PRIVACY TOGGLES
    # =========================================================================
    $Settings = [ordered]@{
        # --- Privacy & Security Filters ---
        FilterIPAddresses      = $true    # ON by default: masks IP addresses as ###.###.###.### (OFF reveals raw IPs)
        UseAdminFeatures       = $false   # OFF by default: skips admin-only calls like Clear-DnsClientCache

        # --- Local Log File Settings ---
        EnableFileLogging      = $true    # Save all events to a local log file on disk
        LogDirectory           = Join-Path $env:USERPROFILE "Documents\LocalActivityLogs"
        LogFilePrefix          = "ActivityLog"

        # --- Startup Integration ---
        InstallToStartup       = $true    # Enabled by default for Startup EXE (press [U] live to toggle ON/OFF)

        # --- What to Log (Set to $false to stop logging specific categories) ---
        LogApps                = $true    # Track opened and closed applications
        LogWindowTitles        = $true    # Track active window and browser tab title changes
        LogWebsites            = $true    # Track DNS domain lookups
        LogInternetConnections = $true    # Track outbound TCP connections
        LogServices            = $true    # Track Windows service start/stop events

        # --- Exclusion Lists (Items here are never logged) ---
        ExcludedProcesses      = @("Idle", "svchost", "SearchIndexer", "backgroundTaskHost", "RuntimeBroker", "AngelSystemLoggerPro", "Start-LocalLogger")
        ExcludedDomains        = @("localhost", "wpad")
        PollIntervalMs         = 800
    }

    # =========================================================================
    # 2. HELPER FUNCTIONS (FILE LOGGING, IP MASKING, STARTUP REGISTRATION)
    # =========================================================================
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
        # Mask IPv4 addresses (replaces digits with #, e.g. 142.250.190.46 -> ###.###.###.##)
        $Masked = [regex]::Replace($Text, '\b(?:\d{1,3}\.){3}\d{1,3}\b', {
            param($Match)
            return ($Match.Value -replace '\d', '#')
        })
        # Mask IPv6 addresses (2+ colons with hex segments)
        $Masked = [regex]::Replace($Masked, '\b(?:[0-9a-fA-F]{1,4}:){2,}[0-9a-fA-F:]+\b', {
            param($Match)
            return ($Match.Value -replace '[0-9a-fA-F]', '#')
        })
        return $Masked
    }

    function Write-ActivityLog ($Tag, $Message, $Color = "White") {
        $Time = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        $ShortTime = Get-Date -Format "HH:mm:ss"
        $PaddedTag = $Tag.PadRight(13)

        # Apply IP filter if enabled
        $SafeMessage = Format-IPFilter $Message

        # Print to console
        Write-Host "[$ShortTime] " -NoNewline -ForegroundColor DarkGray
        Write-Host "[$PaddedTag] " -NoNewline -ForegroundColor $Color
        Write-Host $SafeMessage -ForegroundColor White

        # Append to local log file
        if ($Settings.EnableFileLogging) {
            $LogLine = "[$Time] [$PaddedTag] $SafeMessage"
            Add-Content -Path (Get-LogFilePath) -Value $LogLine -Encoding UTF8 -ErrorAction SilentlyContinue
        }
    }

    function Get-TargetExecutablePath {
        if ($env:ANGEL_LOGGER_EXE_PATH -and (Test-Path $env:ANGEL_LOGGER_EXE_PATH)) {
            return $env:ANGEL_LOGGER_EXE_PATH
        }
        if ($PSCommandPath -and (Test-Path $PSCommandPath)) {
            return $PSCommandPath
        }
        return $null
    }

    function Test-StartupRegistration {
        $RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        $Prop = Get-ItemProperty -Path $RunKey -Name "AngelSystemLoggerPro" -ErrorAction SilentlyContinue
        return ($null -ne $Prop)
    }

    function Set-StartupRegistration ([bool]$Enable) {
        $RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        $AppName = "AngelSystemLoggerPro"
        $StartupDir = [Environment]::GetFolderPath('Startup')
        $LegacyBatPath = Join-Path $StartupDir "Start-LocalActivityLogger.bat"
        $TargetPath = Get-TargetExecutablePath

        if (-not $TargetPath) {
            Write-ActivityLog "STARTUP" "Could not determine executable or script path for Windows Startup." "Red"
            return
        }

        if ($Enable) {
            $CommandValue = if ($TargetPath.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
                "`"$TargetPath`""
            } else {
                "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$TargetPath`""
            }
            Set-ItemProperty -Path $RunKey -Name $AppName -Value $CommandValue -Force
            Write-ActivityLog "STARTUP" "Registered in Windows Startup Apps (Task Manager -> Startup): $TargetPath" "Green"
        } else {
            $Removed = $false
            if (Test-StartupRegistration) {
                Remove-ItemProperty -Path $RunKey -Name $AppName -Force -ErrorAction SilentlyContinue
                $Removed = $true
            }
            if (Test-Path $LegacyBatPath) {
                Remove-Item -Path $LegacyBatPath -Force -ErrorAction SilentlyContinue
                $Removed = $true
            }
            if ($Removed) {
                Write-ActivityLog "STARTUP" "Removed from Windows Startup Apps." "Yellow"
            } else {
                Write-ActivityLog "STARTUP" "Startup registration is not currently active." "DarkGray"
            }
        }
    }

    function Show-Header {
        Clear-Host
        $HasAdminToken = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $AdminStatus   = if ($Settings.UseAdminFeatures -and $HasAdminToken) { "ON (Elevated)" } elseif ($Settings.UseAdminFeatures) { "ON (No Admin Token)" } else { "OFF (Standard User Mode)" }
        $IPStatus      = if ($Settings.FilterIPAddresses) { "ON (Blocking/Masking IPs with #)" } else { "OFF (Revealing Raw IPs)" }
        $FileStatus    = if ($Settings.EnableFileLogging) { "ON -> $(Get-LogFilePath)" } else { "OFF (Console Only)" }
        $StartupStatus = if (Test-StartupRegistration) { "ON (Registered in Windows Startup Apps)" } else { "OFF (Not in Startup)" }

        Write-Host "==========================================================================" -ForegroundColor Cyan
        Write-Host "   ANGEL SYSTEM LOGGER PRO - LOCAL ACTIVITY LOGGER & MONITOR              " -ForegroundColor Green
        Write-Host "==========================================================================" -ForegroundColor Cyan
        Write-Host "   Log File      : $FileStatus" -ForegroundColor Gray
        Write-Host "   Startup   [U] : $StartupStatus" -ForegroundColor $(if (Test-StartupRegistration) { "Green" } else { "DarkGray" })
        Write-Host "   IP Filter [I] : $IPStatus" -ForegroundColor $(if ($Settings.FilterIPAddresses) { "Green" } else { "Yellow" })
        Write-Host "   Admin Mode[A] : $AdminStatus" -ForegroundColor $(if ($Settings.UseAdminFeatures) { "Cyan" } else { "DarkGray" })
        Write-Host "   Categories    : Apps[1]:$($Settings.LogApps) | Tabs[2]:$($Settings.LogWindowTitles) | Web[3]:$($Settings.LogWebsites) | Net[4]:$($Settings.LogInternetConnections) | Svc[5]:$($Settings.LogServices)" -ForegroundColor Gray
        Write-Host "   Controls      : [I] Toggle IP Filter | [A] Toggle Admin | [F] Toggle File Log" -ForegroundColor Yellow
        Write-Host "                   [1-5] Toggle Categories | [U] Toggle Startup | [Ctrl+C] Stop" -ForegroundColor Yellow
        Write-Host "==========================================================================`n" -ForegroundColor Cyan
    }

    # =========================================================================
    # 3. INITIALIZATION & SNAPSHOTS
    # =========================================================================
    if ($Settings.UseAdminFeatures) {
        Clear-DnsClientCache -ErrorAction SilentlyContinue
    }

    if ($Settings.InstallToStartup -and -not (Test-StartupRegistration)) {
        Set-StartupRegistration -Enable $true
    }
    $Settings.InstallToStartup = (Test-StartupRegistration)

    Show-Header
    Write-ActivityLog "LOGGER INIT" "Local logger started (IP Filter: $($Settings.FilterIPAddresses), Admin Mode: $($Settings.UseAdminFeatures), Startup: $($Settings.InstallToStartup))" "Cyan"

    # Take initial snapshot of current system state so only NEW activity is logged
    $Procs = @{}
    Get-Process -ErrorAction SilentlyContinue | ForEach-Object {
        $Procs[$_.Id] = @{ Name = $_.ProcessName; Title = $_.MainWindowTitle }
    }

    $Services = @{}
    Get-Service -ErrorAction SilentlyContinue | ForEach-Object {
        $Services[$_.Name] = $_.Status
    }

    $DnsSeen = @{}
    Get-DnsClientCache -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.Entry) { $DnsSeen[$_.Entry] = $true }
    }

    $NetSeen = @{}
    Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | ForEach-Object {
        $NetSeen["$($_.OwningProcess)-$($_.RemoteAddress):$($_.RemotePort)"] = $true
    }

    # =========================================================================
    # 4. MAIN MONITORING & LIVE KEYBOARD TOGGLE LOOP
    # =========================================================================
    while ($true) {
        # Check for live hotkey presses without blocking
        if ([Console]::KeyAvailable) {
            $KeyInfo = [Console]::ReadKey($true)
            switch ($KeyInfo.Key) {
                'I' {
                    $Settings.FilterIPAddresses = -not $Settings.FilterIPAddresses
                    Show-Header
                    $StateMsg = if ($Settings.FilterIPAddresses) { "ON (IP addresses will be masked with #)" } else { "OFF (Raw IP addresses will be revealed)" }
                    Write-ActivityLog "SETTING" "IP Address Filter toggled $StateMsg" "Magenta"
                }
                'A' {
                    $Settings.UseAdminFeatures = -not $Settings.UseAdminFeatures
                    if ($Settings.UseAdminFeatures) { Clear-DnsClientCache -ErrorAction SilentlyContinue }
                    Show-Header
                    Write-ActivityLog "SETTING" "Administration Mode toggled to: $($Settings.UseAdminFeatures)" "Magenta"
                }
                'F' {
                    $Settings.EnableFileLogging = -not $Settings.EnableFileLogging
                    Show-Header
                    Write-ActivityLog "SETTING" "Local File Logging toggled to: $($Settings.EnableFileLogging)" "Magenta"
                }
                'U' {
                    $Settings.InstallToStartup = -not (Test-StartupRegistration)
                    Set-StartupRegistration -Enable $Settings.InstallToStartup
                    Show-Header
                    Write-ActivityLog "SETTING" "Windows Startup Registration toggled to: $($Settings.InstallToStartup)" "Magenta"
                }
                'D1' { $Settings.LogApps = -not $Settings.LogApps; Show-Header; Write-ActivityLog "SETTING" "App Logging: $($Settings.LogApps)" "Magenta" }
                'D2' { $Settings.LogWindowTitles = -not $Settings.LogWindowTitles; Show-Header; Write-ActivityLog "SETTING" "Window/Tab Logging: $($Settings.LogWindowTitles)" "Magenta" }
                'D3' { $Settings.LogWebsites = -not $Settings.LogWebsites; Show-Header; Write-ActivityLog "SETTING" "Website DNS Logging: $($Settings.LogWebsites)" "Magenta" }
                'D4' { $Settings.LogInternetConnections = -not $Settings.LogInternetConnections; Show-Header; Write-ActivityLog "SETTING" "Internet Connection Logging: $($Settings.LogInternetConnections)" "Magenta" }
                'D5' { $Settings.LogServices = -not $Settings.LogServices; Show-Header; Write-ActivityLog "SETTING" "Service Logging: $($Settings.LogServices)" "Magenta" }
            }
        }

        # 1. Check for Opened Apps, Closed Apps, and Browser Tab Changes
        $CurrentProcs = @{}
        foreach ($p in (Get-Process -ErrorAction SilentlyContinue)) {
            $CurrentProcs[$p.Id] = @{ Name = $p.ProcessName; Title = $p.MainWindowTitle }

            if ($Settings.ExcludedProcesses -contains $p.ProcessName) {
                continue
            }

            if (-not $Procs.ContainsKey($p.Id)) {
                if ($Settings.LogApps) {
                    $TabInfo = if ($Settings.LogWindowTitles -and $p.MainWindowTitle) { " -> Window: '$($p.MainWindowTitle)'" } else { "" }
                    Write-ActivityLog "APP OPENED" "$($p.ProcessName).exe started (PID: $($p.Id))$TabInfo" "Green"
                }
            }
            elseif ($Settings.LogWindowTitles -and $p.MainWindowTitle -and $Procs[$p.Id].Title -ne $p.MainWindowTitle) {
                Write-ActivityLog "ACTIVE TAB" "$($p.ProcessName).exe switched to: '$($p.MainWindowTitle)'" "Cyan"
            }
        }

        if ($Settings.LogApps) {
            foreach ($OldId in $Procs.Keys) {
                if (-not $CurrentProcs.ContainsKey($OldId)) {
                    $ClosedName = $Procs[$OldId].Name
                    if ($Settings.ExcludedProcesses -notcontains $ClosedName) {
                        Write-ActivityLog "APP CLOSED" "$ClosedName.exe closed (PID: $OldId)" "Red"
                    }
                }
            }
        }
        $Procs = $CurrentProcs

        # 2. Check for Websites Being Opened (DNS Requests)
        if ($Settings.LogWebsites) {
            foreach ($d in (Get-DnsClientCache -ErrorAction SilentlyContinue)) {
                if ($d.Entry -and -not $DnsSeen.ContainsKey($d.Entry)) {
                    if ($Settings.ExcludedDomains -notcontains $d.Entry) {
                        Write-ActivityLog "WEBSITE" "Loading domain: $($d.Entry)" "Magenta"
                    }
                    $DnsSeen[$d.Entry] = $true
                }
            }
        }

        # 3. Check for Apps Connecting to the Internet
        if ($Settings.LogInternetConnections) {
            foreach ($c in (Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue)) {
                if ($c.RemoteAddress -notmatch "^(127\.|0\.|::1|fe80:|10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)") {
                    $Key = "$($c.OwningProcess)-$($c.RemoteAddress):$($c.RemotePort)"
                    if (-not $NetSeen.ContainsKey($Key)) {
                        $AppName = if ($CurrentProcs.ContainsKey($c.OwningProcess)) {
                            $CurrentProcs[$c.OwningProcess].Name
                        } elseif ($Settings.UseAdminFeatures) {
                            (Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue).ProcessName
                        } else {
                            $null
                        }

                        if ($AppName -and ($Settings.ExcludedProcesses -notcontains $AppName)) {
                            $PortType = switch ($c.RemotePort) {
                                443     { "HTTPS Secure Web" }
                                80      { "HTTP Web" }
                                53      { "DNS Lookup" }
                                default { "Port $($c.RemotePort)" }
                            }
                            $DisplayIP = if ($Settings.FilterIPAddresses) { Format-IPFilter $c.RemoteAddress } else { $c.RemoteAddress }
                            Write-ActivityLog "INTERNET" "$AppName.exe connected to $DisplayIP ($PortType)" "Yellow"
                        }
                        $NetSeen[$Key] = $true
                    }
                }
            }
        }

        # 4. Check for Windows Services Starting or Stopping
        if ($Settings.LogServices) {
            foreach ($s in (Get-Service -ErrorAction SilentlyContinue)) {
                if ($Services.ContainsKey($s.Name) -and $Services[$s.Name] -ne $s.Status) {
                    Write-ActivityLog "SERVICE" "$($s.DisplayName) changed to $($s.Status)" "Blue"
                    $Services[$s.Name] = $s.Status
                }
            }
        }

        Start-Sleep -Milliseconds $Settings.PollIntervalMs
    }
}
