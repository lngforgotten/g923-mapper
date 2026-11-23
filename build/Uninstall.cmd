@echo off
rem Double-click to uninstall G923 Mapper. Add -RemoveData to also delete your profiles and settings.
title G923 Mapper uninstall
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
echo.
pause
