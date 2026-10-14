[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$thirdPartyRoot = Join-Path $pipelineRoot 'Local\ThirdParty'
$parserRoot = Join-Path $thirdPartyRoot 'wot-space-bin-utils'
$repository = 'https://bitbucket.org/SkepticalFox/wot-space.bin-utils.git'
$revision = '08b226cea3b485935a5118e94d84c3abf908f953'

New-Item -ItemType Directory -Force -Path $thirdPartyRoot | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $parserRoot '.git'))) {
    git clone $repository $parserRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Не удалось клонировать wot-space.bin-utils.'
    }
}

$revisionAvailable = $false
git -C $parserRoot cat-file -e "$revision^{commit}" 2>$null
if ($LASTEXITCODE -eq 0) {
    $revisionAvailable = $true
}

if (-not $revisionAvailable) {
    git -C $parserRoot fetch --quiet origin $revision
    if ($LASTEXITCODE -ne 0) {
        throw 'Не удалось получить закреплённую ревизию wot-space.bin-utils.'
    }
}
git -C $parserRoot checkout --quiet --detach $revision
if ($LASTEXITCODE -ne 0) {
    throw 'Не удалось переключить wot-space.bin-utils на закреплённую ревизию.'
}

$actual = (git -C $parserRoot rev-parse HEAD).Trim()
if ($actual -ne $revision) {
    throw "Ожидалась ревизия $revision, получена $actual"
}

Write-Host "WoT map parser ready: $actual"
