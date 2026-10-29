@echo off
rem Manual backup of the working database (varprice). Same script as the daily scheduled run (MPC-89).
rem Extra arguments are passed to backup-database.ps1, e.g.: backup_varus.bat -SkipRotation
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0backup-database.ps1" %*
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
