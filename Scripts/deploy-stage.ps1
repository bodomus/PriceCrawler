<#
.SYNOPSIS
Deploys a validated PriceCrawler release package to Stage.

.DESCRIPTION
Validates the release ZIP and external Stage configuration, creates a verified
database backup, optionally refreshes Stage from Development, applies only
forward migrations, safely switches the active application files, and starts
Web before Worker. Production-like database names are always rejected.

Passwords are intentionally not accepted. Configure PostgreSQL authentication
through PGPASSWORD/.pgpass/a secret store and runtime connection strings through
external Stage configuration.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$StageRoot,
    [string]$StageDatabase = "varprice_stage",
    [string]$DevelopmentDatabase = "varprice",
    [string]$ProductionDatabase = "varprice_prod",

    [ValidateSet("Auto", "Native", "Docker")]
    [string]$ToolMode = "Auto",
    [string]$DockerContainer,
    [string]$PostgresHost = "localhost",
    [ValidateRange(1, 65535)]
    [int]$PostgresPort = 5432,
    [string]$DeployDatabaseUser,

    [string]$WebUrl,
    [string]$HealthPath = "/health",
    [string]$WebConfigPath,
    [string]$WorkerConfigPath,
    [string[]]$WebArguments = @(),
    [string[]]$WorkerArguments,

    [switch]$RefreshDatabaseFromDevelopment,
    [switch]$ReplaceExistingRelease,
    [switch]$ValidatePackageOnly,
    [switch]$WhatIf,

    [ValidateRange(1, 600)]
    [int]$ProcessStopTimeoutSeconds = 15,
    [ValidateRange(1, 600)]
    [int]$WebStartupTimeoutSeconds = 30,
    [ValidateRange(1, 600)]
    [int]$HealthTimeoutSeconds = 30,
    [ValidateRange(1, 300)]
    [int]$WorkerStabilizationSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PhaseRecords = [Collections.Generic.List[object]]::new()
$script:AppliedMigrations = [Collections.Generic.List[string]]::new()
$script:LogPath = $null
$script:ReportPath = $null
$script:LockPath = $null
$script:LockOwned = $false
$script:Package = $null
$script:Backup = $null
$script:SchemaBefore = $null
$script:SchemaAfter = $null
$script:WebProcess = $null
$script:WorkerProcess = $null
$script:CurrentSwitched = $false
$script:PreviousVersion = $null
$script:FinalResult = "Failed"
$script:FailureMessage = $null
$script:ResolvedToolMode = $null
$script:DeploymentStartedAtUtc = [DateTimeOffset]::UtcNow

function Get-SafeMessage {
    param([AllowEmptyString()][string]$Message)
    $safe = [string]$Message
    $safe = [regex]::Replace($safe, '(?i)(password|pwd)\s*=\s*[^;\s]+', '$1=<redacted>')
    $safe = [regex]::Replace($safe, '(?i)(token|secret|api[_-]?key)\s*[=:]\s*[^;\s]+', '$1=<redacted>')
    return $safe
}

function Write-DeployLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")][string]$Level = "INFO"
    )
    $record = "{0} [{1}] {2}" -f [DateTimeOffset]::UtcNow.ToString("o"), $Level, (Get-SafeMessage $Message)
    Write-Host $record
    if ($script:LogPath -and -not $WhatIf) {
        Add-Content -LiteralPath $script:LogPath -Value $record -Encoding UTF8
    }
}

function Invoke-DeploymentPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )
    $started = [DateTimeOffset]::UtcNow
    Write-DeployLog "Phase started: $Name"
    try {
        $value = & $Action
        $finished = [DateTimeOffset]::UtcNow
        $script:PhaseRecords.Add([pscustomobject]@{
            name = $Name
            startedAtUtc = $started.ToString("o")
            finishedAtUtc = $finished.ToString("o")
            durationMs = [long]($finished - $started).TotalMilliseconds
            result = "Success"
        })
        Write-DeployLog "Phase completed: $Name; DurationMs=$([long]($finished - $started).TotalMilliseconds)"
        return $value
    }
    catch {
        $finished = [DateTimeOffset]::UtcNow
        $script:PhaseRecords.Add([pscustomobject]@{
            name = $Name
            startedAtUtc = $started.ToString("o")
            finishedAtUtc = $finished.ToString("o")
            durationMs = [long]($finished - $started).TotalMilliseconds
            result = "Failed"
        })
        Write-DeployLog "Phase failed: $Name; DurationMs=$([long]($finished - $started).TotalMilliseconds); Error=$($_.Exception.Message)" "ERROR"
        throw
    }
}

function Assert-SafePostgresIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$Name)
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') {
        throw "$Name '$Value' is not a safe PostgreSQL identifier."
    }
}

function Test-ProductionLikeName {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value -match '(?i)(^|[_-])(prod|production)([_-]|$)'
}

function Quote-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Quote-SqlLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$BasePath)
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $BasePath $Path }
    return [IO.Path]::GetFullPath($Path)
}

function Assert-PathWithinRoot {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\', '/'))
    $pathFull = [IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$pathFull' is outside Stage root '$rootFull'."
    }
}

function Get-ForbiddenArchivePathReason {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').ToLowerInvariant()
    if ($normalized.StartsWith('/') -or $normalized -match '^[a-z]:' -or $normalized -match '(^|/)\.\.(/|$)') { return "absolute or traversal path" }
    if ($normalized -match '(^|/)(\.git|\.idea|\.vs|graphify-out|\.code-review-graph|testresults|coverage|backups?|logs?|artifacts)(/|$)') { return "repository, graph, test, backup, log, or artifact directory" }
    if ($normalized -match '(^|/)(\.env|\.pgpass)(/|$)' -or $normalized -match '\.(dump|backup|bak|log|key|pem|pfx)$') { return "secret, dump, backup, log, or private-key file" }
    if ($normalized -match '(^|/)appsettings\.(development|test)\.json$') { return "Development/Test configuration" }
    return $null
}

function Read-ZipEntryText {
    param([Parameter(Mandatory = $true)]$Entry)
    $reader = [IO.StreamReader]::new($Entry.Open(), [Text.Encoding]::UTF8, $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Assert-ReleasePackage {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Release ZIP was not found: $Path" }
    if ([IO.Path]::GetExtension($Path) -ne ".zip") { throw "Package must be a .zip archive: $Path" }

    $sidecarPath = "$Path.sha256"
    if (-not (Test-Path -LiteralPath $sidecarPath -PathType Leaf)) { throw "Required SHA-256 sidecar was not found: $sidecarPath" }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = (Get-Content -LiteralPath $sidecarPath -Raw).Trim()
    if ($sidecar -notmatch '^(?<hash>[0-9a-fA-F]{64})(?:\s+\*?(?<name>[^\r\n]+))?$') { throw "SHA-256 sidecar has invalid format." }
    if ($matches["hash"].ToLowerInvariant() -ne $actualHash) { throw "Release ZIP SHA-256 does not match its sidecar." }
    if ($matches["name"] -and $matches["name"].Trim() -ne [IO.Path]::GetFileName($Path)) { throw "SHA-256 sidecar names a different archive." }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if ($archive.Entries.Count -eq 0) { throw "Release ZIP is empty." }
        if ($archive.Entries.Count -gt 20000) { throw "Release ZIP contains too many entries." }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $paths = [Collections.Generic.List[string]]::new()
        [long]$totalUncompressed = 0
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace('\', '/')
            $reason = Get-ForbiddenArchivePathReason $entryPath
            if ($reason) { throw "Forbidden ZIP entry '$entryPath': $reason." }
            if (-not $seen.Add($entryPath)) { throw "Duplicate ZIP entry '$entryPath'." }
            $totalUncompressed += [long]$entry.Length
            if ($totalUncompressed -gt 2147483648) { throw "Release ZIP expands beyond the 2 GiB safety limit." }
            if (-not $entryPath.EndsWith('/')) { $paths.Add($entryPath) }
        }

        $roots = @($paths | ForEach-Object { ($_ -split '/')[0] } | Sort-Object -Unique)
        foreach ($root in $roots) { if ($root -notin @("web", "crawler", "db", "release.json")) { throw "Unexpected release root '$root'." } }
        foreach ($prefix in @("web/", "crawler/", "db/migrations/")) {
            if (@($paths | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) }).Count -eq 0) { throw "Release ZIP is missing '$prefix'." }
        }
        if ("db/scripts/provision-database-runtime-roles.ps1" -notin $paths) { throw "Release ZIP is missing Stage runtime-role provisioning support." }
        $provisioningContract = Read-ZipEntryText $archive.GetEntry("db/scripts/provision-database-runtime-roles.ps1")
        if ($provisioningContract -notmatch '\[switch\]\s*\$StageOnly' -or $provisioningContract -notmatch 'ExpectedSchemaVersion') { throw "Packaged runtime-role provisioning does not support safe Stage-only deployment." }
        if (@($paths | Where-Object { $_ -eq "release.json" }).Count -ne 1) { throw "Release ZIP must contain exactly one root release.json." }
        if ("web/PriceCrawler.Web.exe" -notin $paths -and "web/PriceCrawler.Web.dll" -notin $paths) { throw "Release ZIP is missing the Web entry point." }
        if ("crawler/PriceCrawler.Worker.exe" -notin $paths -and "crawler/PriceCrawler.Worker.dll" -notin $paths) { throw "Release ZIP is missing the Worker entry point." }

        try { $metadata = (Read-ZipEntryText $archive.GetEntry("release.json")) | ConvertFrom-Json }
        catch { throw "release.json is invalid JSON. $($_.Exception.Message)" }
        if ([string]$metadata.product -ne "PriceCrawler") { throw "release.json product must be PriceCrawler." }
        if ([string]$metadata.version -notmatch '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') { throw "release.json version is missing or invalid." }
        if ([string]$metadata.commit -notmatch '^[0-9a-fA-F]{40}$') { throw "release.json commit must be an exact 40-character Git commit." }
        $builtAt = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact([string]$metadata.builtAtUtc, "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$builtAt)) { throw "release.json builtAtUtc must be normalized UTC." }
        $minimum = [int]$metadata.database.minimumSchemaVersion
        $target = [int]$metadata.database.targetSchemaVersion
        if ($minimum -lt 1 -or $target -lt 1 -or $minimum -gt $target) { throw "release.json database schema range is invalid." }
        if (-not $metadata.components.web -or -not $metadata.components.crawler -or -not $metadata.components.database) { throw "release.json component declarations do not match the required package." }

        $migrationRecords = [Collections.Generic.List[object]]::new()
        foreach ($migrationPath in @($paths | Where-Object { $_ -match '^db/migrations/[^/]+\.sql$' } | Sort-Object)) {
            $fileName = [IO.Path]::GetFileName($migrationPath)
            if ($fileName -notmatch '^(?<version>\d{4})_[a-z0-9_]+\.sql$') { throw "Invalid migration filename '$fileName'." }
            $migrationRecords.Add([pscustomobject]@{ version = [int]$matches["version"]; fileName = $fileName; entryPath = $migrationPath })
        }
        if ($migrationRecords.Count -eq 0) { throw "Release ZIP contains no numbered migrations." }
        $duplicates = @($migrationRecords | Group-Object version | Where-Object Count -gt 1)
        if ($duplicates.Count -gt 0) { throw "Duplicate migration version(s): $((@($duplicates.Name) -join ', '))." }
        for ($index = 0; $index -lt $migrationRecords.Count; $index++) {
            if ($migrationRecords[$index].version -ne ($index + 1)) { throw "Migration sequence must be contiguous from 0001." }
        }
        if ($migrationRecords[-1].version -ne $target) { throw "Packaged migration target does not match release.json target schema version." }
        $declared = @($metadata.database.migrations | ForEach-Object { [string]$_ })
        $actualNames = @($migrationRecords | ForEach-Object fileName)
        if (($declared -join '|') -ne ($actualNames -join '|')) { throw "release.json migration inventory does not match the ZIP." }

        return [pscustomobject]@{
            Path = (Resolve-Path -LiteralPath $Path).Path
            SidecarPath = (Resolve-Path -LiteralPath $sidecarPath).Path
            Sha256 = $actualHash
            Version = [string]$metadata.version
            Commit = ([string]$metadata.commit).ToLowerInvariant()
            BuiltAtUtc = [string]$metadata.builtAtUtc
            MinimumSchemaVersion = $minimum
            TargetSchemaVersion = $target
            Migrations = @($migrationRecords)
            EntryCount = $paths.Count
        }
    }
    finally { $archive.Dispose() }
}

function Get-ConnectionStringPart {
    param([Parameter(Mandatory = $true)][string]$ConnectionString, [Parameter(Mandatory = $true)][string]$Name)
    return [regex]::Match($ConnectionString, "(?i)(?:^|;)\s*$Name\s*=\s*(?<value>[^;]*)").Groups["value"].Value.Trim()
}

function Assert-StageConfiguration {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedRole,
        [Parameter(Mandatory = $true)][string]$Component
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Component Stage configuration was not found: $Path" }
    try { $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "$Component Stage configuration is invalid JSON. $($_.Exception.Message)" }
    if ([string]$configuration.DatabaseSchema.StartupMode -ne "ValidateOnly") { throw "$Component Stage configuration must use DatabaseSchema.StartupMode=ValidateOnly." }
    $connectionString = [string]$configuration.ConnectionStrings.Postgres
    if ([string]::IsNullOrWhiteSpace($connectionString) -or $connectionString -match '<[^>]+>') { throw "$Component Stage connection string is missing or contains unresolved placeholders." }
    $database = Get-ConnectionStringPart $connectionString "Database"
    $user = Get-ConnectionStringPart $connectionString "User(?:name| Id)?"
    if ($database -ne $StageDatabase) { throw "$Component configuration targets database '$database'; expected Stage database '$StageDatabase'." }
    if ($database -eq $DevelopmentDatabase -or $database -eq $ProductionDatabase -or (Test-ProductionLikeName $database)) { throw "$Component configuration does not target an approved Stage database." }
    if ($user -ne $ExpectedRole) { throw "$Component configuration must use runtime role '$ExpectedRole', not '$user'." }
    return [pscustomobject]@{ Path = (Resolve-Path -LiteralPath $Path).Path; Database = $database; User = $user; StartupMode = "ValidateOnly" }
}

function Invoke-TextCommand {
    param([Parameter(Mandatory = $true)][string]$Executable, [Parameter(Mandatory = $true)][string[]]$Arguments, [Parameter(Mandatory = $true)][string]$Description)
    $output = @(& $Executable @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$Description failed with exit code $exitCode. $((Get-SafeMessage (($output | Out-String).Trim())))" }
    return ($output | Out-String).Trim()
}

function Resolve-PostgresToolMode {
    $tools = @("psql", "pg_dump", "pg_restore", "createdb", "dropdb")
    $native = @($tools | Where-Object { -not (Get-Command $_ -ErrorAction SilentlyContinue) }).Count -eq 0
    if ($ToolMode -eq "Native") {
        if (-not $native) { throw "Native PostgreSQL tools are missing: $($tools -join ', ')." }
        return "Native"
    }
    if ($ToolMode -eq "Docker" -or (-not $native -and $DockerContainer)) {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw "Docker CLI was not found." }
        if ([string]::IsNullOrWhiteSpace($DockerContainer)) { throw "-DockerContainer is required for Docker tool mode." }
        $running = Invoke-TextCommand docker @("inspect", "--format", "{{.State.Running}}", $DockerContainer) "Docker container validation"
        if ($running.Trim() -ne "true") { throw "Docker container '$DockerContainer' is not running." }
        foreach ($tool in $tools) { Invoke-TextCommand docker @("exec", $DockerContainer, "sh", "-c", "command -v $tool") "PostgreSQL tool check for $tool" | Out-Null }
        return "Docker"
    }
    if ($native) { return "Native" }
    throw "PostgreSQL tools were not found. Select Native or an explicit Docker container."
}

function Get-PsqlArguments {
    param([Parameter(Mandatory = $true)][string]$Database, [Parameter(Mandatory = $true)][string]$Sql)
    if ($script:ResolvedToolMode -eq "Docker") { return @("exec", "-i", $DockerContainer, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-A", "-t", "--username", $DeployDatabaseUser, "--dbname", $Database, "--command", $Sql) }
    return @("-X", "-v", "ON_ERROR_STOP=1", "-A", "-t", "--host", $PostgresHost, "--port", $PostgresPort.ToString(), "--username", $DeployDatabaseUser, "--dbname", $Database, "--command", $Sql)
}

function Invoke-PsqlQuery {
    param([Parameter(Mandatory = $true)][string]$Database, [Parameter(Mandatory = $true)][string]$Sql, [string]$Description = "PostgreSQL query")
    $executable = if ($script:ResolvedToolMode -eq "Docker") { "docker" } else { "psql" }
    return Invoke-TextCommand $executable (Get-PsqlArguments $Database $Sql) $Description
}

function Invoke-PsqlFile {
    param([Parameter(Mandatory = $true)][string]$Database, [Parameter(Mandatory = $true)][string]$Path)
    if ($script:ResolvedToolMode -eq "Docker") {
        $sql = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
        $arguments = @("exec", "-i", $DockerContainer, "psql", "-X", "-v", "ON_ERROR_STOP=1", "--username", $DeployDatabaseUser, "--dbname", $Database)
        $output = @($sql | & docker @arguments 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Applying migration '$([IO.Path]::GetFileName($Path))' failed. $((Get-SafeMessage (($output | Out-String).Trim())))" }
        return
    }
    Invoke-TextCommand psql @("-X", "-v", "ON_ERROR_STOP=1", "--host", $PostgresHost, "--port", $PostgresPort.ToString(), "--username", $DeployDatabaseUser, "--dbname", $Database, "--file", $Path) "Applying migration '$([IO.Path]::GetFileName($Path))'" | Out-Null
}

function Test-DatabaseExists {
    param([Parameter(Mandatory = $true)][string]$Database)
    return (Invoke-PsqlQuery "postgres" "select exists(select 1 from pg_database where datname=$(Quote-SqlLiteral $Database));" "Database existence check for '$Database'").Trim() -eq "t"
}

function Get-SchemaVersion {
    param([Parameter(Mandatory = $true)][string]$Database)
    $hasMetadata = (Invoke-PsqlQuery $Database "select to_regclass('public.schema_version') is not null;" "Schema metadata check for '$Database'").Trim()
    if ($hasMetadata -ne "t") { throw "Database '$Database' has no public.schema_version. Provision/bootstrap it explicitly; deploy will not repair metadata." }
    $value = (Invoke-PsqlQuery $Database "select coalesce(max(version),0) from public.schema_version;" "Schema version check for '$Database'").Trim()
    $version = 0
    if (-not [int]::TryParse($value, [ref]$version) -or $version -lt 1) { throw "Database '$Database' returned invalid schema version '$value'." }
    return $version
}

function Assert-DumpFile {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Kind)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -le 0) { throw "$Kind dump is missing or empty: $Path" }
    if ($script:ResolvedToolMode -eq "Docker") {
        $remote = "/tmp/pricecrawler-verify-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand docker @("cp", $Path, "${DockerContainer}:$remote") "Copying dump for verification" | Out-Null
            Invoke-TextCommand docker @("exec", $DockerContainer, "pg_restore", "--list", $remote) "Verifying $Kind dump" | Out-Null
        }
        finally { & docker exec $DockerContainer rm -f $remote 2>$null | Out-Null }
    }
    else { Invoke-TextCommand pg_restore @("--list", $Path) "Verifying $Kind dump" | Out-Null }
    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{ path = $item.FullName; sizeBytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}

function New-LogicalDump {
    param([Parameter(Mandatory = $true)][string]$Database, [Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Kind)
    if ($script:ResolvedToolMode -eq "Docker") {
        $remote = "/tmp/pricecrawler-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand docker @("exec", $DockerContainer, "pg_dump", "--username", $DeployDatabaseUser, "--dbname", $Database, "--format=custom", "--no-owner", "--no-privileges", "--file", $remote) "Creating $Kind dump" | Out-Null
            Invoke-TextCommand docker @("exec", $DockerContainer, "pg_restore", "--list", $remote) "Verifying remote $Kind dump" | Out-Null
            Invoke-TextCommand docker @("cp", "${DockerContainer}:$remote", $Path) "Copying $Kind dump" | Out-Null
        }
        finally { & docker exec $DockerContainer rm -f $remote 2>$null | Out-Null }
    }
    else {
        Invoke-TextCommand pg_dump @("--host", $PostgresHost, "--port", $PostgresPort.ToString(), "--username", $DeployDatabaseUser, "--dbname", $Database, "--format=custom", "--no-owner", "--no-privileges", "--file", $Path) "Creating $Kind dump" | Out-Null
    }
    return Assert-DumpFile $Path $Kind
}

function Remove-DatabaseForStageRefresh {
    $literal = Quote-SqlLiteral $StageDatabase
    Invoke-PsqlQuery "postgres" "select pg_terminate_backend(pid) from pg_stat_activity where datname=$literal and pid<>pg_backend_pid();" "Terminating Stage database sessions" | Out-Null
    if ($script:ResolvedToolMode -eq "Docker") {
        Invoke-TextCommand docker @("exec", $DockerContainer, "dropdb", "--username", $DeployDatabaseUser, $StageDatabase) "Dropping Stage for explicit refresh" | Out-Null
        Invoke-TextCommand docker @("exec", $DockerContainer, "createdb", "--username", $DeployDatabaseUser, $StageDatabase) "Recreating Stage for explicit refresh" | Out-Null
    }
    else {
        $base = @("--host", $PostgresHost, "--port", $PostgresPort.ToString(), "--username", $DeployDatabaseUser)
        Invoke-TextCommand dropdb ($base + @($StageDatabase)) "Dropping Stage for explicit refresh" | Out-Null
        Invoke-TextCommand createdb ($base + @($StageDatabase)) "Recreating Stage for explicit refresh" | Out-Null
    }
}

function Restore-LogicalDump {
    param([Parameter(Mandatory = $true)][string]$Database, [Parameter(Mandatory = $true)][string]$Path)
    if ($script:ResolvedToolMode -eq "Docker") {
        $remote = "/tmp/pricecrawler-restore-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand docker @("cp", $Path, "${DockerContainer}:$remote") "Copying refresh dump" | Out-Null
            Invoke-TextCommand docker @("exec", $DockerContainer, "pg_restore", "--username", $DeployDatabaseUser, "--dbname", $Database, "--exit-on-error", "--no-owner", "--no-privileges", $remote) "Restoring explicit Development-to-Stage refresh" | Out-Null
        }
        finally { & docker exec $DockerContainer rm -f $remote 2>$null | Out-Null }
    }
    else { Invoke-TextCommand pg_restore @("--host", $PostgresHost, "--port", $PostgresPort.ToString(), "--username", $DeployDatabaseUser, "--dbname", $Database, "--exit-on-error", "--no-owner", "--no-privileges", $Path) "Restoring explicit Development-to-Stage refresh" | Out-Null }
}

function Get-ProcessRecord {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { Write-DeployLog "Ignoring invalid stale PID record: $Path" "WARN"; return $null }
}

function Stop-RecordedStageProcess {
    param([Parameter(Mandatory = $true)][string]$PidPath, [Parameter(Mandatory = $true)][string]$Component)
    $record = Get-ProcessRecord $PidPath
    if (-not $record) { Remove-Item -LiteralPath $PidPath -Force -ErrorAction SilentlyContinue; return }
    $process = Get-Process -Id ([int]$record.pid) -ErrorAction SilentlyContinue
    if (-not $process) { Write-DeployLog "$Component PID record is stale; process $($record.pid) is absent." "WARN"; Remove-Item -LiteralPath $PidPath -Force; return }
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" -ErrorAction SilentlyContinue
    $expectedPath = [IO.Path]::GetFullPath([string]$record.executablePath)
    $actualPath = if ($cim) { [string]$cim.ExecutablePath } else { $null }
    $commandLine = if ($cim) { [string]$cim.CommandLine } else { "" }
    if ([string]::IsNullOrWhiteSpace($actualPath) -or [IO.Path]::GetFullPath($actualPath) -ne $expectedPath -or $commandLine.IndexOf($StageRoot, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Refusing to stop $Component PID $($process.Id): executable/command line is not owned by the expected Stage directory."
    }
    Write-DeployLog "Stopping $Component PID=$($process.Id)."
    try { $process.CloseMainWindow() | Out-Null } catch { }
    try { Stop-Process -Id $process.Id -ErrorAction Stop } catch { }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ProcessStopTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline -and (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 200 }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Write-DeployLog "$Component did not stop gracefully; controlled force termination follows." "WARN"
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
    }
    Remove-Item -LiteralPath $PidPath -Force -ErrorAction SilentlyContinue
}

function Wait-PortReleased {
    param([Parameter(Mandatory = $true)][int]$Port)
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { return }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($ProcessStopTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Web port $Port was not released after stopping Stage processes."
}

function Copy-DirectoryContents {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) { Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force }
}

function Start-StageComponent {
    param(
        [Parameter(Mandatory = $true)][string]$ComponentRoot,
        [Parameter(Mandatory = $true)][string]$AssemblyName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$StdoutPath,
        [Parameter(Mandatory = $true)][string]$StderrPath,
        [Parameter(Mandatory = $true)][string]$PidPath,
        [string]$Urls
    )
    $exe = Join-Path $ComponentRoot "$AssemblyName.exe"
    $dll = Join-Path $ComponentRoot "$AssemblyName.dll"
    if (Test-Path -LiteralPath $exe -PathType Leaf) { $filePath = $exe; $processArguments = @($Arguments) }
    elseif (Test-Path -LiteralPath $dll -PathType Leaf) { $filePath = (Get-Command dotnet -ErrorAction Stop).Source; $processArguments = @($dll) + @($Arguments) }
    else { throw "$AssemblyName entry point was not found in $ComponentRoot." }

    $environmentNames = @("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "DatabaseSchema__StartupMode", "ASPNETCORE_URLS")
    $saved = @{}
    foreach ($name in $environmentNames) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }
    try {
        [Environment]::SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Stage", "Process")
        [Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Stage", "Process")
        [Environment]::SetEnvironmentVariable("DatabaseSchema__StartupMode", "ValidateOnly", "Process")
        if ($Urls) { [Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", $Urls, "Process") }
        else { [Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", $null, "Process") }
        $process = Start-Process -FilePath $filePath -ArgumentList $processArguments -WorkingDirectory $ComponentRoot -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath -PassThru -WindowStyle Hidden
    }
    finally { foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $saved[$name], "Process") } }
    $record = [ordered]@{ pid = $process.Id; executablePath = [IO.Path]::GetFullPath($filePath); componentRoot = [IO.Path]::GetFullPath($ComponentRoot); startedAtUtc = [DateTimeOffset]::UtcNow.ToString("o"); version = $script:Package.Version }
    $record | ConvertTo-Json | Set-Content -LiteralPath $PidPath -Encoding UTF8
    Write-DeployLog "$AssemblyName started. PID=$($process.Id); Stdout=$StdoutPath; Stderr=$StderrPath"
    return $process
}

function Wait-WebPort {
    param([Parameter(Mandatory = $true)][int]$Port, [Parameter(Mandatory = $true)][int]$ProcessId)
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) { throw "Get-NetTCPConnection is required to verify listener ownership." }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($WebStartupTimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { throw "Web process PID $ProcessId exited before opening port $Port." }
        $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
        if ($listeners.Count -gt 0) {
            if (@($listeners | Where-Object OwningProcess -eq $ProcessId).Count -eq 0) { throw "Port $Port is owned by an unrelated process; expected Web PID $ProcessId." }
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Web port $Port did not enter Listen state within $WebStartupTimeoutSeconds seconds."
}

function Wait-WebHealth {
    param([Parameter(Mandatory = $true)][Uri]$Uri)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($HealthTimeoutSeconds)
    $lastError = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 300) {
                if ($response.Content) {
                    try { $body = $response.Content | ConvertFrom-Json; if ($null -ne $body.ok -and -not [bool]$body.ok) { throw "Health JSON reports ok=false." } } catch [ArgumentException] { }
                }
                return "Healthy"
            }
        }
        catch { $lastError = $_.Exception.Message }
        Start-Sleep -Milliseconds 500
    }
    throw "Web health endpoint did not become healthy. LastError=$(Get-SafeMessage $lastError)"
}

function Acquire-DeploymentLock {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        try { $existing = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json } catch { throw "Deployment lock exists but is invalid: $Path" }
        $currentHost = [Environment]::MachineName
        if ([string]$existing.host -ne $currentHost) { throw "Deployment lock belongs to host '$($existing.host)'; refusing automatic removal." }
        if (Get-Process -Id ([int]$existing.pid) -ErrorAction SilentlyContinue) { throw "Another Stage deployment is active. PID=$($existing.pid); Host=$($existing.host)." }
        Write-DeployLog "Removing confirmed stale deployment lock for absent PID $($existing.pid)." "WARN"
        Remove-Item -LiteralPath $Path -Force
    }
    $lock = [ordered]@{ pid = $PID; host = [Environment]::MachineName; createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o"); packagePath = $script:Package.Path; targetVersion = $script:Package.Version }
    $json = $lock | ConvertTo-Json
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes($json); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    $script:LockOwned = $true
}

function Write-DeploymentReport {
    if (-not $script:ReportPath -or $WhatIf) { return }
    $report = [ordered]@{
        environment = "Stage"
        result = $script:FinalResult
        startedAtUtc = $script:DeploymentStartedAtUtc.ToString("o")
        finishedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        version = if ($script:Package) { $script:Package.Version } else { $null }
        commit = if ($script:Package) { $script:Package.Commit } else { $null }
        packageSha256 = if ($script:Package) { $script:Package.Sha256 } else { $null }
        stageRoot = $StageRoot
        previousVersion = $script:PreviousVersion
        failure = if ($script:FailureMessage) { Get-SafeMessage $script:FailureMessage } else { $null }
        database = [ordered]@{
            name = $StageDatabase
            beforeSchemaVersion = $script:SchemaBefore
            targetSchemaVersion = if ($script:Package) { $script:Package.TargetSchemaVersion } else { $null }
            afterSchemaVersion = $script:SchemaAfter
            backupPath = if ($script:Backup) { $script:Backup.path } else { $null }
            backupSizeBytes = if ($script:Backup) { $script:Backup.sizeBytes } else { $null }
            backupSha256 = if ($script:Backup) { $script:Backup.sha256 } else { $null }
            refreshedFromDevelopment = [bool]$RefreshDatabaseFromDevelopment
            appliedMigrations = @($script:AppliedMigrations)
        }
        web = [ordered]@{ pid = if ($script:WebProcess) { $script:WebProcess.Id } else { $null }; url = $WebUrl; portReady = [bool]($script:FinalResult -eq "Success"); healthStatus = if ($script:FinalResult -eq "Success") { "Healthy" } else { "NotVerified" } }
        worker = [ordered]@{ pid = if ($script:WorkerProcess) { $script:WorkerProcess.Id } else { $null }; started = [bool]$script:WorkerProcess }
        phases = @($script:PhaseRecords)
        databaseRollbackAutomatic = $false
        productionTargetSupported = $false
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $script:ReportPath -Encoding UTF8
}

# Resolve repository and immutable inputs before any mutation.
$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..")).Path
$PackagePath = Resolve-FullPath $PackagePath $repositoryRoot
if ([string]::IsNullOrWhiteSpace($StageRoot)) { $StageRoot = Join-Path $repositoryRoot "stage" }
else { $StageRoot = Resolve-FullPath $StageRoot $repositoryRoot }
$StageRoot = [IO.Path]::GetFullPath($StageRoot)
$script:Package = Invoke-DeploymentPhase "PackageValidation" { Assert-ReleasePackage $PackagePath }

if ($ValidatePackageOnly) {
    Write-Host "Stage package validation succeeded. Version=$($script:Package.Version); Commit=$($script:Package.Commit); SHA256=$($script:Package.Sha256); Schema=$($script:Package.MinimumSchemaVersion)->$($script:Package.TargetSchemaVersion); Entries=$($script:Package.EntryCount)"
    return
}

try {
    Invoke-DeploymentPhase "Preflight" {
        foreach ($item in @(
            @{ Name = "StageDatabase"; Value = $StageDatabase },
            @{ Name = "DevelopmentDatabase"; Value = $DevelopmentDatabase },
            @{ Name = "ProductionDatabase"; Value = $ProductionDatabase },
            @{ Name = "DeployDatabaseUser"; Value = $DeployDatabaseUser }
        )) {
            if ([string]::IsNullOrWhiteSpace($item.Value)) { throw "$($item.Name) is required." }
            Assert-SafePostgresIdentifier $item.Value $item.Name
        }
        if ($StageDatabase -eq $DevelopmentDatabase -or $StageDatabase -eq $ProductionDatabase -or $DevelopmentDatabase -eq $ProductionDatabase) { throw "Stage, Development, and Production database names must be distinct." }
        if (Test-ProductionLikeName $StageDatabase) { throw "Production-like Stage database name '$StageDatabase' is forbidden." }
        if (Test-ProductionLikeName $DevelopmentDatabase) { throw "Production-like Development source '$DevelopmentDatabase' is forbidden." }
        if ([string]::IsNullOrWhiteSpace($WebUrl)) { throw "-WebUrl is required." }
        $webUri = [Uri]$WebUrl
        if (-not $webUri.IsAbsoluteUri -or $webUri.Scheme -notin @("http", "https") -or $webUri.Port -lt 1) { throw "WebUrl must be an absolute HTTP/HTTPS URL with a known port." }
        if (-not $HealthPath.StartsWith('/')) { throw "HealthPath must start with '/'." }
        if (-not $WorkerArguments -or $WorkerArguments.Count -eq 0) { throw "-WorkerArguments is required because PriceCrawler.Worker is an explicit one-shot operational CLI." }
        foreach ($argument in $WorkerArguments) { if ($argument -match '(?i)(password|secret|token|connectionstrings?)') { throw "Worker arguments must not contain secret-bearing options." } }

        if ([string]::IsNullOrWhiteSpace($WebConfigPath)) { $script:ResolvedWebConfigPath = Join-Path $StageRoot "config\web\appsettings.Stage.json" }
        else { $script:ResolvedWebConfigPath = Resolve-FullPath $WebConfigPath $repositoryRoot }
        if ([string]::IsNullOrWhiteSpace($WorkerConfigPath)) { $script:ResolvedWorkerConfigPath = Join-Path $StageRoot "config\crawler\appsettings.Stage.json" }
        else { $script:ResolvedWorkerConfigPath = Resolve-FullPath $WorkerConfigPath $repositoryRoot }
        $script:WebConfiguration = Assert-StageConfiguration $script:ResolvedWebConfigPath "pricecrawler_stage_web" "Web"
        $script:WorkerConfiguration = Assert-StageConfiguration $script:ResolvedWorkerConfigPath "pricecrawler_stage_worker" "Worker"
        $script:ResolvedToolMode = Resolve-PostgresToolMode
        if (-not (Test-DatabaseExists $StageDatabase)) { throw "Stage database '$StageDatabase' does not exist." }
        if ($RefreshDatabaseFromDevelopment -and -not (Test-DatabaseExists $DevelopmentDatabase)) { throw "Development database '$DevelopmentDatabase' does not exist." }
        $script:SchemaBefore = Get-SchemaVersion $StageDatabase
        if ($script:SchemaBefore -gt $script:Package.TargetSchemaVersion) { throw "Stage schema $script:SchemaBefore is newer than package target $($script:Package.TargetSchemaVersion); downgrade is forbidden." }
        if ($script:SchemaBefore -lt $script:Package.MinimumSchemaVersion) { throw "Stage schema $script:SchemaBefore is below package minimum $($script:Package.MinimumSchemaVersion)."
        }

        $script:ReleasesRoot = Join-Path $StageRoot "releases"
        $script:CurrentRoot = Join-Path $StageRoot "current"
        $script:RuntimeRoot = Join-Path $StageRoot "runtime"
        $script:LogsRoot = Join-Path $StageRoot "logs"
        $script:BackupRoot = Join-Path $StageRoot "backups\database"
        $script:ReleaseRoot = Join-Path $script:ReleasesRoot $script:Package.Version
        foreach ($path in @($script:ReleasesRoot, $script:CurrentRoot, $script:RuntimeRoot, $script:LogsRoot, $script:BackupRoot, $script:ReleaseRoot)) { Assert-PathWithinRoot $StageRoot $path }

        if ($WhatIf) {
            Write-Host "Stage deployment dry run"
            Write-Host "Package: $($script:Package.Path)"
            Write-Host "Version: $($script:Package.Version); Commit: $($script:Package.Commit); SHA256: $($script:Package.Sha256)"
            Write-Host "Stage root: $StageRoot"
            Write-Host "Database: $StageDatabase; Schema: $script:SchemaBefore -> $($script:Package.TargetSchemaVersion)"
            Write-Host "Refresh from Development: $([bool]$RefreshDatabaseFromDevelopment)"
            Write-Host "Migrations: $((@($script:Package.Migrations | Where-Object version -gt $script:SchemaBefore | ForEach-Object fileName) -join ', '))"
            Write-Host "Process order: stop Worker -> Web; start Web -> port -> health -> Worker"
            Write-Host "Release: $script:ReleaseRoot; Current: $script:CurrentRoot"
            Write-Host "Web config: $script:ResolvedWebConfigPath"
            Write-Host "Worker config: $script:ResolvedWorkerConfigPath"
            Write-Host "Web: $WebUrl$HealthPath"
            Write-Host "Worker command: PriceCrawler.Worker $($WorkerArguments -join ' ')"
            Write-Host "No backup, extraction, process, database, current, lock, log, or report mutation occurs in -WhatIf."
            return
        }

        foreach ($directory in @($script:ReleasesRoot, $script:RuntimeRoot, $script:LogsRoot, $script:BackupRoot)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        $timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
        $safeVersion = $script:Package.Version -replace '[^0-9A-Za-z._+-]', '_'
        $script:LogPath = Join-Path $script:LogsRoot "deploy-stage-$safeVersion-$timestamp.log"
        $script:ReportPath = Join-Path $script:LogsRoot "deploy-stage-$safeVersion-$timestamp.json"
        $script:LockPath = Join-Path $script:RuntimeRoot "deploy.lock"
        Acquire-DeploymentLock $script:LockPath
        Write-DeployLog "Deployment target validated. Package=$($script:Package.Path); SHA256=$($script:Package.Sha256); Version=$($script:Package.Version); Commit=$($script:Package.Commit); StageRoot=$StageRoot; StageDatabase=$StageDatabase; DevelopmentDatabase=$DevelopmentDatabase; ToolMode=$script:ResolvedToolMode"
    } | Out-Null

    if ($WhatIf) { return }

    Invoke-DeploymentPhase "DatabaseBackup" {
        $timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
        $backupPath = Join-Path $script:BackupRoot "$StageDatabase-before-$($script:Package.Version)-$timestamp.dump"
        $script:Backup = New-LogicalDump $StageDatabase $backupPath "Stage pre-deployment backup"
        Write-DeployLog "Verified Stage backup. Path=$($script:Backup.path); SizeBytes=$($script:Backup.sizeBytes); SHA256=$($script:Backup.sha256)"
    } | Out-Null

    Invoke-DeploymentPhase "StopProcesses" {
        Stop-RecordedStageProcess (Join-Path $script:RuntimeRoot "worker.pid") "Worker"
        Stop-RecordedStageProcess (Join-Path $script:RuntimeRoot "web.pid") "Web"
        Wait-PortReleased ([Uri]$WebUrl).Port
    } | Out-Null

    Invoke-DeploymentPhase "OptionalDatabaseRefresh" {
        if (-not $RefreshDatabaseFromDevelopment) { Write-DeployLog "Development-to-Stage refresh was not requested; existing Stage data is preserved."; return }
        if (-not $script:Backup) { throw "Verified Stage backup is required before refresh." }
        $devDumpPath = Join-Path $script:BackupRoot "$DevelopmentDatabase-for-stage-$($script:Package.Version)-$([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')).dump"
        $developmentDump = New-LogicalDump $DevelopmentDatabase $devDumpPath "Development refresh source"
        Remove-DatabaseForStageRefresh
        Restore-LogicalDump $StageDatabase $developmentDump.path
        $refreshedVersion = Get-SchemaVersion $StageDatabase
        if ($refreshedVersion -gt $script:Package.TargetSchemaVersion) { throw "Refreshed Stage schema $refreshedVersion is newer than target; downgrade is forbidden." }
        if ($refreshedVersion -lt $script:Package.MinimumSchemaVersion) { throw "Refreshed Stage schema $refreshedVersion is below package minimum." }
        $script:SchemaBefore = $refreshedVersion
        Write-DeployLog "Explicit Development-to-Stage refresh completed. Source=$DevelopmentDatabase; Destination=$StageDatabase; SchemaVersion=$refreshedVersion"
    } | Out-Null

    Invoke-DeploymentPhase "Migration" {
        $actual = Get-SchemaVersion $StageDatabase
        if ($actual -gt $script:Package.TargetSchemaVersion) { throw "Stage schema $actual is newer than package target; downgrade is forbidden." }
        $migrationTemp = Join-Path $script:RuntimeRoot "migrations-$([Guid]::NewGuid().ToString('N'))"
        try {
            New-Item -ItemType Directory -Path $migrationTemp -Force | Out-Null
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($script:Package.Path)
            try {
                foreach ($migration in @($script:Package.Migrations | Where-Object version -gt $actual | Sort-Object version)) {
                    if ($migration.version -ne ($actual + 1)) { throw "Missing migration version $($actual + 1)." }
                    $entry = $archive.GetEntry($migration.entryPath)
                    if (-not $entry) { throw "Required migration '$($migration.fileName)' is absent." }
                    $destination = Join-Path $migrationTemp $migration.fileName
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
                    $checksum = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
                    Write-DeployLog "Applying forward migration. Version=$($migration.version); File=$($migration.fileName); SHA256=$checksum"
                    Invoke-PsqlFile $StageDatabase $destination
                    $actual = Get-SchemaVersion $StageDatabase
                    if ($actual -ne $migration.version) { throw "Migration '$($migration.fileName)' completed but schema_version is $actual; expected $($migration.version)." }
                    $script:AppliedMigrations.Add($migration.fileName)
                }
            }
            finally { $archive.Dispose() }
        }
        finally { Remove-Item -LiteralPath $migrationTemp -Recurse -Force -ErrorAction SilentlyContinue }
        if ($actual -ne $script:Package.TargetSchemaVersion) { throw "Stage schema is $actual after migration; target is $($script:Package.TargetSchemaVersion)." }
        $script:SchemaAfter = $actual
    } | Out-Null

    Invoke-DeploymentPhase "RuntimeGrants" {
        $provisionTemp = Join-Path $script:RuntimeRoot "provision-runtime-$([Guid]::NewGuid().ToString('N')).ps1"
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($script:Package.Path)
            try {
                $entry = $archive.GetEntry("db/scripts/provision-database-runtime-roles.ps1")
                if (-not $entry) { throw "Packaged runtime-role provisioning script is missing." }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $provisionTemp, $false)
            }
            finally { $archive.Dispose() }
            $arguments = @{
                ToolMode = $script:ResolvedToolMode
                HostName = $PostgresHost
                Port = $PostgresPort
                AdminUser = $DeployDatabaseUser
                ObjectOwnerRole = $DeployDatabaseUser
                StageDatabase = $StageDatabase
                StageOnly = $true
                ExpectedSchemaVersion = $script:Package.TargetSchemaVersion
            }
            if ($script:ResolvedToolMode -eq "Docker") { $arguments.DockerContainer = $DockerContainer }
            & $provisionTemp @arguments
            Write-DeployLog "Stage-only least-privilege runtime grants and DDL-denial probes completed. Production was not selected."
        }
        finally { Remove-Item -LiteralPath $provisionTemp -Force -ErrorAction SilentlyContinue }
    } | Out-Null

    Invoke-DeploymentPhase "ReleaseExtraction" {
        $tempRoot = Join-Path $script:RuntimeRoot "extract-$([Guid]::NewGuid().ToString('N'))"
        try {
            [IO.Compression.ZipFile]::ExtractToDirectory($script:Package.Path, $tempRoot)
            foreach ($required in @("release.json", "web", "crawler", "db\migrations")) { if (-not (Test-Path -LiteralPath (Join-Path $tempRoot $required))) { throw "Extracted release is missing '$required'." } }
            $extractedMetadata = Get-Content -LiteralPath (Join-Path $tempRoot "release.json") -Raw | ConvertFrom-Json
            if ([string]$extractedMetadata.version -ne $script:Package.Version -or [string]$extractedMetadata.commit -ne $script:Package.Commit) { throw "Extracted release metadata changed after ZIP validation." }
            if (Test-Path -LiteralPath $script:ReleaseRoot) {
                if (-not $ReplaceExistingRelease) { throw "Versioned release already exists: $script:ReleaseRoot. Use -ReplaceExistingRelease only for an explicitly approved replacement." }
                $replaced = Join-Path $script:RuntimeRoot "replaced-$($script:Package.Version)-$([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
                Move-Item -LiteralPath $script:ReleaseRoot -Destination $replaced
                Write-DeployLog "Existing release moved aside by explicit replacement. Evidence=$replaced" "WARN"
            }
            Move-Item -LiteralPath $tempRoot -Destination $script:ReleaseRoot
            $tempRoot = $null
        }
        finally { if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) { Remove-Item -LiteralPath $tempRoot -Recurse -Force } }
    } | Out-Null

    Invoke-DeploymentPhase "Configuration" {
        $currentNew = Join-Path $StageRoot "current.new"
        Assert-PathWithinRoot $StageRoot $currentNew
        Remove-Item -LiteralPath $currentNew -Recurse -Force -ErrorAction SilentlyContinue
        Copy-DirectoryContents $script:ReleaseRoot $currentNew
        Copy-Item -LiteralPath $script:ResolvedWebConfigPath -Destination (Join-Path $currentNew "web\appsettings.Stage.json") -Force
        Copy-Item -LiteralPath $script:ResolvedWorkerConfigPath -Destination (Join-Path $currentNew "crawler\appsettings.Stage.json") -Force
        $script:CurrentNew = $currentNew
    } | Out-Null

    Invoke-DeploymentPhase "CurrentSwitch" {
        if (Test-Path -LiteralPath (Join-Path $script:CurrentRoot "release.json")) {
            try { $script:PreviousVersion = [string](Get-Content -LiteralPath (Join-Path $script:CurrentRoot "release.json") -Raw | ConvertFrom-Json).version } catch { $script:PreviousVersion = "unknown" }
        }
        $previousCurrent = Join-Path $script:RuntimeRoot "previous-current-$([DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
        if (Test-Path -LiteralPath $script:CurrentRoot) { Move-Item -LiteralPath $script:CurrentRoot -Destination $previousCurrent }
        try { Move-Item -LiteralPath $script:CurrentNew -Destination $script:CurrentRoot }
        catch {
            if (Test-Path -LiteralPath $previousCurrent -and -not (Test-Path -LiteralPath $script:CurrentRoot)) { Move-Item -LiteralPath $previousCurrent -Destination $script:CurrentRoot }
            throw
        }
        $script:CurrentSwitched = $true
        $script:PreviousCurrentEvidence = if (Test-Path -LiteralPath $previousCurrent) { $previousCurrent } else { $null }
    } | Out-Null

    $webUri = [Uri]$WebUrl
    Invoke-DeploymentPhase "WebStart" {
        $script:WebProcess = Start-StageComponent (Join-Path $script:CurrentRoot "web") "PriceCrawler.Web" $WebArguments (Join-Path $script:LogsRoot "web-$($script:Package.Version).out.log") (Join-Path $script:LogsRoot "web-$($script:Package.Version).err.log") (Join-Path $script:RuntimeRoot "web.pid") $WebUrl
    } | Out-Null
    Invoke-DeploymentPhase "PortCheck" { Wait-WebPort $webUri.Port $script:WebProcess.Id } | Out-Null
    Invoke-DeploymentPhase "HealthCheck" { Wait-WebHealth ([Uri]::new($webUri, $HealthPath)) | Out-Null } | Out-Null
    Invoke-DeploymentPhase "WorkerStart" {
        $script:WorkerProcess = Start-StageComponent (Join-Path $script:CurrentRoot "crawler") "PriceCrawler.Worker" $WorkerArguments (Join-Path $script:LogsRoot "worker-$($script:Package.Version).out.log") (Join-Path $script:LogsRoot "worker-$($script:Package.Version).err.log") (Join-Path $script:RuntimeRoot "worker.pid")
        Start-Sleep -Seconds $WorkerStabilizationSeconds
        if ($script:WorkerProcess.HasExited) { throw "Worker exited during the $WorkerStabilizationSeconds-second stabilization interval. ExitCode=$($script:WorkerProcess.ExitCode)." }
    } | Out-Null
    Invoke-DeploymentPhase "PostDeployVerification" {
        if ($script:WebProcess.HasExited) { throw "Web exited after startup." }
        if ($script:WorkerProcess.HasExited) { throw "Worker exited after startup." }
        $script:SchemaAfter = Get-SchemaVersion $StageDatabase
        if ($script:SchemaAfter -ne $script:Package.TargetSchemaVersion) { throw "Post-deploy schema version $script:SchemaAfter does not match target $($script:Package.TargetSchemaVersion)." }
        $state = [ordered]@{ environment = "Stage"; version = $script:Package.Version; commit = $script:Package.Commit; deployedAtUtc = [DateTimeOffset]::UtcNow.ToString("o"); previousVersion = $script:PreviousVersion; schemaVersion = $script:SchemaAfter; packageSha256 = $script:Package.Sha256 }
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $StageRoot "deployment-state.json") -Encoding UTF8
    } | Out-Null

    $script:FinalResult = "Success"
    Write-DeployLog "Stage deployment completed successfully. Version=$($script:Package.Version); Commit=$($script:Package.Commit); Schema=$script:SchemaAfter; WebPid=$($script:WebProcess.Id); WorkerPid=$($script:WorkerProcess.Id)"
}
catch {
    $script:FailureMessage = $_.Exception.Message
    Write-DeployLog "Stage deployment failed. Error=$script:FailureMessage" "ERROR"
    if ($script:WorkerProcess -and -not $script:WorkerProcess.HasExited) { Stop-Process -Id $script:WorkerProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($script:WebProcess -and -not $script:WebProcess.HasExited) { Stop-Process -Id $script:WebProcess.Id -Force -ErrorAction SilentlyContinue }
    Write-DeployLog "Verified database backup and failure evidence are preserved. Database rollback is not automatic." "WARN"
    if ($script:CurrentSwitched) { Write-DeployLog "Application files were switched before failure. Previous files may be available under runtime, but automatic application rollback is not attempted because schema compatibility requires operator review." "WARN" }
    throw
}
finally {
    try { Invoke-DeploymentPhase "Report" { Write-DeploymentReport } | Out-Null } catch { Write-Warning "Could not write deployment report: $($_.Exception.Message)" }
    if ($script:LockOwned -and $script:LockPath -and (Test-Path -LiteralPath $script:LockPath)) { Remove-Item -LiteralPath $script:LockPath -Force -ErrorAction SilentlyContinue; $script:LockOwned = $false }
}
