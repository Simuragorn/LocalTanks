[CmdletBinding()]
param(
    [ValidateSet('List', 'Build', 'Validate')]
    [string]$Mode = 'List',
    [string[]]$Ids = @()
)

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent (Split-Path -Parent $pipelineRoot)
$pythonScript = Join-Path $PSScriptRoot 'build_environment_library.py'
$arguments = @(
    $pythonScript,
    '--mode', $Mode.ToLowerInvariant(),
    '--project-root', $projectRoot
)

if ($Ids.Count -gt 0) {
    $arguments += @('--ids', ($Ids -join ','))
}

& python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "WoT environment pipeline failed with exit code $LASTEXITCODE."
}

if ($Mode -eq 'Build') {
    $libraryRoot = Join-Path $projectRoot 'Assets\LocalOnly\WoTEnvironmentGenerated'
    & (Join-Path $PSScriptRoot 'New-WoTEnvironmentPreview.ps1') -LibraryRoot $libraryRoot
}
