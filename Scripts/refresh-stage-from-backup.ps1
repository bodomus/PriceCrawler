[CmdletBinding(SupportsShouldProcess)]
param(
    # A backup of the working database (varprice). Default: the newest varus_*.backup in $BackupDirectory.
    [string]$BackupFile,
    [string]$BackupDirectory = "D:\Backups\Varus\Postgres",
    [string]$ContainerName = "var_postgres",
    [string]$DatabaseUser = "var",
    # The only allowed target. The parameter exists so the guard is explicit and testable.
    [ValidateSet("varprice_stage")][string]$TargetDatabase = "varprice_stage",
    [switch]$Force
)

# Replaces the development database varprice_stage with a backup of the working database varprice (MPC-90).
# Development (Web, Worker from bin\Debug, schema experiments) works only on varprice_stage, so it can be
# refreshed with real data whenever needed. The working database itself is only read (its backup file).
# Exit codes: 0 = refreshed, 1 = failed, 2 = cancelled by the user.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$protectedDatabases = @("varprice", "varprice_prod", "varprice_test", "postgres", "template0", "template1")
$containerBackupPath = "/tmp/varprice_stage_refresh.backup"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Output ("[{0:yyyy-MM-dd HH:mm:ss}] refresh-stage: {1}" -f (Get-Date), $Message)
}

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments, [Parameter(Mandatory)][string]$Description)
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& docker @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code ${exitCode}: $($output -join ' ')"
    }
    return $output
}

function Invoke-AdminSql {
    param([Parameter(Mandatory)][string]$Sql, [Parameter(Mandatory)][string]$Description)
    return Invoke-Docker -Description $Description -Arguments @(
        "exec", $ContainerName, "psql", "-U", $DatabaseUser, "-d", "postgres",
        "-v", "ON_ERROR_STOP=1", "-tAc", $Sql)
}

function Resolve-BackupFile {
    if ($BackupFile) {
        if (-not (Test-Path -LiteralPath $BackupFile -PathType Leaf)) {
            throw "Backup file not found: $BackupFile"
        }
        return (Get-Item -LiteralPath $BackupFile)
    }
    $newest = @(Get-ChildItem -LiteralPath $BackupDirectory -File -Filter "varus_*.backup" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1)
    if ($newest.Count -eq 0) {
        throw "No varus_*.backup files in $BackupDirectory. Run Scripts\backup_varus.bat first."
    }
    return $newest[0]
}

try {
    # Defense in depth on top of ValidateSet: never drop a protected database.
    if ($protectedDatabases -contains $TargetDatabase.ToLowerInvariant()) {
        throw "Refusing to replace protected database '$TargetDatabase'."
    }

    $backup = Resolve-BackupFile
    Write-Step ("source backup: {0} ({1:N0} bytes, {2:yyyy-MM-dd HH:mm})" -f $backup.FullName, $backup.Length, $backup.LastWriteTime)
    Write-Step "target database: $TargetDatabase (container $ContainerName)"

    $state = @(Invoke-Docker -Arguments @("inspect", "-f", "{{.State.Running}}", $ContainerName) -Description "docker inspect $ContainerName")
    if (($state -join "").Trim() -ne "true") {
        throw "Container '$ContainerName' is not running."
    }

    if (-not $Force -and -not $WhatIfPreference) {
        Write-Host ""
        Write-Host "All data in '$TargetDatabase' will be replaced by $($backup.Name)." -ForegroundColor Yellow
        Write-Host "The working database 'varprice' is not touched." -ForegroundColor Yellow
        $answer = Read-Host "Type $TargetDatabase to continue"
        if ($answer -ne $TargetDatabase) {
            Write-Step "cancelled"
            exit 2
        }
    }

    if (-not $PSCmdlet.ShouldProcess($TargetDatabase, "Replace with $($backup.Name)")) {
        exit 0
    }

    try {
        Invoke-Docker -Arguments @("cp", $backup.FullName, "${ContainerName}:$containerBackupPath") -Description "docker cp backup" | Out-Null
        Invoke-Docker -Arguments @("exec", $ContainerName, "pg_restore", "--list", $containerBackupPath) -Description "pg_restore --list (backup verification)" | Out-Null
        Write-Step "backup verified"

        Invoke-AdminSql -Description "drop $TargetDatabase" -Sql "DROP DATABASE IF EXISTS `"$TargetDatabase`" WITH (FORCE);" | Out-Null
        Invoke-AdminSql -Description "create $TargetDatabase" -Sql "CREATE DATABASE `"$TargetDatabase`" OWNER `"$DatabaseUser`";" | Out-Null
        Write-Step "database re-created"

        Invoke-Docker -Description "pg_restore into $TargetDatabase" -Arguments @(
            "exec", $ContainerName, "pg_restore", "-U", $DatabaseUser, "-d", $TargetDatabase,
            "--no-owner", "--no-privileges", "--exit-on-error", $containerBackupPath) | Out-Null
        Write-Step "restored"

        $comment = "PriceCrawler; environment=Development; refreshed_from=$($backup.Name); refreshed_at_utc=$([DateTime]::UtcNow.ToString('o'))"
        Invoke-AdminSql -Description "comment on $TargetDatabase" -Sql "COMMENT ON DATABASE `"$TargetDatabase`" IS '$comment';" | Out-Null
    }
    finally {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & docker exec $ContainerName rm -f $containerBackupPath 2>&1 | Out-Null
        $ErrorActionPreference = $previousPreference
    }

    $counts = @(Invoke-Docker -Description "row counts" -Arguments @(
        "exec", $ContainerName, "psql", "-U", $DatabaseUser, "-d", $TargetDatabase, "-tAc",
        "select 'product=' || (select count(*) from product) || '; price_snapshot=' || (select count(*) from price_snapshot) || '; product_catalog=' || (select count(*) from product_catalog);"))
    Write-Step "OK $TargetDatabase <- $($backup.Name): $(($counts -join '').Trim())"
    exit 0
}
catch {
    Write-Output ("[{0:yyyy-MM-dd HH:mm:ss}] refresh-stage: FAILED {1}" -f (Get-Date), $_.Exception.Message)
    exit 1
}
