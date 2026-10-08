[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,
    [Parameter(Mandatory = $true)]
    [string]$VehicleId
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$pipelineRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$localRoot = [IO.Path]::GetFullPath((Join-Path $pipelineRoot $config.localRoot))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $pipelineRoot $config.outputRoot))
$renderRoot = Join-Path $localRoot ("Renders\{0}" -f $VehicleId)
$hullPath = Join-Path $renderRoot 'hull.png'
$turretPath = Join-Path $renderRoot 'turret.png'

foreach ($path in @($hullPath, $turretPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected render is missing: $path"
    }
}

function Get-AlphaBounds([System.Drawing.Bitmap]$bitmap) {
    $minX = $bitmap.Width
    $minY = $bitmap.Height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if ($bitmap.GetPixel($x, $y).A -gt 2) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw 'The render contains no visible pixels.' }
    return [Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
}

$padding = [int]$config.render.paddingPixels
$gap = [int]$config.render.gapPixels
$scale = [double]$config.render.outputPixelsPerMeter / [double]$config.render.intermediatePixelsPerMeter
$hull = [Drawing.Bitmap]::FromFile($hullPath)
$turret = [Drawing.Bitmap]::FromFile($turretPath)
try {
    $hullBounds = Get-AlphaBounds $hull
    $turretBounds = Get-AlphaBounds $turret
    $hullWidth = [Math]::Max(1, [int][Math]::Round($hullBounds.Width * $scale))
    $hullHeight = [Math]::Max(1, [int][Math]::Round($hullBounds.Height * $scale))
    $turretWidth = [Math]::Max(1, [int][Math]::Round($turretBounds.Width * $scale))
    $turretHeight = [Math]::Max(1, [int][Math]::Round($turretBounds.Height * $scale))
    $width = $padding + $hullWidth + $gap + $turretWidth + $padding
    $height = $padding + [Math]::Max($hullHeight, $turretHeight) + $padding
    $strip = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($strip)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $hullY = $padding + [int](($height - 2 * $padding - $hullHeight) / 2)
            $turretY = $padding + [int](($height - 2 * $padding - $turretHeight) / 2)
            $graphics.DrawImage($hull, [Drawing.Rectangle]::new($padding, $hullY, $hullWidth, $hullHeight), $hullBounds, [Drawing.GraphicsUnit]::Pixel)
            $turretX = $padding + $hullWidth + $gap
            $graphics.DrawImage($turret, [Drawing.Rectangle]::new($turretX, $turretY, $turretWidth, $turretHeight), $turretBounds, [Drawing.GraphicsUnit]::Pixel)
        } finally {
            $graphics.Dispose()
        }
        $vehicleOutput = Join-Path $outputRoot $VehicleId
        New-Item -ItemType Directory -Path $vehicleOutput -Force | Out-Null
        $stripPath = Join-Path $vehicleOutput ("{0}_strip2.png" -f $VehicleId)
        $strip.Save($stripPath, [Drawing.Imaging.ImageFormat]::Png)

        $layout = [ordered]@{
            schemaVersion = 1
            texture = [ordered]@{ width = $width; height = $height }
            hull = [ordered]@{
                x = $padding
                y = $height - ($hullY + $hullHeight)
                width = $hullWidth
                height = $hullHeight
                pivot = [ordered]@{ x = 0.5; y = 0.5 }
            }
            turret = [ordered]@{
                x = $turretX
                y = $height - ($turretY + $turretHeight)
                width = $turretWidth
                height = $turretHeight
                pivot = [ordered]@{ x = 0.5; y = 0.5 }
            }
        }

        $renderMetadataPath = Join-Path $renderRoot 'render_metadata.json'
        if (Test-Path -LiteralPath $renderMetadataPath -PathType Leaf) {
            $renderMetadata = Get-Content -LiteralPath $renderMetadataPath -Raw | ConvertFrom-Json
            if ($null -ne $renderMetadata.turretPivot) {
                $pivotX = ([double]$renderMetadata.turretPivot.xPixels - $turretBounds.X) / $turretBounds.Width
                $pivotFromTop = ([double]$renderMetadata.turretPivot.yPixels - $turretBounds.Y) / $turretBounds.Height
                $layout.turret.pivot.x = [Math]::Max(0.0, [Math]::Min(1.0, $pivotX))
                $layout.turret.pivot.y = [Math]::Max(0.0, [Math]::Min(1.0, 1.0 - $pivotFromTop))
            }
        }

        $layoutPath = Join-Path $vehicleOutput ("{0}_layout.json" -f $VehicleId)
        $layout | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $layoutPath -Encoding utf8
        Write-Host "Sprite strip: $stripPath"
        Write-Host "Sprite layout: $layoutPath"
    } finally {
        $strip.Dispose()
    }
} finally {
    $hull.Dispose()
    $turret.Dispose()
}
