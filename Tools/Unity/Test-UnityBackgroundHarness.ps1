<#
.SYNOPSIS
Checks the Unity launch harness itself: the arguments it builds, the guards it keeps, and the queue.

.DESCRIPTION
The launcher's promises are invisible to the compiler, so they are checked here instead of being claimed:

  * a headless run really passes -batchmode and -nographics, and a graphical one really leaves -nographics
    out - for every operation in the registry, not for one example;
  * no harness file starts Unity Hub, uses Start-Process, calls SetForegroundWindow, sends keys, or opens a
    folder or an image after a run;
  * two runs of the same project are serialised by the queue, and the second one says it waited;
  * a lock whose owner died is taken over instead of blocking the project forever.

The first two groups need nothing but PowerShell. The queue checks launch Unity for real, so they run only
with -WithUnity. Everything is re-runnable:

  ./Tools/Unity/Test-UnityBackgroundHarness.ps1
  ./Tools/Unity/Test-UnityBackgroundHarness.ps1 -WithUnity
#>
param(
    # Also run the checks that need a real Unity launch: serialisation and lock recovery.
    [switch]$WithUnity,

    # Operation used for the Unity-backed checks. Must be quick, and it must not write anything: these runs
    # are about the queue, not about their own result.
    [string]$ProbeOperation = 'ValidateCombatDefinitions',

    # How long to wait for one Unity-backed run.
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$project = Split-Path -Parent (Split-Path -Parent $here)
$launcher = Join-Path $here 'Invoke-UnityBackground.ps1'
$reports = Join-Path $project 'TestResults/UnityLauncher'
$scratch = Join-Path $reports 'harness'

if (-not (Test-Path $scratch)) {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
}

$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Assert-That {
    param([bool]$Condition, [string]$Description, [string]$Detail)

    $script:checks++
    if ($Condition) {
        Write-Host "  ok   $Description" -ForegroundColor Green
        return
    }

    Write-Host "  FAIL $Description" -ForegroundColor Red
    if ($Detail) {
        Write-Host "       $Detail" -ForegroundColor Red
    }

    $script:failures.Add($Description)
}

function Get-DryRunReport {
    param([string]$OperationName, [string]$Path)

    Remove-Item $Path -ErrorAction SilentlyContinue
    # 6>$null hides the launcher's own preview text; the report is what this script reads.
    & $launcher -Operation $OperationName -ReportPath $Path 6>$null | Out-Null
    if (-not (Test-Path $Path)) {
        throw "the launcher wrote no report for '$OperationName'"
    }

    return Get-Content $Path -Raw | ConvertFrom-Json
}

function Start-LauncherRun {
    param([string]$OperationName, [string]$ReportPath, [string]$LogPath)

    Remove-Item $ReportPath -ErrorAction SilentlyContinue
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = Join-Path $PSHOME 'powershell.exe'
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Operation {1} -ReportPath "{2}" -LogPath "{3}" -Run -Quiet' -f `
        $launcher, $OperationName, $ReportPath, $LogPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.WorkingDirectory = $project
    return [System.Diagnostics.Process]::Start($startInfo)
}

function Wait-ForReport {
    param([string]$Path, [int]$Seconds)

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $Path) {
            try {
                # A partly written file is not a report yet.
                return Get-Content $Path -Raw | ConvertFrom-Json
            } catch {
                Start-Sleep -Milliseconds 200
                continue
            }
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

# ---------------------------------------------------------------- 1. the registry decides the arguments

Write-Host ''
Write-Host 'arguments for every registered operation'
$registry = Get-Content (Join-Path $here 'unity-operations.json') -Raw | ConvertFrom-Json

foreach ($entry in $registry.operations) {
    $path = Join-Path $scratch "dry-$($entry.name).json"
    $report = Get-DryRunReport -OperationName $entry.name -Path $path
    $arguments = @($report.arguments)

    $hasBatchmode = $arguments -contains '-batchmode'
    $hasNoGraphics = $arguments -contains '-nographics'
    $wantsGraphics = [bool]$entry.requiresGraphics

    Assert-That $hasBatchmode "$($entry.name): runs in -batchmode" "arguments were: $($arguments -join ' ')"
    Assert-That ($report.graphicsDevice -eq $wantsGraphics) `
        "$($entry.name): mode matches the registry (graphicsDevice=$wantsGraphics)" `
        "the report says graphicsDevice=$($report.graphicsDevice)"

    if ($wantsGraphics) {
        Assert-That (-not $hasNoGraphics) "$($entry.name): a graphical run does not pass -nographics" `
            "arguments were: $($arguments -join ' ')"
    } else {
        Assert-That $hasNoGraphics "$($entry.name): a headless run passes -nographics" `
            "arguments were: $($arguments -join ' ')"
    }
}

# ---------------------------------------------------------------- 2. the guards are still in the files

Write-Host ''
Write-Host 'guards that must not disappear from the harness'
$harnessFiles = @(
    (Join-Path $here 'Invoke-UnityBackground.ps1'),
    (Join-Path $here 'Test-UnityForegroundFocus.ps1'),
    (Join-Path $here 'LocalTanksWindowGuard.cs'),
    (Join-Path $here 'LocalTanksIsolatedDesktop.cs'),
    (Join-Path (Split-Path -Parent $here) 'Invoke-LocalTanksTests.ps1')
)

# A call site is written with its opening parenthesis, so a comment that names the function - which several
# of these files do, to say they do not use it - is not mistaken for a call.
$forbidden = @(
    @{ Pattern = 'SetForegroundWindow\s*\('; Why = 'never move the user''s focus' },
    @{ Pattern = 'SwitchToThisWindow\s*\('; Why = 'never activate a window' },
    @{ Pattern = 'SendKeys|keybd_event|mouse_event\s*\('; Why = 'never synthesise input' },
    @{ Pattern = 'Start-Process'; Why = 'the launcher describes its own process instead' },
    @{ Pattern = 'Invoke-Item|explorer\.exe'; Why = 'never open a folder or a report' },
    @{ Pattern = 'Unity Hub\.exe'; Why = 'Hub must never be started' }
)

foreach ($file in $harnessFiles) {
    $text = Get-Content $file -Raw
    foreach ($rule in $forbidden) {
        $match = [regex]::Match($text, $rule.Pattern)
        Assert-That (-not $match.Success) "$(Split-Path -Leaf $file): $($rule.Why)" `
            "found '$($match.Value)' in $file"
    }
}

# The wrapper must not be able to start Unity on its own: the launcher is the single entry point.
$wrapper = Get-Content (Join-Path (Split-Path -Parent $here) 'Invoke-LocalTanksTests.ps1') -Raw
Assert-That (-not ($wrapper -match 'Unity\.exe')) 'the test wrapper has no Unity path of its own' `
    'the wrapper must ask the launcher, so the editor path and the arguments stay in one file'
Assert-That ($wrapper -match 'Invoke-UnityBackground\.ps1') 'the test wrapper uses the launcher'

# One place knows where the editor lives. This file is left out of the scan because it has to name the
# pattern it looks for.
$unityPathOwners = @()
foreach ($file in (Get-ChildItem $here -Filter '*.ps1') + (Get-ChildItem (Split-Path -Parent $here) -Filter '*.ps1')) {
    if ($file.Name -eq (Split-Path -Leaf $PSCommandPath)) {
        continue
    }

    if ((Get-Content $file.FullName -Raw) -match 'Hub[/\\]Editor') {
        $unityPathOwners += $file.Name
    }
}

Assert-That ($unityPathOwners.Count -eq 1 -and $unityPathOwners[0] -eq 'Invoke-UnityBackground.ps1') `
    'the editor path literal exists in the launcher only' "also named in: $($unityPathOwners -join ', ')"

# ---------------------------------------------------------------- 3. the queue

if (-not $WithUnity) {
    Write-Host ''
    Write-Host 'queue checks skipped; add -WithUnity to run them' -ForegroundColor Yellow
} else {
    Write-Host ''
    Write-Host 'queue: two runs of the same project are serialised'
    $firstReport = Join-Path $scratch 'queue-first.json'
    $secondReport = Join-Path $scratch 'queue-second.json'
    $first = Start-LauncherRun -OperationName $ProbeOperation -ReportPath $firstReport `
        -LogPath (Join-Path $project "TestResults/$ProbeOperation-queue-first.log")
    Start-Sleep -Seconds 3
    $second = Start-LauncherRun -OperationName $ProbeOperation -ReportPath $secondReport `
        -LogPath (Join-Path $project "TestResults/$ProbeOperation-queue-second.log")

    $secondDone = Wait-ForReport -Path $secondReport -Seconds $TimeoutSeconds
    $firstDone = Wait-ForReport -Path $firstReport -Seconds $TimeoutSeconds
    foreach ($process in @($first, $second)) {
        if (-not $process.HasExited) {
            $process.WaitForExit(60000) | Out-Null
        }
    }

    Assert-That ($null -ne $firstDone) 'the first queued run wrote a report'
    Assert-That ($null -ne $secondDone) 'the second queued run wrote a report'
    if ($secondDone) {
        Assert-That ($secondDone.lock.waited -eq $true) 'the second run reports that it waited for the queue' `
            'two Unity processes for one project must never overlap'
        Assert-That ([double]$secondDone.lock.waitSeconds -ge 1) 'the second run measured its wait' `
            "waitSeconds=$($secondDone.lock.waitSeconds) means it did not actually queue behind the first"
    }

    Write-Host ''
    Write-Host 'queue: a lock whose owner died is taken over'
    $lockName = $null
    if ($firstDone) {
        $lockName = $firstDone.lock.name
    }

    Assert-That (-not [string]::IsNullOrWhiteSpace($lockName)) 'the run report names the lock' `
        'the recovery check needs the real name rather than a second copy of the naming rule'
    if ($lockName) {
        # A named mutex stops existing when its last handle closes, and an object that stops existing cannot
        # be abandoned. A real waiter always holds a handle while the owner dies, so this check holds one
        # too: without it the simulation would prove only that a fresh mutex is not abandoned.
        $hold = New-Object System.Threading.Mutex($false, $lockName)
        try {
            # Owning a mutex and exiting without releasing it is exactly what a killed launcher leaves behind.
            $abandon = "`$m = New-Object System.Threading.Mutex(`$false, '$lockName'); " +
                       "`$null = `$m.WaitOne(5000); exit 0"
            $abandonInfo = New-Object System.Diagnostics.ProcessStartInfo
            $abandonInfo.FileName = Join-Path $PSHOME 'powershell.exe'
            $abandonInfo.Arguments = '-NoProfile -Command "{0}"' -f $abandon
            $abandonInfo.UseShellExecute = $false
            $abandonInfo.CreateNoWindow = $true
            $abandonInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
            $abandonProcess = [System.Diagnostics.Process]::Start($abandonInfo)
            $abandonProcess.WaitForExit(30000) | Out-Null

            $recoveredReport = Join-Path $scratch 'queue-recovered.json'
            $recovered = Start-LauncherRun -OperationName $ProbeOperation -ReportPath $recoveredReport `
                -LogPath (Join-Path $project "TestResults/$ProbeOperation-queue-recovered.log")
            $recoveredDone = Wait-ForReport -Path $recoveredReport -Seconds $TimeoutSeconds
            if (-not $recovered.HasExited) {
                $recovered.WaitForExit(60000) | Out-Null
            }

            Assert-That ($null -ne $recoveredDone) 'a run after a killed owner finished' `
                'a dead owner must not block the project forever'
            if ($recoveredDone) {
                Assert-That ($recoveredDone.lock.abandoned -eq $true) 'the takeover is recorded as abandoned' `
                    'the caller has to be able to tell a normal wait from a recovery'
                Assert-That ($recoveredDone.outcome -eq 'Succeeded') 'the run after a recovery still succeeded' `
                    "outcome was $($recoveredDone.outcome)"
            }
        } finally {
            $hold.Dispose()
        }
    }
}

# ---------------------------------------------------------------- verdict

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) of $checks check(s) failed:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }

    exit 1
}

Write-Host "all $checks check(s) passed" -ForegroundColor Green
exit 0
