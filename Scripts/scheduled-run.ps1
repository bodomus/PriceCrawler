[CmdletBinding()]
param(
    [string]$AppRoot = "J:\Apps\PriceCrawler",
    [ValidateSet("auto", "collect-prices", "catalog-refresh")][string]$Mode = "auto",
    [DayOfWeek]$CatalogRefreshDay = [DayOfWeek]::Sunday,
    [string]$ContainerName = "var_postgres",
    [string]$DatabaseUser = "var",
    [string]$DatabaseName = "varprice",
    [string]$BackupDirectory = "D:\Backups\Varus\Postgres",
    [string]$DockerDesktopPath = "C:\Program Files\Docker\Docker\Docker Desktop.exe",
    [ValidateRange(10, 1800)][int]$DockerTimeoutSeconds = 300,
    [ValidateRange(5, 600)][int]$PostgresTimeoutSeconds = 120,
    [ValidateRange(1, 365)][int]$LogRetentionDays = 30,
    [switch]$SkipBackup
)

# Daily price collection from the published Release build (MPC-89). Replaces the old crawler.bat.
# Steps: Docker -> PostgreSQL -> Worker (Production, ValidateOnly) -> backup -> log.
#   auto mode: catalog-refresh + collect-prices on $CatalogRefreshDay, collect-prices on other days.
#   The backup runs after the Worker even when the Worker failed.
# Exit codes (first failure wins):
#   0  ok
#   10 Docker Engine not available        11 PostgreSQL container not ready
#   20 published Worker not found         21 catalog-refresh failed
#   22 collect-prices failed              30 backup failed
#   40 another scheduled run is in progress

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$logDirectory = Join-Path $AppRoot "logs"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$startedAt = Get-Date
$logPath = Join-Path $logDirectory ("scheduled-run_{0:yyyy-MM-dd_HHmmss}.log" -f $startedAt)

function Write-Log {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Message)
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

# Runs a native command, streams stdout+stderr into the log and returns its exit code.
function Invoke-Logged {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @())
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Log "  $_" }
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Test-Native {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments = @())
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>&1 | Out-Null
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Wait-Until {
    param([Parameter(Mandatory)][scriptblock]$Condition, [Parameter(Mandatory)][int]$TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return $true
        }
        Start-Sleep -Seconds 5
    }
    return [bool](& $Condition)
}

function Start-DockerEngine {
    if (Test-Native docker @("info")) {
        Write-Log "Docker Engine is ready."
        return $true
    }
    if (Test-Path -LiteralPath $DockerDesktopPath -PathType Leaf) {
        Write-Log "Starting Docker Desktop: $DockerDesktopPath"
        Start-Process -FilePath $DockerDesktopPath | Out-Null
    }
    else {
        Write-Log "Docker Desktop not found at $DockerDesktopPath; waiting for an already starting engine."
    }
    $ready = Wait-Until -TimeoutSeconds $DockerTimeoutSeconds -Condition { Test-Native docker @("info") }
    Write-Log $(if ($ready) { "Docker Engine is ready." } else { "Docker Engine did not become ready in $DockerTimeoutSeconds s." })
    return $ready
}

function Start-Postgres {
    if (-not (Test-Native docker @("inspect", $ContainerName))) {
        Write-Log "Container '$ContainerName' does not exist. Create it once from the repository: docker compose up -d postgres"
        return $false
    }
    if (-not (Test-Native docker @("start", $ContainerName))) {
        Write-Log "docker start $ContainerName failed."
        return $false
    }
    $ready = Wait-Until -TimeoutSeconds $PostgresTimeoutSeconds -Condition {
        Test-Native docker @("exec", $ContainerName, "pg_isready", "-U", $DatabaseUser, "-d", $DatabaseName)
    }
    Write-Log $(if ($ready) { "PostgreSQL ($ContainerName/$DatabaseName) is ready." } else { "PostgreSQL did not become ready in $PostgresTimeoutSeconds s." })
    return $ready
}

function Get-CollectorExecutable {
    $pointerPath = Join-Path $AppRoot "worker\current.txt"
    if (-not (Test-Path -LiteralPath $pointerPath -PathType Leaf)) {
        Write-Log "Published Worker not found: $pointerPath is missing. Run Scripts\publish-collector.ps1 first."
        return $null
    }
    $version = (Get-Content -LiteralPath $pointerPath -Raw).Trim()
    $exePath = Join-Path $AppRoot "worker\$version\PriceCrawler.Worker.exe"
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        Write-Log "Published Worker not found: $exePath"
        return $null
    }
    Write-Log "Collector version: $version"
    return $exePath
}

function Remove-OldLogs {
    $boundary = (Get-Date).AddDays(-$LogRetentionDays)
    Get-ChildItem -LiteralPath $logDirectory -File -Filter "scheduled-run_*.log" |
        Where-Object { $_.LastWriteTime -lt $boundary } |
        ForEach-Object {
            try { Remove-Item -LiteralPath $_.FullName -Force } catch { Write-Log "Could not remove old log $($_.FullName)" }
        }
}

$mutex = New-Object System.Threading.Mutex($false, "Local\PriceCrawlerScheduledRun")
$hasLock = $false
$exitCode = 0
try {
    try {
        $hasLock = $mutex.WaitOne(0)
    }
    catch [System.Threading.AbandonedMutexException] {
        $hasLock = $true
    }
    if (-not $hasLock) {
        Write-Log "Another scheduled run is in progress. Exiting."
        exit 40
    }

    Write-Log "Scheduled run started. AppRoot=$AppRoot; Mode=$Mode; Log=$logPath"
    $commands = switch ($Mode) {
        "collect-prices" { @("collect-prices") }
        "catalog-refresh" { @("catalog-refresh") }
        default {
            if ($startedAt.DayOfWeek -eq $CatalogRefreshDay) { @("catalog-refresh", "collect-prices") } else { @("collect-prices") }
        }
    }

    $databaseReady = $false
    if (-not (Start-DockerEngine)) {
        $exitCode = 10
    }
    elseif (-not (Start-Postgres)) {
        $exitCode = 11
    }
    else {
        $databaseReady = $true
        $exePath = Get-CollectorExecutable
        if ($null -eq $exePath) {
            $exitCode = 20
        }
        else {
            $env:DOTNET_ENVIRONMENT = "Production"
            foreach ($command in $commands) {
                Write-Log "Worker $command started (DOTNET_ENVIRONMENT=Production)."
                $workerExitCode = Invoke-Logged -FilePath $exePath -Arguments @($command)
                Write-Log "Worker $command finished with exit code $workerExitCode."
                if ($workerExitCode -ne 0 -and $exitCode -eq 0) {
                    $exitCode = if ($command -eq "catalog-refresh") { 21 } else { 22 }
                }
            }
        }
    }

    if ($SkipBackup) {
        Write-Log "Backup skipped (-SkipBackup)."
    }
    elseif (-not $databaseReady) {
        Write-Log "Backup skipped: database is not available."
    }
    else {
        Write-Log "Backup started."
        $backupScript = Join-Path $PSScriptRoot "backup-database.ps1"
        $backupExitCode = Invoke-Logged -FilePath "powershell.exe" -Arguments @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $backupScript,
            "-ContainerName", $ContainerName,
            "-DatabaseUser", $DatabaseUser,
            "-DatabaseName", $DatabaseName,
            "-BackupDirectory", $BackupDirectory)
        Write-Log "Backup finished with exit code $backupExitCode."
        if ($backupExitCode -ne 0 -and $exitCode -eq 0) {
            $exitCode = 30
        }
    }

    Remove-OldLogs
}
catch {
    Write-Log "Scheduled run failed: $($_.Exception.Message)"
    if ($exitCode -eq 0) {
        $exitCode = 1
    }
}
finally {
    if ($hasLock) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}

$duration = (Get-Date) - $startedAt
Write-Log ("Scheduled run finished. ExitCode={0}; Duration={1:hh\:mm\:ss}" -f $exitCode, $duration)
exit $exitCode
