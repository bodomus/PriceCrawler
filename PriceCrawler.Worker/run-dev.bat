@echo off
rem Runs the Worker from its build output as Development (local database, appsettings.Local.json).
rem Usage: run-dev.bat collect-prices ^| catalog-refresh ^| vegetables ^| --help
setlocal
set DOTNET_ENVIRONMENT=Development
"%~dp0PriceCrawler.Worker.exe" %*
exit /b %ERRORLEVEL%
