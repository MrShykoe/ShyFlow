@echo off
rem Builds test exe + release files + installer. Needs .NET 8 SDK and Inno Setup 6.3+.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-all.ps1"
pause
