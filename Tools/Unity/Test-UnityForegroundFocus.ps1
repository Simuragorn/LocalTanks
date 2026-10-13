<#
.SYNOPSIS
Measures whether a Unity run started by this project's launcher ever reaches the user's foreground window.

.DESCRIPTION
This is the diagnostic behind the promise that an agent can compile, test and render while somebody plays a
game, watches a full-screen video or types. It watches the interactive desktop's foreground window from a
second runspace for the whole duration of a launch - before, during and after the moment Unity creates its
windows - and reports every window that held it.

It never activates anything: no SetForegroundWindow, no synthetic keys, no clicking. It reads
GetForegroundWindow and attributes it to a process, which is also why it can be trusted to report a failure
instead of hiding one.

The launcher already records the same thing about itself, in the run report; this script is the independent
measurement, taken from outside the process being judged. Run it after any change to the launcher, to the
desktop isolation or to Unity itself:

  ./Tools/Unity/Test-UnityForegroundFocus.ps1 -Operation RenderCombatVfxProof

A run passes when no window belonging to the Unity process tree ever held the foreground on the interactive
desktop. Exit code 0 means it held; 1 means it did not and the report above says which window took it.

.NOTES
The user's own window switching is reported as information, not as a failure: only windows of the process
tree the launcher started count against a run.
#>
param(
    # One or more registry operations to launch and watch. Each one is measured on its own.
    [string[]]$Operation = @('InspectCombatVfxSprites'),

    # How long to keep sampling before giving up on a launch, in seconds.
    [int]$TimeoutSeconds = 3600,

    # Sampling interval during the first seconds of a run, when a window would appear.
    [int]$StartupMilliseconds = 25,

    # Sampling interval afterwards.
    [int]$SteadyMilliseconds = 250,

    # How long the burst of fast sampling lasts.
    [int]$StartupSeconds = 60,

    # Where the sample log goes. Defaults under TestResults, which Git ignores.
    [string]$SamplePath,

    # Do not run anything; only check that the sampler and the guard work.
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$project = Split-Path -Parent (Split-Path -Parent $here)
$launcher = Join-Path $here 'Invoke-UnityBackground.ps1'
$guardFile = Join-Path $here 'LocalTanksWindowGuard.cs'

if ([string]::IsNullOrWhiteSpace($SamplePath)) {
    $SamplePath = Join-Path $project 'TestResults/UnityLauncher/focus-samples.log'
}

function Write-Probe {
    param([string]$Message, [string]$Colour)
    if ($Colour) {
        Write-Host "[focus] $Message" -ForegroundColor $Colour
    } else {
        Write-Host "[focus] $Message"
    }
}

Add-Type -Path $guardFile -ErrorAction Stop
$guard = [LocalTanks.Harness.LocalTanksWindowGuard]

if ($SelfTest) {
    Write-Probe "the foreground window is now: $($guard::DescribeForeground())"
    Write-Probe "sampling interval: $StartupMilliseconds ms for $StartupSeconds s, then $SteadyMilliseconds ms"
    exit 0
}

# A launcher called through -File receives "a,b" as one string, so a list given that way is split here
# rather than silently treated as one unknown operation name.
$operationList = @()
foreach ($item in $Operation) {
    $operationList += ($item -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

if ($operationList.Count -eq 0) {
    Write-Probe 'no operation was named' 'Red'
    exit 1
}

# The sampler runs in its own runspace because the launch blocks: a foreground window that appears while
# the launcher waits for Unity would otherwise never be seen. It only ever reads.
$sampler = @'
param($guardFile, $samplePath, $stopPath, $timeoutSeconds, $startupMilliseconds, $steadyMilliseconds, $startupSeconds)
Add-Type -Path $guardFile -ErrorAction SilentlyContinue
$guard = [LocalTanks.Harness.LocalTanksWindowGuard]
Set-Content -Path $samplePath -Value "startedUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
$seen = New-Object System.Collections.Generic.List[string]
$started = Get-Date
$count = 0
while (-not (Test-Path $stopPath) -and ((Get-Date) - $started).TotalSeconds -lt $timeoutSeconds) {
    $count++
    $line = "$($guard::DescribeForeground())"
    if ($seen.Count -eq 0 -or $seen[$seen.Count - 1] -ne $line) {
        $seen.Add($line)
        Add-Content -Path $samplePath -Value ("t={0:n2}s {1}" -f ((Get-Date) - $started).TotalSeconds, $line)
    }

    if (((Get-Date) - $started).TotalSeconds -lt $startupSeconds) {
        Start-Sleep -Milliseconds $startupMilliseconds
    } else {
        Start-Sleep -Milliseconds $steadyMilliseconds
    }
}

Add-Content -Path $samplePath -Value "samples=$count"
Add-Content -Path $samplePath -Value "stoppedUtc=$((Get-Date).ToUniversalTime().ToString('o'))"
'@

$failures = 0
$results = @()

foreach ($name in $operationList) {
    Write-Probe "measuring '$name'"
    $sampleFile = $SamplePath -replace '\.log$', "-$($name -replace '[^A-Za-z0-9]', '_').log"
    $stopFile = "$sampleFile.stop"
    $runReport = Join-Path $project "TestResults/UnityLauncher/focus-$($name -replace '[^A-Za-z0-9]', '_').json"
    Remove-Item $sampleFile, $stopFile, $runReport -ErrorAction SilentlyContinue

    $runspace = [powershell]::Create()
    $null = $runspace.AddScript($sampler).
        AddArgument($guardFile).
        AddArgument($sampleFile).
        AddArgument($stopFile).
        AddArgument($TimeoutSeconds).
        AddArgument($StartupMilliseconds).
        AddArgument($SteadyMilliseconds).
        AddArgument($StartupSeconds)
    $async = $runspace.BeginInvoke()

    # One sample before the launch: the sampler needs a moment to start, and its first line is the
    # foreground the user had before anything of ours existed.
    Start-Sleep -Milliseconds 400

    $before = $guard::DescribeForeground()
    Write-Probe "foreground before: $before"

    & $launcher -Operation $name -Run -ReportPath $runReport
    $launcherCode = $LASTEXITCODE

    Set-Content -Path $stopFile -Value 'stop'
    $null = $runspace.EndInvoke($async)
    $runspace.Dispose()

    $after = $guard::DescribeForeground()
    $lines = @()
    if (Test-Path $sampleFile) {
        $lines = @(Get-Content $sampleFile)
    }

    $samples = 0
    foreach ($line in $lines) {
        if ($line -match '^samples=(\d+)$') {
            $samples = [int]$Matches[1]
        }
    }

    # Only windows of the tree the launcher started count. Everything else is the user, who is free to
    # switch windows while a test runs.
    $intrusions = @($lines | Where-Object { $_ -match '\(Unity' })
    $isolation = $null
    $measured = $false
    if (Test-Path $runReport) {
        try {
            $report = Get-Content $runReport -Raw | ConvertFrom-Json
            $isolation = $report.isolation
            $measured = -not [string]::IsNullOrWhiteSpace([string]$report.unityPid)
        } catch {
            $isolation = $null
        }
    }

    # Three outcomes are possible and they must not be confused: the focus held, the focus was taken, or
    # nothing was measured because the launcher refused the request before starting Unity.
    $verdict = 'not measured'
    $passed = $false
    if (-not $measured) {
        Write-Probe "the launcher did not start Unity (exit $launcherCode), so this run measures nothing" 'Red'
    } elseif ($intrusions.Count -eq 0 -and -not ($isolation -and $isolation.unityReachedForeground)) {
        $verdict = 'held'
        $passed = $launcherCode -eq 0
        if (-not $passed) {
            Write-Probe "the focus held but the run failed (exit $launcherCode); whether it passed is not this script's question" 'Yellow'
        }
    } else {
        $verdict = 'taken'
    }

    if ($verdict -ne 'held') {
        $failures++
    }

    Write-Probe ("'{0}': focus {1}; {2} sample(s); launcher exit {3}" -f $name, $verdict, $samples, $launcherCode) $(if ($verdict -eq 'held') { 'Green' } else { 'Red' })
    if ($intrusions.Count -gt 0) {
        Write-Probe 'windows of the launched tree that held the foreground:' 'Red'
        $intrusions | ForEach-Object { Write-Host "    $_" }
    }

    if ($isolation) {
        Write-Probe ("launcher's own record: on the private desktop = {0}; it saw {1} sample(s) and {2} window(s) of its tree in the foreground" -f `
            $isolation.active, $isolation.focusSamples, $isolation.unityReachedForeground)
    }

    Write-Probe "sample log: $sampleFile"
    Write-Probe "run report: $runReport"
    Write-Probe "foreground after: $after"

    $results += [pscustomobject]@{
        Operation = $name
        Verdict = $verdict
        Samples = $samples
        LauncherExit = $launcherCode
        Intrusions = $intrusions.Count
        SampleLog = $sampleFile
    }
}

Write-Host ''
Write-Probe 'summary:'
$results | Format-Table -AutoSize | Out-String | Write-Host

if ($failures -gt 0) {
    Write-Probe "$failures operation(s) did not keep the user's foreground. The harness exists to prevent exactly that." 'Red'
    exit 1
}

Write-Probe 'no window of any measured run reached the user''s foreground.' 'Green'
exit 0
