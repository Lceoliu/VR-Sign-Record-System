@echo off
setlocal
chcp 65001 >nul
title SignVR Recording Host

echo Starting SignVR Host...
echo Use a normal double-click. Do not run this BAT as Administrator.
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-portable.ps1"
set "SIGNVR_EXIT_CODE=%ERRORLEVEL%"

if not "%SIGNVR_EXIT_CODE%"=="0" (
    echo.
    echo SignVR Host stopped with error code %SIGNVR_EXIT_CODE%.
    echo See the message above, then press any key to close this window.
    pause >nul
)

exit /b %SIGNVR_EXIT_CODE%
