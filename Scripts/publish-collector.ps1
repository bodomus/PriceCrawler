[CmdletBinding()]
param(
    [string]$AppRoot = "J:\Apps\PriceCrawler",
    [string]$Configuration = "Release",
    [ValidateRange(2, 50)][int]$KeepVersions = 5,
    [switch]$AllowDirtyWorkingTree,
    [switch]$Force
)

# Publishes the Worker (price collector) as a Release build outside the repository (MPC-89):
#   <AppRoot>\worker\<version>\   one folder per published version
#   <AppRoot>\worker\current.txt  name of the version scheduled-run.ps1 starts
#   <AppRoot>\config\appsettings.Production.json  local settings, copied into every version, never in git
# Rebuilding or debugging in the repository does not affect the collector until the next publish.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "PriceCrawler.Worker\PriceCrawler.Worker.csproj"
$workerRoot = Join-Path $AppRoot "worker"
$settingsPath = Join-Path $AppRoot "config\appsettings.Production.json"
$currentPointerPath = Join-Path $workerRoot "current.txt"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$Description, [Parameter(Mandatory)][scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-CollectorSettings {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Collector settings not found: $Path. Copy config\collector\appsettings.Production.example.json there and fill in the password."
    }
    $settings = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $connectionString = [string]$settings.ConnectionStrings.Postgres
    if ([string]::IsNullOrWhiteSpace($connectionString) -or $connectionString.Contains("<")) {
        throw "ConnectionStrings:Postgres in $Path is empty or still contains a <placeholder>."
    }
    if ([string]$settings.DatabaseSchema.StartupMode -ne "ValidateOnly") {
        throw "DatabaseSchema:StartupMode in $Path must be ValidateOnly: the collector never changes the schema of the working database."
    }
}

function Get-BuildVersion {
    $output = @(& dotnet msbuild $projectPath -t:GetBuildVersion -getProperty:NuGetPackageVersion -getProperty:GitCommitId 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Nerdbank.GitVersioning resolution failed: $($output -join [Environment]::NewLine)"
    }
    $metadata = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    $version = [string]$metadata.Properties.NuGetPackageVersion
    $commit = [string]$metadata.Properties.GitCommitId
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Nerdbank.GitVersioning did not return a version."
    }
    return [pscustomobject]@{ Version = $version.Trim(); Commit = $commit.Trim() }
}

Write-Step "Checks"
Assert-CollectorSettings -Path $settingsPath

$status = @(& git -C $repositoryRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw "git status failed."
}
if ($status.Count -gt 0 -and -not $AllowDirtyWorkingTree) {
    throw "Working tree has uncommitted changes to tracked files. Commit them or pass -AllowDirtyWorkingTree.`n$($status -join [Environment]::NewLine)"
}

$build = Get-BuildVersion
$versionName = $build.Version
if ($status.Count -gt 0) {
    $versionName = "$versionName-dirty-$(Get-Date -Format yyyyMMddHHmmss)"
}
$targetPath = Join-Path $workerRoot $versionName
Write-Host "Version: $versionName (commit $($build.Commit))"
Write-Host "Target:  $targetPath"

$previousVersion = $null
if (Test-Path -LiteralPath $currentPointerPath -PathType Leaf) {
    $previousVersion = (Get-Content -LiteralPath $currentPointerPath -Raw).Trim()
}

if (Test-Path -LiteralPath $targetPath) {
    if ($versionName -eq $previousVersion -and -not $Force) {
        throw "Version $versionName is already published and current. Pass -Force to republish it."
    }
    if (-not $Force) {
        throw "Target already exists: $targetPath. Pass -Force to replace it."
    }
}

Write-Step "dotnet publish ($Configuration)"
$stagingPath = "$targetPath.publishing"
if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
Invoke-Native "dotnet publish" { dotnet publish $projectPath -c $Configuration -o $stagingPath --nologo }

$exePath = Join-Path $stagingPath "PriceCrawler.Worker.exe"
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Worker entry point not found after publish: $exePath"
}

# The repository ships a placeholder appsettings.Production.json; the local one replaces it.
Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $stagingPath "appsettings.Production.json") -Force
[ordered]@{
    version = $versionName
    commit = $build.Commit
    configuration = $Configuration
    publishedAt = (Get-Date).ToString("o")
    previousVersion = $previousVersion
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingPath "collector-release.json") -Encoding UTF8

Write-Step "Smoke check (--help)"
Invoke-Native "PriceCrawler.Worker.exe --help" { & $exePath --help | Out-Null }

Write-Step "Activate"
if (Test-Path -LiteralPath $targetPath) {
    Remove-Item -LiteralPath $targetPath -Recurse -Force
}
Move-Item -LiteralPath $stagingPath -Destination $targetPath
Set-Content -LiteralPath $currentPointerPath -Value $versionName -Encoding ASCII
Write-Host "current -> $versionName (previous: $(if ($previousVersion) { $previousVersion } else { 'none' }))"

Write-Step "Cleanup (keep $KeepVersions versions)"
# @() keeps an array even when only one name is left (StrictMode has no .Count on a string in Windows PowerShell 5.1).
$protected = @(@($versionName, $previousVersion) | Where-Object { $_ })
$versions = @(Get-ChildItem -LiteralPath $workerRoot -Directory |
    Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName "collector-release.json")) -and ($protected -notcontains $_.Name) } |
    Sort-Object LastWriteTime -Descending)
$keepOthers = [Math]::Max(0, $KeepVersions - $protected.Count)
foreach ($old in $versions | Select-Object -Skip $keepOthers) {
    try {
        Remove-Item -LiteralPath $old.FullName -Recurse -Force
        Write-Host "removed $($old.Name)"
    }
    catch {
        Write-Warning "Could not remove $($old.FullName): $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "Published $versionName. Rollback: Set-Content '$currentPointerPath' '<older version>'" -ForegroundColor Green
