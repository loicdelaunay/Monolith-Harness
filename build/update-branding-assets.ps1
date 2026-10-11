param(
    [string]$SourceLogo = '',
    [string]$CliSourceLogo = '',
    [string]$AndroidSourceLogo = '',
    [ValidateSet('All','Desktop','CLI','Android')][string]$Target = 'All'
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this asset exporter on Windows (System.Drawing); exported files are portable.' }
Add-Type -AssemblyName System.Drawing
$brandRoot = Split-Path -Parent $PSScriptRoot
if (-not $SourceLogo) { $SourceLogo = Join-Path $brandRoot 'src/MonolithHarness.App/Assets/logo.png' }
if (-not $CliSourceLogo) { $CliSourceLogo = Join-Path $brandRoot 'src/MonolithHarness.Cli/Assets/logo.png' }
if (-not $AndroidSourceLogo) { $AndroidSourceLogo = Join-Path $brandRoot 'src/MonolithHarness.App.Flutter/assets/logo.png' }
function Export-BrandPng([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scaled = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $scaledGraphics = [Drawing.Graphics]::FromImage($scaled)
        $outline = [Drawing.Drawing2D.GraphicsPath]::new()
        $texture = $null
        try {
            # Keep the dark tile opaque; only the outside of the rounded outline is transparent.
            $scaledGraphics.Clear([Drawing.Color]::FromArgb(255, 22, 24, 29))
            $scaledGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaledGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaledGraphics.DrawImage($logoImage, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
            # Keep the same 20% corner radius in source PNGs and every native icon frame.
            $diameter = [single]($Size * 0.4)
            $offset = [single]($Size - $diameter)
            $outline.AddArc(0, 0, $diameter, $diameter, 180, 90)
            $outline.AddArc($offset, 0, $diameter, $diameter, 270, 90)
            $outline.AddArc($offset, $offset, $diameter, $diameter, 0, 90)
            $outline.AddArc(0, $offset, $diameter, $diameter, 90, 90)
            $outline.CloseFigure()
            $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $texture = [Drawing.TextureBrush]::new($scaled, [Drawing.Drawing2D.WrapMode]::Clamp)
            $graphics.FillPath($texture, $outline)
        } finally {
            if ($null -ne $texture) { $texture.Dispose() }
            $outline.Dispose(); $scaledGraphics.Dispose(); $scaled.Dispose()
        }
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
function Write-BigEndian([IO.BinaryWriter]$Writer, [uint32]$Value) {
    $bytes = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    $Writer.Write($bytes)
}
function Export-BrandSet([string]$Source, [string[]]$Folders, [int[]]$PngSizes, [bool]$WindowsIcon, [bool]$MacIcon) {
    $sourceBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Source).Path)
    $sourceStream = [IO.MemoryStream]::new($sourceBytes, $false)
    $logoImage = [Drawing.Image]::FromStream($sourceStream)
    $frames = @{}
try {
    if ($logoImage.Width -ne $logoImage.Height) { throw 'Use a square source logo to avoid distortion.' }
    foreach ($size in @(16,20,24,32,40,48,64,128,256,512,1024)) { $frames[$size] = Export-BrandPng $size }
    # Persist rounded transparent corners in the source artwork too.
    $sourceBytes = Export-BrandPng $logoImage.Width
    foreach ($folder in $Folders) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $folder 'logo.png'), $sourceBytes)
        foreach ($size in $PngSizes) { [IO.File]::WriteAllBytes((Join-Path $folder "logo-$size.png"), $frames[$size]) }
    }
    if ($WindowsIcon) {
    $ico = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($ico)
    try {
        $sizes = @(16,20,24,32,40,48,64,128,256)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        foreach ($size in $sizes) {
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$size].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$size].Length
        }
        foreach ($size in $sizes) { $writer.Write([byte[]]$frames[$size]) }
        $writer.Flush(); $icoBytes = $ico.ToArray()
        foreach ($folder in $Folders) { [IO.File]::WriteAllBytes((Join-Path $folder 'app.ico'), $icoBytes) }
    } finally { $writer.Dispose(); $ico.Dispose() }
    }
    if ($MacIcon) {
    $icns = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($icns)
    try {
        $chunks = @(@('icp4',16),@('icp5',32),@('icp6',64),@('ic07',128),@('ic08',256),@('ic09',512),@('ic10',1024))
        $length = 8; foreach ($chunk in $chunks) { $length += 8 + $frames[$chunk[1]].Length }
        $writer.Write([Text.Encoding]::ASCII.GetBytes('icns')); Write-BigEndian $writer $length
        foreach ($chunk in $chunks) {
            $writer.Write([Text.Encoding]::ASCII.GetBytes($chunk[0])); Write-BigEndian $writer (8 + $frames[$chunk[1]].Length)
            $writer.Write([byte[]]$frames[$chunk[1]])
        }
        $writer.Flush(); [IO.File]::WriteAllBytes((Join-Path $Folders[-1] 'app.icns'), $icns.ToArray())
    } finally { $writer.Dispose(); $icns.Dispose() }
    }
} finally { $logoImage.Dispose(); $sourceStream.Dispose() }
}
if ($Target -in @('All','Desktop')) {
    Export-BrandSet $SourceLogo @((Join-Path $brandRoot 'src/MonolithHarness.App/Assets'),(Join-Path $brandRoot 'desktop/ui')) @(32,64,256) $true $true
}
if ($Target -in @('All','CLI')) {
    Export-BrandSet $CliSourceLogo @((Join-Path $brandRoot 'src/MonolithHarness.Cli/Assets')) @(32,256) $true $false
}
if ($Target -in @('All','Android')) {
    Export-BrandSet $AndroidSourceLogo @((Join-Path $brandRoot 'src/MonolithHarness.App.Flutter/assets')) @(32,256) $false $false
}
Write-Host "Brand assets exported ($Target): desktop cyan, CLI orange, Android green."
