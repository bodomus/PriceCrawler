@echo off
setlocal enabledelayedexpansion

REM Root = папка, где лежит этот .bat (ожидаем, что тут же лежит .sln)
set "ROOT=%~dp0"

REM Safety check: должен быть хотя бы один .sln в ROOT
pushd "%ROOT%" >nul
dir /b "*.sln" >nul 2>&1
if errorlevel 1 (
  echo [ERROR] No .sln found in "%ROOT%".
  echo Put this .bat next to your solution file (.sln) and run again.
  popd >nul
  exit /b 1
)
popd >nul

echo Root: "%ROOT%"
echo Deleting "bin" and "obj" folders...

for /d /r "%ROOT%" %%D in (bin obj) do (
  if exist "%%D\" (
    echo [DEL] "%%D"
    rmdir /s /q "%%D" 2>nul
  )
)

echo Done.
endlocal
exit /b 0
