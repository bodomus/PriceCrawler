@echo off
setlocal

set "CONTAINER_ID=4b3db4684be1"
set "DB_USER=var"
set "DB_NAME=varprice"
set "BACKUP_DIR=D:\Backups\Varus\Postgres"
set "BACKUP_NAME=varus_%~1.backup"
set "LOCAL_BACKUP=%BACKUP_DIR%\%BACKUP_NAME%"

if "%~1"=="" (
    echo Ошибка: не указана дата.
    echo Пример использования: restore_varus.bat 2026-03-15
    exit /b 1
)

if not exist "%LOCAL_BACKUP%" (
    echo Ошибка: файл backup не найден:
    echo %LOCAL_BACKUP%
    exit /b 1
)

echo ВНИМАНИЕ: будет выполнено восстановление базы %DB_NAME%
echo Источник: %LOCAL_BACKUP%
choice /M "Продолжить"
if errorlevel 2 exit /b 0

echo [1/3] Копирование backup в контейнер...
docker cp "%LOCAL_BACKUP%" %CONTAINER_ID%:/tmp/varus_restore.backup
if errorlevel 1 (
    echo Ошибка при копировании backup в контейнер
    exit /b 1
)

echo [2/3] Восстановление базы...
docker exec -t %CONTAINER_ID% pg_restore -U %DB_USER% -d %DB_NAME% -c /tmp/varus_restore.backup
if errorlevel 1 (
    echo Ошибка при восстановлении базы
    exit /b 1
)

echo [3/3] Удаление временного файла из контейнера...
docker exec -t %CONTAINER_ID% rm -f /tmp/varus_restore.backup

echo Восстановление завершено успешно.
endlocal
pause