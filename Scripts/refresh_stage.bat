@echo off
rem Replaces the development database varprice_stage with the newest backup of varprice (MPC-90).
rem Asks for confirmation. A specific file: refresh_stage.bat -BackupFile D:\Backups\Varus\Postgres\varus_2026-10-30_114414.backup
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0refresh-stage-from-backup.ps1" %*
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
