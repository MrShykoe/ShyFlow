@echo off
rem Пакет для Microsoft Store. Сначала заполни Package\store.json (см. STORE.md)
powershell -ExecutionPolicy Bypass -File "%~dp0build-store.ps1" %*
pause
