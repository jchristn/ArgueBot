@echo off
rem Run ArgueBot interactively in this terminal.
rem Usage: go-terminal.bat [options]   (see go-terminal.bat --help)
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

call npm start -- %*
