@echo off
rem Removes SensorPanelPin. Asks for administrator rights, then runs Uninstall.ps1.
rem The working folder moves out first, and everything after PowerShell sits on one line: this file
rem may be inside the folder being removed, and cmd reads a batch file line by line as it runs.
fltmc >nul 2>&1 || (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
pushd "%SystemRoot%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" & pause & exit /b
