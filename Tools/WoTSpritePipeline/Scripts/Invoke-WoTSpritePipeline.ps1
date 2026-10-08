[CmdletBinding()]
param(
    [ValidateSet('Probe', 'Render')]
    [string]$Mode = 'Probe',
    [string]$VehicleId = '',
    [string]$Nation = '',
    [string]$Tier = '',
    [ValidateSet('', 'lightTank', 'mediumTank', 'heavyTank', 'AT-SPG', 'SPG')]
    [string]$VehicleType = '',
    [int]$ChassisIndex = -1,
    [int]$TurretIndex = -1,
    [int]$GunIndex = -1
)

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $pipelineRoot 'config.local.json'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "Local config is missing. Run Scripts\Setup-WoTSpritePipeline.ps1 first."
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$blender = $config.blenderExecutable
if (-not (Test-Path -LiteralPath $blender -PathType Leaf)) {
    throw "Blender executable is missing: $blender"
}

$env:BLENDER_USER_SCRIPTS = Join-Path $pipelineRoot 'Local\BlenderUserScripts'
$pythonScript = Join-Path $pipelineRoot 'Blender\wot_sprite_pipeline.py'
$arguments = @(
    '--background',
    '--factory-startup',
    '--python', $pythonScript,
    '--',
    '--mode', $Mode.ToLowerInvariant(),
    '--config', $configPath
)

if ($VehicleId) { $arguments += @('--vehicle-id', $VehicleId) }
if ($Nation) { $arguments += @('--nation', $Nation) }
if ($Tier) { $arguments += @('--tier', $Tier) }
if ($VehicleType) { $arguments += @('--vehicle-type', $VehicleType) }
if ($ChassisIndex -ge 0) { $arguments += @('--chassis-index', [string]$ChassisIndex) }
if ($TurretIndex -ge 0) { $arguments += @('--turret-index', [string]$TurretIndex) }
if ($GunIndex -ge 0) { $arguments += @('--gun-index', [string]$GunIndex) }

& $blender @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Blender pipeline failed with exit code $LASTEXITCODE."
}

if ($Mode -eq 'Render') {
    $composeScript = Join-Path $PSScriptRoot 'New-Strip2.ps1'
    & $composeScript -ConfigPath $configPath -VehicleId $VehicleId
}
