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
  echo A .NET SDK 8 or newer, able to target %FRAMEWORK%, is required to build ArgueBot.
  exit /b 1
)
rem Build only the selected framework with any installed SDK that can target it. The repository's
rem pinned SDK applies to verification and packaging, so build from a directory with its own global.json.
set "ROOT=%~dp0"
set "LAUNCHER=%ROOT%.run\launcher"
if not exist "%LAUNCHER%" mkdir "%LAUNCHER%"
> "%LAUNCHER%\global.json" echo {"sdk":{"version":"8.0.100","rollForward":"latestMajor"}}
pushd "%LAUNCHER%"
dotnet build "%ROOT%src\ArgueBot.Server\ArgueBot.Server.csproj" -c Release -p:TargetFrameworks=%FRAMEWORK% -p:NuGetLockFilePath=obj\launcher.%FRAMEWORK%.lock.json
if errorlevel 1 (
  popd
  exit /b 1
)
dotnet build "%ROOT%src\ArgueBot.Tui\ArgueBot.Tui.csproj" -c Release -p:TargetFrameworks=%FRAMEWORK% -p:NuGetLockFilePath=obj\launcher.%FRAMEWORK%.lock.json
if errorlevel 1 (
  popd
  exit /b 1
)
popd
rem Run on a newer runtime when the selected one is not installed.
set "DOTNET_ROLL_FORWARD=Major"
dotnet src\ArgueBot.Tui\bin\Release\%FRAMEWORK%\ArgueBot.Tui.dll!ARGS!
exit /b %errorlevel%
