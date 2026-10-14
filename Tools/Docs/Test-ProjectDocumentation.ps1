[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$failures = [System.Collections.Generic.List[string]]::new()
$checks = 0

function Test-Contract {
    param([bool]$Condition, [string]$Message)
    $script:checks++
    if (-not $Condition) {
        $script:failures.Add($Message)
    }
}

$requiredDocs = @(
    'AGENTS.md',
    'PROJECT_PLAN.md',
    'Docs/README.md',
    'Docs/STATUS.md',
    'Docs/BACKLOG.md',
    'Docs/ARCHITECTURE.md',
    'Docs/GAME_RULES.md',
    'Docs/DATA_SCHEMA.md',
    'Docs/TEST_PLAN.md',
    'Docs/DECISIONS.md',
    'Docs/DEPENDENCIES.md',
    'Docs/KNOWN_ISSUES.md',
    'Docs/CHANGELOG.md'
)

foreach ($relativePath in $requiredDocs) {
    Test-Contract (Test-Path -LiteralPath (Join-Path $ProjectRoot $relativePath) -PathType Leaf) `
        "Required documentation is missing: $relativePath"
}

$statusPath = Join-Path $ProjectRoot 'Docs/STATUS.md'
if (Test-Path -LiteralPath $statusPath) {
    $status = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8
    Test-Contract ($status.Length -le 20000) `
        "Docs/STATUS.md is $($status.Length) characters; move history to CHANGELOG/Git and keep the current snapshot below 20000."
    Test-Contract ($status -match 'SPRINT_010\.md') `
        'Docs/STATUS.md does not name the active execution plan.'
}

$skillsRoot = Join-Path $ProjectRoot '.dsh/skills'
$allowedFrontmatterKeys = @('name', 'description', 'license', 'allowed-tools', 'metadata')
$skillDirectories = @(Get-ChildItem -LiteralPath $skillsRoot -Directory | Sort-Object Name)
$docsIndex = Get-Content -LiteralPath (Join-Path $ProjectRoot 'Docs/README.md') -Raw -Encoding UTF8

foreach ($skillDirectory in $skillDirectories) {
    $skillPath = Join-Path $skillDirectory.FullName 'SKILL.md'
    Test-Contract (Test-Path -LiteralPath $skillPath -PathType Leaf) `
        "Skill '$($skillDirectory.Name)' has no SKILL.md."
    if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf)) { continue }

    $lines = @(Get-Content -LiteralPath $skillPath -Encoding UTF8)
    Test-Contract ($lines.Count -ge 4 -and $lines[0] -eq '---') `
        "Skill '$($skillDirectory.Name)' has no YAML frontmatter."
    if ($lines.Count -lt 4 -or $lines[0] -ne '---') { continue }

    $closing = -1
    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq '---') { $closing = $index; break }
    }
    Test-Contract ($closing -gt 1) "Skill '$($skillDirectory.Name)' has unterminated YAML frontmatter."
    if ($closing -le 1) { continue }

    $frontmatter = $lines[1..($closing - 1)]
    $keys = @($frontmatter | ForEach-Object {
        if ($_ -match '^([A-Za-z0-9_-]+):') { $Matches[1] }
    } | Where-Object { $_ })

    foreach ($key in $keys) {
        Test-Contract ($allowedFrontmatterKeys -contains $key) `
            "Skill '$($skillDirectory.Name)' uses unsupported frontmatter key '$key'."
    }

    $nameLine = $frontmatter | Where-Object { $_ -match '^name:\s*(.+)$' } | Select-Object -First 1
    $declaredName = if ($nameLine -and $nameLine -match '^name:\s*(.+)$') { $Matches[1].Trim() } else { '' }
    Test-Contract ($declaredName -eq $skillDirectory.Name) `
        "Skill folder '$($skillDirectory.Name)' declares name '$declaredName'."
    Test-Contract (@($frontmatter | Where-Object { $_ -match '^description:\s*\S' }).Count -eq 1) `
        "Skill '$($skillDirectory.Name)' must have one non-empty description."
    Test-Contract ($docsIndex -match [regex]::Escape('`' + $skillDirectory.Name + '`')) `
        "Docs/README.md does not list skill '$($skillDirectory.Name)'."
}

$pipelinePairs = @{
    'render-wot-tank-sprite' = 'Tools/WoTSpritePipeline/README.md'
    'sync-wot-vehicle-data' = 'Tools/WoTDataPipeline/README.md'
    'wot-tank-audio' = 'Tools/WoTAudioPipeline/README.md'
    'build-wot-environment-library' = 'Tools/WoTEnvironmentPipeline/README.md'
    'convert-wot-map-to-primitives' = 'Tools/WoTMapPrimitivePipeline/README.md'
}

foreach ($entry in $pipelinePairs.GetEnumerator()) {
    Test-Contract (Test-Path -LiteralPath (Join-Path $ProjectRoot $entry.Value) -PathType Leaf) `
        "Skill '$($entry.Key)' points to a missing pipeline README: $($entry.Value)"
}

$markdownFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Docs') -Filter '*.md' -File
    Get-Item -LiteralPath (Join-Path $ProjectRoot 'README.md')
    Get-Item -LiteralPath (Join-Path $ProjectRoot 'PROJECT_PLAN.md')
    Get-Item -LiteralPath (Join-Path $ProjectRoot 'AGENTS.md')
    Get-ChildItem -LiteralPath $skillsRoot -Filter '*.md' -File -Recurse
)

$linkPattern = [regex]'\[[^\]]+\]\(([^)]+)\)'
foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($match in $linkPattern.Matches($content)) {
        $target = $match.Groups[1].Value.Trim().Trim('<', '>')
        if ($target -match '^(https?://|mailto:|codex:|#)') { continue }
        $target = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $resolved = Join-Path $file.DirectoryName $target
        Test-Contract (Test-Path -LiteralPath $resolved) `
            "Broken local Markdown link in $($file.FullName.Substring($ProjectRoot.Length + 1)): $target"
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Documentation validation failed ({0}/{1} checks):`n- {2}" -f `
        $failures.Count, $checks, ($failures -join "`n- "))
    exit 1
}

Write-Output "Documentation validation passed: $checks checks, $($skillDirectories.Count) skills."
