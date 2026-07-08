@echo off
setlocal
set "TC_HOME=%~dp0"
if not exist "%TC_HOME%thinkcomposer-path.cmd" if exist "%TC_HOME%Installer\thinkcomposer-path.cmd" set "TC_HOME=%TC_HOME%Installer\"
if /I "%~1"=="--help" goto help
if /I "%~1"=="-h" goto help
if /I "%~1"=="help" goto help
if /I "%~1"=="--add-to-path" goto add_to_path
if /I "%~1"=="--remove-from-path" goto remove_from_path
if /I "%~1"=="--path-status" goto path_status
"%TC_HOME%ThinkComposer.Cli.exe" %*
exit /b %ERRORLEVEL%

:help
"%TC_HOME%ThinkComposer.Cli.exe" %*
set "HELP_EXIT=%ERRORLEVEL%"
echo.
echo Installed shim helpers:
echo   thinkcomposer --add-to-path       Add this install folder to the machine PATH.
echo   thinkcomposer --remove-from-path  Remove this install folder from the machine PATH.
echo   thinkcomposer --path-status       Check whether this install folder is on PATH.
exit /b %HELP_EXIT%

:add_to_path
shift
call "%TC_HOME%thinkcomposer-path.cmd" add %*
exit /b %ERRORLEVEL%

:remove_from_path
shift
call "%TC_HOME%thinkcomposer-path.cmd" remove %*
exit /b %ERRORLEVEL%

:path_status
shift
call "%TC_HOME%thinkcomposer-path.cmd" status %*
exit /b %ERRORLEVEL%
