@echo off
setlocal
cd /d "%~dp0"
call load-env.bat

set "TEST_EXIT_CODE=1"
set "DOCKER_OS=%~1"

if not defined DOCKER_IMAGE (
    echo Missing DOCKER_IMAGE in .env
    goto test_done
)

if not defined DOCKER_OS (
    echo Missing Docker OS and context
    goto test_done
)

pwsh.exe -NoLogo -NoProfile -NonInteractive -File common/test-images.ps1 -DockerContext %DOCKER_OS% -ExpectedOs %DOCKER_OS% -Image tmp/%DOCKER_IMAGE%:latest
set "TEST_EXIT_CODE=%ERRORLEVEL%"

:test_done
pause
endlocal & exit /b %TEST_EXIT_CODE%
