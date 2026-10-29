[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ContainerName = "var_postgres",
    [string]$DatabaseUser = "var",
    [string]$DatabaseName = "varprice",
    [string]$BackupDirectory = "D:\Backups\Varus\Postgres",
    [string]$FilePrefix = "varus",
    [ValidateRange(1, 365)][int]$DailyRetentionDays = 14,
    [ValidateRange(0, 260)][int]$WeeklyRetentionWeeks = 13,
    [switch]$SkipRotation,
    # Rotation only, no new backup and no Docker (used by tests and for a manual cleanup with -WhatIf).
    [switch]$RotateOnly,
    [datetime]$Now = (Get-Date)
)

# Verified logical backup of the working database (MPC-89).
# The container is addressed by name, so re-creating it does not break the backup.
# Exit codes: 0 = backup created and verified, 1 = backup failed.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# New-format names only: varus_yyyy-MM-dd_HHmmss.backup. Legacy varus_yyyy-MM-dd.backup files
# created by the old backup_varus.bat are never rotated (deleted) by this script.
$backupNamePattern = '^' + [regex]::Escape($FilePrefix) + '_(?<stamp>\d{4}-\d{2}-\d{2}_\d{6})\.backup$'

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Output ("[{0:yyyy-MM-dd HH:mm:ss}] backup: {1}" -f (Get-Date), $Message)
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

function Get-RotationCandidates {
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][datetime]$Now)

    $backups = @(Get-ChildItem -LiteralPath $Directory -File | ForEach-Object {
        $match = [regex]::Match($_.Name, $backupNamePattern)
        if ($match.Success) {
            [pscustomobject]@{
                File = $_
                CreatedAt = [datetime]::ParseExact($match.Groups["stamp"].Value, "yyyy-MM-dd_HHmmss", [Globalization.CultureInfo]::InvariantCulture)
            }
        }
    } | Sort-Object CreatedAt -Descending)

    $keep = @{}
    $dailyBoundary = $Now.Date.AddDays(-($DailyRetentionDays - 1))
    foreach ($backup in $backups | Where-Object { $_.CreatedAt -ge $dailyBoundary }) {
        $keep[$backup.File.FullName] = $true
    }

    # Older than the daily window: keep the newest backup of each week (weeks start on Monday)
    # for the last $WeeklyRetentionWeeks weeks.
    $weeklyBoundary = $Now.Date.AddDays(-7 * $WeeklyRetentionWeeks)
    $seenWeeks = @{}
    foreach ($backup in $backups | Where-Object { $_.CreatedAt -lt $dailyBoundary -and $_.CreatedAt -ge $weeklyBoundary }) {
        $offset = ([int]$backup.CreatedAt.DayOfWeek + 6) % 7
        $weekStart = $backup.CreatedAt.Date.AddDays(-$offset).ToString("yyyy-MM-dd")
        if (-not $seenWeeks.ContainsKey($weekStart)) {
            $seenWeeks[$weekStart] = $true
            $keep[$backup.File.FullName] = $true
        }
    }

    # Never rotate the newest backup, whatever the clock says.
    if ($backups.Count -gt 0) {
        $keep[$backups[0].File.FullName] = $true
    }

    return @($backups | Where-Object { -not $keep.ContainsKey($_.File.FullName) } | ForEach-Object File)
}

function Invoke-Rotation {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    $candidates = @(Get-RotationCandidates -Directory $BackupDirectory -Now $Now)
    foreach ($file in $candidates) {
        if ($PSCmdlet.ShouldProcess($file.FullName, "Delete rotated backup")) {
            Remove-Item -LiteralPath $file.FullName -Force
            Write-Step "rotated: $($file.Name)"
        }
    }
    Write-Step ("rotation done, {0} file(s) removed (daily {1} d, weekly {2} w)" -f $candidates.Count, $DailyRetentionDays, $WeeklyRetentionWeeks)
}

try {
    if ($RotateOnly) {
        Invoke-Rotation
        exit 0
    }

    $fileName = "{0}_{1:yyyy-MM-dd_HHmmss}.backup" -f $FilePrefix, $Now
    $containerPath = "/tmp/$fileName"
    $targetPath = Join-Path $BackupDirectory $fileName

    if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    }

    $state = @(Invoke-Docker -Arguments @("inspect", "-f", "{{.State.Running}}", $ContainerName) -Description "docker inspect $ContainerName")
    if (($state -join "").Trim() -ne "true") {
        throw "Container '$ContainerName' is not running."
    }

    Write-Step "pg_dump $DatabaseName -> $targetPath"
    if ($PSCmdlet.ShouldProcess($targetPath, "Create backup of $DatabaseName")) {
        try {
            Invoke-Docker -Arguments @("exec", $ContainerName, "pg_dump", "-U", $DatabaseUser, "-d", $DatabaseName, "-F", "c", "-f", $containerPath) -Description "pg_dump" | Out-Null
            Invoke-Docker -Arguments @("exec", $ContainerName, "pg_restore", "--list", $containerPath) -Description "pg_restore --list (backup verification)" | Out-Null
            Invoke-Docker -Arguments @("cp", "${ContainerName}:$containerPath", $targetPath) -Description "docker cp" | Out-Null
        }
        finally {
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            & docker exec $ContainerName rm -f $containerPath 2>&1 | Out-Null
            $ErrorActionPreference = $previousPreference
        }

        $size = (Get-Item -LiteralPath $targetPath).Length
        if ($size -le 0) {
            throw "Backup file is empty: $targetPath"
        }
        Write-Step ("verified, {0:N0} bytes" -f $size)
    }

    if (-not $SkipRotation) {
        Invoke-Rotation
    }

    Write-Step "OK $targetPath"
    exit 0
}
catch {
    Write-Output ("[{0:yyyy-MM-dd HH:mm:ss}] backup: FAILED {1}" -f (Get-Date), $_.Exception.Message)
    exit 1
}
