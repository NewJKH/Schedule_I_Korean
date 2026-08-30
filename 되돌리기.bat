@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0bake.ps1" -Restore
if errorlevel 1 echo [error] script failed - see message above
