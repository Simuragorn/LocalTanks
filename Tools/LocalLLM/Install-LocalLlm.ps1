[CmdletBinding()]
param(
    [string]$Version = "v0.40.2",
    [string]$Model = "qwen3:8b",
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\.."))
)

$ErrorActionPreference = "Stop"
$runtimeDirectory = Join-Path $ProjectRoot "LocalData\Ollama"
$modelDirectory = Join-Path $ProjectRoot "LocalData\OllamaModels"
$downloadDirectory = Join-Path $ProjectRoot "LocalData\OllamaDownload"
$archivePath = Join-Path $downloadDirectory "ollama-windows-amd64.zip"
$checksumPath = Join-Path $downloadDirectory "sha256sum.txt"
$baseReleaseUrl = "https://github.com/ollama/ollama/releases/download/$Version"
$serverProcess = $null

function Wait-OllamaServer {
    param([int]$TimeoutSeconds = 45)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/version" -TimeoutSec 2 | Out-Null
            return
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    throw "Ollama did not become ready within $TimeoutSeconds seconds."
}

$existingServer = $false
try {
    Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/version" -TimeoutSec 2 | Out-Null
    $existingServer = $true
}
catch {
    $existingServer = $false
}

if ($existingServer) {
    throw "Port 11434 already hosts Ollama. Stop it before installing the project-owned runtime."
}

try {
    New-Item -ItemType Directory -Force -Path $runtimeDirectory, $modelDirectory, $downloadDirectory | Out-Null
    Invoke-WebRequest -Uri "$baseReleaseUrl/ollama-windows-amd64.zip" -OutFile $archivePath
    Invoke-WebRequest -Uri "$baseReleaseUrl/sha256sum.txt" -OutFile $checksumPath

    $checksumLine = Get-Content $checksumPath | Where-Object { $_ -match "ollama-windows-amd64\.zip$" } | Select-Object -First 1
    if (-not $checksumLine) {
        throw "Official checksum for ollama-windows-amd64.zip was not found."
    }

    $expectedHash = ($checksumLine -split "\s+")[0].ToLowerInvariant()
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Ollama archive checksum mismatch. Expected $expectedHash, got $actualHash."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $runtimeDirectory -Force
    $ollamaExecutable = Join-Path $runtimeDirectory "ollama.exe"
    if (-not (Test-Path -LiteralPath $ollamaExecutable)) {
        throw "Ollama executable was not found after extraction."
    }

    $processEnvironment = [System.Diagnostics.ProcessStartInfo]::new()
    $processEnvironment.FileName = $ollamaExecutable
    $processEnvironment.Arguments = "serve"
    $processEnvironment.WorkingDirectory = $runtimeDirectory
    $processEnvironment.UseShellExecute = $false
    $processEnvironment.CreateNoWindow = $true
    $processEnvironment.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
    $processEnvironment.EnvironmentVariables["OLLAMA_HOST"] = "127.0.0.1:11434"
    $processEnvironment.EnvironmentVariables["OLLAMA_MODELS"] = $modelDirectory
    $processEnvironment.EnvironmentVariables["OLLAMA_KEEP_ALIVE"] = "10m"
    $serverProcess = [System.Diagnostics.Process]::Start($processEnvironment)
    Wait-OllamaServer

    $env:OLLAMA_HOST = "127.0.0.1:11434"
    $env:OLLAMA_MODELS = $modelDirectory
    & $ollamaExecutable pull $Model
    if ($LASTEXITCODE -ne 0) {
        throw "Ollama failed to pull model '$Model'."
    }

    Write-Host "Installed Ollama $Version and model $Model inside the LocalTanks project."
    Write-Host "No Windows startup entry or service was created."
}
finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        $serverProcess.Kill()
        $serverProcess.WaitForExit(5000) | Out-Null
    }

    Remove-Item -LiteralPath $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
