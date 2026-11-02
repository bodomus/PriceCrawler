[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$AppRoot = "J:\Apps\PriceCrawler",
    [string]$TaskName = "PriceCrawler Daily Collect",
    [ValidatePattern('^\d{2}:\d{2}$')][string]$StartTime = "08:00",
    # Hourly restarts after $StartTime for this many hours: power cuts are frequent, and
    # scheduled-run.ps1 -Scheduled exits at once when today's run has already succeeded.
    [ValidateRange(0, 23)][int]$RetryHours = 14,
    [ValidateRange(1, 23)][int]$TimeLimitHours = 8,
    # The broken pre-MPC-89 task (crawler.bat -> missing VarPrice.Worker.exe).
    [string[]]$LegacyTaskNames = @("Magazine Crawler"),
    [switch]$KeepLegacyTask
)

# Registers (or updates) the Windows Task Scheduler task for the daily price collection (MPC-91).
# Run it once as the user who works on this computer; no administrator rights are needed.
#   Trigger:  daily at $StartTime, repeated every hour for $RetryHours hours, and as soon as possible
#             after a missed start (computer off or no power).
#   Action:   powershell -File <AppRoot>\scripts\scheduled-run.ps1 -Scheduled
#             (the copy made by publish-collector.ps1, not the repository file).
#   Runs only while the user is logged on: Docker Desktop starts with the user session.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $AppRoot "scripts\scheduled-run.ps1"
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "$scriptPath not found. Run Scripts\publish-collector.ps1 first: it copies the scheduled scripts there."
}

$userId = "$env:USERDOMAIN\$env:USERNAME"
$arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Scheduled -AppRoot `"$AppRoot`""
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments -WorkingDirectory $AppRoot

$trigger = New-ScheduledTaskTrigger -Daily -At $StartTime
if ($RetryHours -gt 0) {
    # A daily trigger cannot take a repetition directly in Windows PowerShell 5.1; borrow it from a one-time trigger.
    $repeat = New-ScheduledTaskTrigger -Once -At $StartTime `
        -RepetitionInterval (New-TimeSpan -Hours 1) `
        -RepetitionDuration (New-TimeSpan -Hours $RetryHours)
    $trigger.Repetition = $repeat.Repetition
}

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Hours $TimeLimitHours)

$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited

$description = "PriceCrawler: daily price collection into varprice (MPC-91). " +
    "Starts $StartTime, retries hourly for $RetryHours h; a run that already succeeded today is skipped. " +
    "Logs: $AppRoot\logs. Re-register with Scripts\register-scheduled-task.ps1."

if ($PSCmdlet.ShouldProcess($TaskName, "Register scheduled task for $userId")) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Principal $principal -Description $description -Force | Out-Null
    Write-Host "Registered task '$TaskName' for $userId."
}

if (-not $KeepLegacyTask) {
    foreach ($legacyName in $LegacyTaskNames) {
        $legacy = @(Get-ScheduledTask -TaskName $legacyName -ErrorAction SilentlyContinue)
        foreach ($task in $legacy) {
            if ($PSCmdlet.ShouldProcess("$($task.TaskPath)$($task.TaskName)", "Remove legacy scheduled task")) {
                try {
                    Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false
                    Write-Host "Removed legacy task '$($task.TaskPath)$($task.TaskName)'."
                }
                catch {
                    # A task created by an administrator or another user can only be removed from an elevated shell.
                    Write-Warning ("Could not remove legacy task '{0}{1}': {2} Remove it from an elevated PowerShell: Unregister-ScheduledTask -TaskName '{1}' -TaskPath '{0}' -Confirm:`$false" -f $task.TaskPath, $task.TaskName, $_.Exception.Message)
                }
            }
        }
    }
}

$registered = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $registered) {
    $info = $registered | Get-ScheduledTaskInfo
    Write-Host ""
    Write-Host "Task:      $TaskName"
    Write-Host "Action:    powershell.exe $arguments"
    Write-Host "Next run:  $($info.NextRunTime)"
    Write-Host "Last run:  $($info.LastRunTime) (result $($info.LastTaskResult))"
}
