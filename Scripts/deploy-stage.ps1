<#
.SYNOPSIS
Deploys a local release ZIP to the Stage environment.

.EXAMPLE
.\scripts\deploy-stage.ps1 -Version "v0.4.0"

.EXAMPLE
.\scripts\deploy-stage.ps1 `
    -Version "v0.4.0" `
    -ZipPath ".\artifacts\PriceCrawler-v0.4.0.zip"

Expected ZIP structure:

WEB\
    ...
crawler\
    ...
#>

[CmdletBinding()]
param
(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$ZipPath,

    [Parameter(Mandatory = $false)]
    [string]$WebProcessName = "PriceCrawler.Web",

    [Parameter(Mandatory = $false)]
    [string]$CrawlerProcessName = "PriceCrawler.Crawler",

    [Parameter(Mandatory = $false)]
    [string]$WebExeName = "PriceCrawler.Web.exe",

    [Parameter(Mandatory = $false)]
    [string]$CrawlerExeName = "PriceCrawler.Crawler.exe"
)


Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"


# ============================================================
# Helpers
# ============================================================

function Write-Step
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Message
    Write-Host "============================================================"
}


function Stop-StageProcess
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProcessName
    )

    $processes = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue

    if (-not $processes)
    {
        Write-Host "Process '$ProcessName' is not running."
        return
    }

    foreach ($process in $processes)
    {
        Write-Host "Stopping process '$ProcessName' PID=$($process.Id)..."

        Stop-Process `
            -Id $process.Id `
            -Force `
            -ErrorAction Stop
    }

    Start-Sleep -Seconds 2

    $remaining = Get-Process `
        -Name $ProcessName `
        -ErrorAction SilentlyContinue

    if ($remaining)
    {
        throw "Failed to stop process '$ProcessName'."
    }

    Write-Host "Process '$ProcessName' stopped."
}


function Start-StageProcess
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string]$DisplayName
    )

    if (-not (Test-Path $ExecutablePath))
    {
        throw "Executable not found: $ExecutablePath"
    }

    Write-Host "Starting $DisplayName..."
    Write-Host "Executable: $ExecutablePath"
    Write-Host "Working directory: $WorkingDirectory"

    $process = Start-Process `
        -FilePath $ExecutablePath `
        -WorkingDirectory $WorkingDirectory `
        -PassThru

    Start-Sleep -Seconds 2

    if ($process.HasExited)
    {
        throw "$DisplayName exited immediately with exit code $($process.ExitCode)."
    }

    Write-Host "$DisplayName started. PID=$($process.Id)"
}


# ============================================================
# Resolve paths
# ============================================================

Write-Step "Resolve deployment paths"


# deploy-stage.ps1:
#
# <solution>\scripts\deploy-stage.ps1
#
# $PSScriptRoot:
#
# <solution>\scripts
#
# SolutionRoot:
#
# <solution>

$SolutionRoot = Split-Path `
    -Parent $PSScriptRoot


$ArtifactsRoot = Join-Path `
    $SolutionRoot `
    "artifacts"


$StageRoot = Join-Path `
    $SolutionRoot `
    "stage"


$CurrentRoot = Join-Path `
    $StageRoot `
    "current"


$ReleasesRoot = Join-Path `
    $StageRoot `
    "releases"


$BackupsRoot = Join-Path `
    $StageRoot `
    "backups"


$LogsRoot = Join-Path `
    $StageRoot `
    "logs"


$SharedRoot = Join-Path `
    $StageRoot `
    "shared"


$ReleaseRoot = Join-Path `
    $ReleasesRoot `
    $Version


$StageConfigPath = Join-Path `
    $SharedRoot `
    "appsettings.Stage.json"


if ([string]::IsNullOrWhiteSpace($ZipPath))
{
    $ZipPath = Join-Path `
        $ArtifactsRoot `
        "PriceCrawler-$Version.zip"
}
elseif (-not [System.IO.Path]::IsPathRooted($ZipPath))
{
    $ZipPath = Join-Path `
        $SolutionRoot `
        $ZipPath
}


$ZipPath = [System.IO.Path]::GetFullPath($ZipPath)


Write-Host "Solution root : $SolutionRoot"
Write-Host "Artifacts     : $ArtifactsRoot"
Write-Host "ZIP           : $ZipPath"
Write-Host "Stage root    : $StageRoot"
Write-Host "Release       : $ReleaseRoot"
Write-Host "Current       : $CurrentRoot"


# ============================================================
# Validate input
# ============================================================

Write-Step "Validate deployment input"


if (-not (Test-Path $ZipPath))
{
    throw @"
Release ZIP not found:

$ZipPath

Expected default location:

artifacts\PriceCrawler-$Version.zip
"@
}


if (-not (Test-Path $StageConfigPath))
{
    throw @"
Stage configuration file not found:

$StageConfigPath

Expected:

stage\shared\appsettings.Stage.json
"@
}


# ============================================================
# Create directory structure
# ============================================================

Write-Step "Prepare Stage directories"


$requiredDirectories = @(
    $StageRoot,
    $CurrentRoot,
    $ReleasesRoot,
    $BackupsRoot,
    $LogsRoot,
    $SharedRoot
)


foreach ($directory in $requiredDirectories)
{
    if (-not (Test-Path $directory))
    {
        Write-Host "Creating directory: $directory"

        New-Item `
            -ItemType Directory `
            -Path $directory `
            -Force | Out-Null
    }
}


# ============================================================
# Validate duplicate deployment
# ============================================================

if (Test-Path $ReleaseRoot)
{
    throw @"
Release already exists:

$ReleaseRoot

A deployed release directory is immutable.

Delete it manually only if this is an intentional redeployment.
"@
}


# ============================================================
# Extract release
# ============================================================

Write-Step "Extract release $Version"


New-Item `
    -ItemType Directory `
    -Path $ReleaseRoot `
    -Force | Out-Null


Expand-Archive `
    -Path $ZipPath `
    -DestinationPath $ReleaseRoot `
    -Force


$ReleaseWebRoot = Join-Path `
    $ReleaseRoot `
    "WEB"


$ReleaseCrawlerRoot = Join-Path `
    $ReleaseRoot `
    "crawler"


if (-not (Test-Path $ReleaseWebRoot))
{
    throw "ZIP does not contain expected WEB directory."
}


if (-not (Test-Path $ReleaseCrawlerRoot))
{
    throw "ZIP does not contain expected crawler directory."
}


Write-Host "Release extracted successfully."


# ============================================================
# Stop Stage processes
# ============================================================

Write-Step "Stop Stage processes"


Stop-StageProcess `
    -ProcessName $WebProcessName


Stop-StageProcess `
    -ProcessName $CrawlerProcessName


# ============================================================
# Backup current deployment
# ============================================================

Write-Step "Backup current Stage deployment"


$currentItems = Get-ChildItem `
    -Path $CurrentRoot `
    -Force `
    -ErrorAction SilentlyContinue


if ($currentItems)
{
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

    $backupPath = Join-Path `
        $BackupsRoot `
        "current-$timestamp"


    Write-Host "Creating backup:"
    Write-Host $backupPath


    Copy-Item `
        -Path $CurrentRoot `
        -Destination $backupPath `
        -Recurse `
        -Force
}
else
{
    Write-Host "Current deployment is empty. Backup skipped."
}


# ============================================================
# Clear current
# ============================================================

Write-Step "Clear current Stage deployment"


Get-ChildItem `
    -Path $CurrentRoot `
    -Force `
    -ErrorAction SilentlyContinue |
    Remove-Item `
        -Recurse `
        -Force


# ============================================================
# Copy release to current
# ============================================================

Write-Step "Deploy release to current"


Copy-Item `
    -Path "$ReleaseRoot\*" `
    -Destination $CurrentRoot `
    -Recurse `
    -Force


$CurrentWebRoot = Join-Path `
    $CurrentRoot `
    "WEB"


$CurrentCrawlerRoot = Join-Path `
    $CurrentRoot `
    "crawler"


# ============================================================
# Apply Stage configuration
# ============================================================

Write-Step "Apply Stage configuration"


$WebConfigTarget = Join-Path `
    $CurrentWebRoot `
    "appsettings.Stage.json"


$CrawlerConfigTarget = Join-Path `
    $CurrentCrawlerRoot `
    "appsettings.Stage.json"


Copy-Item `
    -Path $StageConfigPath `
    -Destination $WebConfigTarget `
    -Force


Copy-Item `
    -Path $StageConfigPath `
    -Destination $CrawlerConfigTarget `
    -Force


Write-Host "Stage configuration copied to:"
Write-Host "  $WebConfigTarget"
Write-Host "  $CrawlerConfigTarget"


# ============================================================
# Start Stage
# ============================================================

Write-Step "Start Stage processes"


$WebExePath = Join-Path `
    $CurrentWebRoot `
    $WebExeName


$CrawlerExePath = Join-Path `
    $CurrentCrawlerRoot `
    $CrawlerExeName


Start-StageProcess `
    -ExecutablePath $WebExePath `
    -WorkingDirectory $CurrentWebRoot `
    -DisplayName "Stage WEB"


Start-StageProcess `
    -ExecutablePath $CrawlerExePath `
    -WorkingDirectory $CurrentCrawlerRoot `
    -DisplayName "Stage Crawler"


# ============================================================
# Deployment log
# ============================================================

Write-Step "Write deployment log"


$DeployLogPath = Join-Path `
    $LogsRoot `
    "deployments.log"


$deployRecord = "{0} | Version={1} | ZIP={2} | Status=OK" -f `
    (Get-Date -Format "yyyy-MM-dd HH:mm:ss"),
    $Version,
    $ZipPath


Add-Content `
    -Path $DeployLogPath `
    -Value $deployRecord


# ============================================================
# Done
# ============================================================

Write-Host ""
Write-Host "Stage deployment completed successfully."
Write-Host ""
Write-Host "Version : $Version"
Write-Host "Release : $ReleaseRoot"
Write-Host "Current : $CurrentRoot"
Write-Host ""