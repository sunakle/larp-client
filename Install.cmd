@echo off
powershell.exe -STA -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
if errorlevel 1 (echo Installation failed.)
pause
