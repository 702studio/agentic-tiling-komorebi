@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0komorebi-update.ps1" %*
exit /b %ERRORLEVEL%
