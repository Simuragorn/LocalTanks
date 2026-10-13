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
$tracksPath = Join-Path $renderRoot 'tracks.png'
$metadataPath = Join-Path $renderRoot 'render_metadata.json'
$overridePath = Join-Path $localRoot ("Tracks\{0}.json" -f $VehicleId)

# Alpha below this value is treated as empty. The same threshold keeps anti-aliased
# silhouette edges out of the mask so a track never gains a translucent halo.
$maskAlphaCutoff = 8

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

function Get-ScaledCrop([Drawing.Image]$source, [Drawing.Rectangle]$crop, [int]$width, [int]$height) {
    $target = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($target)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $width, $height), $crop, [Drawing.GraphicsUnit]::Pixel)
    } finally {
        $graphics.Dispose()
    }
    return $target
}

function Get-ImageMetadata([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Get-TrackOverride([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Get-ScalarOr($value, $fallback) {
    if ($null -eq $value) { return $fallback }
    return $value
}

function New-EmptyStats {
    return @{
        Pixels = 0
        SumX = 0.0
        SumY = 0.0
        MinX = [int]::MaxValue
        MaxX = -1
        MinY = [int]::MaxValue
        MaxY = -1
        RowWidths = @{}
    }
}

function Add-StatsPixel($stats, [int]$x, [int]$y) {
    $stats.Pixels++
    $stats.SumX += $x
    $stats.SumY += $y
    if ($x -lt $stats.MinX) { $stats.MinX = $x }
    if ($x -gt $stats.MaxX) { $stats.MaxX = $x }
    if ($y -lt $stats.MinY) { $stats.MinY = $y }
    if ($y -gt $stats.MaxY) { $stats.MaxY = $y }
    if ($stats.RowWidths.ContainsKey($y)) { $stats.RowWidths[$y]++ } else { $stats.RowWidths[$y] = 1 }
}

function Get-MeanRowWidth($stats) {
    if ($stats.Pixels -eq 0) { return 0.0 }
    $total = 0
    foreach ($count in $stats.RowWidths.Values) { $total += $count }
    return [double]$total / [double]$stats.RowWidths.Count
}

function Get-TrackStats($stats) {
    if ($stats.Pixels -eq 0) {
        return [ordered]@{
            pixels = 0
            centroidX = 0.0
            centroidY = 0.0
            widthPixels = 0.0
            lengthPixels = 0.0
        }
    }
    return [ordered]@{
        pixels = $stats.Pixels
        centroidX = [Math]::Round($stats.SumX / $stats.Pixels, 3)
        centroidY = [Math]::Round($stats.SumY / $stats.Pixels, 3)
        widthPixels = [Math]::Round((Get-MeanRowWidth $stats), 3)
        lengthPixels = $stats.MaxY - $stats.MinY + 1
    }
}

# Lateral weight of a masked band: 1 in the middle, 0 on the silhouette edge. The shader
# multiplies the moving pattern by it, so a band never reads as a painted rectangle with
# straight sides, which is what a wide fallback band would otherwise look like. A narrow band
# keeps a solid core, because fading it across its whole width would erase it.
function Get-LateralWeight([int]$x, [int]$minX, [int]$maxX) {
    $width = $maxX - $minX + 1
    $edge = [Math]::Min([Math]::Max(1.5, $width * 0.45), 10.0)
    $distance = [Math]::Min($x - $minX, $maxX - $x) + 0.5
    $ramp = [Math]::Min(1.0, $distance / $edge)
    return $ramp * $ramp * (3.0 - 2.0 * $ramp)
}

# Second pass of a mask: per row and per side the band owns a lateral range, and every pixel
# stores its position inside that range in the blue channel.
function Add-LateralWeights($mask, $rows) {
    for ($y = 0; $y -lt $mask.Height; $y++) {
        foreach ($side in @('left', 'right')) {
            $range = $rows[$side][$y]
            if ($null -eq $range) { continue }
            $minX = $range[0]
            $maxX = $range[1]
            for ($x = $minX; $x -le $maxX; $x++) {
                $pixel = $mask.GetPixel($x, $y)
                if ($pixel.A -lt $maskAlphaCutoff) { continue }
                $weight = [int][Math]::Round(255.0 * (Get-LateralWeight $x $minX $maxX))
                if ($side -eq 'left') {
                    $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($pixel.A, 255, 0, $weight))
                } else {
                    $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($pixel.A, 0, 255, $weight))
                }
            }
        }
    }
}

# The mask carries coverage in the alpha channel, one flat channel per side and the lateral
# weight in the blue one, so a texture that is tone mapped, filtered or resampled still
# classifies a pixel safely and still knows how far it sits from the band edge.
function New-TrackMask([Drawing.Bitmap]$raw, [bool]$swapSides) {
    $mask = New-Object Drawing.Bitmap $raw.Width, $raw.Height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $left = New-EmptyStats
    $right = New-EmptyStats
    $rows = @{ left = @{}; right = @{} }
    for ($y = 0; $y -lt $raw.Height; $y++) {
        for ($x = 0; $x -lt $raw.Width; $x++) {
            $pixel = $raw.GetPixel($x, $y)
            if ($pixel.A -lt $maskAlphaCutoff) { continue }
            $isLeft = $pixel.R -gt $pixel.G
            if ($swapSides) { $isLeft = -not $isLeft }
            if ($isLeft) {
                $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($pixel.A, 255, 0, 0))
                Add-StatsPixel $left $x $y
                $side = 'left'
            } else {
                $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($pixel.A, 0, 255, 0))
                Add-StatsPixel $right $x $y
                $side = 'right'
            }

            if ($rows[$side].ContainsKey($y)) {
                $range = $rows[$side][$y]
                if ($x -lt $range[0]) { $range[0] = $x }
                if ($x -gt $range[1]) { $range[1] = $x }
            } else {
                $rows[$side][$y] = @($x, $x)
            }
        }
    }
    Add-LateralWeights $mask $rows
    return @{ Bitmap = $mask; Left = $left; Right = $right }
}

# Fallback for a model whose track parts cannot be identified: the outer bands of the
# hull silhouette. Coarse on purpose, and recorded as such in the layout.
function New-SideBandMask([Drawing.Bitmap]$hull, [double]$bandFraction, [bool]$redOnImageLeft) {
    $mask = New-Object Drawing.Bitmap $hull.Width, $hull.Height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $left = New-EmptyStats
    $right = New-EmptyStats
    $rows = @{ left = @{}; right = @{} }
    for ($y = 0; $y -lt $hull.Height; $y++) {
        $minX = $hull.Width
        $maxX = -1
        for ($x = 0; $x -lt $hull.Width; $x++) {
            if ($hull.GetPixel($x, $y).A -gt 32) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
            }
        }
        if ($maxX -lt 0) { continue }
        $band = [Math]::Max(2, [int][Math]::Round(($maxX - $minX + 1) * $bandFraction))
        $bandLeftEnd = [Math]::Min($minX + $band - 1, $maxX)
        $bandRightStart = [Math]::Max($minX, $maxX - $band + 1)
        for ($x = $minX; $x -le $maxX; $x++) {
            $alpha = $hull.GetPixel($x, $y).A
            if ($alpha -lt 32) { continue }
            $onLeftBand = $x -le $bandLeftEnd
            $onRightBand = $x -ge $bandRightStart
            if (-not ($onLeftBand -or $onRightBand)) { continue }
            $sideIsImageLeft = $x -le [int](($minX + $maxX) / 2)
            if ($redOnImageLeft) { $isLeft = $sideIsImageLeft } else { $isLeft = -not $sideIsImageLeft }
            if ($isLeft) {
                $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, 255, 0, 0))
                Add-StatsPixel $left $x $y
                $side = 'left'
            } else {
                $mask.SetPixel($x, $y, [Drawing.Color]::FromArgb($alpha, 0, 255, 0))
                Add-StatsPixel $right $x $y
                $side = 'right'
            }

            if ($rows[$side].ContainsKey($y)) {
                $range = $rows[$side][$y]
                if ($x -lt $range[0]) { $range[0] = $x }
                if ($x -gt $range[1]) { $range[1] = $x }
            } else {
                $rows[$side][$y] = @($x, $x)
            }
        }
    }
    Add-LateralWeights $mask $rows
    return @{ Bitmap = $mask; Left = $left; Right = $right }
}

function Test-TrackMask($result, [int]$width, [int]$height) {
    $left = Get-TrackStats $result.Left
    $right = Get-TrackStats $result.Right
    $checks = [ordered]@{
        leftPixels = $left.pixels
        rightPixels = $right.pixels
        coverage = 0.0
        lateralSeparationFraction = 0.0
        widthDifferenceFraction = 0.0
        mirrorErrorFraction = 0.0
    }
    if ($left.pixels -eq 0 -or $right.pixels -eq 0) {
        return @{ Valid = $false; Reason = 'one of the two track channels is empty'; Checks = $checks; Left = $left; Right = $right }
    }

    $checks.coverage = [Math]::Round(($left.pixels + $right.pixels) / [double]($width * $height), 4)
    $checks.lateralSeparationFraction = [Math]::Round([Math]::Abs($left.centroidX - $right.centroidX) / [double]$width, 4)
    $widest = [Math]::Max($left.widthPixels, $right.widthPixels)
    $checks.widthDifferenceFraction = [Math]::Round([Math]::Abs($left.widthPixels - $right.widthPixels) / $widest, 4)
    $checks.mirrorErrorFraction = [Math]::Round([Math]::Abs(($left.centroidX + $right.centroidX) - ($width - 1)) / [double]$width, 4)

    if ($checks.coverage -lt 0.05 -or $checks.coverage -gt 0.7) {
        return @{ Valid = $false; Reason = "track coverage $($checks.coverage) is not plausible"; Checks = $checks; Left = $left; Right = $right }
    }
    if ($checks.lateralSeparationFraction -lt 0.2) {
        return @{ Valid = $false; Reason = 'the two tracks are not separated across the hull'; Checks = $checks; Left = $left; Right = $right }
    }
    if ($checks.widthDifferenceFraction -gt 0.4) {
        return @{ Valid = $false; Reason = 'the left and right mask bands differ too much in width'; Checks = $checks; Left = $left; Right = $right }
    }
    return @{ Valid = $true; Reason = ''; Checks = $checks; Left = $left; Right = $right }
}

function Write-TrackDiagnostic(
    [Drawing.Bitmap]$hull,
    [Drawing.Bitmap]$mask,
    $result,
    $trackLayout,
    [int]$scale,
    [string]$path) {
    $width = $mask.Width * $scale
    $height = $mask.Height * $scale
    $diagram = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($diagram)
    $font = [Drawing.Font]::new('Consolas', [Math]::Max(11, [int]($scale * 5)))
    $smallFont = [Drawing.Font]::new('Consolas', [Math]::Max(9, [int]($scale * 4)))
    $white = [Drawing.Brushes]::White
    $whitePen = [Drawing.Pen]::new([Drawing.Color]::White, 1.0)
    $yellow = [Drawing.Pen]::new([Drawing.Color]::Yellow, 1.0)
    $cyan = [Drawing.Pen]::new([Drawing.Color]::Cyan, 1.0)
    $magenta = [Drawing.Pen]::new([Drawing.Color]::Magenta, 1.0)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
        $graphics.DrawImage($hull, [Drawing.Rectangle]::new(0, 0, $width, $height))
        $dim = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(190, 0, 0, 0))
        $overlay = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(120, 255, 255, 255))
        try {
            $graphics.FillRectangle($dim, 0, 0, $width, $height)
            $graphics.DrawImage($mask, [Drawing.Rectangle]::new(0, 0, $width, $height))
            $graphics.FillRectangle($overlay, 0, 0, $width, $height)
        } finally {
            $dim.Dispose()
            $overlay.Dispose()
        }

        foreach ($side in @(
                @{ Stats = $result.Left; Pen = $yellow; Label = 'LEFT (red)' },
                @{ Stats = $result.Right; Pen = $cyan; Label = 'RIGHT (green)' })) {
            $stats = $side.Stats
            if ($stats.Pixels -eq 0) { continue }
            $graphics.DrawRectangle(
                $side.Pen,
                $stats.MinX * $scale,
                $stats.MinY * $scale,
                [Math]::Max(1, ($stats.MaxX - $stats.MinX + 1) * $scale),
                [Math]::Max(1, ($stats.MaxY - $stats.MinY + 1) * $scale))
            $centroidX = [float]($stats.SumX / $stats.Pixels * $scale)
            $centroidY = [float]($stats.SumY / $stats.Pixels * $scale)
            $graphics.DrawLine($side.Pen, $centroidX - 8, $centroidY, $centroidX + 8, $centroidY)
            $graphics.DrawLine($side.Pen, $centroidX, $centroidY - 8, $centroidX, $centroidY + 8)
            $labelWidth = $graphics.MeasureString($side.Label, $smallFont).Width
            $labelX = [Math]::Min([Math]::Max($centroidX - $labelWidth / 2.0, 4), $width - $labelWidth - 4)
            $graphics.DrawString($side.Label, $smallFont, $white, [float]$labelX, $centroidY + 8)
        }

        # Tank front: the layout records it, and every scroll direction is derived from it.
        $sign = [double]$trackLayout.forwardSignV
        $centreX = $width / 2.0
        $centreY = $height / 2.0
        $tipY = $centreY - $sign * ($height * 0.42)
        $magenta.Width = 2.0
        $graphics.DrawLine($magenta, [float]$centreX, [float]$centreY, [float]$centreX, [float]$tipY)
        $arrow = @(
            [Drawing.PointF]::new([float]$centreX, [float]($tipY - $sign * 10)),
            [Drawing.PointF]::new([float]($centreX - 7), [float]$tipY),
            [Drawing.PointF]::new([float]($centreX + 7), [float]$tipY)
        )
        $graphics.FillPolygon([Drawing.Brushes]::Magenta, $arrow)
        $graphics.DrawString('FRONT', $font, [Drawing.Brushes]::Magenta, [float]($centreX + 10), [float]$tipY)

        # Tread feature spacing: the shader repeats this pattern, so one period is ruled out.
        $pitchPixels = [double]$trackLayout.treadPitchMetres * [double]$trackLayout.outputPixelsPerMeter
        if ($pitchPixels -gt 1) {
            $steps = [int][Math]::Floor($trackLayout.mask.height / $pitchPixels)
            for ($step = 0; $step -le $steps; $step++) {
                $tickY = [float]($step * $pitchPixels * $scale)
                $graphics.DrawLine($whitePen, 0, $tickY, 8 * $scale, $tickY)
            }
            $graphics.DrawString(
                ("tread pitch {0:0.###} m = {1:0.#} px" -f $trackLayout.treadPitchMetres, $pitchPixels),
                $smallFont, $white, 10 * $scale, [float]($height - 8 - $smallFont.Height))
        }

        $caption = @(
            ("vehicle          {0}" -f $VehicleId)
            ("mask source      {0}" -f $trackLayout.source)
            ("sides            {0}{1}" -f $trackLayout.sideSource, $(if ($trackLayout.channelsSwapped) { ' (channels swapped)' } else { '' }))
            ("mask             {0} x {1} px" -f $trackLayout.mask.width, $trackLayout.mask.height)
            ("band separation  {0:0.#} px = {1:0.###} m" -f $trackLayout.bandSeparationPixels, $trackLayout.bandSeparationMetres)
            ("band width       {0:0.#} px = {1:0.###} m" -f $trackLayout.bandWidthPixels, $trackLayout.bandWidthMetres)
            ("coverage         {0:0.###}" -f $trackLayout.checks.coverage)
            ("mirror error     {0:0.###}" -f $trackLayout.checks.mirrorErrorFraction)
            ("forward sign V   {0}" -f $trackLayout.forwardSignV)
            ("fallback reason  {0}" -f $(if ($trackLayout.Contains('fallbackReason')) { $trackLayout.fallbackReason } else { '-' }))
        ) -join "`n"
        $captionSize = $graphics.MeasureString($caption, $smallFont)
        $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(210, 0, 0, 0))
        try {
            $graphics.FillRectangle($background, 4, 4, $captionSize.Width + 8, $captionSize.Height + 8)
        } finally {
            $background.Dispose()
        }
        $graphics.DrawString($caption, $smallFont, $white, 8, 8)

        $diagram.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $font.Dispose()
        $smallFont.Dispose()
        $whitePen.Dispose()
        $yellow.Dispose()
        $cyan.Dispose()
        $magenta.Dispose()
        $diagram.Dispose()
    }
}

$padding = [int]$config.render.paddingPixels
$gap = [int]$config.render.gapPixels
$scale = [double]$config.render.outputPixelsPerMeter / [double]$config.render.intermediatePixelsPerMeter
$outputPixelsPerMeter = [double]$config.render.outputPixelsPerMeter
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
            schemaVersion = 3
            texture = [ordered]@{ width = $width; height = $height }
            hull = [ordered]@{
                x = $padding
                y = $height - ($hullY + $hullHeight)
                width = $hullWidth
                height = $hullHeight
                pivot = [ordered]@{ x = 0.5; y = 0.5 }
                turretMount = [ordered]@{ x = 0.5; y = 0.5 }
            }
            turret = [ordered]@{
                x = $turretX
                y = $height - ($turretY + $turretHeight)
                width = $turretWidth
                height = $turretHeight
                pivot = [ordered]@{ x = 0.5; y = 0.5 }
            }
        }

        $renderMetadataPath = $metadataPath
        if (Test-Path -LiteralPath $renderMetadataPath -PathType Leaf) {
            $renderMetadata = Get-Content -LiteralPath $renderMetadataPath -Raw | ConvertFrom-Json
            if ($null -ne $renderMetadata.turretPivot) {
                $pivotPixelX = [double]$renderMetadata.turretPivot.xPixels
                $pivotPixelY = [double]$renderMetadata.turretPivot.yPixels
                $turretPivotX = ($pivotPixelX - $turretBounds.X) / $turretBounds.Width
                $turretPivotY = 1.0 - (($pivotPixelY - $turretBounds.Y) / $turretBounds.Height)
                $hullMountX = ($pivotPixelX - $hullBounds.X) / $hullBounds.Width
                $hullMountY = 1.0 - (($pivotPixelY - $hullBounds.Y) / $hullBounds.Height)
                foreach ($coordinate in @($turretPivotX, $turretPivotY, $hullMountX, $hullMountY)) {
                    if ($coordinate -lt 0.0 -or $coordinate -gt 1.0) {
                        throw "Projected turret-ring coordinate lies outside a cropped sprite: $coordinate"
                    }
                }
                $layout.turret.pivot.x = $turretPivotX
                $layout.turret.pivot.y = $turretPivotY
                $layout.hull.turretMount.x = $hullMountX
                $layout.hull.turretMount.y = $hullMountY
            }
        }

        # ---------------------------------------------------------------- track mask
        $metadata = Get-ImageMetadata $metadataPath
        $override = Get-TrackOverride $overridePath
        $sideSource = 'cameraConvention'
        $starboardImageX = 0.0
        $forwardImageY = 1.0
        $detected = $false
        if ($null -ne $metadata -and $null -ne $metadata.trackMask) {
            $detected = [bool]$metadata.trackMask.detected
            if ($null -ne $metadata.trackMask.starboardImage) {
                $starboardImageX = [double]$metadata.trackMask.starboardImage.xPixels
                $sideSource = [string]$metadata.trackMask.sideSource
            }
            if ($null -ne $metadata.trackMask.forwardImage) {
                $forwardImageY = [double]$metadata.trackMask.forwardImage.yPixels
            }
        }
        $forceFallback = $false
        if ($null -ne $override -and $null -ne $override.forceFallback) {
            $forceFallback = [bool]$override.forceFallback
        }
        $swapSides = $false
        if ($null -ne $override -and $null -ne $override.swapSides) {
            $swapSides = [bool]$override.swapSides
        }
        $bandFraction = 0.18
        if ($null -ne $override -and $null -ne $override.bandFraction) {
            $bandFraction = [double]$override.bandFraction
        }
        $treadPitchMetres = [double](Get-ScalarOr $config.render.trackFeaturePitchMetres 0.22)
        if ($null -ne $override -and $null -ne $override.treadPitchMetres) {
            $treadPitchMetres = [double]$override.treadPitchMetres
        }
        if ($treadPitchMetres -le 0.0) {
            throw "The tread feature pitch must be positive, got $treadPitchMetres metres."
        }

        # Image x grows to the right in both the render and the mask, so the projected
        # starboard direction says which half of the mask holds the tank's right track. Red marks
        # the left track, which is therefore on the opposite half of the image.
        $starboardIsImageRight = $starboardImageX -ge 0.0
        $geometryKnown = $sideSource -eq 'gunDirection'
        $redOnImageLeft = $starboardIsImageRight
        $forwardSignV = -1
        if ($forwardImageY -lt 0.0) { $forwardSignV = 1 }

        $hullMask = Get-ScaledCrop $hull $hullBounds $hullWidth $hullHeight
        try {
            $maskSource = 'trackMeshes'
            $fallbackReason = ''
            $channelsSwapped = $swapSides
            $result = $null
            $verdict = $null
            if ($detected -and -not $forceFallback -and (Test-Path -LiteralPath $tracksPath -PathType Leaf)) {
                $tracksBitmap = [Drawing.Bitmap]::FromFile($tracksPath)
                try {
                    $rawMask = Get-ScaledCrop $tracksBitmap $hullBounds $hullWidth $hullHeight
                } finally {
                    $tracksBitmap.Dispose()
                }
                try {
                    if ($geometryKnown -and -not $swapSides) {
                        # The name-based sides are verified against the model's own starboard
                        # direction; a mirrored result means the two channels are exchanged.
                        $probe = New-TrackMask $rawMask $false
                        try {
                            $probeStats = Test-TrackMask $probe $hullWidth $hullHeight
                            if ($probeStats.Valid) {
                                $rightIsImageRight = $probeStats.Right.centroidX -gt $probeStats.Left.centroidX
                                if ($rightIsImageRight -ne $starboardIsImageRight) {
                                    $channelsSwapped = $true
                                }
                            }
                        } finally {
                            ($probe.Bitmap).Dispose()
                        }
                    }
                    $result = New-TrackMask $rawMask $channelsSwapped
                } finally {
                    $rawMask.Dispose()
                }
                $verdict = Test-TrackMask $result $hullWidth $hullHeight
            } else {
                $maskSource = 'hullSideBands'
                if ($forceFallback) {
                    $fallbackReason = 'the local track override forces the side-band mask'
                } elseif (-not $detected) {
                    $fallbackReason = 'no track meshes were identified in the 3D model'
                } else {
                    $fallbackReason = 'the track mask render is missing'
                }
            }

            if ($null -eq $result) {
                $result = New-SideBandMask $hullMask $bandFraction $redOnImageLeft
                $verdict = Test-TrackMask $result $hullWidth $hullHeight
            } elseif (-not $verdict.Valid) {
                # An unusable mesh mask is replaced by the safe silhouette bands rather
                # than failing the whole render, and the reason is recorded.
                ($result.Bitmap).Dispose()
                $fallbackReason = "the track mesh mask was rejected: $($verdict.Reason)"
                $maskSource = 'hullSideBands'
                $result = New-SideBandMask $hullMask $bandFraction $redOnImageLeft
                $verdict = Test-TrackMask $result $hullWidth $hullHeight
            }
            if (-not $verdict.Valid) {
                throw "Track mask for $VehicleId could not be built: $($verdict.Reason)"
            }

            if ($maskSource -eq 'trackMeshes') {
                if (-not $geometryKnown) {
                    $sideSource = 'meshNames'
                }
            } else {
                if ($geometryKnown) { $sideSource = 'gunDirection' } else { $sideSource = 'cameraConvention' }
            }

            $maskFile = "{0}_tracks_mask.png" -f $VehicleId
            $maskPath = Join-Path $vehicleOutput $maskFile
            ($result.Bitmap).Save($maskPath, [Drawing.Imaging.ImageFormat]::Png)

            $leftStats = Get-TrackStats $result.Left
            $rightStats = Get-TrackStats $result.Right
            $gaugePixels = [Math]::Round([Math]::Abs($leftStats.centroidX - $rightStats.centroidX), 3)
            $meanWidthPixels = [Math]::Round(($leftStats.widthPixels + $rightStats.widthPixels) / 2.0, 3)
            $trackLayout = [ordered]@{
                schemaVersion = 1
                source = $maskSource
                maskFile = $maskFile
                mask = [ordered]@{ width = $hullWidth; height = $hullHeight }
                outputPixelsPerMeter = $outputPixelsPerMeter
                sideSource = $sideSource
                geometryVerified = $geometryKnown
                channelsSwapped = $channelsSwapped
                starboardImageXPixels = $starboardImageX
                forwardSignV = $forwardSignV
                bandSeparationPixels = $gaugePixels
                bandSeparationMetres = [Math]::Round($gaugePixels / $outputPixelsPerMeter, 4)
                bandWidthPixels = $meanWidthPixels
                bandWidthMetres = [Math]::Round($meanWidthPixels / $outputPixelsPerMeter, 4)
                treadPitchMetres = $treadPitchMetres
                left = $leftStats
                right = $rightStats
                checks = $verdict.Checks
                diagnostic = ("Tools/WoTSpritePipeline/Local/Renders/{0}/tracks_mask_diagnostic.png" -f $VehicleId)
            }
            if ($fallbackReason) { $trackLayout.fallbackReason = $fallbackReason }
            $layout.tracks = $trackLayout

            $diagnosticPath = Join-Path $renderRoot 'tracks_mask_diagnostic.png'
            $diagnosticScale = [Math]::Max(1, [int][Math]::Ceiling(420.0 / $hullWidth))
            Write-TrackDiagnostic $hullMask $result.Bitmap $result $trackLayout $diagnosticScale $diagnosticPath
            ($result.Bitmap).Dispose()
            Write-Host "Track mask: $maskPath"
            Write-Host "Track diagnostic: $diagnosticPath"
        } finally {
            $hullMask.Dispose()
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
