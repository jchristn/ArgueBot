@echo off
setlocal
cd /d "%~dp0"
rem This application has a local build; refresh base images before rebuilding.
docker compose build --pull
if errorlevel 1 exit /b 1
docker compose down
if errorlevel 1 exit /b 1
docker compose up -d --wait
if errorlevel 1 exit /b 1
docker ps -a
