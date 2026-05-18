@echo off
setlocal

echo Starting Docker Desktop...

start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"

echo Waiting for Docker Engine...

set RETRIES=60

:WAIT_DOCKER
docker info >nul 2>&1
if %errorlevel%==0 goto DOCKER_READY

set /a RETRIES-=1
if %RETRIES%==0 goto DOCKER_FAILED

timeout /t 5 /nobreak >nul
goto WAIT_DOCKER

:DOCKER_READY
echo Docker Engine is ready.

cd /d "J:\Projects\c#\(!!!VARUS)"

docker context use desktop-linux

echo Starting PostgreSQL container...
docker compose up -d postgres
if errorlevel 1 goto COMPOSE_FAILED

echo Waiting for PostgreSQL readiness...

set PG_RETRIES=60

:WAIT_POSTGRES
docker compose exec -T postgres pg_isready -U postgres >nul 2>&1
if %errorlevel%==0 goto POSTGRES_READY

set /a PG_RETRIES-=1
if %PG_RETRIES%==0 goto POSTGRES_FAILED

timeout /t 3 /nobreak >nul
goto WAIT_POSTGRES

:POSTGRES_READY
echo PostgreSQL is ready.

echo Starting VarPrice Worker...

cd /d "j:\Projects\c#\VARUS_STAGE\crawler\Debug\net8.0\"
call "VarPrice.Worker.exe"

if errorlevel 1 goto WORKER_FAILED

echo Done.
exit /b 0

:DOCKER_FAILED
echo Docker Engine did not start in time.
exit /b 1

:COMPOSE_FAILED
echo docker compose up failed.
exit /b 2

:POSTGRES_FAILED
echo PostgreSQL did not become ready in time.
exit /b 3

:WORKER_FAILED
echo VarPrice.Worker.exe failed.
exit /b 4
