@echo off
setlocal

set "CONTAINER_ID=b9c59ecb00ef"
set "DB_USER=var"
set "DB_NAME=varprice"
set "BACKUP_DIR=D:\Backups\Varus\Postgres"
set "BACKUP_NAME=varus_%~1.backup"
set "LOCAL_BACKUP=%BACKUP_DIR%\%BACKUP_NAME%"

if "%~1"=="" (
    echo Error: date was not specified.
    echo Usage example: restore_varus.bat 2026-03-15
    exit /b 1
)

if not exist "%LOCAL_BACKUP%" (
    echo Error: backup file was not found:
    echo %LOCAL_BACKUP%
    exit /b 1
)

echo WARNING: database %DB_NAME% will be restored
echo Source: %LOCAL_BACKUP%
choice /M "Continue"
if errorlevel 2 exit /b 0

echo [1/3] Copying backup to container...
docker cp "%LOCAL_BACKUP%" %CONTAINER_ID%:/tmp/varus_restore.backup
if errorlevel 1 (
    echo Error while copying backup to container
    exit /b 1
)

echo [2/3] Restoring database...
docker exec -t %CONTAINER_ID% pg_restore -U %DB_USER% -d %DB_NAME% -c /tmp/varus_restore.backup
if errorlevel 1 (
    echo Error while restoring database
    exit /b 1
)

echo [3/3] Removing temporary file from container...
docker exec -t %CONTAINER_ID% rm -f /tmp/varus_restore.backup

echo Restore completed successfully.
endlocal
pause