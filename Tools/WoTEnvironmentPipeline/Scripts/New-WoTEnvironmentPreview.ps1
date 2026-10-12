[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LibraryRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$resolvedRoot = (Resolve-Path -LiteralPath $LibraryRoot).Path
$manifestPath = Join-Path $resolvedRoot 'library_manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$columns = 5
$objectCellWidth = 350
$objectCellHeight = 390
$objectRows = [Math]::Ceiling($manifest.objects.Count / $columns)
$surfaceCellWidth = 250
$surfaceRows = [Math]::Ceiling($manifest.surfaces.Count / 7)
$surfaceTop = 70 + ($objectRows * $objectCellHeight)
$height = $surfaceTop + 300 + ($surfaceRows * 260)
$canvas = New-Object System.Drawing.Bitmap(1800, $height)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$titleFont = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
$labelFont = New-Object System.Drawing.Font('Segoe UI', 11)
$mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 205, 212, 202))
$cellBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 42, 49, 42))

try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 55, 67, 55))
    $graphics.SmoothingMode = 'HighQuality'
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.DrawString('LocalTanks - objects', $titleFont, [System.Drawing.Brushes]::White, 24, 16)

    for ($index = 0; $index -lt $manifest.objects.Count; $index++) {
        $entry = $manifest.objects[$index]
        $column = $index % $columns
        $row = [Math]::Floor($index / $columns)
        $x = 24 + ($column * $objectCellWidth)
        $y = 52 + ($row * $objectCellHeight)
        $cell = New-Object System.Drawing.Rectangle($x, $y, 326, 340)
        $graphics.FillRectangle($cellBrush, $cell)
        $image = [System.Drawing.Image]::FromFile((Join-Path $resolvedRoot $entry.sprite))
        try {
            $scale = [Math]::Min(294 / $image.Width, 288 / $image.Height)
            $width = [int]($image.Width * $scale)
            $imageHeight = [int]($image.Height * $scale)
            $drawX = $x + [int](($cell.Width - $width) / 2)
            $drawY = $y + 12 + [int]((288 - $imageHeight) / 2)
            $graphics.DrawImage($image, $drawX, $drawY, $width, $imageHeight)
        }
        finally {
            $image.Dispose()
        }
        $graphics.DrawString($entry.id, $labelFont, [System.Drawing.Brushes]::White, $x + 10, $y + 302)
        $sizeLabel = '{0:0.0} x {1:0.0} m' -f $entry.worldSizeMeters[0], $entry.worldSizeMeters[1]
        $graphics.DrawString($sizeLabel, $labelFont, $mutedBrush, $x + 10, $y + 321)
    }

    $graphics.DrawString('Tileable ground textures', $titleFont, [System.Drawing.Brushes]::White, 24, $surfaceTop)
    for ($index = 0; $index -lt $manifest.surfaces.Count; $index++) {
        $entry = $manifest.surfaces[$index]
        $column = $index % 7
        $row = [Math]::Floor($index / 7)
        $x = 24 + ($column * $surfaceCellWidth)
        $y = $surfaceTop + 42 + ($row * 260)
        $image = [System.Drawing.Image]::FromFile((Join-Path $resolvedRoot $entry.texture))
        try {
            $graphics.DrawImage($image, $x, $y, 220, 220)
        }
        finally {
            $image.Dispose()
        }
        $graphics.DrawString($entry.id, $labelFont, [System.Drawing.Brushes]::White, $x, $y + 226)
    }

    $previewPath = Join-Path $resolvedRoot 'library_preview.png'
    $canvas.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Preview: $previewPath"
}
finally {
    $mutedBrush.Dispose()
    $cellBrush.Dispose()
    $labelFont.Dispose()
    $titleFont.Dispose()
    $graphics.Dispose()
    $canvas.Dispose()
}
