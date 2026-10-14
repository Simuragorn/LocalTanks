param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$toolRoot = Join-Path $ProjectRoot "LocalData\WoTAudioPipeline\ThirdParty"
New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null

function Get-File([string]$Uri, [string]$Destination) {
    if ((Test-Path -LiteralPath $Destination) -and -not $Force) { return }
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $Destination
}

$wwiser = Join-Path $toolRoot "wwiser.pyz"
$namesDb = Join-Path $toolRoot "wwnames.db3"
$vgmZip = Join-Path $toolRoot "vgmstream-r2117-win64.zip"
$vgmRoot = Join-Path $toolRoot "vgmstream"
$quickBmsZip = Join-Path $toolRoot "quickbms-0.12.0.zip"
$quickBmsRoot = Join-Path $toolRoot "quickbms"
$pckScript = Join-Path $toolRoot "wwise_pck_extractor.bms"

Get-File "https://github.com/bnnm/wwiser/releases/download/v20260808/wwiser.pyz" $wwiser
Get-File "https://github.com/bnnm/wwiser/releases/download/v20260808/wwnames.db3" $namesDb
Get-File "https://github.com/vgmstream/vgmstream/releases/download/r2117/vgmstream-win64.zip" $vgmZip
Get-File "https://aluigi.altervista.org/papers/quickbms.zip" $quickBmsZip
Get-File "https://raw.githubusercontent.com/bnnm/wwiser-utils/ae22a94e33fc981feafd53327ce9d3f4aa6afa0f/scripts/wwise_pck_extractor.bms" $pckScript

if ($Force -or -not (Test-Path -LiteralPath (Join-Path $vgmRoot "vgmstream-cli.exe"))) {
    New-Item -ItemType Directory -Force -Path $vgmRoot | Out-Null
    Expand-Archive -LiteralPath $vgmZip -DestinationPath $vgmRoot -Force
}

if ($Force -or -not (Test-Path -LiteralPath (Join-Path $quickBmsRoot "quickbms.exe"))) {
    New-Item -ItemType Directory -Force -Path $quickBmsRoot | Out-Null
    Expand-Archive -LiteralPath $quickBmsZip -DestinationPath $quickBmsRoot -Force
}

foreach ($required in @(
    $wwiser,
    $namesDb,
    (Join-Path $vgmRoot "vgmstream-cli.exe"),
    (Join-Path $quickBmsRoot "quickbms.exe"),
    $pckScript
)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "WoT audio dependency was not installed: $required"
    }
}

Write-Host "WoT audio tools are ready under LocalData (ignored by Git)."
