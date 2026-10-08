[CmdletBinding()]
param(
    [string]$BlenderExecutable = 'C:\Program Files\Blender Foundation\Blender 4.3\blender.exe',
    [string]$WotRoot = 'C:\Games\World_of_Tanks_EU'
)

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $pipelineRoot '..\..')).Path
$addonSource = Join-Path $pipelineRoot 'ThirdParty\WoT-Blender-Toolkit'
$localRoot = Join-Path $pipelineRoot 'Local'
$addonHost = Join-Path $localRoot 'BlenderUserScripts\addons'
$addonLink = Join-Path $addonHost 'wot_blender_toolkit'
$configPath = Join-Path $pipelineRoot 'config.local.json'
$addonRepository = 'https://github.com/wotcuk/WoT-Blender-Toolkit.git'
$addonRevision = '7739e42655ae305d6bd3c00eeffedb7fdc6df33d'

if (-not (Test-Path -LiteralPath $BlenderExecutable -PathType Leaf)) {
    throw "Blender 4.3 executable was not found: $BlenderExecutable"
}
if (-not (Test-Path -LiteralPath (Join-Path $WotRoot 'res\packages\scripts.pkg') -PathType Leaf)) {
    throw "World of Tanks scripts.pkg was not found below: $WotRoot"
}
if (-not (Test-Path -LiteralPath (Join-Path $addonSource '__init__.py') -PathType Leaf)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $addonSource) -Force | Out-Null
    & git clone $addonRepository $addonSource
    if ($LASTEXITCODE -ne 0) { throw 'Failed to clone WoT-Blender-Toolkit.' }
    & git -C $addonSource checkout $addonRevision
    if ($LASTEXITCODE -ne 0) { throw "Failed to check out add-on revision $addonRevision." }
}

New-Item -ItemType Directory -Path $addonHost -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $localRoot 'Reports') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Assets\LocalOnly\WoTGenerated') -Force | Out-Null

if (Test-Path -LiteralPath $addonLink) {
    $existing = Get-Item -LiteralPath $addonLink -Force
    if (-not ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Expected an add-on junction but found a normal item: $addonLink"
    }
} else {
    New-Item -ItemType Junction -Path $addonLink -Target $addonSource | Out-Null
}

$relativeAddon = 'ThirdParty\WoT-Blender-Toolkit'
$relativeLocal = 'Local'
$relativeOutput = '..\..\Assets\LocalOnly\WoTGenerated'
$config = [ordered]@{
    schemaVersion = 1
    blenderExecutable = $BlenderExecutable
    wotRoot = $WotRoot
    addonPath = $relativeAddon
    localRoot = $relativeLocal
    outputRoot = $relativeOutput
    render = [ordered]@{
        resolution = 1536
        intermediatePixelsPerMeter = 128
        outputPixelsPerMeter = 36
        paddingPixels = 8
        gapPixels = 8
        shadowless = $true
        saveBlend = $true
    }
}
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding utf8

Write-Host "Local configuration: $configPath"
Write-Host 'Running the headless catalog probe...'
& (Join-Path $PSScriptRoot 'Invoke-WoTSpritePipeline.ps1') -Mode Probe
