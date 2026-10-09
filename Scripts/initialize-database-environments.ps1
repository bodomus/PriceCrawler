[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [ValidateSet("Auto", "Native", "Docker")]
    [string]$ToolMode = "Auto",

    [string]$DockerContainer,

    [string]$HostName = "localhost",

    [ValidateRange(1, 65535)]
    [int]$Port = 5432,

    [Parameter(Mandatory = $true)]
    [string]$AdminUser,

    [string]$DevelopmentDatabase = "varprice",

    [string]$TestDatabase = "varprice_test",

    [string]$StageDatabase = "varprice_stage",

    [string]$ProductionDatabase = "varprice_prod",

    [switch]$InitializeTest,

    [switch]$InitializeStage,

    [switch]$InitializeProduction,

    [switch]$InitializeAll,

    [switch]$ReplaceExistingTest,

    [switch]$ReplaceExistingStage,

    [switch]$ConfirmInitialProductionBootstrap,

    [string]$TestRuntimeRole,

    [string]$StageRuntimeRole,

    [string]$ProductionRuntimeRole,

    [string]$VerifiedDevelopmentDumpPath,

    [string]$ArtifactsRoot,

    [string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$script:LogPath = $null
$script:ResolvedToolMode = $null
$script:FailureLogged = $false
$script:RecoveryLogged = $false
$script:RecoveryDevelopmentDumpPath = $null
$script:StageReplacementStartedByCurrentRun = $false
$script:StageBootstrapCompletedByCurrentRun = $false
$script:ProductionCreatedByCurrentRun = $false
$script:ProductionRestoreStartedByCurrentRun = $false
$script:ProductionMarkerPersistedByCurrentRun = $false
$script:ArtifactRecords = [System.Collections.Generic.List[object]]::new()
$script:EnvironmentResults = [System.Collections.Generic.List[object]]::new()
$script:CriticalTables = @(
    "crawl_error",
    "crawler_run",
    "crawler_run_stage",
    "db_routine_script",
    "ingestion_run",
    "price_collect_queue",
    "price_snapshot",
    "product",
    "product_catalog",
    "product_catalog_refresh",
    "schema_version"
)
$script:BusinessTables = @(
    "product",
    "price_snapshot",
    "crawler_run",
    "crawler_run_stage",
    "ingestion_run",
    "price_collect_queue",
    "product_catalog",
    "product_catalog_refresh",
    "crawl_error"
)

function Write-OperatorLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )

    $safeMessage = $Message
    $safeMessage = [regex]::Replace(
        $safeMessage,
        '(?i)(password|pwd)\s*=\s*[^;\s]+',
        '$1=<redacted>')
    $record = "{0} [{1}] {2}" -f [DateTime]::UtcNow.ToString("o"), $Level, $safeMessage
    Write-Host $record

    if ($script:LogPath -and -not $WhatIfPreference) {
        Add-Content -LiteralPath $script:LogPath -Value $record -Encoding UTF8
    }
}

function Write-FailureOnce {
    param([Parameter(Mandatory = $true)][string]$Message)

    if (-not $script:FailureLogged) {
        $script:FailureLogged = $true
        Write-OperatorLog "Database environment initialization failed. $Message" "ERROR"
    }
}

function Get-RecoveryRerunCommand {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Stage", "Production")]
        [string]$EnvironmentName
    )

    $toolArguments = "-ToolMode $script:ResolvedToolMode"
    if ($script:ResolvedToolMode -eq "Docker") {
        $toolArguments += " -DockerContainer `"$DockerContainer`""
    }

    $operationArguments = if ($EnvironmentName -eq "Production") {
        "-InitializeProduction -ConfirmInitialProductionBootstrap"
    }
    else {
        "-InitializeStage -ReplaceExistingStage"
    }

    return ".\scripts\initialize-database-environments.ps1 $toolArguments -HostName `"$HostName`" -Port $Port -AdminUser `"$AdminUser`" -DevelopmentDatabase `"$DevelopmentDatabase`" -TestDatabase `"$TestDatabase`" -StageDatabase `"$StageDatabase`" -ProductionDatabase `"$ProductionDatabase`" $operationArguments -VerifiedDevelopmentDumpPath `"$script:RecoveryDevelopmentDumpPath`""
}

function Write-PartialFailureRecoveryOnce {
    if ($script:RecoveryLogged) {
        return
    }

    $hasStageRecovery = $script:StageReplacementStartedByCurrentRun -and -not $script:StageBootstrapCompletedByCurrentRun
    $hasProductionRecovery = $script:ProductionRestoreStartedByCurrentRun
    if (-not $hasStageRecovery -and -not $hasProductionRecovery) {
        return
    }

    $script:RecoveryLogged = $true

    if ($hasStageRecovery) {
        Write-OperatorLog "RECOVERY REQUIRED: Stage replacement started but did not complete. The current '$StageDatabase' database may be partial. No Stage database was deleted automatically by error handling." "ERROR"
        Write-OperatorLog "Verified Development dump for Stage recovery: $script:RecoveryDevelopmentDumpPath" "ERROR"
        if (Test-Path -LiteralPath $stageBackupPath -PathType Leaf) {
            Write-OperatorLog "Previous Stage backup retained for rollback: $stageBackupPath" "ERROR"
        }
        Write-OperatorLog "After operator review, rerun the guarded Stage restore from the same verified dump:" "ERROR"
        Write-OperatorLog (Get-RecoveryRerunCommand -EnvironmentName "Stage") "ERROR"
    }

    if ($hasProductionRecovery -and -not $script:ProductionMarkerPersistedByCurrentRun) {
        Write-OperatorLog "RECOVERY REQUIRED: initial Production restore/validation failed before the independence marker was persisted. No Production database was deleted automatically." "ERROR"
        Write-OperatorLog "Before deletion, an authorized operator must prove that '$ProductionDatabase' has never been successfully introduced into service and confirm that the independence marker is absent." "ERROR"

        $markerSql = "select coalesce(shobj_description(oid,'pg_database'),'') from pg_database where datname='$ProductionDatabase';"
        if ($script:ResolvedToolMode -eq "Docker") {
            Write-OperatorLog "Marker check: docker exec `"$DockerContainer`" psql --username `"$AdminUser`" --dbname postgres --command `"$markerSql`"" "ERROR"
        }
        else {
            Write-OperatorLog "Marker check: psql --host `"$HostName`" --port $Port --username `"$AdminUser`" --dbname postgres --command `"$markerSql`"" "ERROR"
        }

        if ($script:ProductionCreatedByCurrentRun) {
            Write-OperatorLog "The failed Production database was created by this script run. Only after the audit proof above, remove that failed bootstrap database manually:" "ERROR"
            $terminateSql = "select pg_terminate_backend(pid) from pg_stat_activity where datname='$ProductionDatabase' and pid<>pg_backend_pid();"
            if ($script:ResolvedToolMode -eq "Docker") {
                Write-OperatorLog "docker exec `"$DockerContainer`" psql --username `"$AdminUser`" --dbname postgres --command `"$terminateSql`"" "ERROR"
                Write-OperatorLog "docker exec `"$DockerContainer`" dropdb --username `"$AdminUser`" `"$ProductionDatabase`"" "ERROR"
            }
            else {
                Write-OperatorLog "psql --host `"$HostName`" --port $Port --username `"$AdminUser`" --dbname postgres --command `"$terminateSql`"" "ERROR"
                Write-OperatorLog "dropdb --host `"$HostName`" --port $Port --username `"$AdminUser`" `"$ProductionDatabase`"" "ERROR"
            }
            Write-OperatorLog "Then repeat the guarded bootstrap from the same verified dump:" "ERROR"
            Write-OperatorLog (Get-RecoveryRerunCommand -EnvironmentName "Production") "ERROR"
        }
        else {
            Write-OperatorLog "The script did not create the Production database in this run. Do not delete it using a generated command; escalate to a DBA for ownership/history verification." "ERROR"
        }
    }
    elseif ($hasProductionRecovery -and $script:ProductionMarkerPersistedByCurrentRun) {
        Write-OperatorLog "Production independence marker was already persisted before the later failure. Do not delete Production and do not rerun bootstrap. Treat Production as independent and complete the missing backup/verification step through a reviewed recovery procedure." "ERROR"
    }
}

function Quote-Identifier {
    param([Parameter(Mandatory = $true)][string]$Value)

    return '"' + $Value.Replace('"', '""') + '"'
}

function Quote-Literal {
    param([Parameter(Mandatory = $true)][string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Assert-SafeIdentifier {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "$Name '$Value' is not a safe PostgreSQL identifier. Use letters, digits, and underscores only."
    }
}

function Invoke-TextCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $output = @(& $Executable @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $details = ($output | Out-String).Trim()
        throw "$Description failed with exit code $exitCode. $details"
    }

    return ($output | Out-String).Trim()
}

function Get-PsqlArguments {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    if ($script:ResolvedToolMode -eq "Docker") {
        return @(
            "exec", "-i", $DockerContainer,
            "psql", "-X", "-v", "ON_ERROR_STOP=1", "-A", "-t",
            "--username", $AdminUser,
            "--dbname", $Database,
            "--command", $Sql
        )
    }

    return @(
        "-X", "-v", "ON_ERROR_STOP=1", "-A", "-t",
        "--host", $HostName,
        "--port", $Port.ToString(),
        "--username", $AdminUser,
        "--dbname", $Database,
        "--command", $Sql
    )
}

function Invoke-PsqlQuery {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Sql,
        [string]$Description = "PostgreSQL query"
    )

    $executable = if ($script:ResolvedToolMode -eq "Docker") { "docker" } else { "psql" }
    return Invoke-TextCommand `
        -Executable $executable `
        -Arguments (Get-PsqlArguments -Database $Database -Sql $Sql) `
        -Description $Description
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($script:ResolvedToolMode -eq "Docker") {
        $arguments = @(
            "exec", "-i", $DockerContainer,
            "psql", "-X", "-v", "ON_ERROR_STOP=1",
            "--username", $AdminUser,
            "--dbname", $Database
        )
        $sql = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
        $output = @($sql | & docker @arguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Applying SQL file '$Path' failed. $(($output | Out-String).Trim())"
        }
        return
    }

    Invoke-TextCommand `
        -Executable "psql" `
        -Arguments @(
            "-X", "-v", "ON_ERROR_STOP=1",
            "--host", $HostName,
            "--port", $Port.ToString(),
            "--username", $AdminUser,
            "--dbname", $Database,
            "--file", $Path
        ) `
        -Description "Applying SQL file '$Path'" | Out-Null
}

function Resolve-ToolMode {
    $requiredNativeTools = @("psql", "pg_dump", "pg_restore", "createdb", "dropdb")
    $nativeToolsAvailable = @($requiredNativeTools | Where-Object {
        -not (Get-Command $_ -ErrorAction SilentlyContinue)
    }).Count -eq 0

    if ($ToolMode -eq "Native") {
        if (-not $nativeToolsAvailable) {
            throw "Native PostgreSQL tools are missing. Required: $($requiredNativeTools -join ', '). Install them or use -ToolMode Docker -DockerContainer <container>."
        }
        return "Native"
    }

    if ($ToolMode -eq "Docker" -or (-not $nativeToolsAvailable -and $DockerContainer)) {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw "Docker CLI was not found. Install PostgreSQL tools or Docker."
        }
        if ([string]::IsNullOrWhiteSpace($DockerContainer)) {
            throw "-DockerContainer is required when Docker tool mode is selected."
        }
        $running = Invoke-TextCommand `
            -Executable "docker" `
            -Arguments @("inspect", "--format", "{{.State.Running}}", $DockerContainer) `
            -Description "Docker container validation"
        if ($running.Trim() -ne "true") {
            throw "Docker container '$DockerContainer' is not running."
        }
        foreach ($tool in $requiredNativeTools) {
            Invoke-TextCommand `
                -Executable "docker" `
                -Arguments @("exec", $DockerContainer, "sh", "-c", "command -v $tool") `
                -Description "PostgreSQL tool validation for $tool" | Out-Null
        }
        return "Docker"
    }

    if ($nativeToolsAvailable) {
        return "Native"
    }

    throw "PostgreSQL tools were not found. Install psql/pg_dump/pg_restore/createdb/dropdb or specify -ToolMode Docker -DockerContainer <container>."
}

function Get-ExpectedSchemaContract {
    param([Parameter(Mandatory = $true)][string]$ContractPath)

    $contract = Get-Content -LiteralPath $ContractPath -Raw -Encoding UTF8
    $versionMatch = [regex]::Match($contract, 'public\s+const\s+int\s+ExpectedVersion\s*=\s*(?<value>\d+)\s*;')
    $applicationMatch = [regex]::Match($contract, 'public\s+const\s+string\s+BaselineApplicationVersion\s*=\s*"(?<value>[^"]+)"\s*;')
    if (-not $versionMatch.Success -or -not $applicationMatch.Success) {
        throw "Could not resolve the centralized database schema contract from '$ContractPath'."
    }

    return [pscustomobject]@{
        Version = [int]$versionMatch.Groups["value"].Value
        ApplicationVersion = $applicationMatch.Groups["value"].Value
    }
}

function Test-DatabaseExists {
    param([Parameter(Mandatory = $true)][string]$Database)

    $literal = Quote-Literal $Database
    return (Invoke-PsqlQuery -Database "postgres" -Sql "select exists(select 1 from pg_database where datname=$literal);" -Description "Database existence check").Trim() -eq "t"
}

function New-Database {
    param([Parameter(Mandatory = $true)][string]$Database)

    Invoke-PsqlQuery -Database "postgres" -Sql "create database $(Quote-Identifier $Database);" -Description "Creating database '$Database'" | Out-Null
}

function Remove-Database {
    param([Parameter(Mandatory = $true)][string]$Database)

    $literal = Quote-Literal $Database
    $identifier = Quote-Identifier $Database
    Invoke-PsqlQuery -Database "postgres" -Sql "select pg_terminate_backend(pid) from pg_stat_activity where datname=$literal and pid<>pg_backend_pid();" -Description "Terminating connections to '$Database'" | Out-Null
    Invoke-PsqlQuery -Database "postgres" -Sql "drop database $identifier;" -Description "Dropping database '$Database'" | Out-Null
}

function Assert-DatabaseSchema {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][int]$ExpectedVersion
    )

    $hasMetadata = (Invoke-PsqlQuery -Database $Database -Sql "select to_regclass('public.schema_version') is not null;" -Description "Schema metadata check for '$Database'").Trim()
    if ($hasMetadata -ne "t") {
        throw "Database '$Database' does not contain public.schema_version."
    }

    $actualVersion = [int](Invoke-PsqlQuery -Database $Database -Sql "select coalesce(max(version),0) from public.schema_version;" -Description "Schema version check for '$Database'").Trim()
    if ($actualVersion -ne $ExpectedVersion) {
        throw "Database '$Database' schema version is $actualVersion; expected $ExpectedVersion."
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($table in $script:CriticalTables) {
        $exists = (Invoke-PsqlQuery -Database $Database -Sql "select to_regclass('public.$table') is not null;" -Description "Critical object check for '$table'").Trim()
        if ($exists -ne "t") {
            $missing.Add("public.$table")
        }
    }
    if ($missing.Count -gt 0) {
        throw "Database '$Database' is missing critical objects: $($missing -join ', ')."
    }

    $routineExists = (Invoke-PsqlQuery -Database $Database -Sql "select to_regprocedure('public.product_catalog_get_due(integer,timestamp with time zone,integer,text)') is not null;" -Description "Critical routine check").Trim()
    if ($routineExists -ne "t") {
        throw "Database '$Database' is missing critical routine public.product_catalog_get_due."
    }

    return $actualVersion
}

function Get-RowCounts {
    param([Parameter(Mandatory = $true)][string]$Database)

    $parts = @($script:BusinessTables | ForEach-Object {
        "select $(Quote-Literal $_) as table_name, count(*)::bigint as row_count from public.$(Quote-Identifier $_)"
    })
    $output = Invoke-PsqlQuery -Database $Database -Sql (($parts -join " union all ") + " order by table_name;") -Description "Critical row counts for '$Database'"
    $counts = [ordered]@{}
    foreach ($line in @($output -split "`r?`n")) {
        if ($line -match '^(?<table>[^|]+)\|(?<count>\d+)$') {
            $counts[$matches["table"]] = [long]$matches["count"]
        }
    }
    if ($counts.Count -ne $script:BusinessTables.Count) {
        throw "Could not capture all critical row counts from '$Database'."
    }
    return $counts
}

function Assert-RowCountsEqual {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Expected,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Actual,
        [Parameter(Mandatory = $true)][string]$EnvironmentName
    )

    foreach ($table in $script:BusinessTables) {
        if ([long]$Expected[$table] -ne [long]$Actual[$table]) {
            throw "$EnvironmentName row-count mismatch for '$table': expected $($Expected[$table]), actual $($Actual[$table])."
        }
    }
}

function Assert-DevelopmentQuiescent {
    param([Parameter(Mandatory = $true)][string]$Database)

    $literal = Quote-Literal $Database
    $connections = [int](Invoke-PsqlQuery -Database "postgres" -Sql "select count(*) from pg_stat_activity where datname=$literal;" -Description "Development connection check").Trim()
    if ($connections -ne 0) {
        throw "Development database '$Database' has $connections open connection(s). Stop Web/Worker and other clients before creating a consistent bootstrap dump."
    }
}

function Assert-DumpFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Kind dump was not created: $Path"
    }
    $item = Get-Item -LiteralPath $Path
    if ($item.Length -le 0) {
        throw "$Kind dump is empty: $Path"
    }

    if ($script:ResolvedToolMode -eq "Docker") {
        $remotePath = "/tmp/pricecrawler-verify-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand -Executable "docker" -Arguments @("cp", $Path, "${DockerContainer}:$remotePath") -Description "Copying dump for verification" | Out-Null
            Invoke-TextCommand -Executable "docker" -Arguments @("exec", $DockerContainer, "pg_restore", "--list", $remotePath) -Description "Verifying $Kind dump catalog" | Out-Null
        }
        finally {
            & docker exec $DockerContainer rm -f $remotePath 2>$null | Out-Null
        }
    }
    else {
        Invoke-TextCommand -Executable "pg_restore" -Arguments @("--list", $Path) -Description "Verifying $Kind dump catalog" | Out-Null
    }

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $record = [pscustomobject]@{
        Kind = $Kind
        Path = (Resolve-Path -LiteralPath $Path).Path
        SizeBytes = $item.Length
        Sha256 = $hash
    }
    $script:ArtifactRecords.Add($record)
    Write-OperatorLog "$Kind dump verified. Path=$($record.Path); SizeBytes=$($record.SizeBytes); SHA256=$hash"
    return $record
}

function New-LogicalDump {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    if ($script:ResolvedToolMode -eq "Docker") {
        $remotePath = "/tmp/pricecrawler-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand `
                -Executable "docker" `
                -Arguments @(
                    "exec", $DockerContainer,
                    "pg_dump",
                    "--username", $AdminUser,
                    "--dbname", $Database,
                    "--format=custom",
                    "--no-owner",
                    "--no-privileges",
                    "--serializable-deferrable",
                    "--file", $remotePath
                ) `
                -Description "Creating $Kind dump" | Out-Null
            Invoke-TextCommand -Executable "docker" -Arguments @("exec", $DockerContainer, "pg_restore", "--list", $remotePath) -Description "Verifying $Kind dump catalog" | Out-Null
            Invoke-TextCommand -Executable "docker" -Arguments @("cp", "${DockerContainer}:$remotePath", $Path) -Description "Copying $Kind dump" | Out-Null
        }
        finally {
            & docker exec $DockerContainer rm -f $remotePath 2>$null | Out-Null
        }
    }
    else {
        Invoke-TextCommand `
            -Executable "pg_dump" `
            -Arguments @(
                "--host", $HostName,
                "--port", $Port.ToString(),
                "--username", $AdminUser,
                "--dbname", $Database,
                "--format=custom",
                "--no-owner",
                "--no-privileges",
                "--serializable-deferrable",
                "--file", $Path
            ) `
            -Description "Creating $Kind dump" | Out-Null
    }

    return Assert-DumpFile -Path $Path -Kind $Kind
}

function Restore-LogicalDump {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if ($script:ResolvedToolMode -eq "Docker") {
        $remotePath = "/tmp/pricecrawler-restore-$([Guid]::NewGuid().ToString('N')).dump"
        try {
            Invoke-TextCommand -Executable "docker" -Arguments @("cp", $Path, "${DockerContainer}:$remotePath") -Description "Copying restore artifact" | Out-Null
            Invoke-TextCommand `
                -Executable "docker" `
                -Arguments @(
                    "exec", $DockerContainer,
                    "pg_restore",
                    "--username", $AdminUser,
                    "--dbname", $Database,
                    "--exit-on-error",
                    "--no-owner",
                    "--no-privileges",
                    $remotePath
                ) `
                -Description "Restoring database '$Database'" | Out-Null
        }
        finally {
            & docker exec $DockerContainer rm -f $remotePath 2>$null | Out-Null
        }
        return
    }

    Invoke-TextCommand `
        -Executable "pg_restore" `
        -Arguments @(
            "--host", $HostName,
            "--port", $Port.ToString(),
            "--username", $AdminUser,
            "--dbname", $Database,
            "--exit-on-error",
            "--no-owner",
            "--no-privileges",
            $Path
        ) `
        -Description "Restoring database '$Database'" | Out-Null
}

function Get-ProductionMarker {
    param([Parameter(Mandatory = $true)][string]$Database)

    $literal = Quote-Literal $Database
    return (Invoke-PsqlQuery -Database "postgres" -Sql "select coalesce(shobj_description(oid,'pg_database'),'') from pg_database where datname=$literal;" -Description "Production independence marker check").Trim()
}

function Assert-ProductionCanInitialize {
    param([Parameter(Mandatory = $true)][string]$Database)

    if (-not (Test-DatabaseExists -Database $Database)) {
        return
    }

    $marker = Get-ProductionMarker -Database $Database
    $hasApplicationTables = (Invoke-PsqlQuery -Database $Database -Sql "select to_regclass('public.schema_version') is not null or to_regclass('public.product') is not null;" -Description "Production application object check").Trim() -eq "t"
    $userTableCount = [int](Invoke-PsqlQuery -Database $Database -Sql "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace where n.nspname='public' and c.relkind in ('r','p');" -Description "Production user table check").Trim()

    if ($marker -match 'initial_bootstrap_completed=true' -or $hasApplicationTables -or $userTableCount -gt 0) {
        throw "Production database '$Database' is already initialized or contains application objects. Development-to-Production bootstrap is permanently refused. Future Production changes must use forward migrations."
    }
}

function Set-ProductionMarker {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][int]$SchemaVersion,
        [Parameter(Mandatory = $true)][string]$ApplicationVersion
    )

    $marker = "PriceCrawler; environment=Production; initial_bootstrap_completed=true; initial_bootstrap_source=Development; initial_bootstrap_application_version=$ApplicationVersion; initial_bootstrap_schema_version=$SchemaVersion; completed_at_utc=$([DateTime]::UtcNow.ToString('o'))"
    Invoke-PsqlQuery -Database "postgres" -Sql "comment on database $(Quote-Identifier $Database) is $(Quote-Literal $marker);" -Description "Writing Production independence marker" | Out-Null
}

function Grant-RuntimeAccess {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [string]$Role
    )

    if ([string]::IsNullOrWhiteSpace($Role)) {
        Write-OperatorLog "No runtime role was supplied for '$Database'. Provision a non-superuser login externally before application deployment." "WARN"
        return
    }

    Assert-SafeIdentifier -Value $Role -Name "Runtime role"
    $roleLiteral = Quote-Literal $Role
    $roleInfo = (Invoke-PsqlQuery -Database "postgres" -Sql "select rolsuper::text || '|' || rolcanlogin::text from pg_roles where rolname=$roleLiteral;" -Description "Runtime role validation").Trim()
    if ([string]::IsNullOrWhiteSpace($roleInfo)) {
        throw "Runtime role '$Role' does not exist. Create it through the secret-management/deployment process first."
    }
    if ($roleInfo -match '^true\|') {
        throw "Runtime role '$Role' is a superuser and cannot be used as a Stage/Production runtime identity."
    }

    $quotedRole = Quote-Identifier $Role
    Invoke-PsqlQuery -Database "postgres" -Sql "grant connect on database $(Quote-Identifier $Database) to $quotedRole;" -Description "Granting database connection to '$Role'" | Out-Null
    Invoke-PsqlQuery -Database $Database -Sql "grant usage on schema public to $quotedRole; grant select,insert,update,delete on all tables in schema public to $quotedRole; grant usage,select,update on all sequences in schema public to $quotedRole; grant execute on all functions in schema public to $quotedRole; grant execute on all procedures in schema public to $quotedRole;" -Description "Granting runtime access to '$Role'" | Out-Null
    Write-OperatorLog "Runtime grants applied. Database=$Database; Role=$Role; SchemaCreateGranted=false"
}

function Add-EnvironmentResult {
    param(
        [Parameter(Mandatory = $true)][string]$Environment,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][int]$SchemaVersion,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$RowCounts,
        [Parameter(Mandatory = $true)][string]$DataPolicy
    )

    $script:EnvironmentResults.Add([pscustomobject]@{
        Environment = $Environment
        Database = $Database
        SchemaVersion = $SchemaVersion
        RowCounts = $RowCounts
        DataPolicy = $DataPolicy
    })
}

function Write-BootstrapReport {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ApplicationVersion,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$DevelopmentCounts
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Database environments bootstrap report")
    $lines.Add("")
    $lines.Add("Generated UTC: $([DateTime]::UtcNow.ToString('o'))")
    $lines.Add("")
    $lines.Add("- Source: Development database ``$DevelopmentDatabase``")
    $lines.Add("- Expected schema version: $ExpectedVersion")
    $lines.Add("- Application version: ``$ApplicationVersion``")
    $lines.Add("- PostgreSQL endpoint: ``${HostName}:$Port``")
    $lines.Add("- Tool mode: ``$script:ResolvedToolMode``")
    $lines.Add("")
    $lines.Add("## Environment results")
    $lines.Add("")
    $lines.Add("| Environment | Database | Schema | Data policy |")
    $lines.Add("|---|---|---:|---|")
    foreach ($result in $script:EnvironmentResults) {
        $lines.Add("| $($result.Environment) | ``$($result.Database)`` | $($result.SchemaVersion) | $($result.DataPolicy) |")
    }
    $lines.Add("")
    $lines.Add("## Critical row counts")
    $lines.Add("")
    $header = "| Table | Development |"
    $divider = "|---|---:|"
    foreach ($result in $script:EnvironmentResults) {
        $header += " $($result.Environment) |"
        $divider += "---:|"
    }
    $lines.Add($header)
    $lines.Add($divider)
    foreach ($table in $script:BusinessTables) {
        $row = "| ``$table`` | $($DevelopmentCounts[$table]) |"
        foreach ($result in $script:EnvironmentResults) {
            $row += " $($result.RowCounts[$table]) |"
        }
        $lines.Add($row)
    }
    $lines.Add("")
    $lines.Add("## Logical dump and backup artifacts")
    $lines.Add("")
    $lines.Add("| Kind | Path | Bytes | SHA-256 |")
    $lines.Add("|---|---|---:|---|")
    foreach ($artifact in $script:ArtifactRecords) {
        $lines.Add("| $($artifact.Kind) | ``$($artifact.Path)`` | $($artifact.SizeBytes) | ``$($artifact.Sha256)`` |")
    }
    $lines.Add("")
    $lines.Add("## Production independence")
    $lines.Add("")
    if (@($script:EnvironmentResults | Where-Object Environment -eq "Production").Count -gt 0) {
        $lines.Add("Production was initialized exactly once from the verified Development logical dump. A durable database-level marker records completion, and the provisioning script refuses any future Development-to-Production overwrite.")
    }
    else {
        $lines.Add("Production was not selected by this execution.")
    }
    $lines.Add("")
    $lines.Add("> After initial bootstrap, Production must never be replaced from Development.")
    $lines.Add("")
    $lines.Add("Future Production schema changes use forward migrations only.")
    $lines.Add("")
    $lines.Add("## Remaining manual steps")
    $lines.Add("")
    $lines.Add("- Create environment-specific non-superuser login identities through the deployment secret store.")
    $lines.Add("- Apply approved runtime grants through the deployment process; do not rerun Production bootstrap.")
    $lines.Add("- Supply connection strings through external configuration; do not store credentials in the repository.")
    $lines.Add("- Apply future Stage/Production schema changes through deployment forward migrations before Web or Worker starts.")
    $lines.Add("")
    $lines.Add("## Restore command pattern")
    $lines.Add("")
    $lines.Add("Use ``pg_restore --exit-on-error --no-owner --no-privileges --dbname <empty-target> <verified-dump>`` with an authorized deployment identity. Never restore a Development dump over initialized Production.")
    $lines.Add("")
    $lines.Add("## Partial failure recovery")
    $lines.Add("")
    $lines.Add("The script never deletes Production automatically in error handling. If a Production database created by the current run fails before the independence marker is persisted, the operator log records the marker check, manual drop commands, retained verified dump, and exact guarded rerun command. Deletion is permitted only after proving that Production was never successfully introduced into service.")
    $lines.Add("")
    $lines.Add("If Stage replacement fails, the operator log records the partial target, verified Development dump, previous Stage backup when present, and exact guarded Stage rerun command. If Production fails after its marker is persisted, it is already independent: do not delete it and do not rerun bootstrap.")

    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
    Write-OperatorLog "Bootstrap report written. Path=$Path"
}

function Show-DryRunPlan {
    param(
        [Parameter(Mandatory = $true)][bool]$TestExists,
        [Parameter(Mandatory = $true)][bool]$StageExists,
        [Parameter(Mandatory = $true)][bool]$ProductionExists,
        [Parameter(Mandatory = $true)][string]$DevelopmentDumpPath,
        [Parameter(Mandatory = $true)][string]$StageBackupPath,
        [Parameter(Mandatory = $true)][string]$ProductionBackupPath
    )

    Write-Host ""
    Write-Host "Database environment initialization dry run"
    Write-Host "Tool mode: $script:ResolvedToolMode"
    Write-Host "Endpoint: ${HostName}:$Port"
    Write-Host "Development: $DevelopmentDatabase"
    Write-Host "Test: $TestDatabase (exists=$TestExists; replace=$([bool]$ReplaceExistingTest); selected=$([bool]($InitializeAll -or $InitializeTest)))"
    Write-Host "Stage: $StageDatabase (exists=$StageExists; replace=$([bool]$ReplaceExistingStage); selected=$([bool]($InitializeAll -or $InitializeStage)))"
    Write-Host "Production: $ProductionDatabase (exists=$ProductionExists; confirmation=$([bool]$ConfirmInitialProductionBootstrap); selected=$([bool]($InitializeAll -or $InitializeProduction)))"
    Write-Host "Development dump: $DevelopmentDumpPath"
    Write-Host "Stage backup: $StageBackupPath"
    Write-Host "Production backup: $ProductionBackupPath"
    Write-Host "Validation: schema version, critical objects, critical row counts, ValidateOnly startup after provisioning"
    Write-Host "No database, dump, backup, log, marker, or report was changed because -WhatIf is active."
}

trap {
    Write-FailureOnce -Message $_.Exception.Message
    Write-PartialFailureRecoveryOnce
    break
}

# Resolve repository-owned paths and validate arguments before any database access.
$scriptDirectory = Split-Path -Parent $PSCommandPath
$repositoryRoot = (Resolve-Path (Join-Path $scriptDirectory "..")).Path
$schemaContractPath = Join-Path $repositoryRoot "PriceCrawler.Infrastructure\Persistence\DatabaseSchema.cs"
$baselinePath = Join-Path $repositoryRoot "db\migrations\0001_baseline.sql"
if (-not (Test-Path -LiteralPath $schemaContractPath -PathType Leaf)) {
    throw "Database schema contract was not found: $schemaContractPath"
}
if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw "Baseline migration was not found: $baselinePath"
}

if ($InitializeAll -and ($InitializeTest -or $InitializeStage -or $InitializeProduction)) {
    throw "-InitializeAll cannot be combined with individual initialization switches."
}
if (-not ($InitializeAll -or $InitializeTest -or $InitializeStage -or $InitializeProduction)) {
    throw "Select at least one operation: -InitializeTest, -InitializeStage, -InitializeProduction, or -InitializeAll."
}

$initializeTestEffective = [bool]($InitializeAll -or $InitializeTest)
$initializeStageEffective = [bool]($InitializeAll -or $InitializeStage)
$initializeProductionEffective = [bool]($InitializeAll -or $InitializeProduction)

foreach ($entry in @(
    @{ Name = "AdminUser"; Value = $AdminUser },
    @{ Name = "DevelopmentDatabase"; Value = $DevelopmentDatabase },
    @{ Name = "TestDatabase"; Value = $TestDatabase },
    @{ Name = "StageDatabase"; Value = $StageDatabase },
    @{ Name = "ProductionDatabase"; Value = $ProductionDatabase }
)) {
    Assert-SafeIdentifier -Value $entry.Value -Name $entry.Name
}

$databaseNames = @($DevelopmentDatabase, $TestDatabase, $StageDatabase, $ProductionDatabase)
if (@($databaseNames | Sort-Object -Unique).Count -ne $databaseNames.Count) {
    throw "Development, Test, Stage, and Production database names must all be unique."
}

if ($initializeProductionEffective -and -not $ConfirmInitialProductionBootstrap) {
    throw "Production initialization requires -ConfirmInitialProductionBootstrap. There is no force or replacement override."
}

if (-not [string]::IsNullOrWhiteSpace($VerifiedDevelopmentDumpPath) -and -not ($initializeStageEffective -or $initializeProductionEffective)) {
    throw "-VerifiedDevelopmentDumpPath is valid only with Stage or Production initialization."
}
if (-not [string]::IsNullOrWhiteSpace($StageRuntimeRole) -or -not [string]::IsNullOrWhiteSpace($ProductionRuntimeRole)) {
    throw "Combined Stage/Production runtime roles are no longer supported. Run scripts/provision-database-runtime-roles.ps1 after database provisioning to create separate Web and Worker identities."
}

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repositoryRoot "artifacts\db"
}
elseif (-not [System.IO.Path]::IsPathRooted($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repositoryRoot $ArtifactsRoot
}
$ArtifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $repositoryRoot "database-environments-bootstrap-report.md"
}
elseif (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path $repositoryRoot $ReportPath
}
$ReportPath = [System.IO.Path]::GetFullPath($ReportPath)

$dumpDirectory = Join-Path $ArtifactsRoot "bootstrap"
$stageBackupDirectory = Join-Path $ArtifactsRoot "backups\stage"
$productionBackupDirectory = Join-Path $ArtifactsRoot "backups\production"
$logDirectory = Join-Path $ArtifactsRoot "logs"
if ([string]::IsNullOrWhiteSpace($VerifiedDevelopmentDumpPath)) {
    $developmentDumpPath = Join-Path $dumpDirectory "varprice-dev-v1-$script:Timestamp.dump"
}
else {
    if (-not [System.IO.Path]::IsPathRooted($VerifiedDevelopmentDumpPath)) {
        $VerifiedDevelopmentDumpPath = Join-Path $repositoryRoot $VerifiedDevelopmentDumpPath
    }
    $developmentDumpPath = [System.IO.Path]::GetFullPath($VerifiedDevelopmentDumpPath)
}
$stageBackupPath = Join-Path $stageBackupDirectory "varprice-stage-before-bootstrap-$script:Timestamp.dump"
$productionBackupPath = Join-Path $productionBackupDirectory "varprice-prod-initial-v1-$script:Timestamp.dump"
$script:LogPath = Join-Path $logDirectory "initialize-database-environments-$script:Timestamp.log"

$schemaContract = Get-ExpectedSchemaContract -ContractPath $schemaContractPath
$script:ResolvedToolMode = Resolve-ToolMode

if (-not $WhatIfPreference -and -not (Test-Path -LiteralPath $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

Write-OperatorLog "Starting database environment initialization. ToolMode=$script:ResolvedToolMode; Host=$HostName; Port=$Port; Development=$DevelopmentDatabase; Test=$TestDatabase; Stage=$StageDatabase; Production=$ProductionDatabase"

# Read-only preflight.
Invoke-PsqlQuery -Database "postgres" -Sql "select 1;" -Description "PostgreSQL connectivity check" | Out-Null
if (-not (Test-DatabaseExists -Database $DevelopmentDatabase)) {
    throw "Development database '$DevelopmentDatabase' does not exist."
}
$developmentVersion = Assert-DatabaseSchema -Database $DevelopmentDatabase -ExpectedVersion $schemaContract.Version
$developmentCounts = Get-RowCounts -Database $DevelopmentDatabase
$testExists = Test-DatabaseExists -Database $TestDatabase
$stageExists = Test-DatabaseExists -Database $StageDatabase
$productionExists = Test-DatabaseExists -Database $ProductionDatabase

if ($initializeTestEffective -and $testExists -and -not $ReplaceExistingTest) {
    throw "Test database '$TestDatabase' already exists. Use -ReplaceExistingTest only when Test is the selected disposable target."
}
if ($initializeStageEffective -and $stageExists -and -not $ReplaceExistingStage) {
    throw "Stage database '$StageDatabase' already exists. Use -ReplaceExistingStage to create a verified backup before replacement."
}
if ($initializeProductionEffective) {
    Assert-ProductionCanInitialize -Database $ProductionDatabase
}

if ($WhatIfPreference) {
    Show-DryRunPlan `
        -TestExists $testExists `
        -StageExists $stageExists `
        -ProductionExists $productionExists `
        -DevelopmentDumpPath $developmentDumpPath `
        -StageBackupPath $stageBackupPath `
        -ProductionBackupPath $productionBackupPath
    return
}

foreach ($directory in @($dumpDirectory, $stageBackupDirectory, $productionBackupDirectory, $logDirectory)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

try {
    $developmentDump = $null
    if ($initializeStageEffective -or $initializeProductionEffective) {
        Assert-DevelopmentQuiescent -Database $DevelopmentDatabase
        if (-not [string]::IsNullOrWhiteSpace($VerifiedDevelopmentDumpPath)) {
            $developmentDump = Assert-DumpFile -Path $developmentDumpPath -Kind "Reused verified Development bootstrap"
            Write-OperatorLog "Reusing explicitly supplied verified Development dump for recovery. Path=$($developmentDump.Path); Development schema/count validation still applies."
        }
        elseif ($PSCmdlet.ShouldProcess($DevelopmentDatabase, "Create verified logical bootstrap dump")) {
            $developmentDump = New-LogicalDump -Database $DevelopmentDatabase -Path $developmentDumpPath -Kind "Development bootstrap"
        }
        $script:RecoveryDevelopmentDumpPath = $developmentDump.Path
    }

    if ($initializeTestEffective -and $PSCmdlet.ShouldProcess($TestDatabase, "Recreate disposable Test database from baseline")) {
        if ($testExists) {
            Write-OperatorLog "Destructive Test replacement authorized. Database=$TestDatabase" "WARN"
            Remove-Database -Database $TestDatabase
        }
        New-Database -Database $TestDatabase
        Invoke-PsqlFile -Database $TestDatabase -Path $baselinePath
        Grant-RuntimeAccess -Database $TestDatabase -Role $TestRuntimeRole
        $version = Assert-DatabaseSchema -Database $TestDatabase -ExpectedVersion $schemaContract.Version
        $counts = Get-RowCounts -Database $TestDatabase
        foreach ($table in $script:BusinessTables) {
            if ([long]$counts[$table] -ne 0) {
                throw "Test data policy violation: '$table' contains $($counts[$table]) row(s) after baseline initialization."
            }
        }
        Add-EnvironmentResult -Environment "Test" -Database $TestDatabase -SchemaVersion $version -RowCounts $counts -DataPolicy "baseline structure only; no Development business data"
        Write-OperatorLog "Test initialization succeeded. Database=$TestDatabase; SchemaVersion=$version"
    }

    if ($initializeStageEffective -and $PSCmdlet.ShouldProcess($StageDatabase, "Back up, recreate, and restore Stage from Development logical dump")) {
        if ($stageExists) {
            New-LogicalDump -Database $StageDatabase -Path $stageBackupPath -Kind "Stage pre-bootstrap backup" | Out-Null
            Write-OperatorLog "Destructive Stage replacement authorized after verified backup. Database=$StageDatabase" "WARN"
            $script:StageReplacementStartedByCurrentRun = $true
            Remove-Database -Database $StageDatabase
        }
        else {
            $script:StageReplacementStartedByCurrentRun = $true
        }
        New-Database -Database $StageDatabase
        Restore-LogicalDump -Database $StageDatabase -Path $developmentDump.Path
        $version = Assert-DatabaseSchema -Database $StageDatabase -ExpectedVersion $schemaContract.Version
        $counts = Get-RowCounts -Database $StageDatabase
        Assert-RowCountsEqual -Expected $developmentCounts -Actual $counts -EnvironmentName "Stage"
        Add-EnvironmentResult -Environment "Stage" -Database $StageDatabase -SchemaVersion $version -RowCounts $counts -DataPolicy "initial consistent Development logical snapshot"
        $script:StageBootstrapCompletedByCurrentRun = $true
        Write-OperatorLog "Stage initialization succeeded. Database=$StageDatabase; SchemaVersion=$version; RowCountsMatch=true"
    }

    if ($initializeProductionEffective -and $PSCmdlet.ShouldProcess($ProductionDatabase, "Perform one-time Production bootstrap from Development logical dump")) {
        # Re-check immediately before mutation so a concurrent initialization cannot be overwritten.
        Assert-ProductionCanInitialize -Database $ProductionDatabase
        if (-not (Test-DatabaseExists -Database $ProductionDatabase)) {
            New-Database -Database $ProductionDatabase
            $script:ProductionCreatedByCurrentRun = $true
        }
        $script:ProductionRestoreStartedByCurrentRun = $true
        Restore-LogicalDump -Database $ProductionDatabase -Path $developmentDump.Path
        $version = Assert-DatabaseSchema -Database $ProductionDatabase -ExpectedVersion $schemaContract.Version
        $counts = Get-RowCounts -Database $ProductionDatabase
        Assert-RowCountsEqual -Expected $developmentCounts -Actual $counts -EnvironmentName "Production"
        Set-ProductionMarker -Database $ProductionDatabase -SchemaVersion $schemaContract.Version -ApplicationVersion $schemaContract.ApplicationVersion
        $script:ProductionMarkerPersistedByCurrentRun = $true
        $marker = Get-ProductionMarker -Database $ProductionDatabase
        if ($marker -notmatch 'initial_bootstrap_completed=true') {
            throw "Production independence marker was not persisted."
        }
        New-LogicalDump -Database $ProductionDatabase -Path $productionBackupPath -Kind "Production initial backup" | Out-Null
        Add-EnvironmentResult -Environment "Production" -Database $ProductionDatabase -SchemaVersion $version -RowCounts $counts -DataPolicy "one-time Development snapshot; now independent"
        Write-OperatorLog "Production initial bootstrap succeeded. Database=$ProductionDatabase; SchemaVersion=$version; RowCountsMatch=true; Independent=true"
    }

    Write-BootstrapReport -Path $ReportPath -ExpectedVersion $schemaContract.Version -ApplicationVersion $schemaContract.ApplicationVersion -DevelopmentCounts $developmentCounts
    Write-OperatorLog "Database environment initialization completed successfully."
}
catch {
    Write-FailureOnce -Message $_.Exception.Message
    Write-PartialFailureRecoveryOnce
    throw
}
