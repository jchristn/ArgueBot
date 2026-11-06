@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-all.bat ^<tag^>
    echo Example: build-all.bat v2.2.0
    endlocal
    exit /b 1
)

set "TAG=%~1"
set "IMAGE=jchristn77/arguebot"

pushd "%~dp0"

rem Build on the cloud builder Armada uses (cloud-jchristn77-jchristn77) for both architectures and push the multi-arch manifest to Docker Hub.
echo Building %IMAGE%:latest and %IMAGE%:%TAG%...
docker buildx build ^
    --builder cloud-jchristn77-jchristn77 ^
    --platform linux/amd64,linux/arm64/v8 ^
    -t %IMAGE%:latest ^
    -t %IMAGE%:%TAG% ^
    -f docker/app/Dockerfile ^
    --push ^
    .
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

rem Pull the pushed tags back from Docker Hub to update the local copy.
echo Pulling %IMAGE%:latest...
docker pull %IMAGE%:latest
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" goto :done

echo Pulling %IMAGE%:%TAG%...
docker pull %IMAGE%:%TAG%
set "EXIT_CODE=%ERRORLEVEL%"

:done
if "%EXIT_CODE%"=="0" echo === %IMAGE%:%TAG% and :latest built, pushed to Docker Hub and pulled locally ===
if "%EXIT_CODE%"=="0" echo Native installers are built separately with build-installers.bat; see packaging\README.md.
popd
endlocal & exit /b %EXIT_CODE%
