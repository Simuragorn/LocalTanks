[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TankName,

    [string]$ProjectRoot = 'C:\UnityProjects\LocalTanks'
)

$ErrorActionPreference = 'Stop'

function Normalize-Name([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    return ([regex]::Replace($Value.ToLowerInvariant(), '[^\p{L}\p{Nd}]', ''))
}

if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    throw "LocalTanks project was not found: $ProjectRoot"
}

$needle = Normalize-Name $TankName
if (-not $needle) { throw 'TankName does not contain searchable characters.' }

$canonicalRoot = Join-Path $ProjectRoot 'Assets\LocalOnly\WoTGenerated'
$candidates = [System.Collections.Generic.List[object]]::new()

if (Test-Path -LiteralPath $canonicalRoot -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $canonicalRoot -Recurse -File -Filter '*_strip2.png') {

        $stem = $file.BaseName -replace '_strip2$', ''
        $folder = $file.Directory.Name
        $stemKey = Normalize-Name $stem
        $folderKey = Normalize-Name $folder
        $relativeKey = Normalize-Name $file.FullName.Substring($canonicalRoot.Length)
        $score = 0

        if ($stemKey -eq $needle) { $score = 120 }
        elseif ($folderKey -eq $needle) { $score = 110 }
        elseif ($stemKey.EndsWith($needle) -or $needle.EndsWith($stemKey)) { $score = 90 }
        elseif ($stemKey.Contains($needle) -or $folderKey.Contains($needle)) { $score = 75 }
        elseif ($relativeKey.Contains($needle)) { $score = 50 }

        $layoutPath = Join-Path $file.Directory.FullName ($file.BaseName -replace '_strip2$', '_layout.json')
        $manifestPath = Join-Path $file.Directory.FullName ($file.BaseName -replace '_strip2$', '_manifest.json')
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            try {
                $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                $manifestText = @($manifest.vehicle.id, $manifest.vehicle.displayName) -join ' '
                $manifestKey = Normalize-Name $manifestText
                if ($manifestKey -eq $needle) { $score = [Math]::Max($score, 115) }
                elseif ($manifestKey.Contains($needle)) { $score = [Math]::Max($score, 80) }
            }
            catch {
                # A malformed optional manifest does not make the PNG disappear.
            }
        }

        if ($score -le 0) { continue }
        $candidates.Add([pscustomobject]@{
            score = $score
            spritePath = $file.FullName
            layoutPath = if (Test-Path -LiteralPath $layoutPath -PathType Leaf) { $layoutPath } else { $null }
            manifestPath = if (Test-Path -LiteralPath $manifestPath -PathType Leaf) { $manifestPath } else { $null }
            canonical = $true
            localOnly = $true
        })
    }
}

$ranked = @($candidates | Sort-Object @{ Expression = 'score'; Descending = $true }, @{ Expression = 'spritePath'; Descending = $false })
$bestScore = if ($ranked.Count) { $ranked[0].score } else { 0 }
$best = @($ranked | Where-Object score -eq $bestScore)

[ordered]@{
    query = $TankName
    found = $ranked.Count -gt 0
    ambiguous = $best.Count -gt 1
    best = if ($best.Count -eq 1) { $best[0] } else { $null }
    candidates = $ranked
} | ConvertTo-Json -Depth 6
