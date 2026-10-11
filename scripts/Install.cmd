@echo off
rem Installs SensorPanelPin from this folder. Asks for administrator rights, then runs Install.ps1.
rem Bypass applies to this one script only: Windows blocks scripts that came in a downloaded ZIP.
fltmc >nul 2>&1 || (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
pushd "%SystemRoot%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" & pause & exit /b
