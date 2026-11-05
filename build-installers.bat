@echo off
setlocal
cd /d "%~dp0"
for %%C in (archive inno) do (
  dotnet run --project src/ArgueBot.Publisher -c Release -f net10.0 -- --channel %%C %*
  if errorlevel 1 exit /b 1
)
