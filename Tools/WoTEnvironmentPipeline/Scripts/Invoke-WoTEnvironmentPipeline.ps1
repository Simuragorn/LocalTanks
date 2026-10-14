[CmdletBinding()]
param(
    [ValidateSet('List', 'Build', 'Validate', 'ListGround', 'BuildGround', 'ValidateGround')]
    [string]$Mode = 'List',
    [string[]]$Ids = @(),
    [string[]]$MapIds = @()
)

$ErrorActionPreference = 'Stop'
$pipelineRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent (Split-Path -Parent $pipelineRoot)
$pythonScript = Join-Path $PSScriptRoot 'build_environment_library.py'
$groundScript = Join-Path $PSScriptRoot 'bake_ground_texture.py'

if ($Mode -in @('ListGround', 'BuildGround', 'ValidateGround')) {
    # Keep image-processing dependencies project-local. A fresh workstation can build ground recipes with
    # one command, while the repository and the user's system Python remain clean.
    $pythonPackages = Join-Path $pipelineRoot 'Local\PythonPackages'
    New-Item -ItemType Directory -Force -Path $pythonPackages | Out-Null
    $previousPythonPath = $env:PYTHONPATH
    $env:PYTHONPATH = if ([string]::IsNullOrWhiteSpace($previousPythonPath)) {
        $pythonPackages
    } else {
        "$pythonPackages$([IO.Path]::PathSeparator)$previousPythonPath"
    }
    & python -c 'import PIL' 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Installing the project-local ground image dependency (Pillow)...'
        & python -m pip install --disable-pip-version-check --target $pythonPackages -r (Join-Path $pipelineRoot 'requirements-ground.txt')
        if ($LASTEXITCODE -ne 0) {
            throw "Could not install the WoT ground pipeline image dependency."
        }
    }

    $groundMode = switch ($Mode) {
        'ListGround' { 'list' }
        'BuildGround' { 'build' }
        'ValidateGround' { 'validate' }
    }
    $arguments = @(
        $groundScript,
        '--mode', $groundMode,
        '--project-root', $projectRoot
    )
    foreach ($mapId in $MapIds) {
        $arguments += @('--map-id', $mapId)
    }
    try {
        & python @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "WoT ground pipeline failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:PYTHONPATH = $previousPythonPath
    }
    return
}

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
