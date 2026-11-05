[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ContainerName = "var_postgres",
    [string]$DatabaseUser = "var",
    [string]$DatabaseName = "varprice",
    [string]$BackupDirectory = "D:\Backups\Varus\Postgres",
    # Skips the confirmation prompt (the backup is still mandatory).
    [switch]$Yes
)

# Applies pending forward migrations (db\migrations\NNNN_*.sql) to the working database of the
# release collector (MPC-96). The collector itself runs with StartupMode=ValidateOnly and never
# changes the schema, so a release that raises DatabaseSchema.ExpectedVersion needs this step
# before publish-collector.ps1.
#
# Order: read the current schema version -> verified backup (backup-database.ps1) -> apply every
# newer migration in order, each in its own transaction -> check the new version.
# Exit codes: 0 = up to date or migrated, 1 = failed (nothing after the failing migration is applied).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 pipes text to native programs as ASCII by default.
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$migrationsPath = Join-Path (Join-Path $repositoryRoot "db") "migrations"
$schemaContractPath = Join-Path $repositoryRoot "PriceCrawler.Infrastructure\Persistence\DatabaseSchema.cs"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Output ("[{0:yyyy-MM-dd HH:mm:ss}] migrate: {1}" -f (Get-Date), $Message)
}

function Get-ExpectedSchemaVersion {
    $contract = Get-Content -LiteralPath $schemaContractPath -Raw
    $match = [regex]::Match($contract, 'public\s+const\s+int\s+ExpectedVersion\s*=\s*(?<value>\d+)\s*;')
    if (-not $match.Success) {
        throw "Could not read DatabaseSchema.ExpectedVersion from $schemaContractPath"
    }
    return [int]$match.Groups["value"].Value
}

function Get-DatabaseSchemaVersion {
    $output = @(& docker exec $ContainerName psql -U $DatabaseUser -d $DatabaseName -tAX -v ON_ERROR_STOP=1 `
        -c "select coalesce(max(version), 0) from public.schema_version;" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read schema_version from $ContainerName/$DatabaseName : $($output -join ' ')"
    }
    return [int](($output -join "").Trim())
}

try {
    $expected = Get-ExpectedSchemaVersion
    $current = Get-DatabaseSchemaVersion
    Write-Step "Database $ContainerName/$DatabaseName is at schema version $current; this release expects $expected."

    if ($current -gt $expected) {
        throw "The database is newer than this release ($current > $expected). Publish a newer collector instead."
    }
    if ($current -eq $expected) {
        Write-Step "Nothing to apply."
        exit 0
    }

    $pending = @(Get-ChildItem -LiteralPath $migrationsPath -File -Filter "*.sql" |
        Where-Object { $_.Name -match '^(?<n>\d{4})_[a-z0-9_]+\.sql$' } |
        ForEach-Object { [pscustomobject]@{ Version = [int]$_.Name.Substring(0, 4); File = $_ } } |
        Where-Object { $_.Version -gt $current -and $_.Version -le $expected } |
        Sort-Object Version)
    $expectedVersions = @(($current + 1)..$expected)
    if ((@($pending | ForEach-Object Version) -join ",") -ne ($expectedVersions -join ",")) {
        throw "Migrations $($expectedVersions -join ', ') are required but db\migrations has: $(@($pending | ForEach-Object { $_.File.Name }) -join ', ')."
    }

    Write-Step "Pending: $(@($pending | ForEach-Object { $_.File.Name }) -join ', ')"
    if (-not $Yes -and -not $WhatIfPreference) {
        $answer = Read-Host "Apply to the WORKING database '$DatabaseName' after a backup? Type yes"
        if ($answer -ne "yes") {
            Write-Step "Cancelled."
            exit 1
        }
    }

    if (-not $PSCmdlet.ShouldProcess("$ContainerName/$DatabaseName", "backup and apply $($pending.Count) migration(s)")) {
        exit 0
    }

    Write-Step "Backup before migration"
    & (Join-Path $PSScriptRoot "backup-database.ps1") -ContainerName $ContainerName -DatabaseUser $DatabaseUser `
        -DatabaseName $DatabaseName -BackupDirectory $BackupDirectory -SkipRotation
    if ($LASTEXITCODE -ne 0) {
        throw "Backup failed; no migration was applied."
    }

    foreach ($migration in $pending) {
        Write-Step "Applying $($migration.File.Name)"
        # Each migration file has its own BEGIN/COMMIT and guard on the previous version.
        Get-Content -LiteralPath $migration.File.FullName -Raw -Encoding UTF8 |
            & docker exec -i $ContainerName psql -U $DatabaseUser -d $DatabaseName -X -q -v ON_ERROR_STOP=1
        if ($LASTEXITCODE -ne 0) {
            throw "$($migration.File.Name) failed (exit $LASTEXITCODE); the database stays at the previous version."
        }
        $now = Get-DatabaseSchemaVersion
        if ($now -ne $migration.Version) {
            throw "$($migration.File.Name) finished but schema version is $now, expected $($migration.Version)."
        }
    }

    Write-Step "Done: schema version $expected. Now publish the collector: Scripts\publish-collector.ps1"
    exit 0
}
catch {
    Write-Step "FAILED: $($_.Exception.Message)"
    exit 1
}
