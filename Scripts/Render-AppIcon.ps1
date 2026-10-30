<#
.SYNOPSIS
    Rendert das App-Icon und das Logo-PNG aus der Maskottchen-Grafik in Views\Mascot.xaml.

.DESCRIPTION
    Views\Mascot.xaml ist die einzige Quelle für das Pinguin-Design. Nach jeder Änderung daran
    dieses Skript ausführen, damit Icon (Exe, Taskleiste, Tray, Fenster) und Logo nachziehen:

        powershell.exe -ExecutionPolicy Bypass -File .\Scripts\Render-AppIcon.ps1

    Schreibt:
      - TimeTrack Logo.ico          (ApplicationIcon der Exe)
      - Images\TimeTrack Logo.ico
      - Images\TimeTrack Logo.png   (512 x 512)

    Muss in Windows PowerShell 5.1 (STA, .NET Framework mit WPF) laufen.
#>
$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$IconSizes = 16, 20, 24, 32, 40, 48, 64, 256
$LogoSize = 512
$DesignSize = 256.0 # Koordinatensystem von MascotAppIconDrawing

function Get-IconDrawing {
    $path = Join-Path $RepoRoot 'Views\Mascot.xaml'
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $dictionary = [System.Windows.Markup.XamlReader]::Load($stream)
    }
    finally {
        $stream.Dispose()
    }
    return $dictionary['MascotAppIconDrawing']
}

# Rendert die Grafik in der gewünschten Kantenlänge, liefert eine Bgra32-Bitmap (nicht vormultipliziertes Alpha).
function Render-Drawing([System.Windows.Media.Drawing]$drawing, [int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $DesignSize), ($size / $DesignSize)))
    $context.DrawDrawing($drawing)
    $context.Pop()
    $context.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    return New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
}

function Get-PngBytes([System.Windows.Media.Imaging.BitmapSource]$bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object System.IO.MemoryStream
    $encoder.Save($memory)
    return ,$memory.ToArray() # Komma: Array nicht in die Pipeline entrollen
}

# Klassischer 32-Bit-DIB-Eintrag (BITMAPINFOHEADER + BGRA von unten nach oben + leere AND-Maske),
# den auch ältere Windows-Komponenten zuverlässig lesen.
function Get-DibBytes([System.Windows.Media.Imaging.BitmapSource]$bitmap) {
    $size = $bitmap.PixelWidth
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)

    $maskStride = [int]([Math]::Floor(($size + 31) / 32)) * 4
    $memory = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $memory
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$size)
    $writer.Write([Int32]($size * 2)) # Höhe inkl. AND-Maske
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]($pixels.Length + $maskStride * $size))
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)
    for ($row = $size - 1; $row -ge 0; $row--) {
        $writer.Write($pixels, $row * $stride, $stride)
    }
    $writer.Write((New-Object byte[] ($maskStride * $size)))
    $writer.Flush()
    return ,$memory.ToArray() # Komma: Array nicht in die Pipeline entrollen
}

function Write-Ico([System.Windows.Media.Drawing]$drawing, [string[]]$paths) {
    $images = foreach ($size in $IconSizes) {
        $bitmap = Render-Drawing $drawing $size
        # 256er als PNG (spart Platz, ab Vista Standard), kleinere als DIB.
        [byte[]]$bytes = if ($size -ge 256) { Get-PngBytes $bitmap } else { Get-DibBytes $bitmap }
        [pscustomobject]@{ Size = $size; Bytes = $bytes }
    }

    $memory = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $memory
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }
    $writer.Flush()

    foreach ($path in $paths) {
        [System.IO.File]::WriteAllBytes($path, $memory.ToArray())
        Write-Host "Geschrieben: $path"
    }
}

$drawing = Get-IconDrawing
if ($null -eq $drawing) {
    throw "MascotAppIconDrawing nicht in Views\Mascot.xaml gefunden."
}

Write-Ico $drawing @(
    (Join-Path $RepoRoot 'TimeTrack Logo.ico'),
    (Join-Path $RepoRoot 'Images\TimeTrack Logo.ico')
)

$logoPath = Join-Path $RepoRoot 'Images\TimeTrack Logo.png'
[System.IO.File]::WriteAllBytes($logoPath, (Get-PngBytes (Render-Drawing $drawing $LogoSize)))
Write-Host "Geschrieben: $logoPath"
