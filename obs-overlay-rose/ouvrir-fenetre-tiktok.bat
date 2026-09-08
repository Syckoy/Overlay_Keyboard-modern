@echo off
cd /d "%~dp0"
title Overlay TikTok - fenetre
echo 1. start-overlay.bat doit etre ouvert
echo 2. Une fenetre Edge va s'ouvrir (fond vert)
echo 3. Dans TikTok Live Studio : source Fenetre / Window
echo    capture cette fenetre Edge
echo 4. Active le fond vert (chroma key / green screen)
echo.
start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --app="http://127.0.0.1:7689/?tiktok=1" --window-size=720,340 --disable-features=TranslateUI
if errorlevel 1 start "" "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" --app="http://127.0.0.1:7689/?tiktok=1" --window-size=720,340
echo Fenetre lancee.
pause
