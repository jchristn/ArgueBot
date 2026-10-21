@echo off
rem Stop the selected ArgueBot service.
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET is required to stop the ArgueBot service.
  exit /b 1
)
if not exist src\ArgueBot.Cli\bin\Release\net10.0\ArgueBot.Cli.dll (
  dotnet build src\ArgueBot.Cli -c Release
  if errorlevel 1 exit /b 1
)
dotnet src\ArgueBot.Cli\bin\Release\net10.0\ArgueBot.Cli.dll service stop %*
exit /b %errorlevel%
