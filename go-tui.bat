@echo off
rem Build and run the C# terminal interface for one framework, for example: go-tui.bat net8.0 --home .run\home
rem The first argument is the target framework (net8.0 or net10.0, default net10.0). Remaining arguments are forwarded to the TUI.
setlocal EnableDelayedExpansion
cd /d "%~dp0"
set "FRAMEWORK=net10.0"
set "FIRST=%~1"
if defined FIRST if not "!FIRST:~0,1!"=="-" (
  set "FRAMEWORK=!FIRST!"
  shift
)
if /i not "%FRAMEWORK%"=="net8.0" if /i not "%FRAMEWORK%"=="net10.0" (
  echo Unsupported framework '%FRAMEWORK%'. Use net8.0 or net10.0.
  exit /b 1
)
set "ARGS="
:collect
if "%~1"=="" goto run
set ARGS=!ARGS! %1
shift
goto collect
:run
where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK for %FRAMEWORK% is required to build ArgueBot.
  exit /b 1
)
dotnet build src/ArgueBot.Server/ArgueBot.Server.csproj -c Release -f %FRAMEWORK%
if errorlevel 1 exit /b 1
dotnet build src/ArgueBot.Tui/ArgueBot.Tui.csproj -c Release -f %FRAMEWORK%
if errorlevel 1 exit /b 1
dotnet src/ArgueBot.Tui/bin/Release/%FRAMEWORK%/ArgueBot.Tui.dll!ARGS!
exit /b %errorlevel%
