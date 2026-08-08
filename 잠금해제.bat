@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$cfg='C:\Program Files (x86)\Steam\steamapps\common\Schedule I\BepInEx\config\AutoTranslatorConfig.ini'; $me=\"$env:USERDOMAIN\$env:USERNAME\"; icacls $cfg /remove:d $me; Write-Host 'config unlock done'; pause"
