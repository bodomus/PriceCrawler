@echo off
setlocal

set "CONTAINER_ID=4b3db4684be1"
set "DB_USER=var"
set "DB_NAME=varprice"
set "BACKUP_DIR=D:\Backups\Varus\Postgres"
set "BACKUP_NAME=varus"

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do set "CURDATE=%%i"

if not exist "%BACKUP_DIR%" (
    mkdir "%BACKUP_DIR%"
)

echo [1/2] Creating backup in container...
docker exec -t %CONTAINER_ID% pg_dump -U %DB_USER% -d %DB_NAME% -F c -f /tmp/varus.backup
if errorlevel 1 (
    echo Error while creating pg_dump
    exit /b 1
)

echo [2/2] Copy backup on the disk...
docker cp %CONTAINER_ID%:/tmp/varus.backup "%BACKUP_DIR%\%BACKUP_NAME%_%CURDATE%.backup"
if errorlevel 1 (
    echo Error while copying backup
    exit /b 1
)

echo Backup successfully created:
echo %BACKUP_DIR%\%BACKUP_NAME%_%CURDATE%.backup

endlocal
pause