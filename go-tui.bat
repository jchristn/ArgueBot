@echo off
rem Build and run the C# terminal interface. Forward all TUI arguments.
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK is required to build ArgueBot.
  exit /b 1
)
dotnet build src/ArgueBot.sln -c Release
if errorlevel 1 exit /b 1
dotnet src/ArgueBot.Tui/bin/Release/net10.0/ArgueBot.Tui.dll %*
exit /b %errorlevel%
