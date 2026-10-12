[CmdletBinding()]
param(
    [ValidateSet('Build', 'Validate', 'List')]
    [string]$Mode = 'Build',

    [string]$Map = '10_hills',

    [string]$WoTRoot
)

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $pipelineRoot '..\..')).Path
$catalog = Join-Path $pipelineRoot 'map_conversion_catalog.json'
$parserRoot = Join-Path $pipelineRoot 'Local\ThirdParty\wot-space-bin-utils'

if ($Mode -eq 'List') {
    & python (Join-Path $PSScriptRoot 'convert_wot_map_to_primitives.py') `
        --project-root $projectRoot `
        --wot-root $projectRoot `
        --parser-root $parserRoot `
        --catalog $catalog `
        --map $Map `
        --mode list
    if ($LASTEXITCODE -ne 0) {
        throw "WoT map primitive pipeline failed with exit code $LASTEXITCODE"
    }
    return
}

if (-not $WoTRoot) {
    $sharedConfigPath = Join-Path $projectRoot 'Tools\WoTSpritePipeline\config.local.json'
    if (Test-Path -LiteralPath $sharedConfigPath) {
        $sharedConfig = Get-Content -Raw -LiteralPath $sharedConfigPath | ConvertFrom-Json
        $WoTRoot = $sharedConfig.wotRoot
    }
}

if (-not $WoTRoot) {
    throw 'Укажите -WoTRoot или настройте Tools/WoTSpritePipeline/config.local.json.'
}

if (-not (Test-Path -LiteralPath (Join-Path $parserRoot 'compiled_space'))) {
    & (Join-Path $PSScriptRoot 'Setup-WoTMapPrimitivePipeline.ps1')
}

$modeArgument = $Mode.ToLowerInvariant()
& python (Join-Path $PSScriptRoot 'convert_wot_map_to_primitives.py') `
    --project-root $projectRoot `
    --wot-root $WoTRoot `
    --parser-root $parserRoot `
    --catalog $catalog `
    --map $Map `
    --mode $modeArgument

if ($LASTEXITCODE -ne 0) {
    throw "WoT map primitive pipeline failed with exit code $LASTEXITCODE"
}
