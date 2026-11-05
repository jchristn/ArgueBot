@echo off
setlocal
cd /d "%~dp0"
rem Pull the published ArgueBot image and recreate the stack. Volumes (data, keys, work files) are kept.
docker compose pull
if errorlevel 1 exit /b 1
docker compose down
if errorlevel 1 exit /b 1
docker compose up -d --wait
if errorlevel 1 exit /b 1
docker ps -a
