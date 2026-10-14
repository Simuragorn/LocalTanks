[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9_]+$')]
    [string]$TankId,

    [Parameter(Mandatory = $true)]
    [string]$SpritePath,

    [Parameter(Mandatory = $true)]
    [string]$PrefabPath,

    [string]$ProjectRoot = 'C:\UnityProjects\LocalTanks'
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure([string]$Message) {
    $failures.Add($Message)
}

function Resolve-ProjectPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $ProjectRoot $Path
}

if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    throw "LocalTanks project was not found: $ProjectRoot"
}

$tankJsonPath = Join-Path $ProjectRoot "Assets\Game\GameData\Definitions\Tanks\$TankId.json"
$scenarioPath = Join-Path $ProjectRoot 'Assets\Game\GameData\Scenarios\river_crossing_battle.json'
$prototypeSetupPath = Join-Path $ProjectRoot 'Assets\Game\Editor\PrototypeSetup.cs'
$riverSetupPath = Join-Path $ProjectRoot 'Assets\Game\Editor\RiverCrossingSetup.cs'
$resolvedSprite = Resolve-ProjectPath $SpritePath
$resolvedPrefab = Resolve-ProjectPath $PrefabPath
$canonicalRoot = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'Assets\LocalOnly\WoTGenerated'))
$audioClipRoot = Join-Path $ProjectRoot 'Assets\LocalOnly\Resources\WoTAudio\Clips'
$audioProfilePath = Join-Path $ProjectRoot 'Tools\WoTAudioPipeline\audio_clips.json'

foreach ($requiredPath in @($tankJsonPath, $scenarioPath, $prototypeSetupPath, $riverSetupPath, $resolvedSprite, $resolvedPrefab)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        Add-Failure "Missing required path: $requiredPath"
    }
}

$normalizedSprite = [IO.Path]::GetFullPath($resolvedSprite)
if (-not $normalizedSprite.StartsWith($canonicalRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    Add-Failure "Sprite is outside the canonical WoTGenerated root: $normalizedSprite"
}

$tank = $null
if (Test-Path -LiteralPath $tankJsonPath) {
    $tank = Get-Content -LiteralPath $tankJsonPath -Raw | ConvertFrom-Json
    if ($tank.id -ne $TankId) { Add-Failure "Tank JSON id '$($tank.id)' does not match '$TankId'." }
    if ([string]::IsNullOrWhiteSpace($tank.weaponId)) { Add-Failure 'Tank weaponId is empty.' }
    if ([string]::IsNullOrWhiteSpace($tank.vehicleClass)) { Add-Failure 'Tank vehicleClass is empty.' }
    if ([string]::IsNullOrWhiteSpace($tank.nation)) { Add-Failure 'Tank nation is empty.' }
    $tierProperty = $tank.PSObject.Properties['tier']
    if ($null -eq $tierProperty) {
        Add-Failure 'Tank tier is missing.'
    }
    elseif ([double]$tank.tier -ne [Math]::Truncate([double]$tank.tier) -or [int]$tank.tier -lt 1 -or [int]$tank.tier -gt 10) {
        Add-Failure "Tank tier '$($tank.tier)' must be an integer from 1 through 10."
    }
    if ($null -eq $tank.availableInGame) { Add-Failure 'Tank availableInGame is missing.' }
    if ($tank.availableInGame) {
        if ($null -eq $tank.audio) {
            Add-Failure 'Available tank audio profile is missing.'
        }
        else {
            foreach ($audioField in @('engineClipId', 'shotClipId', 'turretClipId')) {
                $clipId = [string]$tank.audio.$audioField
                if ([string]::IsNullOrWhiteSpace($clipId)) {
                    Add-Failure "Tank audio field '$audioField' is empty."
                    continue
                }

                $clipPath = Join-Path $audioClipRoot "$clipId.wav"
                if (-not (Test-Path -LiteralPath $clipPath)) {
                    Add-Failure "Local audio clip '$clipId' is missing; run Invoke-WoTAudioPipeline.ps1."
                }
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($tank.weaponId)) {
        $weaponPath = Join-Path $ProjectRoot "Assets\Game\GameData\Definitions\Weapons\$($tank.weaponId).json"
        if (-not (Test-Path -LiteralPath $weaponPath)) {
            Add-Failure "Missing weapon definition: $weaponPath"
        }
        else {
            $weapon = Get-Content -LiteralPath $weaponPath -Raw | ConvertFrom-Json
            $shellPath = Join-Path $ProjectRoot "Assets\Game\GameData\Definitions\Shells\$($weapon.shellId).json"
            if ([string]::IsNullOrWhiteSpace($weapon.shellId) -or -not (Test-Path -LiteralPath $shellPath)) {
                Add-Failure "Missing shell definition for weapon '$($tank.weaponId)': $shellPath"
            }
        }
    }
}

if (Test-Path -LiteralPath $scenarioPath) {
    $scenario = Get-Content -LiteralPath $scenarioPath -Raw | ConvertFrom-Json
    $teamA = @($scenario.entries | Where-Object team -eq 'TeamA')
    $teamB = @($scenario.entries | Where-Object team -eq 'TeamB')
    $players = @($scenario.entries | Where-Object playerControlled -eq $true)
    if ($teamA.Count -eq 0) { Add-Failure 'TeamA has no entries.' }
    if ($teamB.Count -eq 0) { Add-Failure 'TeamB has no entries.' }
    if ($teamA.Count -ne $teamB.Count) {
        Add-Failure "Battle roster is asymmetric: TeamA has $($teamA.Count), TeamB has $($teamB.Count)."
    }
    if ($players.Count -ne 1) { Add-Failure "Scenario has $($players.Count) player entries instead of 1." }
    if ($tank.availableInGame -and -not ($scenario.entries.tankId -contains $TankId)) {
        Add-Failure "Scenario does not contain tank '$TankId'."
    }
    if (-not $tank.availableInGame -and ($scenario.entries.tankId -contains $TankId)) {
        Add-Failure "Unavailable tank '$TankId' is present in the scenario."
    }

    $definitionRoot = Join-Path $ProjectRoot 'Assets\Game\GameData\Definitions\Tanks'
    $definitions = @{}
    foreach ($definitionFile in Get-ChildItem -LiteralPath $definitionRoot -Filter '*.json' -File) {
        $definition = Get-Content -LiteralPath $definitionFile.FullName -Raw | ConvertFrom-Json
        $definitions[$definition.id] = $definition
    }
    foreach ($entry in $scenario.entries) {
        $definition = $definitions[$entry.tankId]
        if ($null -eq $definition) { continue }
        if (-not $definition.availableInGame) {
            Add-Failure "Scenario uses unavailable tank '$($entry.tankId)'."
        }
    }
    foreach ($teamName in @('TeamA', 'TeamB')) {
        $teamNations = @($scenario.entries | Where-Object team -eq $teamName | ForEach-Object {
            $definitions[$_.tankId].nation
        } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        if ($teamNations.Count -ne 1) {
            Add-Failure "$teamName must contain exactly one nation; found $($teamNations -join ', ')."
        }
    }

    $spawnKeys = @($scenario.entries | ForEach-Object { "$($_.baseId):$($_.spawnSlot)" })
    if (@($spawnKeys | Select-Object -Unique).Count -ne $spawnKeys.Count) {
        Add-Failure 'Scenario contains duplicate base spawn slots.'
    }
}

if (Test-Path -LiteralPath $prototypeSetupPath) {
    $prototypeSource = Get-Content -LiteralPath $prototypeSetupPath -Raw
    if ($prototypeSource -notmatch [regex]::Escape("$TankId.asset")) {
        Add-Failure "PrototypeSetup does not reference generated definition '$TankId.asset'."
    }
    $prefabAssetPath = $PrefabPath.Replace('\', '/')
    if ($prototypeSource -notmatch [regex]::Escape($prefabAssetPath)) {
        Add-Failure "PrototypeSetup does not reference prefab '$prefabAssetPath'."
    }
}

if ($tank.availableInGame -and (Test-Path -LiteralPath $riverSetupPath)) {
    $riverSource = Get-Content -LiteralPath $riverSetupPath -Raw
    $derivedBindingMarkers = @(
        'tank.availableInGame',
        'scenario?.entries',
        'PrototypeSetup.FindTankPrefabPath(tankId)'
    )
    foreach ($marker in $derivedBindingMarkers) {
        if ($riverSource -notmatch [regex]::Escape($marker)) {
            Add-Failure "RiverCrossingSetup no longer derives playable/scenario prefab bindings from data (missing '$marker')."
        }
    }
}

if ($tank.availableInGame -and (Test-Path -LiteralPath $audioProfilePath) -and $null -ne $tank.audio) {
    $audioProfile = Get-Content -LiteralPath $audioProfilePath -Raw | ConvertFrom-Json
    $registeredClipIds = @($audioProfile.clips.clipId)
    foreach ($audioField in @('engineClipId', 'shotClipId', 'turretClipId')) {
        $clipId = [string]$tank.audio.$audioField
        if (-not [string]::IsNullOrWhiteSpace($clipId) -and -not ($registeredClipIds -contains $clipId)) {
            Add-Failure "Audio clip '$clipId' is not registered in Tools/WoTAudioPipeline/audio_clips.json."
        }
    }
}

if (Test-Path -LiteralPath $resolvedPrefab) {
    $prefabSource = Get-Content -LiteralPath $resolvedPrefab -Raw
    if ($prefabSource -notmatch 'LocalTanks\.TankAudioController') {
        Add-Failure "Prefab does not contain TankAudioController: $resolvedPrefab"
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("LocalTanks tank validation failed:`n- " + ($failures -join "`n- "))
    exit 1
}

Write-Output "Validated '$TankId' at WoT tier $($tank.tier): canonical sprite, data references, prefab registration, availability, and national roster rules are consistent."
