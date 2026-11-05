@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-all.bat ^<tag^>
    echo Example: build-all.bat v2.1.0
    endlocal
    exit /b 1
)

set "TAG=%~1"

echo === Building ArgueBot image ===
call "%~dp0build-arguebot.bat" %TAG%
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo === ArgueBot image built for %TAG% and latest, pushed to Docker Hub, and pulled locally ===
echo Native installers are built separately with build-installers.bat; see packaging\README.md.

:done
endlocal & exit /b %EXIT_CODE%
