@echo off
cd /d "%~dp0"
title Overlay V2 - panneau reglages
echo 1. start-overlay.bat doit etre ouvert
echo 2. Le panneau de reglages va s'ouvrir dans Edge
echo 3. Change couleur, design, AZERTY/QWERTY, Super Glide
echo    - ca s'applique en direct sur l'overlay OBS
echo.
start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --app="http://127.0.0.1:7689/panel" --window-size=580,820 --disable-features=TranslateUI
if errorlevel 1 start "" "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" --app="http://127.0.0.1:7689/panel" --window-size=580,820
echo Panneau lance.
pause
