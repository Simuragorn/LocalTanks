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
if ($profile.schemaVersion -ne 1) { throw "Unsupported audio profile schema: $($profile.schemaVersion)" }

$bankNames = @($profile.clips | ForEach-Object {
    $_.bank
    if (-not [string]::IsNullOrWhiteSpace($_.mediaBank)) { $_.mediaBank }
} | Sort-Object -Unique)
$requiredEntries = @("Init.bnk")
foreach ($bankName in $bankNames) {
    $requiredEntries += "$bankName.bnk"
    $requiredEntries += "$bankName.pck"
}

$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter "audioww-part*.pkg" | Sort-Object Name)
if ($packages.Count -eq 0) { throw "No audioww packages were found in $packageRoot" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Export-PackageEntry([string]$EntryName) {
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

            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            return $target
        }
        finally {
            $archive.Dispose()
        }
    }

    throw "Audio entry '$EntryName' was not found in audioww packages."
}

$sourceFiles = @{}
foreach ($entryName in $requiredEntries) {
    $sourceFiles[$entryName] = Export-PackageEntry $entryName
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

foreach ($bankName in $bankNames) {
    $marker = Join-Path $mediaRoot "$bankName.extracted"
    if ($Force -or -not (Test-Path -LiteralPath $marker)) {
        $log = Join-Path $workRoot "quickbms-$bankName.log"
        $errorLog = Join-Path $workRoot "quickbms-$bankName.error.log"
        $arguments = @(
            "-o",
            "`"$pckScript`"",
            "`"$($sourceFiles["$bankName.pck"])`"",
            "`"$mediaRoot`""
        )
        $process = Start-Process -FilePath $quickBms -ArgumentList $arguments -Wait -PassThru `
            -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $errorLog
        if ($process.ExitCode -ne 0) { throw "QuickBMS failed for $bankName.pck. See $log and $errorLog" }
        Set-Content -LiteralPath $marker -Value "ok"
    }

    $bankMediaRoot = Join-Path $mediaRoot $bankName
    New-Item -ItemType Directory -Force -Path $bankMediaRoot | Out-Null
    Copy-Item -LiteralPath $sourceFiles["$bankName.bnk"] -Destination (Join-Path $bankMediaRoot "$bankName.bnk") -Force
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
        $bankPath = $sourceFiles["$($clip.bank).bnk"]
        $mediaBank = if ([string]::IsNullOrWhiteSpace($clip.mediaBank)) { $clip.bank } else { $clip.mediaBank }
        $mediaPath = (Resolve-Path -LiteralPath (Join-Path $mediaRoot $mediaBank)).Path
        & python $wwiser $sourceFiles["Init.bnk"] $bankPath -g -gf $clip.event `
            -go $eventRoot -gw $mediaPath -nl (Join-Path $bankRoot "wwnames.txt") -nd $namesDb
        if ($LASTEXITCODE -ne 0) { throw "wwiser failed for event '$($clip.event)'." }
        $txtpFiles = @(Get-ChildItem -LiteralPath $eventRoot -Filter "*.txtp" | Sort-Object Name)
    }

    $variantIndex = [int]$clip.variantIndex
    if ($variantIndex -lt 0 -or $variantIndex -ge $txtpFiles.Count) {
        throw "Event '$($clip.event)' produced $($txtpFiles.Count) variants; requested index $variantIndex."
    }

    $selected = $txtpFiles[$variantIndex]
    $destination = Join-Path $clipRoot "$($clip.clipId).wav"
    if ($Force -or -not (Test-Path -LiteralPath $destination)) {
        & $vgmstream -i -o $destination $selected.FullName
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $destination)) {
            throw "vgmstream failed for '$($clip.clipId)'."
        }
    }

    $file = Get-Item -LiteralPath $destination
    $manifestClips += [ordered]@{
        clipId = $clip.clipId
        bank = $clip.bank
        mediaBank = if ([string]::IsNullOrWhiteSpace($clip.mediaBank)) { $clip.bank } else { $clip.mediaBank }
        event = $clip.event
        variantIndex = $variantIndex
        variantCount = $txtpFiles.Count
        selectedTxtp = $selected.Name
        usage = $clip.usage
        bytes = $file.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    sourceRoot = $WoTRoot
    wwiseBankVersion = $bankVersion
    clips = $manifestClips
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "audio_manifest.json") -Encoding utf8

Write-Host "Generated $($manifestClips.Count) local WoT clips in $clipRoot"
Write-Host "Run Unity's 'Local Tanks > Build Combat Assets' to refresh tank prefabs."
