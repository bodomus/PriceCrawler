[CmdletBinding()]
param(
    [string]$AppRoot = "J:\Apps\PriceCrawler",
    [string]$Configuration = "Release",
    [ValidateRange(2, 50)][int]$KeepVersions = 5,
    [switch]$AllowDirtyWorkingTree,
    [switch]$Force,
    [string]$ContainerName = "var_postgres",
    [string]$DatabaseUser = "var",
    [string]$DatabaseName = "varprice",
    # Publish without comparing the working database schema with the release (e.g. Docker is not running).
    [switch]$SkipSchemaCheck
)

# Publishes the Worker (price collector) as a Release build outside the repository (MPC-89):
#   <AppRoot>\worker\<version>\   one folder per published version
#   <AppRoot>\worker\current.txt  name of the version scheduled-run.ps1 starts
#   <AppRoot>\worker\active\      fixed folder the Worker runs from (MPC-108): scheduled-run.ps1 copies the
#                                  current version there, so a per-path firewall rule (TinyWall) survives publishes
#   <AppRoot>\config\appsettings.Production.json  local settings, copied into every version, never in git
#   <AppRoot>\scripts\                scheduled-run.ps1 + backup-database.ps1 started by the Task Scheduler (MPC-91)
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

function Assert-WorkingDatabaseSchema {
    # The collector runs with ValidateOnly and stops when the database schema differs from
    # DatabaseSchema.ExpectedVersion, so a release must not be activated before its migrations
    # are applied (Scripts\apply-collector-migrations.ps1, MPC-96).
    $contractPath = Join-Path $repositoryRoot "PriceCrawler.Infrastructure\Persistence\DatabaseSchema.cs"
    $match = [regex]::Match((Get-Content -LiteralPath $contractPath -Raw), 'public\s+const\s+int\s+ExpectedVersion\s*=\s*(?<value>\d+)\s*;')
    if (-not $match.Success) {
        throw "Could not read DatabaseSchema.ExpectedVersion from $contractPath"
    }
    $expected = [int]$match.Groups["value"].Value
    $output = @(& docker exec $ContainerName psql -U $DatabaseUser -d $DatabaseName -tAX `
        -c "select coalesce(max(version), 0) from public.schema_version;" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the schema version of $ContainerName/$DatabaseName ($($output -join ' ')). Start Docker and the container, or pass -SkipSchemaCheck."
    }
    $actual = [int](($output -join "").Trim())
    if ($actual -lt $expected) {
        throw "Working database $DatabaseName is at schema version $actual, this release needs $expected. Run Scripts\apply-collector-migrations.ps1 first (it makes a backup), then publish again."
    }
    if ($actual -gt $expected) {
        throw "Working database $DatabaseName is at schema version $actual, newer than this release ($expected). Publish a newer commit."
    }
    Write-Host "Working database schema version: $actual (matches the release)"
}

function Assert-DotNetRuntime {
    # The collector is published framework-dependent and runs on this machine, so the shared
    # runtimes of the Worker's target framework must be installed here (MPC-97: net10.0).
    $match = [regex]::Match((Get-Content -LiteralPath $projectPath -Raw), '<TargetFramework>net(?<major>\d+)\.\d+</TargetFramework>')
    if (-not $match.Success) {
        throw "Could not read TargetFramework from $projectPath"
    }
    $major = $match.Groups["major"].Value
    $runtimes = @(& dotnet --list-runtimes 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --list-runtimes failed: $($runtimes -join ' ')"
    }
    foreach ($name in @("Microsoft.NETCore.App", "Microsoft.AspNetCore.App")) {
        if (-not ($runtimes | Where-Object { $_ -match "^$([regex]::Escape($name)) $major\." })) {
            throw "$name $major.x is not installed on this machine; the collector would not start. Install the .NET $major SDK or runtimes, then publish again."
        }
    }
    Write-Host ".NET $major runtimes: installed"
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
Assert-DotNetRuntime
if ($SkipSchemaCheck) {
    Write-Warning "Schema check skipped: the collector will refuse to start if $DatabaseName is not at the release schema version."
}
else {
    Assert-WorkingDatabaseSchema
}

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
if ($versionName -in @("active", "active.new")) {
    throw "Version name '$versionName' is reserved for the fixed Worker folder."
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

# The Task Scheduler runs these copies, so editing or switching branches in the repository
# does not change the daily run until the next publish.
$scriptsPath = Join-Path $AppRoot "scripts"
New-Item -ItemType Directory -Path $scriptsPath -Force | Out-Null
foreach ($scriptName in @("scheduled-run.ps1", "backup-database.ps1")) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $scriptsPath $scriptName) -Force
}
Write-Host "scripts -> $scriptsPath"

Write-Step "Cleanup (keep $KeepVersions versions)"
# @() keeps an array even when only one name is left (StrictMode has no .Count on a string in Windows PowerShell 5.1).
# worker\active is the running copy (MPC-108), never a version to clean up.
$protected = @(@($versionName, $previousVersion) | Where-Object { $_ })
$reserved = @("active", "active.new")
$versions = @(Get-ChildItem -LiteralPath $workerRoot -Directory |
    Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName "collector-release.json")) -and ($protected -notcontains $_.Name) -and ($reserved -notcontains $_.Name) } |
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
Write-Host "Published $versionName. The next scheduled-run.ps1 copies it to $(Join-Path $workerRoot 'active') and runs it from there." -ForegroundColor Green
Write-Host "Rollback: Set-Content '$currentPointerPath' '<older version>'" -ForegroundColor Green
