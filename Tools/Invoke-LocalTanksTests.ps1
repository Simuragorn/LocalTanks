# Runs a LocalTanks Unity test platform in batch mode and reports the tally.
#
# The point of this wrapper over calling Unity directly is the diagnostic switches it can pass
# through: a PlayMode battle can be recorded, run faster than real time, and have its tuning
# constants overridden, all without rebuilding anything. See Docs/AI_TESTING.md.
#
#   ./Tools/Invoke-LocalTanksTests.ps1 -Platform PlayMode -Filter WoTMapPlayabilityTests -Run
#
#   -Run alone runs the suite as usual. Add -Telemetry to record the battle, -Speed to run it
#   faster, and -Override to sweep a field:
#
#   -Run -Telemetry -Speed 20 -Override NavigationAgent.avoidanceRadius=0.9
#
# A recorded run writes JSON next to the results; query it instead of reading the log.

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
    [int]$Seed = 0
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe'

if (-not (Test-Path $unity)) {
    throw "Unity was not found at $unity. Update this script if the editor moved."
}

if ([string]::IsNullOrWhiteSpace($Xml)) {
    $Xml = Join-Path $project "TestResults/$Platform-results.xml"
}
if ([string]::IsNullOrWhiteSpace($Log)) {
    $Log = Join-Path $project "TestResults/$Platform.log"
}

$arguments = @(
    '-batchmode', '-nographics',
    '-projectPath', "`"$project`"",
    '-runTests', '-testPlatform', $Platform,
    '-testResults', "`"$Xml`"",
    '-logFile', "`"$Log`""
)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @('-testFilter', "`"$Filter`"")
}
if ($Telemetry) {
    $arguments += @('-localtanksTelemetry')
    if (-not [string]::IsNullOrWhiteSpace($TelemetryOut)) {
        $arguments += @('-localtanksTelemetryOut', "`"$TelemetryOut`"")
    }
}
if ($Speed -gt 1) {
    $arguments += @('-localtanksSpeed', $Speed)
}
if ($Seconds -gt 0) {
    $arguments += @('-localtanksSeconds', $Seconds)
}
foreach ($item in $Override) {
    $arguments += @('-localtanksParam', $item)
}
if ($Seed -ne 0) {
    $arguments += @('-localtanksSeed', $Seed)
}

$command = "& `"$unity`" $($arguments -join ' ')"
if (-not $Run) {
    Write-Output 'Nothing was run. Add -Run to execute:'
    Write-Output $command
    return
}

Write-Output "Running $Platform$(if ($Filter) { " filtered by '$Filter'" })..."
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $unity
$psi.Arguments = $arguments -join ' '
$psi.UseShellExecute = $false
$psi.WorkingDirectory = $project
$process = [System.Diagnostics.Process]::Start($psi)
$process.WaitForExit()

if (-not (Test-Path $Xml)) {
    Write-Output "Unity exited with $($process.ExitCode) and wrote no results. Check $Log."
    exit 1
}

[xml]$results = Get-Content $Xml
# Deliberately not named $run: that is the script's own switch, and PowerShell would try to treat the
# assignment as setting it.
$summary = $results.'test-run'
Write-Output ("{0}: total={1} passed={2} failed={3} skipped={4}" -f `
    $Platform, $summary.total, $summary.passed, $summary.failed, $summary.skipped)
$results.SelectNodes('//test-case') | Where-Object { $_.result -ne 'Passed' } | ForEach-Object {
    Write-Output ("  {0}: {1}" -f $_.result, $_.Name)
}
if ($Telemetry) {
    Write-Output 'The battle recording is beside the results; query it rather than reading the log.'
}
if ($summary.failed -gt 0) { exit 1 }
