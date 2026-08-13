@echo off
REM Double-click to play Glimmer Grove. Rebuilds the player first if sources changed.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Play.ps1" %*
if errorlevel 1 pause
