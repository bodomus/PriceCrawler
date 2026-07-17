[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version,
    [string]$OutputDirectory,
    [switch]$ReplaceExistingArtifact,
    [switch]$SkipTests,
    [switch]$AllowDirtyWorkingTree,
    [switch]$ValidatePackageInputsOnly
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

function Normalize-Version {
    param([Parameter(Mandatory)][string]$Value)
    $normalized = $Value.Trim()
    if ($normalized -notmatch '^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
        throw "Invalid release version '$normalized'. Expected semantic version format like v0.4.1-alpha.1."
    }
    if (-not $normalized.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = "v$normalized"
    }
    return $normalized
}

function Get-ExpectedDatabaseSchemaVersion {
    param([Parameter(Mandatory)][string]$ContractPath)
    $contract = Get-Content -LiteralPath $ContractPath -Raw
    $match = [regex]::Match(
        $contract,
        'public\s+const\s+int\s+ExpectedVersion\s*=\s*(?<version>\d+)\s*;',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Could not read DatabaseSchema.ExpectedVersion from $ContractPath"
    }
    return [int]$match.Groups["version"].Value
}

function Get-CanonicalBuildVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)
    $arguments = @(
        "msbuild", $ProjectPath,
        "-t:GetBuildVersion",
        "-getProperty:AssemblyInformationalVersion",
        "-getProperty:GitCommitId")
    $output = @(& dotnet @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Nerdbank.GitVersioning resolution failed: $($output -join [Environment]::NewLine)"
    }
    try {
        $metadata = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "Nerdbank.GitVersioning returned invalid metadata. $($_.Exception.Message)"
    }
    $informationalVersion = [string]$metadata.Properties.AssemblyInformationalVersion
    $commit = [string]$metadata.Properties.GitCommitId
    if ([string]::IsNullOrWhiteSpace($informationalVersion) -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Nerdbank.GitVersioning did not provide application version and Git commit."
    }
    return [pscustomobject]@{
        Version = Normalize-Version $informationalVersion
        Commit = $commit.Trim().ToLowerInvariant()
    }
}

function Get-DatabaseMigrationInventory {
    param(
        [Parameter(Mandatory)][string]$MigrationsPath,
        [Parameter(Mandatory)][string]$ScriptsPath,
        [Parameter(Mandatory)][int]$ExpectedVersion
    )
    $baselinePath = Join-Path $MigrationsPath "0001_baseline.sql"
    $bootstrapPath = Join-Path $ScriptsPath "bootstrap-schema-version.sql"
    foreach ($requiredPath in @($baselinePath, $bootstrapPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Required database release file not found: $requiredPath"
        }
    }

    $records = [Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $MigrationsPath -File -Filter "*.sql" | Sort-Object Name)) {
        $match = [regex]::Match($file.Name, '^(?<number>\d{4})_[a-z0-9_]+\.sql$')
        if (-not $match.Success) {
            throw "Invalid database migration filename '$($file.Name)'. Expected NNNN_description.sql."
        }
        $records.Add([pscustomobject]@{ Version = [int]$match.Groups["number"].Value; File = $file })
    }
    if ($records.Count -eq 0) {
        throw "No database migration files were found in $MigrationsPath"
    }

    $duplicates = @($records | Group-Object Version | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        throw "Duplicate database migration version(s): $((@($duplicates.Name) -join ', '))."
    }
    for ($index = 0; $index -lt $records.Count; $index++) {
        $expectedNumber = $index + 1
        if ($records[$index].Version -ne $expectedNumber) {
            throw "Database migration versions must be contiguous and strictly increasing from 0001. Found '$($records[$index].File.Name)' where version $expectedNumber was expected."
        }
    }

    $targetVersion = $records[-1].Version
    if ($targetVersion -ne $ExpectedVersion) {
        throw "Database migration target version $targetVersion does not match application expected version $ExpectedVersion."
    }
    $baseline = Get-Content -LiteralPath $baselinePath -Raw
    $bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw
    $metadataPattern = "(?is)insert\s+into\s+public\.schema_version.*?values\s*\(\s*$ExpectedVersion\s*,\s*'0001_baseline'"
    if ($baseline -notmatch $metadataPattern -or $bootstrap -notmatch $metadataPattern) {
        throw "Baseline/bootstrap metadata does not register expected schema version $ExpectedVersion."
    }
    return [pscustomobject]@{
        Files = @($records | ForEach-Object File)
        FileNames = @($records | ForEach-Object { $_.File.Name })
        TargetVersion = $targetVersion
        BootstrapPath = $bootstrapPath
    }
}

function Assert-DirectoryNotEmpty {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Name)
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
        [Parameter(Mandatory)][string]$Name)
    if (
        -not (Test-Path -LiteralPath (Join-Path $PublishPath "$AssemblyName.dll") -PathType Leaf) -and
        -not (Test-Path -LiteralPath (Join-Path $PublishPath "$AssemblyName.exe") -PathType Leaf)
    ) {
        throw "$Name entry point was not found in $PublishPath."
    }
}

function Get-NormalizedRelativePath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is outside expected root '$rootPath'."
    }
    return $fullPath.Substring($prefix.Length).Replace('\', '/')
}

function Get-ForbiddenPackagePathReason {
    param([Parameter(Mandatory)][string]$RelativePath)
    $path = $RelativePath.Replace('\', '/').ToLowerInvariant()
    if ($path -match '(^|/)(\.git|\.idea|\.vs|graphify-out|\.code-review-graph|testresults|coverage|backups?|logs?)(/|$)') {
        return "repository, graph, test, backup, or log directory"
    }
    if ($path -match '(^|/)(\.env|\.pgpass)$' -or $path -match '\.(dump|backup|bak|log)$') {
        return "secret, dump, backup, or log file"
    }
    if ($path -match '(^|/)appsettings\.(development|test)\.json$') {
        return "development/test environment configuration"
    }
    return $null
}

function Assert-SafeConfigurationJson {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Json)
    try {
        $configuration = $Json | ConvertFrom-Json
    }
    catch {
        throw "Packaged configuration '$Name' is invalid JSON. $($_.Exception.Message)"
    }
    $connectionString = $configuration.ConnectionStrings.Postgres
    if (-not [string]::IsNullOrWhiteSpace([string]$connectionString)) {
        $password = [regex]::Match([string]$connectionString, '(?i)(?:^|;)\s*password\s*=\s*(?<value>[^;]*)').Groups["value"].Value.Trim()
        if (-not [string]::IsNullOrWhiteSpace($password) -and $password -notmatch '^<[^>]+>$') {
            throw "Packaged configuration '$Name' contains a non-placeholder database password."
        }
    }
    if ($Name -match '(?i)appsettings\.(stage|staging|production)\.json$' -and
        [string]$configuration.DatabaseSchema.StartupMode -ne "ValidateOnly") {
        throw "Packaged Stage/Production configuration '$Name' must use ValidateOnly."
    }
}

function Assert-NoPlaintextSecrets {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $matches = [regex]::Matches(
        $Text,
        '(?im)["'']?(?:password|api[_-]?key|access[_-]?token|client[_-]?secret)["'']?\s*[:=]\s*["'']?(?<value>[^"'',;\r\n}]*)')
    foreach ($match in $matches) {
        $value = $match.Groups["value"].Value.Trim()
        if (
            -not [string]::IsNullOrWhiteSpace($value) -and
            $value -notmatch '^<[^>]+>$' -and
            $value -notmatch '^\\u003c.+\\u003e$' -and
            $value -notmatch '^\$\{[^}]+\}$' -and
            $value -notin @("***", "null")
        ) {
            throw "Package text '$Name' contains a non-placeholder secret-like value."
        }
    }
}

function Set-SafePackagedConfiguration {
    param([Parameter(Mandatory)][string]$ComponentPath)
    foreach ($environmentFile in @("appsettings.Development.json", "appsettings.Test.json")) {
        Remove-Item -LiteralPath (Join-Path $ComponentPath $environmentFile) -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath (Join-Path $ComponentPath "db") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $ComponentPath "schema.sql") -Force -ErrorAction SilentlyContinue
    $basePath = Join-Path $ComponentPath "appsettings.json"
    if (-not (Test-Path -LiteralPath $basePath -PathType Leaf)) {
        throw "Published component does not contain appsettings.json: $ComponentPath"
    }
    $configuration = Get-Content -LiteralPath $basePath -Raw | ConvertFrom-Json
    if ($null -eq $configuration.ConnectionStrings) {
        $configuration | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue ([pscustomobject]@{})
    }
    if ($null -eq $configuration.ConnectionStrings.Postgres) {
        $configuration.ConnectionStrings | Add-Member -NotePropertyName Postgres -NotePropertyValue ""
    }
    $configuration.ConnectionStrings.Postgres = "Host=<runtime-host>;Port=<port>;Database=<database>;Username=<runtime-role>;Password=<secret-from-environment-or-secret-store>"
    $json = $configuration | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($basePath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Assert-ReleaseMetadata {
    param(
        [Parameter(Mandatory)]$Metadata,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][int]$ExpectedSchemaVersion,
        [Parameter(Mandatory)][string[]]$MigrationNames)
    if ([string]$Metadata.product -ne "PriceCrawler") { throw "release.json product is invalid." }
    if ([string]$Metadata.version -ne $Version) { throw "release.json version does not match build input." }
    if ([string]$Metadata.commit -ne $Commit) { throw "release.json commit does not match source revision." }
    $builtAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
        [string]$Metadata.builtAtUtc,
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$builtAt)) {
        throw "release.json builtAtUtc is not a normalized UTC timestamp."
    }
    if (
        [int]$Metadata.database.minimumSchemaVersion -ne $ExpectedSchemaVersion -or
        [int]$Metadata.database.targetSchemaVersion -ne $ExpectedSchemaVersion -or
        [int]$Metadata.database.minimumSchemaVersion -gt [int]$Metadata.database.targetSchemaVersion
    ) { throw "release.json database schema range is inconsistent." }
    if (-not $Metadata.components.web -or -not $Metadata.components.crawler -or -not $Metadata.components.database) {
        throw "release.json component presence metadata is inconsistent."
    }
    $metadataMigrations = @($Metadata.database.migrations | ForEach-Object { [string]$_ })
    if (($metadataMigrations -join '|') -ne ($MigrationNames -join '|')) {
        throw "release.json migration inventory does not match packaged migrations."
    }
}

function Assert-ReleaseStagingTree {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][int]$ExpectedSchemaVersion,
        [Parameter(Mandatory)][string[]]$MigrationNames,
        [Parameter(Mandatory)][string]$RepositoryRoot)
    $required = @(
        "db/migrations/0001_baseline.sql",
        "db/scripts/bootstrap-schema-version.sql",
        "db/scripts/provision-database-runtime-roles.ps1",
        "release.json")
    $files = @(Get-ChildItem -LiteralPath $PackageRoot -File -Recurse)
    $paths = @($files | ForEach-Object { Get-NormalizedRelativePath -Root $PackageRoot -Path $_.FullName })
    foreach ($requiredPath in $required) {
        if ($requiredPath -notin $paths) { throw "Release staging tree is missing '$requiredPath'." }
    }
    foreach ($component in @(@("web", "PriceCrawler.Web"), @("crawler", "PriceCrawler.Worker"))) {
        if (-not (($component[0] + "/" + $component[1] + ".dll") -in $paths) -and
            -not (($component[0] + "/" + $component[1] + ".exe") -in $paths)) {
            throw "Release staging tree is missing $($component[0]) entry point."
        }
    }
    foreach ($path in $paths) {
        $rootName = ($path -split '/')[0]
        if ($rootName -notin @("web", "crawler", "db", "release.json")) {
            throw "Unexpected release root entry '$rootName'."
        }
        $reason = Get-ForbiddenPackagePathReason -RelativePath $path
        if ($reason) { throw "Forbidden package path '$path': $reason." }
    }
    foreach ($file in $files | Where-Object Name -Like "appsettings*.json") {
        Assert-SafeConfigurationJson -Name (Get-NormalizedRelativePath $PackageRoot $file.FullName) -Json (Get-Content $file.FullName -Raw)
    }
    $textExtensions = @(".json", ".xml", ".config", ".txt", ".md", ".ps1", ".sql", ".yml", ".yaml")
    foreach ($file in $files | Where-Object { $_.Extension.ToLowerInvariant() -in $textExtensions }) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text -like "*$RepositoryRoot*") {
            throw "Package file '$($file.Name)' contains a developer-machine absolute path."
        }
        if ($file.Extension.ToLowerInvariant() -in @(".json", ".xml", ".config", ".yml", ".yaml")) {
            Assert-NoPlaintextSecrets -Name (Get-NormalizedRelativePath $PackageRoot $file.FullName) -Text $text
        }
    }
    $metadata = Get-Content -LiteralPath (Join-Path $PackageRoot "release.json") -Raw | ConvertFrom-Json
    Assert-ReleaseMetadata $metadata $Version $Commit $ExpectedSchemaVersion $MigrationNames
}

function New-OrderedZipArchive {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][DateTimeOffset]$EntryTimestamp)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $filesByPath = @{}
    foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -File -Recurse) {
        $filesByPath[(Get-NormalizedRelativePath $SourceRoot $file.FullName)] = $file.FullName
    }
    $relativePaths = [string[]]@($filesByPath.Keys)
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($relativePath in $relativePaths) {
                $entry = $archive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $EntryTimestamp
                $input = [IO.File]::OpenRead($filesByPath[$relativePath])
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Assert-ReleaseArchive {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][int]$ExpectedSchemaVersion,
        [Parameter(Mandatory)][string[]]$MigrationNames)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $paths = @($archive.Entries | ForEach-Object FullName)
        if (@($paths | Sort-Object -Unique).Count -ne $paths.Count) { throw "Release archive contains duplicate entries." }
        if (@($paths | Where-Object { $_ -match '\\' }).Count -gt 0) { throw "Release archive contains non-normalized separators." }
        foreach ($path in $paths) {
            $rootName = ($path -split '/')[0]
            if ($rootName -notin @("web", "crawler", "db", "release.json")) { throw "Unexpected archive root entry '$rootName'." }
            $reason = Get-ForbiddenPackagePathReason $path
            if ($reason) { throw "Forbidden archive path '$path': $reason." }
        }
        foreach ($requiredPath in @("db/migrations/0001_baseline.sql", "db/scripts/bootstrap-schema-version.sql", "db/scripts/provision-database-runtime-roles.ps1", "release.json")) {
            if ($requiredPath -notin $paths) { throw "Release archive is missing '$requiredPath'." }
        }
        foreach ($entryPoint in @(@("web", "PriceCrawler.Web"), @("crawler", "PriceCrawler.Worker"))) {
            if (($entryPoint[0] + "/" + $entryPoint[1] + ".dll") -notin $paths -and
                ($entryPoint[0] + "/" + $entryPoint[1] + ".exe") -notin $paths) {
                throw "Release archive is missing $($entryPoint[0]) entry point."
            }
        }
        $releaseEntry = $archive.GetEntry("release.json")
        $reader = [IO.StreamReader]::new($releaseEntry.Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json }
        finally { $reader.Dispose() }
        Assert-ReleaseMetadata $metadata $Version $Commit $ExpectedSchemaVersion $MigrationNames

        foreach ($entry in $archive.Entries | Where-Object { $_.Name -like "appsettings*.json" }) {
            $reader = [IO.StreamReader]::new($entry.Open())
            try { Assert-SafeConfigurationJson -Name $entry.FullName -Json $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..")).Path
$solutionPath = Join-Path $repositoryRoot "PriceCrawler.sln"
$versionProjectPath = Join-Path $repositoryRoot "PriceCrawler.Application\PriceCrawler.Application.csproj"
$webProjectPath = Join-Path $repositoryRoot "PriceCrawler.Web\PriceCrawler.Web.csproj"
$workerProjectPath = Join-Path $repositoryRoot "PriceCrawler.Worker\PriceCrawler.Worker.csproj"
$databaseSchemaContractPath = Join-Path $repositoryRoot "PriceCrawler.Infrastructure\Persistence\DatabaseSchema.cs"
$databaseMigrationsPath = Join-Path $repositoryRoot "db\migrations"
$databaseScriptsPath = Join-Path $repositoryRoot "db\scripts"
$databaseReadmePath = Join-Path $repositoryRoot "db\README.md"
$runtimeRoleProvisioningScriptPath = Join-Path $repositoryRoot "scripts\provision-database-runtime-roles.ps1"

Write-Step "Validating repository and database release inputs"
foreach ($requiredPath in @($solutionPath, $versionProjectPath, $webProjectPath, $workerProjectPath, $databaseSchemaContractPath, $runtimeRoleProvisioningScriptPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Required file not found: $requiredPath" }
}
$expectedDatabaseSchemaVersion = Get-ExpectedDatabaseSchemaVersion $databaseSchemaContractPath
$migrationInventory = Get-DatabaseMigrationInventory $databaseMigrationsPath $databaseScriptsPath $expectedDatabaseSchemaVersion
if ($ValidatePackageInputsOnly) {
    Write-Host "Release package inputs are valid. SchemaVersion=$expectedDatabaseSchemaVersion; Migrations=$($migrationInventory.FileNames -join ',')"
    return
}

Push-Location $repositoryRoot
try {
    Invoke-NativeCommand "Git repository validation" { git rev-parse --is-inside-work-tree | Out-Null }
    if (-not $AllowDirtyWorkingTree) {
        $workingTreeStatus = @(git status --porcelain)
        if ($LASTEXITCODE -ne 0) { throw "Could not inspect Git working tree." }
        if ($workingTreeStatus.Count -gt 0) {
            throw "Working tree contains uncommitted changes. Commit them or explicitly use -AllowDirtyWorkingTree for a non-final local build."
        }
    }

    $canonical = Get-CanonicalBuildVersion $versionProjectPath
    $gitCommit = (git rev-parse HEAD).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-f]{40}$') { throw "Could not resolve exact Git commit." }
    if ($canonical.Commit -ne $gitCommit) { throw "NBGV commit does not match git rev-parse HEAD." }
    $versionSource = "Nerdbank.GitVersioning"
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $canonical.Version }
    else { $Version = Normalize-Version $Version; $versionSource = "explicit" }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot "artifacts\releases"
    }
    elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
    }
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $archiveName = "PriceCrawler-$Version.zip"
    $archivePath = Join-Path $OutputDirectory $archiveName
    $checksumPath = "$archivePath.sha256"
    if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
        if (-not $ReplaceExistingArtifact) {
            throw "Release artifact already exists. Refusing to overwrite '$archivePath'. Use -ReplaceExistingArtifact only for an explicitly approved local replacement."
        }
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Version: $Version ($versionSource)"
    Write-Host "Commit: $gitCommit"
    Write-Host "Schema: $expectedDatabaseSchemaVersion -> $($migrationInventory.TargetVersion)"
    Write-Host "Archive: $archivePath"
    if (-not $SkipTests) {
        Write-Step "Restoring and testing solution"
        Invoke-NativeCommand "dotnet restore" { dotnet restore $solutionPath }
        Invoke-NativeCommand "dotnet test" { dotnet test $solutionPath --configuration $Configuration --no-restore }
    }
    else { Write-Step "Skipping tests by explicit -SkipTests request" }

    $publishRoot = Join-Path $repositoryRoot "artifacts\publish"
    $webPublishPath = Join-Path $publishRoot "web"
    $crawlerPublishPath = Join-Path $publishRoot "crawler"
    $packageRoot = Join-Path $repositoryRoot "artifacts\release-staging"
    foreach ($path in @($publishRoot, $packageRoot)) {
        $resolved = [IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts")), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe generated-directory path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $webPublishPath, $crawlerPublishPath, $packageRoot, $OutputDirectory -Force | Out-Null

    Write-Step "Publishing Web and Crawler"
    Invoke-NativeCommand "PriceCrawler.Web publish" { dotnet publish $webProjectPath --configuration $Configuration --output $webPublishPath --no-restore }
    Invoke-NativeCommand "PriceCrawler.Worker publish" { dotnet publish $workerProjectPath --configuration $Configuration --output $crawlerPublishPath --no-restore }
    $webFileCount = Assert-DirectoryNotEmpty $webPublishPath "Web"
    $crawlerFileCount = Assert-DirectoryNotEmpty $crawlerPublishPath "Crawler"
    Assert-PublishEntryPoint $webPublishPath "PriceCrawler.Web" "Web"
    Assert-PublishEntryPoint $crawlerPublishPath "PriceCrawler.Worker" "Crawler"

    Write-Step "Preparing and validating release staging tree"
    $packageWebPath = Join-Path $packageRoot "web"
    $packageCrawlerPath = Join-Path $packageRoot "crawler"
    $packageDatabaseMigrationsPath = Join-Path $packageRoot "db\migrations"
    $packageDatabaseScriptsPath = Join-Path $packageRoot "db\scripts"
    Copy-Item $webPublishPath $packageWebPath -Recurse -Force
    Copy-Item $crawlerPublishPath $packageCrawlerPath -Recurse -Force
    New-Item -ItemType Directory -Path $packageDatabaseMigrationsPath, $packageDatabaseScriptsPath -Force | Out-Null
    foreach ($migration in $migrationInventory.Files) { Copy-Item $migration.FullName $packageDatabaseMigrationsPath -Force }
    Copy-Item $migrationInventory.BootstrapPath $packageDatabaseScriptsPath -Force
    Copy-Item $runtimeRoleProvisioningScriptPath (Join-Path $packageDatabaseScriptsPath "provision-database-runtime-roles.ps1") -Force
    if (Test-Path $databaseReadmePath -PathType Leaf) { Copy-Item $databaseReadmePath (Join-Path $packageRoot "db") -Force }
    Set-SafePackagedConfiguration $packageWebPath
    Set-SafePackagedConfiguration $packageCrawlerPath

    $builtAtUtc = [DateTimeOffset]::UtcNow
    $builtAtUtc = [DateTimeOffset]::new($builtAtUtc.Year, $builtAtUtc.Month, $builtAtUtc.Day, $builtAtUtc.Hour, $builtAtUtc.Minute, $builtAtUtc.Second, [TimeSpan]::Zero)
    $releaseInfo = [ordered]@{
        product = "PriceCrawler"
        version = $Version
        commit = $gitCommit
        builtAtUtc = $builtAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
        database = [ordered]@{
            minimumSchemaVersion = $expectedDatabaseSchemaVersion
            targetSchemaVersion = $migrationInventory.TargetVersion
            migrations = @($migrationInventory.FileNames)
        }
        components = [ordered]@{ web = $true; crawler = $true; database = $true }
        build = [ordered]@{ configuration = $Configuration; testsSkipped = [bool]$SkipTests; versionSource = $versionSource }
    }
    $releaseJson = $releaseInfo | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $packageRoot "release.json"), $releaseJson + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Assert-ReleaseStagingTree $packageRoot $Version $gitCommit $expectedDatabaseSchemaVersion $migrationInventory.FileNames $repositoryRoot

    Write-Step "Creating and validating ordered ZIP archive"
    New-OrderedZipArchive $packageRoot $archivePath $builtAtUtc
    if ((Get-Item $archivePath).Length -le 0) { throw "Release archive is empty." }
    Assert-ReleaseArchive $archivePath $Version $gitCommit $expectedDatabaseSchemaVersion $migrationInventory.FileNames

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$hash  $archiveName`n", [Text.UTF8Encoding]::new($false))
    $recordedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0]
    if ($recordedHash -ne $hash) { throw "Generated SHA-256 sidecar does not match the ZIP." }

    $archive = Get-Item $archivePath
    Write-Step "Release created successfully"
    Write-Host "Path: $($archive.FullName)" -ForegroundColor Green
    Write-Host "Checksum: $checksumPath"
    Write-Host "SHA256: $hash"
    Write-Host "Size: $($archive.Length) bytes"
    Write-Host "Version: $Version"
    Write-Host "Commit: $gitCommit"
    Write-Host "SchemaVersion: $expectedDatabaseSchemaVersion"
    Write-Host "Files: Web=$webFileCount; Crawler=$crawlerFileCount; Migrations=$($migrationInventory.Files.Count)"
}
finally {
    $stagingPath = Join-Path $repositoryRoot "artifacts\release-staging"
    if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue }
    Pop-Location
}
