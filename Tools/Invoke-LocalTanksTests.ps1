# Runs a LocalTanks Unity test platform in the background and reports the tally.
#
# This script no longer knows how to start Unity: it describes the run and hands it to
# Tools/Unity/Invoke-UnityBackground.ps1, which owns the editor path, the arguments, the queue, the private
# desktop, the log file and the exit code. What is left here is the test-specific part: which platform, which
# switches, and which files the results land in.
#
# Both platforms run headless, because a test proves what it proves from data, logs and telemetry:
#
#   ./Tools/Invoke-LocalTanksTests.ps1 -Platform PlayMode -Filter WoTMapPlayabilityTests -Run
#
#   -Run alone runs the suite as usual. Add -Telemetry to record the battle, -Speed to run it faster, and
#   -Override to sweep a field:
#
#   -Run -Telemetry -Speed 20 -Override NavigationAgent.avoidanceRadius=0.9
#
# A recorded run writes JSON next to the results; query it instead of reading the log.
#
# -Mode exists for diagnosing the launcher, not for normal use. Auto, the default, is Headless for both
# platforms; Graphical starts Unity with a real graphics device, which a test suite has no use for and which
# slows a run down. If a check really needs pixels, it is a diagnostic renderer with its own registry entry:
#
#   ./Tools/Unity/Invoke-UnityBackground.ps1 -Operation RenderCombatVfxProof -Run

param(
    [ValidateSet('EditMode', 'PlayMode')]
    [string]$Platform = 'EditMode',

    # Test name substring. Empty runs the whole platform.
    [string]$Filter,

    # Where the NUnit XML and Unity log go. Defaults under TestResults, which is ignored by Git.
    [string]$Xml,
    [string]$Log,

    # Run the suite. Without it the script only prints the command it would use.
    [switch]$Run,

    # Record the battle to JSON. Off by default: a normal battle must pay nothing for it.
    [switch]$Telemetry,

    # Where to write the recording. Defaults to TestResults/telemetry-<timestamp>.json.
    [string]$TelemetryOut,

    # Simulation multiplier. Never changes the physics step, so trajectories stay comparable.
    [ValidateRange(1, 200)]
    [float]$Speed = 1,

    # Stop a batch battle after this many simulated seconds. 0 leaves the scene in charge.
    [float]$Seconds = 0,

    # Field overrides, as Component.field=value. Repeatable.
    [string[]]$Override = @(),

    # Random seed for the run, so a sweep can tell a real effect from run-to-run spread.
    [int]$Seed = 0,

    # Auto follows the operation registry. Headless and Graphical exist for diagnosing the harness.
    [ValidateSet('Auto', 'Headless', 'Graphical')]
    [string]$Mode = 'Auto'
)

$ErrorActionPreference = 'Stop'

$launcher = Join-Path $PSScriptRoot 'Unity/Invoke-UnityBackground.ps1'
if (-not (Test-Path $launcher)) {
    throw "The Unity launcher was not found at $launcher. It is the only supported way to start Unity."
}

if ([string]::IsNullOrWhiteSpace($Xml)) {
    $Xml = "TestResults/$Platform-results.xml"
}

if ([string]::IsNullOrWhiteSpace($Log)) {
    $Log = "TestResults/$Platform.log"
}

$request = @{
    # The registry names the test operations EditModeTests and PlayModeTests, and decides their mode.
    Operation = "${Platform}Tests"
    Mode = $Mode
    ResultsPath = $Xml
    LogPath = $Log
    ReportPath = "TestResults/$Platform-run.json"
    Run = [bool]$Run
}

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $request.Filter = $Filter
}

if ($Telemetry) {
    $request.Telemetry = $true
    if (-not [string]::IsNullOrWhiteSpace($TelemetryOut)) {
        $request.TelemetryOut = $TelemetryOut
    }
}

if ($Speed -gt 1) {
    $request.Speed = $Speed
}

if ($Seconds -gt 0) {
    $request.Seconds = $Seconds
}

if ($Override.Count -gt 0) {
    $request.Override = $Override
}

if ($Seed -ne 0) {
    $request.Seed = $Seed
}

& $launcher @request
exit $LASTEXITCODE
