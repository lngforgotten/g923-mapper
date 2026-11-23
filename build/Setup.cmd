@echo off
rem Double-click to install G923 Mapper for the current user. Extra options are passed on, e.g.:
rem   Setup.cmd -InstallDrivers -WithHidHide -DesktopShortcut
title G923 Mapper setup
echo Installing G923 Mapper...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Launch %*
echo.
pause
