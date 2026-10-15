<#
.SYNOPSIS
Runs one Unity operation for this project without ever making Unity the active window.

.DESCRIPTION
Every Unity launch in LocalTanks goes through this script: the test suites, the data importers, the asset
builders and the diagnostic renderers. It owns the things that must not be duplicated - which editor to use,
which arguments a run needs, the queue that keeps two agents from opening the same project at once, the
private desktop that keeps Unity away from the user's focus, the log and the exit code.

Two modes exist, and -Auto chooses between them from Tools/Unity/unity-operations.json:

  Headless            -batchmode -nographics. Compilation, Edit Mode and Play Mode tests, AI and battle
                      simulations, telemetry, data and prefab checks, and asset builders that do not need a
                      GPU. The default for everything that can be proved from data, logs or telemetry.

  Graphical           -batchmode without -nographics. Only for evidence that cannot exist without a real
                      graphics device: a rendered frame, a particle effect, a material, transparency, a
                      camera or the UI. The operation writes its picture to a file and the caller reads the
                      file; nobody waits to see a window.

No launch asks the user for permission, starts Unity Hub, opens an Explorer window, activates the editor or
sends input. A window that Unity does create is kept out of reach rather than hidden after the fact: the
process is created on a private Windows desktop (see LocalTanksIsolatedDesktop.cs for the measurement that
made this necessary), so activation is confined to a desktop the user never sees.

.EXAMPLE
# What would an Edit Mode run look like, without running it?
./Tools/Unity/Invoke-UnityBackground.ps1 -Operation EditModeTests

.EXAMPLE
# Run one diagnostic renderer; the registry knows it needs a graphics device, so no mode is given.
./Tools/Unity/Invoke-UnityBackground.ps1 -Operation RenderCombatVfxProof -Run

.EXAMPLE
# Force a mode, for diagnosing the launcher itself.
./Tools/Unity/Invoke-UnityBackground.ps1 -Operation InspectCombatVfxSprites -Mode Graphical -Run

.NOTES
Exit codes: 0 succeeded, 1 the run failed, 2 the request is wrong, 3 the project is open elsewhere,
4 waiting for the queue timed out, 5 the launcher itself failed.
#>
[CmdletBinding()]
param(
    # Operation name from Tools/Unity/unity-operations.json. It decides the arguments and the mode.
    [string]$Operation,

    # Auto picks the mode from the registry. Headless and Graphical exist for diagnosing the launcher.
    [ValidateSet('Auto', 'Headless', 'Graphical')]
    [string]$Mode = 'Auto',

    # Editor entry point for something the registry does not describe yet.
    [string]$ExecuteMethod,

    # Test platform; equivalent to -Operation EditModeTests or -Operation PlayModeTests.
    [ValidateSet('EditMode', 'PlayMode')]
    [string]$Platform,

    # Test name substring. Empty runs the whole platform.
    [string]$Filter,

    # NUnit XML and Unity log. Defaults under TestResults, which Git ignores.
    [string]$ResultsPath,
    [string]$LogPath,

    # Record a Play Mode battle to JSON.
    [switch]$Telemetry,
    [string]$TelemetryOut,

    # Simulation multiplier; never changes the physics step.
    [ValidateRange(1, 200)]
    [float]$Speed = 1,

    # Stop a batch battle after this many simulated seconds. 0 leaves the scene in charge.
    [float]$Seconds = 0,

    # Field overrides, as Component.field=value. Repeatable.
    [string[]]$Override = @(),

    # Random seed for a run.
    [int]$Seed = 0,

    # Extra arguments, passed to Unity unchanged.
    [string[]]$UnityArgument = @(),

    # Actually run. Without it the launcher prints the command it would use.
    [switch]$Run,

    # Print the registry and exit.
    [switch]$ListOperations,

    # How long to wait for another agent's run to finish before giving up.
    [int]$LockTimeoutSeconds = 1800,

    # Kill this run after this many seconds. 0 uses the operation's own limit, and a negative value waits
    # forever. Only a process this launcher started is ever terminated.
    [int]$TimeoutSeconds = 0,

    # Fall back to a hidden window on the interactive desktop. For diagnosing the launcher itself.
    [switch]$NoDesktopIsolation,

    # Do not sample the foreground window while the run starts.
    [switch]$NoFocusWatch,

    # Suppress the periodic progress line.
    [switch]$Quiet,

    # Where the machine-readable result goes. A second copy is kept beside it, named after the run.
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'

# Exit codes, also documented in Docs/TEST_PLAN.md. They are stable because agents branch on them.
$ExitSucceeded = 0
$ExitRunFailed = 1
$ExitConfiguration = 2
$ExitProjectBusy = 3
$ExitLockTimeout = 4
$ExitLauncherError = 5

# This script lives in Tools/Unity/, so the project is two levels up. The check is here because every path
# below is built from it, and a wrong root would put logs and results in Tools/ without saying so.
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))) {
    Write-Host "[launcher] $PSScriptRoot does not look like Tools/Unity inside a Unity project" -ForegroundColor Red
    exit 2
}

$registryFile = Join-Path $PSScriptRoot 'unity-operations.json'
$guardFile = Join-Path $PSScriptRoot 'LocalTanksWindowGuard.cs'
$desktopFile = Join-Path $PSScriptRoot 'LocalTanksIsolatedDesktop.cs'
$reportsDirectory = Join-Path $project 'TestResults/UnityLauncher'

# ---------------------------------------------------------------- output helpers

function Write-Launcher {
    param([string]$Message, [string]$Colour)
    if ($Colour) {
        Write-Host "[launcher] $Message" -ForegroundColor $Colour
    } else {
        Write-Host "[launcher] $Message"
    }
}

function Write-Problem {
    param([string]$Message)
    Write-Host "[launcher] $Message" -ForegroundColor Red
}

function ConvertTo-AbsolutePath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $project $Path))
}

function Write-JsonFile {
    param([string]$Path, $Value, [int]$Depth = 8)

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth $Depth
    # Written without a byte order mark: the file is read by scripts and by Unity, and a BOM only ever
    # confuses the second reader.
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------- registry

function Get-OperationRegistry {
    if (-not (Test-Path $registryFile)) {
        throw "The operation registry is missing: $registryFile"
    }

    $registry = Get-Content $registryFile -Raw | ConvertFrom-Json
    $operations = @()
    foreach ($entry in @($registry.operations)) {
        $operations += $entry
    }

    return $operations
}

function Find-Operation {
    param($Operations, [string]$Name)
    foreach ($entry in $Operations) {
        if ($entry.name -eq $Name) {
            return $entry
        }
    }

    return $null
}

function Show-Operations {
    param($Operations)
    Write-Host 'Graphical operations need a real graphics device; everything else runs with -nographics.'
    Write-Host ''
    foreach ($entry in $Operations | Sort-Object requiresGraphics, name) {
        $device = 'headless '
        if ($entry.requiresGraphics) {
            $device = 'GRAPHICS '
        }

        Write-Host ("  {0} {1,-34} {2}" -f $device, $entry.name, $entry.description)
    }

    Write-Host ''
    Write-Host ("  {0} operation(s) in {1}" -f $Operations.Count, $registryFile)
}

# ---------------------------------------------------------------- editor discovery

function Resolve-UnityEditor {
    # One place in the repository knows where Unity lives. The version comes from the project's own
    # ProjectVersion.txt, so upgrading the editor does not need an edit here; the Hub root is the only
    # literal, and LOCALTANKS_UNITY overrides both for a machine that keeps the editor elsewhere.
    $override = $env:LOCALTANKS_UNITY
    if (-not [string]::IsNullOrWhiteSpace($override)) {
        if (Test-Path $override) {
            return [pscustomobject]@{ Path = $override; Version = 'from LOCALTANKS_UNITY'; Source = 'environment' }
        }

        throw "LOCALTANKS_UNITY points at '$override', which does not exist."
    }

    $versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
    $version = $null
    if (Test-Path $versionFile) {
        $match = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
        if ($match) {
            $version = $match.Matches[0].Groups[1].Value
        }
    }

    $hubRoot = Join-Path $env:ProgramFiles 'Unity/Hub/Editor'
    if ($version) {
        $exact = Join-Path $hubRoot "$version/Editor/Unity.exe"
        if (Test-Path $exact) {
            return [pscustomobject]@{ Path = $exact; Version = $version; Source = 'ProjectVersion.txt' }
        }
    }

    # A different editor is better than no editor, but the caller has to know it is not the project's.
    if (Test-Path $hubRoot) {
        $installed = Get-ChildItem -Path $hubRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'Editor/Unity.exe') } |
            Sort-Object Name -Descending
        if ($installed) {
            $newest = $installed[0]
            return [pscustomobject]@{
                Path = Join-Path $newest.FullName 'Editor/Unity.exe'
                Version = "$($newest.Name) (project asks for $version)"
                Source = 'newest installed editor'
            }
        }
    }

    throw "No Unity editor was found. Install $version, or set LOCALTANKS_UNITY to Unity.exe."
}

# ---------------------------------------------------------------- conflict and queue

function Get-UnityProcessesForProject {
    # An interactive editor does not use this launcher and does not take its lock, so it has to be found:
    # Unity refuses to open a project twice, and a second instance that fails halfway is worse than a clear
    # answer. Nothing found here is ever terminated.
    $found = @()
    try {
        $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
    } catch {
        # Without a command line, any editor at all is a possible conflict; the caller is told the
        # difference so it can decide instead of guessing.
        $loose = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
        foreach ($process in $loose) {
            $found += [pscustomobject]@{
                ProcessId = $process.Id
                CommandLine = '(command line unavailable)'
                Certain = $false
            }
        }

        return $found
    }

    $normalised = $project.Replace('\', '/').ToLowerInvariant()
    foreach ($process in $processes) {
        $line = '' + $process.CommandLine
        $comparable = $line.Replace('\', '/').ToLowerInvariant()
        if ($comparable.Contains($normalised)) {
            $found += [pscustomobject]@{ ProcessId = $process.ProcessId; CommandLine = $line; Certain = $true }
        }
    }

    return $found
}

function Get-LockIdentity {
    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($project.ToLowerInvariant())
        $hash = $sha.ComputeHash($bytes)
        $text = ([System.BitConverter]::ToString($hash) -replace '-', '').Substring(0, 16)
    } finally {
        $sha.Dispose()
    }

    return "Local\LocalTanks.Unity.$text"
}

function Get-OwnerRecordPath {
    param([string]$LockName)
    $safe = ($LockName -replace '[^A-Za-z0-9]', '_')
    return Join-Path $reportsDirectory "$safe.owner.json"
}

function Read-OwnerRecord {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content $Path -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Format-Owner {
    param($Owner)
    if (-not $Owner) {
        return 'an unknown run'
    }

    $alive = $false
    try {
        $alive = $null -ne (Get-Process -Id $Owner.launcherPid -ErrorAction Stop)
    } catch {
        $alive = $false
    }

    $state = 'finished or gone'
    if ($alive) {
        $state = 'still running'
    }

    return "pid $($Owner.launcherPid) running '$($Owner.operation)' since $($Owner.startedUtc) ($state)"
}

# ---------------------------------------------------------------- run bookkeeping

function New-RunReport {
    param([string]$OperationName, [string]$Kind, [string]$ResolvedMode, [bool]$Graphics)

    return [ordered]@{
        schemaVersion = 1
        outcome = 'NotStarted'
        exitCode = $ExitLauncherError
        unityExitCode = $null
        operation = $OperationName
        kind = $Kind
        mode = $ResolvedMode
        graphicsDevice = $Graphics
        description = $null
        executeMethod = $null
        testPlatform = $null
        projectPath = $project
        unity = $null
        arguments = @()
        commandLine = $null
        launcherPid = $PID
        unityPid = $null
        startedUtc = (Get-Date).ToUniversalTime().ToString('o')
        finishedUtc = $null
        wallSeconds = $null
        lock = @{ name = $null; waited = $false; waitSeconds = 0; owner = $null; abandoned = $false }
        isolation = @{
            requested = $true
            active = $false
            desktop = $null
            focusSamples = 0
            foregroundBefore = $null
            foregroundAfter = $null
            unityReachedForeground = $false
            windowsHidden = 0
            note = $null
        }
        paths = @{ log = $null; results = $null; telemetry = $null; report = $null }
        outputs = @()
        nunit = $null
        warnings = @()
        error = $null
    }
}

function Complete-RunReport {
    param($Report, [string]$Outcome, [int]$ExitCode, $StartedAt)

    $Report.outcome = $Outcome
    $Report.exitCode = $ExitCode
    $Report.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
    if ($StartedAt) {
        $Report.wallSeconds = [math]::Round(((Get-Date) - $StartedAt).TotalSeconds, 2)
    }

    $target = $ReportPath
    if ([string]::IsNullOrWhiteSpace($target)) {
        $target = Join-Path $reportsDirectory 'last-run.json'
    }

    $target = ConvertTo-AbsolutePath $target
    $Report.paths.report = $target
    Write-JsonFile -Path $target -Value $Report

    # A copy per run, so a queued run does not overwrite the record an agent is still reading.
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    $name = 'run'
    if ($Report.operation) {
        $name = $Report.operation
    }

    $copy = Join-Path $reportsDirectory "$name-$stamp.json"
    if ($copy -ne $target) {
        Write-JsonFile -Path $copy -Value $Report
    }

    return $target
}

function Get-NunitSummary {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $null
    }

    try {
        [xml]$results = Get-Content $Path
    } catch {
        return [ordered]@{ error = "the result file could not be read: $($_.Exception.Message)" }
    }

    $summary = $results.'test-run'
    if (-not $summary) {
        return $null
    }

    $failures = @()
    foreach ($case in $results.SelectNodes('//test-case')) {
        if ($case.result -ne 'Passed') {
            $failures += [ordered]@{ name = $case.Name; result = $case.result }
        }
    }

    return [ordered]@{
        total = [int]$summary.total
        passed = [int]$summary.passed
        failed = [int]$summary.failed
        skipped = [int]$summary.skipped
        durationSeconds = [double]$summary.duration
        failures = $failures
    }
}

function Get-RunExitCode {
    param($Process, $Launch)

    # The private desktop hands back a process object it did not start itself, and Windows PowerShell 5.1
    # answers an empty value for ExitCode on those. The handle kept beside it is the reliable source.
    if ($Launch -and $Launch.Started) {
        return $Launch.ExitCode()
    }

    return $Process.ExitCode
}

function Get-LastLogLine {    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $null
    }

    try {
        $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
        try {
            $length = $stream.Length
            $take = [math]::Min(8192, $length)
            $stream.Seek($length - $take, 'Begin') | Out-Null
            $buffer = New-Object byte[] $take
            $read = $stream.Read($buffer, 0, $take)
            $text = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        } finally {
            $stream.Dispose()
        }

        $lines = $text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ($lines.Count -eq 0) {
            return $null
        }

        return $lines[$lines.Count - 1]
    } catch {
        return $null
    }
}

function Show-LogTail {
    param([string]$Path, [int]$Lines = 25)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return
    }

    Write-Host "[launcher] last $Lines line(s) of $Path"
    foreach ($line in (Get-Content $Path -Tail $Lines)) {
        Write-Host "  $line"
    }
}

# ---------------------------------------------------------------- the run

function Invoke-UnityOperation {
    param(
        [string]$OperationName,
        $Entry,
        [string]$ResolvedMode,
        [bool]$Graphics,
        [string]$ResolvedExecuteMethod,
        [string]$ResolvedPlatform
    )

    $startedAt = Get-Date
    $report = New-RunReport -OperationName $OperationName -Kind $Entry.kind -ResolvedMode $ResolvedMode -Graphics $Graphics
    $report.description = $Entry.description
    # @($null) is a one-element array, which would print an empty output list for a test operation.
    $report.outputs = @($Entry.outputs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $timeout = $TimeoutSeconds
    if ($timeout -eq 0) {
        $timeout = 1800
        if ($Entry.timeoutSeconds) {
            $timeout = [int]$Entry.timeoutSeconds
        }
    }

    # ------------------------------------------------------------ editor
    try {
        $editor = Resolve-UnityEditor
    } catch {
        Write-Problem $_.Exception.Message
        $report.error = $_.Exception.Message
        $target = Complete-RunReport -Report $report -Outcome 'ConfigurationError' -ExitCode $ExitConfiguration -StartedAt $startedAt
        Write-Host "[launcher] report: $target"
        return $ExitConfiguration
    }

    if ($editor.Source -ne 'ProjectVersion.txt' -and $editor.Source -ne 'environment') {
        $warning = "using $($editor.Path), which is not the version the project asks for"
        Write-Launcher $warning 'Yellow'
        $report.warnings += $warning
    }

    $report.unity = @{ path = $editor.Path; version = $editor.Version; source = $editor.Source }

    # ------------------------------------------------------------ arguments
    $absoluteLog = ConvertTo-AbsolutePath $LogPath
    $absoluteResults = ConvertTo-AbsolutePath $ResultsPath
    if ($Entry.kind -eq 'Tests' -and -not $absoluteResults) {
        $absoluteResults = ConvertTo-AbsolutePath "TestResults/$OperationName-results.xml"
    }

    if (-not $absoluteLog) {
        $absoluteLog = ConvertTo-AbsolutePath "TestResults/$OperationName.log"
    }

    $report.paths.log = $absoluteLog
    $report.paths.results = $absoluteResults

    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('-batchmode')
    if (-not $Graphics) {
        $arguments.Add('-nographics')
    }

    # A crash dialog would block the queue and, worse, be a window the user did not ask for.
    $arguments.Add('-silent-crashes')
    $arguments.Add('-projectPath')
    $arguments.Add("`"$project`"")
    $arguments.Add('-logFile')
    $arguments.Add("`"$absoluteLog`"")

    if ($Entry.kind -eq 'Tests') {
        $arguments.Add('-runTests')
        $arguments.Add('-testPlatform')
        $arguments.Add($ResolvedPlatform)
        $arguments.Add('-testResults')
        $arguments.Add("`"$absoluteResults`"")
        if (-not [string]::IsNullOrWhiteSpace($Filter)) {
            $arguments.Add('-testFilter')
            $arguments.Add("`"$Filter`"")
        }
    } else {
        $arguments.Add('-executeMethod')
        $arguments.Add($ResolvedExecuteMethod)
        # Most editor methods finish synchronously and let Unity quit immediately. Long-running methods
        # explicitly marked asynchronous own their EditorApplication lifecycle and call Exit themselves.
        if (-not $Entry.asynchronous) {
            $arguments.Add('-quit')
        }
    }

    if ($Telemetry) {
        $arguments.Add('-localtanksTelemetry')

        # The recorder has its own default name, but a path nobody can predict cannot be reported, and the
        # recording is the whole point of the switch. Naming it here keeps the documented shape.
        if ([string]::IsNullOrWhiteSpace($TelemetryOut)) {
            $TelemetryOut = "TestResults/telemetry-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
        }

        $absoluteTelemetry = ConvertTo-AbsolutePath $TelemetryOut
        $arguments.Add('-localtanksTelemetryOut')
        $arguments.Add("`"$absoluteTelemetry`"")
        $report.paths.telemetry = $absoluteTelemetry
    }

    if ($Speed -gt 1) {
        $arguments.Add('-localtanksSpeed')
        $arguments.Add($Speed)
    }

    if ($Seconds -gt 0) {
        $arguments.Add('-localtanksSeconds')
        $arguments.Add($Seconds)
    }

    foreach ($item in $Override) {
        $arguments.Add('-localtanksParam')
        $arguments.Add("`"$item`"")
    }

    if ($Seed -ne 0) {
        $arguments.Add('-localtanksSeed')
        $arguments.Add($Seed)
    }

    foreach ($extra in @($Entry.unityArguments)) {
        if (-not [string]::IsNullOrWhiteSpace($extra)) {
            $arguments.Add($extra)
        }
    }

    foreach ($extra in $UnityArgument) {
        $arguments.Add($extra)
    }

    $report.arguments = @($arguments)
    $report.commandLine = "& `"$($editor.Path)`" $($arguments -join ' ')"
    $report.executeMethod = $ResolvedExecuteMethod
    $report.testPlatform = $ResolvedPlatform

    if (-not $Run) {
        Write-Host ''
        Write-Host "Operation : $OperationName ($($Entry.kind))"
        Write-Host "Mode      : $ResolvedMode$(if ($Graphics) { ' (real graphics device)' } else { ' (no graphics device)' })"
        if ($Entry.description) { Write-Host "What      : $($Entry.description)" }
        Write-Host "Log       : $absoluteLog"
        if ($absoluteResults) { Write-Host "Results   : $absoluteResults" }
        Write-Host ''
        Write-Host 'Nothing was run. Add -Run to execute:'
        Write-Host $report.commandLine
        $target = Complete-RunReport -Report $report -Outcome 'Preview' -ExitCode $ExitSucceeded -StartedAt $startedAt
        Write-Host "[launcher] report: $target"
        return $ExitSucceeded
    }

    # Unity does not create the directory of -logFile or -testResults, and an entry point that runs before
    # the first write would otherwise fail with a message about the file rather than about the folder.
    $directories = @()
    foreach ($file in @($absoluteLog, $absoluteResults)) {
        if (-not [string]::IsNullOrWhiteSpace($file)) {
            $directories += Split-Path -Parent $file
        }
    }

    foreach ($directory in $directories) {
        if ($directory -and -not (Test-Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
    }

    # A result file left by an earlier run is worse than no result file: a run that fails to compile writes
    # none, and the tally of the previous, greener run would be reported as this one's. The file is
    # generated, so removing it costs nothing.
    if (-not [string]::IsNullOrWhiteSpace($absoluteResults) -and (Test-Path $absoluteResults)) {
        Remove-Item $absoluteResults -Force
        Write-Launcher "removed the previous result file at $absoluteResults so this run cannot report it"
    }

    Write-Launcher "operation '$OperationName' in $ResolvedMode mode: $($report.commandLine)"

    # ------------------------------------------------------------ queue
    $lockName = Get-LockIdentity
    $ownerPath = Get-OwnerRecordPath -LockName $lockName
    $report.lock.name = $lockName
    $mutex = New-Object System.Threading.Mutex($false, $lockName)
    $waited = $false
    $waitStarted = Get-Date
    try {
        while ($true) {
            $acquired = $false
            try {
                $acquired = $mutex.WaitOne(0)
            } catch [System.Threading.AbandonedMutexException] {
                # The previous owner died without releasing: the lock is ours now, and the caller is told,
                # because a run that ends this way leaves its own record behind.
                $acquired = $true
                $report.lock.abandoned = $true
                Write-Launcher 'the previous run died without releasing the queue; taking it over' 'Yellow'
            }

            if ($acquired) {
                break
            }

            $owner = Read-OwnerRecord -Path $ownerPath
            $elapsed = ((Get-Date) - $waitStarted).TotalSeconds
            if ($elapsed -ge $LockTimeoutSeconds) {
                $report.error = "another run held the queue for more than $LockTimeoutSeconds s"
                $report.lock.owner = $owner
                $report.lock.waitSeconds = [math]::Round($elapsed, 1)
                Write-Problem "the queue was still held after $LockTimeoutSeconds s by $(Format-Owner $owner)"
                $mutex.Dispose()
                $target = Complete-RunReport -Report $report -Outcome 'LockTimeout' -ExitCode $ExitLockTimeout -StartedAt $startedAt
                Write-Host "[launcher] report: $target"
                return $ExitLockTimeout
            }

            if (-not $waited) {
                $waited = $true
                $report.lock.waited = $true
                $report.lock.owner = $owner
                Write-Launcher "waiting for the queue, held by $(Format-Owner $owner)"
            } elseif ([int]$elapsed % 15 -lt 1) {
                Write-Launcher ("still waiting ({0:n0}s) for {1}" -f $elapsed, (Format-Owner (Read-OwnerRecord -Path $ownerPath)))
            }

            Start-Sleep -Seconds 1
        }

        $report.lock.waitSeconds = [math]::Round(((Get-Date) - $waitStarted).TotalSeconds, 1)

        # -------------------------------------------------------- a foreign editor
        $foreign = @(Get-UnityProcessesForProject)
        $lockFile = Join-Path $project 'Temp/UnityLockfile'
        if ($foreign.Count -gt 0) {
            $ids = ($foreign | ForEach-Object { $_.ProcessId }) -join ', '
            $message = "the project is already open in Unity (pid $ids). Close that editor, or let it finish; " +
                       'this launcher never terminates somebody else''s Unity.'
            Write-Problem $message
            $report.error = $message
            $report.warnings += 'static checks that do not need Unity remain available'
            $mutex.ReleaseMutex()
            $mutex.Dispose()
            $target = Complete-RunReport -Report $report -Outcome 'ProjectBusy' -ExitCode $ExitProjectBusy -StartedAt $startedAt
            Write-Host "[launcher] report: $target"
            return $ExitProjectBusy
        }

        if (Test-Path $lockFile) {
            $warning = "Temp/UnityLockfile exists but no Unity process uses this project; treating it as stale"
            Write-Launcher $warning 'Yellow'
            $report.warnings += $warning
        }

        Write-JsonFile -Path $ownerPath -Value ([ordered]@{
            launcherPid = $PID
            operation = $OperationName
            mode = $ResolvedMode
            startedUtc = (Get-Date).ToUniversalTime().ToString('o')
            host = $env:COMPUTERNAME
        })

        try {
            return Invoke-UnityProcess -Report $report -Editor $editor.Path -Arguments $arguments `
                -ResolvedMode $ResolvedMode -Graphics $Graphics -Timeout $timeout -LogPath $absoluteLog `
                -ResultsPath $absoluteResults -StartedAt $startedAt
        } catch {
            # An exception here is the launcher's own failure - a missing editor, a refused desktop, a
            # process that cannot be started. It still writes a report, because a caller that gets no report
            # has no way to tell this from a run that never happened.
            $report.error = "$($_.Exception.GetType().Name): $($_.Exception.Message)"
            Write-Problem "the run could not be completed: $($report.error)"
            $target = Complete-RunReport -Report $report -Outcome 'LauncherError' -ExitCode $ExitLauncherError -StartedAt $startedAt
            Write-Host "[launcher] report: $target"
            return $ExitLauncherError
        } finally {
            Remove-Item $ownerPath -ErrorAction SilentlyContinue
        }
    } finally {
        if ($mutex) {
            try {
                $mutex.ReleaseMutex()
            } catch {
                # Not the owner any more, which the abandoned path already reported.
            }

            $mutex.Dispose()
        }
    }
}

function Invoke-UnityProcess {
    param(
        $Report,
        [string]$Editor,
        $Arguments,
        [string]$ResolvedMode,
        [bool]$Graphics,
        [int]$Timeout,
        [string]$LogPath,
        [string]$ResultsPath,
        $StartedAt
    )

    # ------------------------------------------------------------ the guard
    $guardLoaded = $false
    if (Test-Path $guardFile) {
        try {
            Add-Type -Path $guardFile -ErrorAction Stop
            $guardLoaded = $true
        } catch {
            $warning = "the window guard could not be loaded: $($_.Exception.Message)"
            Write-Launcher $warning 'Yellow'
            $Report.warnings += $warning
        }
    }

    $desktopLoaded = $false
    if (-not $NoDesktopIsolation -and (Test-Path $desktopFile)) {
        try {
            Add-Type -Path $desktopFile -ErrorAction Stop
            $desktopLoaded = $true
        } catch {
            $warning = "the private desktop could not be loaded: $($_.Exception.Message)"
            Write-Launcher $warning 'Yellow'
            $Report.warnings += $warning
        }
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Editor
    $startInfo.Arguments = $Arguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.WorkingDirectory = $project

    $process = $null
    $launch = $null
    if ($desktopLoaded) {
        $launch = [LocalTanks.Harness.LocalTanksIsolatedDesktop]::Start('LocalTanksUnity', $startInfo)
        if ($launch.Started) {
            $process = $launch.Process
            $Report.isolation.active = $true
            $Report.isolation.desktop = $launch.DesktopPath
        } else {
            $warning = "the private desktop refused the run ($($launch.Error)); falling back to a hidden window"
            Write-Launcher $warning 'Yellow'
            $Report.warnings += $warning
            $Report.isolation.note = $launch.Error
        }
    }

    if (-not $process) {
        $Report.isolation.requested = $false
        $process = [System.Diagnostics.Process]::Start($startInfo)
    }

    $Report.unityPid = $process.Id
    Write-Launcher "started Unity pid $($process.Id)"

    # ------------------------------------------------------------ watch while it runs
    $foregroundBefore = $null
    if ($guardLoaded -and -not $NoFocusWatch) {
        $foregroundBefore = [LocalTanks.Harness.LocalTanksWindowGuard]::DescribeForeground()
        $Report.isolation.foregroundBefore = "$foregroundBefore"
    }

    $deadline = $null
    if ($Timeout -gt 0) {
        $deadline = (Get-Date).AddSeconds($Timeout)
    }

    $nextNotice = (Get-Date).AddSeconds(20)
    $samples = 0
    $treePids = @()
    $treeRefreshed = (Get-Date).AddSeconds(-30)
    $hidden = 0

    while (-not $process.HasExited) {
        if ($deadline -and (Get-Date) -gt $deadline) {
            Write-Problem "the run exceeded $Timeout s; terminating only the process this launcher started"
            if ($guardLoaded) {
                foreach ($treeId in [LocalTanks.Harness.LocalTanksWindowGuard]::ProcessTree($process.Id)) {
                    Stop-Process -Id $treeId -Force -ErrorAction SilentlyContinue
                }
            } else {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }

            $process.WaitForExit(30000) | Out-Null
            $Report.unityExitCode = Get-RunExitCode -Process $process -Launch $launch
            $Report.error = "the run did not finish within $Timeout s"
            $target = Complete-RunReport -Report $Report -Outcome 'TimedOut' -ExitCode $ExitRunFailed -StartedAt $StartedAt
            Write-Host "[launcher] report: $target"
            return $ExitRunFailed
        }

        if ($guardLoaded) {
            $samples++
            if ((Get-Date) -gt $treeRefreshed) {
                $treePids = @([LocalTanks.Harness.LocalTanksWindowGuard]::ProcessTree($process.Id))
                $treeRefreshed = (Get-Date).AddSeconds(5)
            }

            # Sampling the interactive desktop is the proof that nothing leaked into it. On the private
            # desktop there is nothing to hide, so hiding only runs in the fallback.
            $foreground = [LocalTanks.Harness.LocalTanksWindowGuard]::DescribeForeground()
            if ($treePids -contains $foreground.ProcessId -or $foreground.ProcessId -eq $process.Id) {
                if (-not $Report.isolation.unityReachedForeground) {
                    $Report.isolation.unityReachedForeground = $true
                    Write-Problem "a window of this run reached the user's foreground: $foreground"
                }
            }

            if (-not $Report.isolation.active) {
                $hidden += [LocalTanks.Harness.LocalTanksWindowGuard]::HideTreeWindows($process.Id)
            }
        }

        if (-not $Quiet -and (Get-Date) -gt $nextNotice) {
            $nextNotice = (Get-Date).AddSeconds(20)
            $line = Get-LastLogLine -Path $LogPath
            $elapsed = [int]((Get-Date) - $StartedAt).TotalSeconds
            $suffix = ''
            if ($line) {
                $suffix = " | $line"
            }

            Write-Launcher "still running after ${elapsed}s$suffix"
        }

        Start-Sleep -Milliseconds 50
    }

    $process.WaitForExit()
    $Report.unityExitCode = Get-RunExitCode -Process $process -Launch $launch

    # Unity usually shuts its helpers down and occasionally does not: a run that failed to compile once left a
    # Burst compiler server alive with an open handle for half an hour, which is a lock nobody can see and a
    # process nobody asked for. Only descendants of the process this launcher started are terminated, and only
    # after Unity itself has left.
    if ($guardLoaded) {
        # One second is enough for the shutdown Unity asks for on its way out; anything still alive after
        # that was not going to leave on its own.
        Start-Sleep -Seconds 1
        $leftovers = @()
        foreach ($childId in [LocalTanks.Harness.LocalTanksWindowGuard]::ProcessTree($process.Id)) {
            if ($childId -ne $process.Id -and (Get-Process -Id $childId -ErrorAction SilentlyContinue)) {
                $leftovers += $childId
            }
        }

        foreach ($childId in $leftovers) {
            Stop-Process -Id $childId -Force -ErrorAction SilentlyContinue
        }

        if ($leftovers.Count -gt 0) {
            Write-Launcher "stopped $($leftovers.Count) helper process(es) left behind by the run: $($leftovers -join ', ')"
        }
    }
    $Report.isolation.focusSamples = $samples
    $Report.isolation.windowsHidden = $hidden
    if ($guardLoaded -and -not $NoFocusWatch) {
        $Report.isolation.foregroundAfter = "$([LocalTanks.Harness.LocalTanksWindowGuard]::DescribeForeground())"
    }

    if ($launch) {
        $launch.Close()
    }

    $Report.nunit = Get-NunitSummary -Path $ResultsPath

    $didFail = $false
    $outcome = 'Succeeded'
    if ($Report.unityExitCode -ne 0) {
        $didFail = $true
        $outcome = 'UnityFailed'
        $Report.error = "Unity exited with $($Report.unityExitCode)"
    }

    if ($Report.nunit -and $Report.nunit.failed -gt 0) {
        $didFail = $true
        $outcome = 'TestsFailed'
    }

    if ($Report.nunit -and $Report.nunit.Contains('error')) {
        $didFail = $true
        $outcome = 'ResultsUnreadable'
        $Report.error = $Report.nunit['error']
    } elseif ($Report.nunit -and $Report.nunit.total -eq 0) {
        $didFail = $true
        $outcome = 'NoTests'
        $Report.error = 'the run produced an empty result file'
    }

    # A run that reached the user's foreground is not a successful run, even when its results are good:
    # the harness exists so that the user can keep playing, typing or watching something full screen.
    if ($Report.isolation.unityReachedForeground) {
        $didFail = $true
        if ($outcome -eq 'Succeeded') {
            $outcome = 'FocusStolen'
        }
    }

    $code = $ExitSucceeded
    if ($didFail) {
        $code = $ExitRunFailed
    }

    $target = Complete-RunReport -Report $Report -Outcome $outcome -ExitCode $code -StartedAt $StartedAt

    # ------------------------------------------------------------ what the caller reads
    Write-Host ''
    Write-Launcher "outcome $outcome, Unity exit $($Report.unityExitCode), launched on $(if ($Report.isolation.active) { $Report.isolation.desktop } else { 'the interactive desktop (fallback)' })"
    if ($Report.nunit -and $Report.nunit.Contains('total')) {
        Write-Host ("[launcher] {0}: total={1} passed={2} failed={3} skipped={4}" -f `
            $Report.operation, $Report.nunit.total, $Report.nunit.passed, $Report.nunit.failed, $Report.nunit.skipped)
        foreach ($failure in $Report.nunit.failures | Select-Object -First 25) {
            Write-Host ("  {0}: {1}" -f $failure.result, $failure.name)
        }
    }

    if ($Report.outputs.Count -gt 0) {
        Write-Host "[launcher] this operation writes: $($Report.outputs -join ', ')"
    }

    Write-Host "[launcher] log     : $LogPath"
    if ($ResultsPath) { Write-Host "[launcher] results : $ResultsPath" }
    if ($Report.paths.telemetry) { Write-Host "[launcher] telemetry: $($Report.paths.telemetry)" }
    Write-Host "[launcher] report  : $target"

    if ($code -ne $ExitSucceeded) {
        Show-LogTail -Path $LogPath
    }

    if ($Report.nunit -and $Report.nunit.failed -gt 0) {
        Write-Host '[launcher] read the result file or the report, not the log, for the list of failures.'
    }

    return $code
}

# ---------------------------------------------------------------- entry point

try {
    $operations = Get-OperationRegistry
} catch {
    Write-Problem $_.Exception.Message
    exit $ExitConfiguration
}

if ($ListOperations) {
    Show-Operations -Operations $operations
    exit $ExitSucceeded
}

# Work out what to run. The registry decides the mode, so the same operation name always behaves the same
# way no matter which agent or which script asks for it.
$entry = $null
$operationName = $Operation
$resolvedPlatform = $Platform
$resolvedExecuteMethod = $ExecuteMethod

if (-not [string]::IsNullOrWhiteSpace($Operation)) {
    $entry = Find-Operation -Operations $operations -Name $Operation
    if (-not $entry) {
        Write-Problem "unknown operation '$Operation'. Known operations:"
        Show-Operations -Operations $operations
        exit $ExitConfiguration
    }
} elseif (-not [string]::IsNullOrWhiteSpace($Platform)) {
    $operationName = "${Platform}Tests"
    $entry = Find-Operation -Operations $operations -Name $operationName
    if (-not $entry) {
        Write-Problem "the registry has no '$operationName' entry; add one to $registryFile"
        exit $ExitConfiguration
    }
} elseif (-not [string]::IsNullOrWhiteSpace($ExecuteMethod)) {
    foreach ($candidate in $operations) {
        if ($candidate.executeMethod -eq $ExecuteMethod) {
            $entry = $candidate
            $operationName = $candidate.name
            break
        }
    }

    if (-not $entry) {
        # Allowed, but never silently: an operation nobody registered cannot be checked by the registry
        # test and will not be found by the next agent, so it runs headless and says so.
        $entry = [pscustomobject]@{
            name = $ExecuteMethod
            kind = 'ExecuteMethod'
            executeMethod = $ExecuteMethod
            requiresGraphics = $false
            timeoutSeconds = 1800
            description = 'ad-hoc entry point, not in the registry'
            outputs = @()
        }

        $operationName = $ExecuteMethod
        Write-Launcher "'$ExecuteMethod' is not in the registry; running headless. Add it to $registryFile so the next agent finds it." 'Yellow'
    }
} else {
    Write-Problem 'Nothing to run: pass -Operation, -Platform or -ExecuteMethod. Use -ListOperations to see the registry.'
    exit $ExitConfiguration
}

if ($entry.kind -eq 'Tests') {
    if ([string]::IsNullOrWhiteSpace($resolvedPlatform)) {
        $resolvedPlatform = $entry.testPlatform
    }

    if ($resolvedPlatform -ne $entry.testPlatform) {
        Write-Problem "'$operationName' runs the $($entry.testPlatform) platform, not $resolvedPlatform"
        exit $ExitConfiguration
    }
} elseif ($resolvedPlatform) {
    Write-Problem "'$operationName' is not a test run, so -Platform does not apply"
    exit $ExitConfiguration
}

if ($entry.kind -eq 'ExecuteMethod') {
    if ([string]::IsNullOrWhiteSpace($resolvedExecuteMethod)) {
        $resolvedExecuteMethod = $entry.executeMethod
    }

    if ([string]::IsNullOrWhiteSpace($resolvedExecuteMethod)) {
        Write-Problem "'$operationName' has no editor entry point"
        exit $ExitConfiguration
    }
}

# A test operation may name the tests it exists for, so the registry stays the single description of what
# an operation does: 'CaptureSettingsPanel' must not silently become a full graphical suite.
if ($entry.kind -eq 'Tests' -and [string]::IsNullOrWhiteSpace($Filter) -and
    -not [string]::IsNullOrWhiteSpace($entry.filter)) {
    $Filter = [string]$entry.filter
}

$graphics = [bool]$entry.requiresGraphics
$resolvedMode = 'Graphical'
if (-not $graphics) {
    $resolvedMode = 'Headless'
}

if ($Mode -eq 'Headless') {
    $graphics = $false
    $resolvedMode = 'Headless'
    if ($entry.requiresGraphics) {
        Write-Launcher "'$operationName' is registered as needing a graphics device; -Mode Headless was asked for explicitly and will probably produce an empty picture." 'Yellow'
    }
} elseif ($Mode -eq 'Graphical') {
    $graphics = $true
    $resolvedMode = 'Graphical'
}

$code = Invoke-UnityOperation -OperationName $operationName -Entry $entry -ResolvedMode $resolvedMode `
    -Graphics $graphics -ResolvedExecuteMethod $resolvedExecuteMethod -ResolvedPlatform $resolvedPlatform

exit $code
