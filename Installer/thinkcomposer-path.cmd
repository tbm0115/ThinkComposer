@echo off
setlocal

set "SCRIPT=%~dp0thinkcomposer-path.ps1"
if not exist "%SCRIPT%" (
    echo Cannot find "%SCRIPT%".
    exit /b 1
)

if "%~1"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" add
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
)

exit /b %ERRORLEVEL%
