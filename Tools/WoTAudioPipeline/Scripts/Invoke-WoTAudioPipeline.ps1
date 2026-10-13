param(
    [string]$WoTRoot = "C:\Games\World_of_Tanks_EU",
    [string]$ProjectRoot,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}
$pipelineRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageRoot = Join-Path $WoTRoot "res\packages"
$localRoot = Join-Path $ProjectRoot "LocalData\WoTAudioPipeline"
$toolRoot = Join-Path $localRoot "ThirdParty"
$workRoot = Join-Path $localRoot "Work"
$bankRoot = Join-Path $workRoot "Banks\audioww"
$mediaRoot = Join-Path $workRoot "Media"
$txtpRoot = Join-Path $workRoot "Txtp"
$outputRoot = Join-Path $ProjectRoot "Assets\LocalOnly\Resources\WoTAudio"
$clipRoot = Join-Path $outputRoot "Clips"

$wwiser = Join-Path $toolRoot "wwiser.pyz"
$namesDb = Join-Path $toolRoot "wwnames.db3"
$vgmstream = Join-Path $toolRoot "vgmstream\vgmstream-cli.exe"
$quickBms = Join-Path $toolRoot "quickbms\quickbms.exe"
$pckScript = Join-Path $toolRoot "wwise_pck_extractor.bms"

if (-not (Test-Path -LiteralPath $packageRoot)) {
    throw "World of Tanks packages were not found: $packageRoot"
}

if (-not (Test-Path -LiteralPath $wwiser) -or
    -not (Test-Path -LiteralPath $vgmstream) -or
    -not (Test-Path -LiteralPath $quickBms)) {
    & (Join-Path $PSScriptRoot "Setup-WoTAudioPipeline.ps1") -ProjectRoot $ProjectRoot
}

New-Item -ItemType Directory -Force -Path $bankRoot, $mediaRoot, $txtpRoot, $clipRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $pipelineRoot "wwnames.txt") -Destination (Join-Path $bankRoot "wwnames.txt") -Force

$profile = Get-Content -Raw -LiteralPath (Join-Path $pipelineRoot "audio_clips.json") | ConvertFrom-Json
if ($profile.schemaVersion -notin @(1, 2)) { throw "Unsupported audio profile schema: $($profile.schemaVersion)" }

$clipsById = @{}
foreach ($clip in $profile.clips) {
    if ($clipsById.ContainsKey($clip.clipId)) { throw "Duplicate clipId '$($clip.clipId)'." }
    $clipsById[$clip.clipId] = $clip
}

function Get-BankEntry($Clip) {
    if (-not [string]::IsNullOrWhiteSpace($Clip.bankEntry)) { return [string]$Clip.bankEntry }
    return "$($Clip.bank).bnk"
}

function Get-MediaBank($Clip) {
    if (-not [string]::IsNullOrWhiteSpace($Clip.mediaBank)) { return [string]$Clip.mediaBank }
    return [string]$Clip.bank
}

function Get-MediaEntries($Clip, [string[]]$Trail = @()) {
    if ($Trail -contains $Clip.clipId) {
        throw "Circular mediaEntriesFrom reference: $($Trail -join ' -> ') -> $($Clip.clipId)"
    }
    if ($null -ne $Clip.mediaEntries -and @($Clip.mediaEntries).Count -gt 0) {
        return @($Clip.mediaEntries | ForEach-Object { [string]$_ })
    }
    if (-not [string]::IsNullOrWhiteSpace($Clip.mediaEntriesFrom)) {
        if (-not $clipsById.ContainsKey($Clip.mediaEntriesFrom)) {
            throw "Clip '$($Clip.clipId)' references unknown media source '$($Clip.mediaEntriesFrom)'."
        }
        return @(Get-MediaEntries $clipsById[$Clip.mediaEntriesFrom] ($Trail + $Clip.clipId))
    }
    return @("$(Get-MediaBank $Clip).pck")
}

function Get-SafeFileKey([string]$Value) {
    return ($Value -replace '[^A-Za-z0-9_.-]', '_')
}

$eventBankEntries = @($profile.clips | ForEach-Object { Get-BankEntry $_ } | Sort-Object -Unique)
$mediaEntries = @($profile.clips | ForEach-Object { Get-MediaEntries $_ } | Sort-Object -Unique)
$requiredEntries = @("Init.bnk") + $eventBankEntries

$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter "audioww-part*.pkg" | Sort-Object Name)
if ($packages.Count -eq 0) { throw "No audioww packages were found in $packageRoot" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Export-PackageEntry([string]$EntryName, [bool]$Required = $true) {
    $target = Join-Path $bankRoot $EntryName
    if ((Test-Path -LiteralPath $target) -and -not $Force) { return $target }

    foreach ($package in $packages) {
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $wanted = "audioww/$EntryName"
            $entry = $archive.Entries | Where-Object {
                $_.FullName.Equals($wanted, [StringComparison]::OrdinalIgnoreCase)
            } | Select-Object -First 1
            if ($null -eq $entry) { continue }

            $targetDirectory = Split-Path -Parent $target
            New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            return $target
        }
        finally {
            $archive.Dispose()
        }
    }

    if ($Required) {
        throw "Audio entry '$EntryName' was not found in audioww packages."
    }
    return $null
}

$sourceFiles = @{}
foreach ($entryName in $requiredEntries) {
    $sourceFiles[$entryName] = Export-PackageEntry $entryName $true
}
foreach ($entryName in $mediaEntries) {
    if (-not $sourceFiles.ContainsKey($entryName)) {
        $sourceFiles[$entryName] = Export-PackageEntry $entryName $false
    }
}

function Write-XorPad([uint32]$EncryptedVersion, [uint32]$PlainVersion) {
    $key = [uint32]($EncryptedVersion -bxor $PlainVersion)
    $keyBytes = [BitConverter]::GetBytes($key)
    $bytes = New-Object byte[] 24
    [Array]::Copy($keyBytes, 0, $bytes, 8, 4)
    [IO.File]::WriteAllBytes((Join-Path $bankRoot "xorpad.bin"), $bytes)
}

function Resolve-BankVersion {
    $probeBank = $sourceFiles["Init.bnk"]
    $header = [IO.File]::ReadAllBytes($probeBank)
    if ($header.Length -lt 12) { throw "Init.bnk is too small." }
    $encryptedVersion = [BitConverter]::ToUInt32($header, 8)
    $probeRoot = Join-Path $workRoot "VersionProbe"
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    $candidates = @(172, 171, 169, 168, 160, 154, 152, 150, 145, 144, 141, 140, 136, 135, 134, 132, 129, 128, 126, 125, 122, 120, 118, 113, 112)

    foreach ($candidate in $candidates) {
        Write-XorPad $encryptedVersion ([uint32]$candidate)
        $logPath = Join-Path $probeRoot "wwiser.log"
        if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
        Push-Location $probeRoot
        try {
            & python $wwiser $probeBank -d none -l *> $null
        }
        finally {
            Pop-Location
        }

        $log = if (Test-Path -LiteralPath $logPath) { Get-Content -Raw -LiteralPath $logPath } else { "" }
        if ($log -notmatch "encrypted bank version|ERRORS!|SKIPS!|unknown bank version" -and
            $log -match "parser: done") {
            return $candidate
        }
    }

    throw "Could not determine the Wwise bank version. Update the candidate list after a WoT audio format change."
}

$bankVersion = Resolve-BankVersion
Write-Host "Detected Wwise bank version $bankVersion."

$xorPadPath = Join-Path $bankRoot "xorpad.bin"
foreach ($bankEntry in $eventBankEntries) {
    $bankDirectory = Split-Path -Parent $sourceFiles[$bankEntry]
    if (-not $bankDirectory.Equals($bankRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $xorPadPath -Destination (Join-Path $bankDirectory "xorpad.bin") -Force
    }
}

foreach ($entryName in $mediaEntries) {
    $packagePath = $sourceFiles[$entryName]
    if ($null -eq $packagePath) { continue }

    $entryKey = Get-SafeFileKey ([IO.Path]::GetFileNameWithoutExtension($entryName))
    $marker = Join-Path $mediaRoot "$entryKey.extracted"
    if ($Force -or -not (Test-Path -LiteralPath $marker)) {
        $log = Join-Path $workRoot "quickbms-$entryKey.log"
        $errorLog = Join-Path $workRoot "quickbms-$entryKey.error.log"
        $arguments = @(
            "-o",
            "`"$pckScript`"",
            "`"$packagePath`"",
            "`"$mediaRoot`""
        )
        $process = Start-Process -FilePath $quickBms -ArgumentList $arguments -Wait -PassThru `
            -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errorLog
        if ($process.ExitCode -ne 0) { throw "QuickBMS failed for $entryName. See $log and $errorLog" }
        Set-Content -LiteralPath $marker -Value "ok"
    }
}

foreach ($clip in $profile.clips) {
    $mediaBank = Get-MediaBank $clip
    $bankMediaRoot = Join-Path $mediaRoot $mediaBank
    New-Item -ItemType Directory -Force -Path $bankMediaRoot | Out-Null

    $bankEntry = Get-BankEntry $clip
    $bankPath = $sourceFiles[$bankEntry]
    Copy-Item -LiteralPath $bankPath -Destination (Join-Path $bankMediaRoot ([IO.Path]::GetFileName($bankEntry))) -Force

    foreach ($mediaEntry in @(Get-MediaEntries $clip)) {
        if ($null -eq $sourceFiles[$mediaEntry]) { continue }
        $extractedFolderName = [IO.Path]::GetFileNameWithoutExtension($mediaEntry)
        $extractedRoot = Join-Path $mediaRoot $extractedFolderName
        if (-not (Test-Path -LiteralPath $extractedRoot) -or
            $extractedRoot.Equals($bankMediaRoot, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        foreach ($wem in Get-ChildItem -LiteralPath $extractedRoot -Filter "*.wem" -File -Recurse) {
            $linkPath = Join-Path $bankMediaRoot $wem.Name
            if (Test-Path -LiteralPath $linkPath) {
                $existing = Get-Item -LiteralPath $linkPath
                if ($existing.Length -ne $wem.Length) {
                    throw "Media ID collision while merging '$mediaEntry': $($wem.Name)"
                }
                continue
            }
            try {
                New-Item -ItemType HardLink -Path $linkPath -Target $wem.FullName | Out-Null
            }
            catch {
                Copy-Item -LiteralPath $wem.FullName -Destination $linkPath
            }
        }
    }
}

$manifestClips = @()
foreach ($clip in $profile.clips) {
    if ([string]::IsNullOrWhiteSpace($clip.clipId) -or $clip.clipId -notmatch '^[a-z0-9_]+$') {
        throw "Invalid clipId '$($clip.clipId)'. Use lowercase letters, digits and underscores only."
    }

    $eventRoot = Join-Path $txtpRoot $clip.clipId
    if ($Force -and (Test-Path -LiteralPath $eventRoot)) {
        $resolvedEventRoot = (Resolve-Path -LiteralPath $eventRoot).Path
        $resolvedTxtpRoot = (Resolve-Path -LiteralPath $txtpRoot).Path.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedEventRoot.StartsWith($resolvedTxtpRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected TXTP directory: $resolvedEventRoot"
        }
        Remove-Item -LiteralPath $resolvedEventRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $eventRoot | Out-Null

    $txtpFiles = @(Get-ChildItem -LiteralPath $eventRoot -Filter "*.txtp" | Sort-Object Name)
    if ($txtpFiles.Count -eq 0) {
        $bankEntry = Get-BankEntry $clip
        $bankPath = $sourceFiles[$bankEntry]
        $mediaBank = Get-MediaBank $clip
        $mediaPath = (Resolve-Path -LiteralPath (Join-Path $mediaRoot $mediaBank)).Path
        $wwiserArguments = @(
            $wwiser,
            $sourceFiles["Init.bnk"],
            $bankPath,
            "-g", "-gf", $clip.event,
            "-go", $eventRoot,
            "-gw", $mediaPath,
            "-nl", (Join-Path $bankRoot "wwnames.txt"),
            "-nd", $namesDb
        )
        if (-not [string]::IsNullOrWhiteSpace($clip.languageId)) {
            $wwiserArguments += @("-gl", [string]$clip.languageId)
        }
        & python @wwiserArguments
        if ($LASTEXITCODE -ne 0) { throw "wwiser failed for event '$($clip.event)'." }
        $txtpFiles = @(Get-ChildItem -LiteralPath $eventRoot -Filter "*.txtp" | Sort-Object Name)
    }

    if ($txtpFiles.Count -eq 0) {
        throw "Event '$($clip.event)' in bank '$(Get-BankEntry $clip)' produced no TXTP variants. Verify the event name and bank mapping."
    }

    $variantIndex = [int]$clip.variantIndex
    if ($variantIndex -lt 0 -or $variantIndex -ge $txtpFiles.Count) {
        throw "Event '$($clip.event)' produced $($txtpFiles.Count) variants; requested index $variantIndex."
    }

    $selected = $txtpFiles[$variantIndex]
    $group = if ([string]::IsNullOrWhiteSpace($clip.group)) { "Vehicles" } else { [string]$clip.group }
    if ([IO.Path]::IsPathRooted($group) -or $group -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "Invalid output group '$group' for '$($clip.clipId)'."
    }
    $destinationRoot = if ($group -eq "Vehicles") { $clipRoot } else { Join-Path $clipRoot $group }
    New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
    $destination = Join-Path $destinationRoot "$($clip.clipId).wav"
    if ($Force -or -not (Test-Path -LiteralPath $destination)) {
        & $vgmstream -i -o $destination $selected.FullName
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $destination)) {
            throw "vgmstream failed for '$($clip.clipId)'."
        }
    }
    if ($null -ne $clip.trimTrailingSilenceDb) {
        & python (Join-Path $PSScriptRoot "trim_wav_silence.py") $destination `
            --threshold-db ([string]$clip.trimTrailingSilenceDb) --tail-ms 150
        if ($LASTEXITCODE -ne 0) { throw "Silence trimming failed for '$($clip.clipId)'." }
    }

    $file = Get-Item -LiteralPath $destination
    $manifestClips += [ordered]@{
        clipId = $clip.clipId
        group = $group.Replace('\', '/')
        bank = $clip.bank
        bankEntry = Get-BankEntry $clip
        mediaBank = Get-MediaBank $clip
        mediaEntries = @(Get-MediaEntries $clip)
        languageId = if ([string]::IsNullOrWhiteSpace($clip.languageId)) { $null } else { [string]$clip.languageId }
        event = $clip.event
        variantIndex = $variantIndex
        variantCount = $txtpFiles.Count
        selectedTxtp = $selected.Name
        usage = $clip.usage
        trimTrailingSilenceDb = if ($null -eq $clip.trimTrailingSilenceDb) { $null } else { [double]$clip.trimTrailingSilenceDb }
        relativePath = $destination.Substring($outputRoot.TrimEnd('\').Length + 1).Replace('\', '/')
        bytes = $file.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schemaVersion = 2
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    sourceRoot = $WoTRoot
    wwiseBankVersion = $bankVersion
    clips = $manifestClips
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "audio_manifest.json") -Encoding utf8

Write-Host "Generated $($manifestClips.Count) local WoT clips in $clipRoot"
Write-Host "Run Unity's 'Local Tanks > Build Combat Assets' to refresh tank prefabs."
