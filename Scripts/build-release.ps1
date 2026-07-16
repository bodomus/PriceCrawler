[CmdletBinding()]
param(
    [string]$Configuration = "Release",

    # По умолчанию версия берётся из Git-тега текущего коммита.
    # Можно указать вручную:
    # .\scripts\build-release.ps1 -Version v0.4.1
    [string]$Version,

    [switch]$SkipTests,

    # Разрешить сборку при наличии незакоммиченных изменений.
    [switch]$AllowDirtyWorkingTree
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][scriptblock]$Command
    )

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-DirectoryNotEmpty {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Name publish directory does not exist: $Path"
    }

    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse)

    if ($files.Count -eq 0) {
        throw "$Name publish directory is empty: $Path"
    }

    return $files.Count
}

function Assert-PublishEntryPoint {
    param(
        [Parameter(Mandatory)][string]$PublishPath,
        [Parameter(Mandatory)][string]$AssemblyName,
        [Parameter(Mandatory)][string]$Name
    )

    $dllPath = Join-Path $PublishPath "$AssemblyName.dll"
    $exePath = Join-Path $PublishPath "$AssemblyName.exe"

    if (
        -not (Test-Path -LiteralPath $dllPath -PathType Leaf) -and
        -not (Test-Path -LiteralPath $exePath -PathType Leaf)
    ) {
        throw "$Name entry point was not found. Expected '$dllPath' or '$exePath'."
    }
}

function Normalize-Version {
    param([Parameter(Mandatory)][string]$Value)

    $normalized = $Value.Trim()

    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "Release version is empty."
    }

    if ($normalized -notmatch '^v?\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$') {
        throw "Invalid release version '$normalized'. Expected format like v0.4.1."
    }

    if (-not $normalized.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = "v$normalized"
    }

    return $normalized
}

# scripts/build-release.ps1 -> repository root
$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..")).Path

$solutionPath = Join-Path $repositoryRoot "PriceCrawler.sln"
$webProjectPath = Join-Path $repositoryRoot "PriceCrawler.Web\PriceCrawler.Web.csproj"
$workerProjectPath = Join-Path $repositoryRoot "PriceCrawler.Worker\PriceCrawler.Worker.csproj"

$artifactsPath = Join-Path $repositoryRoot "artifacts"
$publishRoot = Join-Path $artifactsPath "publish"
$webPublishPath = Join-Path $publishRoot "web"
$crawlerPublishPath = Join-Path $publishRoot "crawler"

$releaseRoot = Join-Path $artifactsPath "release"
$packageRoot = Join-Path $releaseRoot "_package"

Write-Step "Validating repository structure"

foreach ($requiredPath in @($solutionPath, $webProjectPath, $workerProjectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file not found: $requiredPath"
    }
}

Push-Location $repositoryRoot

try {
    Invoke-NativeCommand "Git repository validation" {
        git rev-parse --is-inside-work-tree | Out-Null
    }

    if (-not $AllowDirtyWorkingTree) {
        $workingTreeStatus = @(git status --porcelain)

        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect Git working tree."
        }

        if ($workingTreeStatus.Count -gt 0) {
            throw @"
Working tree contains uncommitted changes.
Commit or stash them before creating a release,
or run with -AllowDirtyWorkingTree.
"@
        }
    }

    Write-Step "Resolving release version"

    if ([string]::IsNullOrWhiteSpace($Version)) {
        Invoke-NativeCommand "Fetching Git tags" {
            git fetch --tags --force
        }

        $tag = git describe --tags --exact-match HEAD 2>$null

        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tag)) {
            throw @"
The current commit does not have an exact Git tag.
Create and push a tag, for example:

    git tag v0.4.1
    git push origin v0.4.1

Or provide the version explicitly:

    .\scripts\build-release.ps1 -Version v0.4.1
"@
        }

        $Version = $tag
    }

    $Version = Normalize-Version $Version
    $archiveName = "PriceCrawler-$Version.zip"
    $archivePath = Join-Path $releaseRoot $archiveName

    Write-Host "Version: $Version"
    Write-Host "Archive: $archivePath"

    if (-not $SkipTests) {
        Write-Step "Restoring and testing solution"

        Invoke-NativeCommand "dotnet restore" {
            dotnet restore $solutionPath
        }

        Invoke-NativeCommand "dotnet test" {
            dotnet test $solutionPath `
                --configuration $Configuration `
                --no-restore
        }
    }
    else {
        Write-Step "Skipping tests"
    }

    Write-Step "Cleaning previous publish output"

    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Path $webPublishPath -Force | Out-Null
    New-Item -ItemType Directory -Path $crawlerPublishPath -Force | Out-Null
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

    Write-Step "Publishing PriceCrawler.Web"

    Invoke-NativeCommand "PriceCrawler.Web publish" {
        dotnet publish $webProjectPath `
            --configuration $Configuration `
            --output $webPublishPath `
            --no-restore
    }

    Write-Step "Publishing PriceCrawler.Worker"

    Invoke-NativeCommand "PriceCrawler.Worker publish" {
        dotnet publish $workerProjectPath `
            --configuration $Configuration `
            --output $crawlerPublishPath `
            --no-restore
    }

    Write-Step "Validating publish output"

    $webFileCount = Assert-DirectoryNotEmpty `
        -Path $webPublishPath `
        -Name "Web"

    $crawlerFileCount = Assert-DirectoryNotEmpty `
        -Path $crawlerPublishPath `
        -Name "Crawler"

    Assert-PublishEntryPoint `
        -PublishPath $webPublishPath `
        -AssemblyName "PriceCrawler.Web" `
        -Name "Web"

    Assert-PublishEntryPoint `
        -PublishPath $crawlerPublishPath `
        -AssemblyName "PriceCrawler.Worker" `
        -Name "Crawler"

    Write-Host "Web files: $webFileCount"
    Write-Host "Crawler files: $crawlerFileCount"

    Write-Step "Preparing release package"

    $packageWebPath = Join-Path $packageRoot "web"
    $packageCrawlerPath = Join-Path $packageRoot "crawler"

    Copy-Item `
        -LiteralPath $webPublishPath `
        -Destination $packageWebPath `
        -Recurse `
        -Force

    Copy-Item `
        -LiteralPath $crawlerPublishPath `
        -Destination $packageCrawlerPath `
        -Recurse `
        -Force

    $releaseInfo = [ordered]@{
        product       = "PriceCrawler"
        version       = $Version
        configuration = $Configuration
        commit        = (git rev-parse HEAD).Trim()
        createdUtc    = [DateTime]::UtcNow.ToString("o")
        components    = @("web", "crawler")
    }

    $releaseInfo |
        ConvertTo-Json -Depth 4 |
        Set-Content `
            -LiteralPath (Join-Path $packageRoot "release.json") `
            -Encoding UTF8

    Write-Step "Creating ZIP archive"

    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

    Compress-Archive `
        -Path (Join-Path $packageRoot "*") `
        -DestinationPath $archivePath `
        -CompressionLevel Optimal `
        -Force

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Release archive was not created: $archivePath"
    }

    $archive = Get-Item -LiteralPath $archivePath

    if ($archive.Length -eq 0) {
        throw "Release archive is empty: $archivePath"
    }

    Write-Step "Release created successfully"

    Write-Host "Path: $($archive.FullName)" -ForegroundColor Green
    Write-Host "Size: $([Math]::Round($archive.Length / 1MB, 2)) MB"
}
finally {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
    Pop-Location
}
