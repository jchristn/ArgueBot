@echo off
rem Build the C# dashboard, then start or attach to its service. Forward --home and --port.
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK is required to build ArgueBot.
  exit /b 1
)
where npm >nul 2>nul
if errorlevel 1 (
  echo Node.js and npm are required to build the web dashboard.
  exit /b 1
)
if not exist dashboard\node_modules (
  call npm --prefix dashboard ci
  if errorlevel 1 exit /b 1
)
call npm --prefix dashboard run build
if errorlevel 1 exit /b 1
dotnet build src/ArgueBot.sln -c Release
if errorlevel 1 exit /b 1
dotnet src/ArgueBot.Cli/bin/Release/net10.0/ArgueBot.Cli.dll service web %*
exit /b %errorlevel%
