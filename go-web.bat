@echo off
rem Start the ArgueBot web UI on a localhost listener.
rem Usage: go-web.bat [--port N]
setlocal
cd /d "%~dp0"

where npm >nul 2>nul
if errorlevel 1 (
  echo npm was not found. Install Node.js 18+ from https://nodejs.org/
  exit /b 1
)

if not exist node_modules (
  echo Installing dependencies...
  call npm install
  if errorlevel 1 exit /b 1
)

call npm run web -- --host 127.0.0.1 %*
