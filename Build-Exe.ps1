<#
.SYNOPSIS
    Builds the native WPF GUI AngelSystemLoggerPro.exe from Start-LocalLogger.ps1 and AngelSystemLoggerPro.cs
#>

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$IconPath   = Join-Path $ProjectDir "AngelSystemLoggerPro.ico"
$CsPath     = Join-Path $ProjectDir "AngelSystemLoggerPro.cs"
$Ps1Path    = Join-Path $ProjectDir "Start-LocalLogger.ps1"
$ExePath    = Join-Path $ProjectDir "AngelSystemLoggerPro.exe"

# Stop any running instance before rebuilding
Get-Process -Name "AngelSystemLoggerPro" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*Start-LocalLogger.ps1*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

# 1. Generate a clean dark-mode application icon (.ico) with embedded PNG (64x64)
Add-Type -AssemblyName System.Drawing
$Bmp = New-Object System.Drawing.Bitmap(64, 64)
$Gfx = [System.Drawing.Graphics]::FromImage($Bmp)
$Gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$Gfx.Clear([System.Drawing.Color]::Transparent)

# Draw rounded dark charcoal badge with gold & green telemetry accent
$BgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point(64, 64)),
    [System.Drawing.Color]::FromArgb(255, 24, 24, 27),
    [System.Drawing.Color]::FromArgb(255, 39, 39, 45)
)
$BorderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 213, 164, 73), 3)
$Gfx.FillEllipse($BgBrush, 4, 4, 56, 56)
$Gfx.DrawEllipse($BorderPen, 4, 4, 56, 56)

# Draw green activity pulse line across the badge
$PulsePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 211, 153), 3.5)
$PulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$PulsePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
$PulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$Points = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point(12, 34)),
    (New-Object System.Drawing.Point(22, 34)),
    (New-Object System.Drawing.Point(27, 19)),
    (New-Object System.Drawing.Point(34, 46)),
    (New-Object System.Drawing.Point(40, 25)),
    (New-Object System.Drawing.Point(45, 34)),
    (New-Object System.Drawing.Point(52, 34))
)
$Gfx.DrawLines($PulsePen, $Points)

$PngStream = New-Object System.IO.MemoryStream
$Bmp.Save($PngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$PngBytes = $PngStream.ToArray()

$IcoStream = New-Object System.IO.FileStream($IconPath, [System.IO.FileMode]::Create)
$Writer = New-Object System.IO.BinaryWriter($IcoStream)
$Writer.Write([UInt16]0)
$Writer.Write([UInt16]1)
$Writer.Write([UInt16]1)
$Writer.Write([Byte]64)
$Writer.Write([Byte]64)
$Writer.Write([Byte]0)
$Writer.Write([Byte]0)
$Writer.Write([UInt16]1)
$Writer.Write([UInt16]32)
$Writer.Write([UInt32]$PngBytes.Length)
$Writer.Write([UInt32]22)
$Writer.Write($PngBytes)
$Writer.Close()
$IcoStream.Close()
$Gfx.Dispose()
$Bmp.Dispose()

Write-Host "[1/3] Generated application icon: $IconPath" -ForegroundColor Cyan

# 2. Locate C# compiler (csc.exe) & WPF assemblies
$FwDir = "$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$CscPath = Join-Path $FwDir "csc.exe"
$WpfDir  = Join-Path $FwDir "WPF"

if (-not (Test-Path $CscPath)) {
    throw "Could not find Microsoft .NET Framework csc.exe compiler at $CscPath"
}

# 3. Compile standalone Windows GUI EXE (/target:winexe) with embedded PowerShell script and icon
$CompileArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/out:`"$ExePath`"",
    "/win32icon:`"$IconPath`"",
    "/reference:`"$(Join-Path $WpfDir 'PresentationFramework.dll')`"",
    "/reference:`"$(Join-Path $WpfDir 'PresentationCore.dll')`"",
    "/reference:`"$(Join-Path $WpfDir 'WindowsBase.dll')`"",
    "/reference:`"$(Join-Path $FwDir 'System.Xaml.dll')`"",
    "/resource:`"$Ps1Path`",Start-LocalLogger.ps1",
    "`"$CsPath`""
)

Write-Host "[2/3] Compiling native Windows GUI executable..." -ForegroundColor Cyan
$Proc = Start-Process -FilePath $CscPath -ArgumentList $CompileArgs -NoNewWindow -Wait -PassThru
if ($Proc.ExitCode -ne 0) {
    throw "Compilation failed with exit code $($Proc.ExitCode)."
}

Write-Host "[3/3] Successfully built GUI application: $ExePath" -ForegroundColor Green
