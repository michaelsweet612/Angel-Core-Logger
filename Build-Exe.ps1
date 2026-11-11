<#
.SYNOPSIS
    Builds AngelSystemLoggerPro.exe from Start-LocalLogger.ps1 and AngelSystemLoggerPro.cs
#>

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$IconPath   = Join-Path $ProjectDir "AngelSystemLoggerPro.ico"
$CsPath     = Join-Path $ProjectDir "AngelSystemLoggerPro.cs"
$Ps1Path    = Join-Path $ProjectDir "Start-LocalLogger.ps1"
$ExePath    = Join-Path $ProjectDir "AngelSystemLoggerPro.exe"

# 1. Generate a clean application icon (.ico) with embedded PNG (64x64)
Add-Type -AssemblyName System.Drawing
$Bmp = New-Object System.Drawing.Bitmap(64, 64)
$Gfx = [System.Drawing.Graphics]::FromImage($Bmp)
$Gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$Gfx.Clear([System.Drawing.Color]::Transparent)

# Draw rounded dark slate background badge
$BgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 0)),
    (New-Object System.Drawing.Point(64, 64)),
    [System.Drawing.Color]::FromArgb(255, 15, 23, 42),
    [System.Drawing.Color]::FromArgb(255, 30, 41, 59)
)
$BorderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 6, 182, 212), 3)
$Gfx.FillEllipse($BgBrush, 4, 4, 56, 56)
$Gfx.DrawEllipse($BorderPen, 4, 4, 56, 56)

# Draw green activity pulse line across the badge
$PulsePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 34, 197, 94), 3.5)
$PulsePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$PulsePen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
$PulsePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$Points = [System.Drawing.Point[]]@(
    (New-Object System.Drawing.Point(12, 34)),
    (New-Object System.Drawing.Point(22, 34)),
    (New-Object System.Drawing.Point(27, 20)),
    (New-Object System.Drawing.Point(34, 46)),
    (New-Object System.Drawing.Point(40, 26)),
    (New-Object System.Drawing.Point(44, 34)),
    (New-Object System.Drawing.Point(52, 34))
)
$Gfx.DrawLines($PulsePen, $Points)

$PngStream = New-Object System.IO.MemoryStream
$Bmp.Save($PngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$PngBytes = $PngStream.ToArray()

$IcoStream = New-Object System.IO.FileStream($IconPath, [System.IO.FileMode]::Create)
$Writer = New-Object System.IO.BinaryWriter($IcoStream)
# ICONDIR header
$Writer.Write([UInt16]0)
$Writer.Write([UInt16]1)
$Writer.Write([UInt16]1)
# ICONDIRENTRY
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

# 2. Locate C# compiler (csc.exe)
$Csc = Get-ChildItem -Path "$env:windir\Microsoft.NET\Framework64" -Filter "csc.exe" -Recurse | Select-Object -Last 1
if (-not $Csc) {
    throw "Could not find Microsoft .NET Framework csc.exe compiler."
}

# 3. Compile standalone EXE with embedded PowerShell script and icon
$CompileArgs = @(
    "/nologo",
    "/target:exe",
    "/optimize+",
    "/out:`"$ExePath`"",
    "/win32icon:`"$IconPath`"",
    "/resource:`"$Ps1Path`",Start-LocalLogger.ps1",
    "`"$CsPath`""
)

Write-Host "[2/3] Compiling standalone executable..." -ForegroundColor Cyan
$Proc = Start-Process -FilePath $Csc.FullName -ArgumentList $CompileArgs -NoNewWindow -Wait -PassThru
if ($Proc.ExitCode -ne 0) {
    throw "Compilation failed with exit code $($Proc.ExitCode)."
}

Write-Host "[3/3] Successfully built: $ExePath" -ForegroundColor Green
