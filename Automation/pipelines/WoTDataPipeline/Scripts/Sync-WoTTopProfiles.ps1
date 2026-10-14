[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [switch]$Apply,
    [switch]$ApplyGunHandling
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
}
$script = Join-Path $PSScriptRoot 'sync_wot_top_profiles.py'
$config = Join-Path (Split-Path -Parent $PSScriptRoot) 'vehicle_profiles.json'
$arguments = @($script, '--project-root', $ProjectRoot, '--config', $config)
if ($Apply) { $arguments += '--apply' }
if ($ApplyGunHandling) { $arguments += '--apply-gun-handling' }

& python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "WoT top-profile synchronization failed with exit code $LASTEXITCODE."
}
