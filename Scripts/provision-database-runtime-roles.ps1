[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [ValidateSet("Auto", "Docker", "Native")]
    [string]$ToolMode = "Auto",

    [string]$DockerContainer = "var_postgres",
    [string]$HostName = "localhost",
    [ValidateRange(1, 65535)]
    [int]$Port = 5432,
    [string]$AdminUser = "var",
    [string]$ObjectOwnerRole,

    [string]$StageDatabase = "varprice_stage",
    [string]$ProductionDatabase = "varprice_prod",

    [string]$StageWebRole = "pricecrawler_stage_web",
    [string]$StageWorkerRole = "pricecrawler_stage_worker",
    [string]$ProductionWebRole = "pricecrawler_prod_web",
    [string]$ProductionWorkerRole = "pricecrawler_prod_worker",

    [string]$StageWebPasswordEnvironmentVariable = "PRICECRAWLER_STAGE_WEB_DB_PASSWORD",
    [string]$StageWorkerPasswordEnvironmentVariable = "PRICECRAWLER_STAGE_WORKER_DB_PASSWORD",
    [string]$ProductionWebPasswordEnvironmentVariable = "PRICECRAWLER_PROD_WEB_DB_PASSWORD",
    [string]$ProductionWorkerPasswordEnvironmentVariable = "PRICECRAWLER_PROD_WORKER_DB_PASSWORD"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:RuntimePasswords = @()

function Assert-SafeIdentifier {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[a-z_][a-z0-9_]{0,62}$') {
        throw "$Name '$Value' is not a safe unquoted PostgreSQL identifier."
    }
}

function Quote-Identifier {
    param([Parameter(Mandatory = $true)][string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function Quote-Literal {
    param([AllowEmptyString()][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Resolve-ToolMode {
    if ($ToolMode -eq "Native") {
        if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
            throw "Native PostgreSQL tool 'psql' was not found on PATH."
        }
        return "Native"
    }

    if ($ToolMode -eq "Docker") {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw "Docker CLI was not found."
        }
        & docker inspect $DockerContainer *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL Docker container '$DockerContainer' is unavailable."
        }
        return "Docker"
    }

    if (Get-Command psql -ErrorAction SilentlyContinue) {
        return "Native"
    }
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        & docker inspect $DockerContainer *> $null
        if ($LASTEXITCODE -eq 0) {
            return "Docker"
        }
    }
    throw "No PostgreSQL client is available. Install psql or select a running Docker container."
}

function Remove-Secrets {
    param(
        [AllowEmptyString()][string]$Text,
        [string[]]$Secrets
    )

    $sanitized = $Text
    foreach ($secret in $Secrets) {
        if (-not [string]::IsNullOrEmpty($secret)) {
            $sanitized = $sanitized.Replace($secret, "<redacted>")
        }
    }
    return $sanitized
}

function Invoke-Psql {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$User,
        [Parameter(Mandatory = $true)][string]$Sql,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$Password,
        [switch]$ExpectFailure
    )

    $previousPassword = $env:PGPASSWORD
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Native stderr is captured and evaluated through the process exit code below.
        $ErrorActionPreference = "Continue"
        if ($script:ResolvedToolMode -eq "Native" -and -not [string]::IsNullOrEmpty($Password)) {
            $env:PGPASSWORD = $Password
        }

        if ($script:ResolvedToolMode -eq "Docker") {
            $arguments = @(
                "exec", "-i", $DockerContainer, "psql",
                "--username", $User,
                "--dbname", $Database,
                "--no-psqlrc",
                "--set", "ON_ERROR_STOP=1",
                "--tuples-only",
                "--no-align",
                "--quiet"
            )
            $output = @($Sql | & docker @arguments 2>&1)
        }
        else {
            $arguments = @(
                "--host", $HostName,
                "--port", $Port,
                "--username", $User,
                "--dbname", $Database,
                "--no-psqlrc",
                "--set", "ON_ERROR_STOP=1",
                "--tuples-only",
                "--no-align",
                "--quiet"
            )
            $output = @($Sql | & psql @arguments 2>&1)
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $env:PGPASSWORD = $previousPassword
        $ErrorActionPreference = $previousErrorActionPreference
    }

    $combined = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    $safeOutput = Remove-Secrets -Text $combined -Secrets $script:RuntimePasswords
    if ($ExpectFailure) {
        if ($exitCode -eq 0) {
            throw "$Description unexpectedly succeeded. Runtime DDL protection is not effective."
        }
        return $safeOutput
    }
    if ($exitCode -ne 0) {
        throw "$Description failed. $safeOutput"
    }
    return $safeOutput.Trim()
}

function Get-RequiredSecret {
    param([Parameter(Mandatory = $true)][string]$EnvironmentVariable)

    $value = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required secret environment variable '$EnvironmentVariable' is not set. Passwords are never accepted as script parameters."
    }
    return $value
}

function New-OrUpdateRuntimeRole {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Password
    )

    $roleLiteral = Quote-Literal $Role
    $passwordLiteral = Quote-Literal $Password
    $sql = @"
do `$runtime_role`$
begin
    if exists (select 1 from pg_roles where rolname = $roleLiteral) then
        execute 'alter role ' || quote_ident($roleLiteral) || ' with login nosuperuser nocreatedb nocreaterole noinherit noreplication nobypassrls password ' || quote_literal($passwordLiteral);
    else
        execute 'create role ' || quote_ident($roleLiteral) || ' with login nosuperuser nocreatedb nocreaterole noinherit noreplication nobypassrls password ' || quote_literal($passwordLiteral);
    end if;
end
`$runtime_role`$;
"@

    # The generated SQL is sent only over stdin and is never logged or placed in process arguments.
    Invoke-Psql -Database "postgres" -User $AdminUser -Sql $sql -Description "Provisioning runtime role '$Role'" | Out-Null

    $attributes = Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select rolcanlogin::text || '|' || rolsuper::text || '|' || rolcreatedb::text || '|' || rolcreaterole::text || '|' || rolinherit::text || '|' || rolreplication::text || '|' || rolbypassrls::text from pg_roles where rolname=$roleLiteral;" -Description "Verifying runtime role '$Role'"
    if ($attributes -ne "true|false|false|false|false|false|false") {
        throw "Runtime role '$Role' has unsafe attributes: $attributes"
    }

    $membershipSql = @"
do `$runtime_memberships`$
declare
    granted_role record;
begin
    for granted_role in
        select parent.rolname
        from pg_auth_members membership
        join pg_roles parent on parent.oid = membership.roleid
        join pg_roles member on member.oid = membership.member
        where member.rolname = $roleLiteral
    loop
        execute format('revoke %I from %I', granted_role.rolname, $roleLiteral);
    end loop;
end
`$runtime_memberships`$;
"@
    Invoke-Psql -Database "postgres" -User $AdminUser -Sql $membershipSql -Description "Removing inherited role memberships from '$Role'" | Out-Null
}

function Grant-EnvironmentRuntimeAccess {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$WebRole,
        [Parameter(Mandatory = $true)][string]$WorkerRole
    )

    $databaseIdentifier = Quote-Identifier $Database
    $webIdentifier = Quote-Identifier $WebRole
    $workerIdentifier = Quote-Identifier $WorkerRole
    $ownerIdentifier = Quote-Identifier $ObjectOwnerRole
    $webRoleLiteral = Quote-Literal $WebRole

    $databaseSql = @"
revoke create, temporary on database $databaseIdentifier from public;
revoke all privileges on database $databaseIdentifier from $webIdentifier, $workerIdentifier;
grant connect on database $databaseIdentifier to $webIdentifier, $workerIdentifier;
"@
    Invoke-Psql -Database "postgres" -User $AdminUser -Sql $databaseSql -Description "Restricting database privileges for '$Database'" | Out-Null

    $webRoutines = @(
        "routine_support_trim_nullable", "routine_support_trim_required",
        "routine_support_run_status", "routine_support_queue_status",
        "crawler_run_start", "crawler_run_finish", "crawler_run_complete",
        "crawler_run_get_by_id", "crawler_run_stage_get", "crawler_run_get_recent", "crawler_run_get_aggregate",
        "ingestion_run_start", "ingestion_run_finish", "price_observation_store", "crawl_error_add",
        "price_collect_queue_enqueue", "price_collect_queue_enqueue_result", "price_collect_queue_reserve_batch",
        "price_collect_queue_mark_succeeded", "price_collect_queue_mark_retry", "price_collect_queue_mark_dead",
        "price_collect_queue_reap_expired", "price_collect_queue_has_outstanding", "price_collect_queue_get_run_stats"
    ) | ForEach-Object { Quote-Literal $_ }
    $webRoutineList = $webRoutines -join ","

    $workerRoutines = @(
        "routine_support_trim_nullable", "routine_support_trim_required",
        "routine_support_run_status", "routine_support_queue_status",
        "crawler_run_start", "crawler_run_finish", "crawler_run_complete",
        "ingestion_run_start", "ingestion_run_finish", "price_observation_store", "crawl_error_add",
        "price_collect_queue_enqueue", "price_collect_queue_enqueue_result", "price_collect_queue_reserve_batch",
        "price_collect_queue_mark_succeeded", "price_collect_queue_mark_retry", "price_collect_queue_mark_dead",
        "price_collect_queue_reap_expired", "price_collect_queue_has_outstanding", "price_collect_queue_get_run_stats",
        "product_catalog_refresh_start", "product_catalog_refresh_complete", "product_catalog_refresh_complete_with_run",
        "product_catalog_refresh_fail", "product_catalog_refresh_fail_with_run", "product_catalog_refresh_get_by_id",
        "product_catalog_upsert_discovered", "product_catalog_get_active_count", "product_catalog_deactivate_missing",
        "product_catalog_get_by_id", "product_catalog_get_by_source_normalized_url", "product_catalog_get_due",
        "product_catalog_mark_checked", "product_catalog_mark_failed", "product_catalog_release_reservations"
    ) | ForEach-Object { Quote-Literal $_ }
    $workerRoutineList = $workerRoutines -join ","

    $objectSql = @"
revoke create on schema public from public;
revoke all privileges on schema public from $webIdentifier, $workerIdentifier;
grant usage on schema public to $webIdentifier, $workerIdentifier;

revoke all privileges on all tables in schema public from public, $webIdentifier, $workerIdentifier;
revoke all privileges on all sequences in schema public from public, $webIdentifier, $workerIdentifier;
revoke execute on all functions in schema public from public, $webIdentifier, $workerIdentifier;
revoke execute on all procedures in schema public from public, $webIdentifier, $workerIdentifier;

grant select on
    public.schema_version,
    public.crawler_run,
    public.crawler_run_stage,
    public.ingestion_run,
    public.price_collect_queue,
    public.product,
    public.price_snapshot,
    public.crawl_error
to $webIdentifier;
grant insert, update, delete on
    public.crawler_run,
    public.crawler_run_stage,
    public.ingestion_run,
    public.price_collect_queue,
    public.product,
    public.price_snapshot,
    public.crawl_error
to $webIdentifier;
grant usage, select, update on
    public.crawler_run_id_seq,
    public.crawler_run_stage_id_seq,
    public.ingestion_run_ingestion_run_id_seq,
    public.price_collect_queue_id_seq,
    public.product_id_seq,
    public.price_snapshot_id_seq,
    public.crawl_error_id_seq
to $webIdentifier;

grant select, insert, update, delete on
    public.crawler_run,
    public.crawler_run_stage,
    public.ingestion_run,
    public.price_collect_queue,
    public.product,
    public.price_snapshot,
    public.crawl_error,
    public.product_catalog,
    public.product_catalog_refresh
to $workerIdentifier;
grant select on public.schema_version to $workerIdentifier;
grant usage, select, update on
    public.crawler_run_id_seq,
    public.crawler_run_stage_id_seq,
    public.ingestion_run_ingestion_run_id_seq,
    public.price_collect_queue_id_seq,
    public.product_id_seq,
    public.price_snapshot_id_seq,
    public.crawl_error_id_seq,
    public.product_catalog_id_seq,
    public.product_catalog_refresh_id_seq
to $workerIdentifier;

do `$web_routines`$
declare
    routine record;
begin
    for routine in
        select p.oid, p.prokind
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.proname in ($webRoutineList)
    loop
        execute format(
            'grant execute on %s %s to %I',
            case when routine.prokind = 'p' then 'procedure' else 'function' end,
            routine.oid::regprocedure,
            $webRoleLiteral);
    end loop;
end
`$web_routines`$;

do `$worker_routines`$
declare
    routine record;
begin
    for routine in
        select p.oid, p.prokind
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'
          and p.proname in ($workerRoutineList)
    loop
        execute format(
            'grant execute on %s %s to %I',
            case when routine.prokind = 'p' then 'procedure' else 'function' end,
            routine.oid::regprocedure,
            $(Quote-Literal $WorkerRole));
    end loop;
end
`$worker_routines`$;

alter default privileges for role $ownerIdentifier in schema public revoke execute on routines from public;
alter default privileges for role $ownerIdentifier in schema public
    revoke all privileges on tables from public;
alter default privileges for role $ownerIdentifier in schema public
    revoke all privileges on sequences from public;
"@
    Invoke-Psql -Database $Database -User $AdminUser -Sql $objectSql -Description "Applying least-privilege runtime grants to '$Database'" | Out-Null
}

function Assert-RuntimeRoleSafety {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$Password
    )

    $roleLiteral = Quote-Literal $Role
    $databaseLiteral = Quote-Literal $Database
    $databasePrivileges = Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select has_database_privilege($roleLiteral,$databaseLiteral,'CONNECT')::text || '|' || has_database_privilege($roleLiteral,$databaseLiteral,'CREATE')::text || '|' || has_database_privilege($roleLiteral,$databaseLiteral,'TEMPORARY')::text;" -Description "Checking database privileges for '$Role'"
    if ($databasePrivileges -ne "true|false|false") {
        throw "Runtime role '$Role' has unexpected database privileges: $databasePrivileges"
    }

    $schemaPrivileges = Invoke-Psql -Database $Database -User $AdminUser -Sql "select has_schema_privilege($roleLiteral,'public','USAGE')::text || '|' || has_schema_privilege($roleLiteral,'public','CREATE')::text;" -Description "Checking schema privileges for '$Role'"
    if ($schemaPrivileges -ne "true|false") {
        throw "Runtime role '$Role' has unexpected schema privileges: $schemaPrivileges"
    }

    $ownsDatabase = Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select exists(select 1 from pg_database d join pg_roles r on r.oid=d.datdba where d.datname=$databaseLiteral and r.rolname=$roleLiteral);" -Description "Checking database ownership for '$Role'"
    $ownsSchema = Invoke-Psql -Database $Database -User $AdminUser -Sql "select exists(select 1 from pg_namespace n join pg_roles r on r.oid=n.nspowner where n.nspname='public' and r.rolname=$roleLiteral);" -Description "Checking schema ownership for '$Role'"
    $ownedObjects = Invoke-Psql -Database $Database -User $AdminUser -Sql "select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace join pg_roles r on r.oid=c.relowner where n.nspname='public' and r.rolname=$roleLiteral;" -Description "Checking object ownership for '$Role'"
    if ($ownsDatabase -eq "t" -or $ownsSchema -eq "t") {
        throw "Runtime role '$Role' owns database/schema resources and would bypass runtime grants."
    }
    if ([int]$ownedObjects -ne 0) {
        throw "Runtime role '$Role' owns $ownedObjects public object(s); ownership would bypass runtime grants."
    }

    $membershipCount = Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select count(*) from pg_auth_members membership join pg_roles member on member.oid=membership.member where member.rolname=$roleLiteral;" -Description "Checking role memberships for '$Role'"
    if ([int]$membershipCount -ne 0) {
        throw "Runtime role '$Role' retains $membershipCount role membership(s) and could bypass its direct grants."
    }

    $version = Invoke-Psql -Database $Database -User $Role -Password $Password -Sql "select max(version) from schema_version;" -Description "ValidateOnly schema read as '$Role'"
    if ($version -ne "1") {
        throw "Runtime role '$Role' read unexpected schema version '$version'."
    }

    Invoke-Psql -Database $Database -User $Role -Password $Password -Sql "begin; create table public.__pricecrawler_runtime_create_probe(id integer); rollback;" -Description "CREATE TABLE probe for '$Role'" -ExpectFailure | Out-Null
    Invoke-Psql -Database $Database -User $Role -Password $Password -Sql "begin; alter table public.schema_version add column __pricecrawler_runtime_alter_probe integer; rollback;" -Description "ALTER TABLE probe for '$Role'" -ExpectFailure | Out-Null
}

foreach ($entry in @(
    @{ Name = "AdminUser"; Value = $AdminUser },
    @{ Name = "StageDatabase"; Value = $StageDatabase },
    @{ Name = "ProductionDatabase"; Value = $ProductionDatabase },
    @{ Name = "StageWebRole"; Value = $StageWebRole },
    @{ Name = "StageWorkerRole"; Value = $StageWorkerRole },
    @{ Name = "ProductionWebRole"; Value = $ProductionWebRole },
    @{ Name = "ProductionWorkerRole"; Value = $ProductionWorkerRole }
)) {
    Assert-SafeIdentifier -Value $entry.Value -Name $entry.Name
}

if ([string]::IsNullOrWhiteSpace($ObjectOwnerRole)) {
    $ObjectOwnerRole = $AdminUser
}
Assert-SafeIdentifier -Value $ObjectOwnerRole -Name "ObjectOwnerRole"

$roleNames = @($StageWebRole, $StageWorkerRole, $ProductionWebRole, $ProductionWorkerRole)
if (@($roleNames | Sort-Object -Unique).Count -ne $roleNames.Count) {
    throw "Stage/Production Web and Worker role names must all be unique."
}
if ($roleNames -contains $AdminUser -or $roleNames -contains $ObjectOwnerRole) {
    throw "Runtime roles must be separate from the administrative and object-owner identities."
}
if ($StageDatabase -eq $ProductionDatabase) {
    throw "Stage and Production database names must differ."
}

$script:ResolvedToolMode = Resolve-ToolMode
$roleSpecs = @(
    @{ Environment = "Stage"; Host = "Web"; Database = $StageDatabase; Role = $StageWebRole; SecretVariable = $StageWebPasswordEnvironmentVariable },
    @{ Environment = "Stage"; Host = "Worker"; Database = $StageDatabase; Role = $StageWorkerRole; SecretVariable = $StageWorkerPasswordEnvironmentVariable },
    @{ Environment = "Production"; Host = "Web"; Database = $ProductionDatabase; Role = $ProductionWebRole; SecretVariable = $ProductionWebPasswordEnvironmentVariable },
    @{ Environment = "Production"; Host = "Worker"; Database = $ProductionDatabase; Role = $ProductionWorkerRole; SecretVariable = $ProductionWorkerPasswordEnvironmentVariable }
)

Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select 1;" -Description "PostgreSQL connectivity check" | Out-Null
foreach ($database in @($StageDatabase, $ProductionDatabase)) {
    $exists = Invoke-Psql -Database "postgres" -User $AdminUser -Sql "select exists(select 1 from pg_database where datname=$(Quote-Literal $database));" -Description "Checking database '$database'"
    if ($exists -ne "t") {
        throw "Database '$database' does not exist. Runtime-role provisioning never creates or restores databases."
    }
    $version = Invoke-Psql -Database $database -User $AdminUser -Sql "select max(version) from schema_version;" -Description "Validating schema version for '$database'"
    if ($version -ne "1") {
        throw "Database '$database' has schema version '$version'; expected '1'. No migration or bootstrap was attempted."
    }
}

if ($WhatIfPreference) {
    Write-Host "Runtime role provisioning dry run"
    Write-Host "Tool mode: $script:ResolvedToolMode"
    Write-Host "Stage: $StageDatabase -> Web=$StageWebRole; Worker=$StageWorkerRole"
    Write-Host "Production: $ProductionDatabase -> Web=$ProductionWebRole; Worker=$ProductionWorkerRole"
    Write-Host "Credentials: required from the four named environment variables; values were not read or displayed."
    Write-Host "Planned validation: role attributes, ownership, ValidateOnly schema read, CREATE TABLE denial, ALTER TABLE denial."
    Write-Host "No role, grant, schema, database, or credential was changed because -WhatIf is active."
    return
}

foreach ($spec in $roleSpecs) {
    $spec.Password = Get-RequiredSecret -EnvironmentVariable $spec.SecretVariable
    $script:RuntimePasswords += $spec.Password
}

if (-not $PSCmdlet.ShouldProcess("$StageDatabase and $ProductionDatabase", "Create/update four runtime login roles and apply non-DDL grants")) {
    return
}

foreach ($spec in $roleSpecs) {
    New-OrUpdateRuntimeRole -Role $spec.Role -Password $spec.Password
}
Grant-EnvironmentRuntimeAccess -Database $StageDatabase -WebRole $StageWebRole -WorkerRole $StageWorkerRole
Grant-EnvironmentRuntimeAccess -Database $ProductionDatabase -WebRole $ProductionWebRole -WorkerRole $ProductionWorkerRole

foreach ($spec in $roleSpecs) {
    Assert-RuntimeRoleSafety -Database $spec.Database -Role $spec.Role -Password $spec.Password
    Write-Host "Verified $($spec.Environment) $($spec.Host) runtime role '$($spec.Role)': ValidateOnly read succeeded; CREATE TABLE denied; ALTER TABLE denied."
}

Write-Host "Runtime role provisioning completed. No migration, baseline, bootstrap, or schema-version change was executed."
