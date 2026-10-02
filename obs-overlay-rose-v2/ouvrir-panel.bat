@echo off
cd /d "%~dp0"
title Overlay V2 - panneau reglages
echo 1. start-overlay.bat doit etre ouvert (serveur bridge)
echo 2. Le panneau s'ouvre en fenetre application separee
echo 3. Change couleur, design, AZERTY/QWERTY, Super Glide
echo    - ca s'applique en direct sur l'overlay OBS
echo.

set "BROWSER="
if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" set "BROWSER=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not defined BROWSER if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" set "BROWSER=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not defined BROWSER if exist "%LocalAppData%\Microsoft\Edge\Application\msedge.exe" set "BROWSER=%LocalAppData%\Microsoft\Edge\Application\msedge.exe"
if not defined BROWSER if exist "%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe" set "BROWSER=%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe"
if not defined BROWSER if exist "%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe" set "BROWSER=%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe"
if not defined BROWSER if exist "%ProgramFiles%\Google\Chrome\Application\chrome.exe" set "BROWSER=%ProgramFiles%\Google\Chrome\Application\chrome.exe"
if not defined BROWSER if exist "%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe" set "BROWSER=%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"
if not defined BROWSER if exist "%LocalAppData%\Google\Chrome\Application\chrome.exe" set "BROWSER=%LocalAppData%\Google\Chrome\Application\chrome.exe"

if not defined BROWSER (
  echo Aucun navigateur trouve ^(Edge / Brave / Chrome^).
  echo Installe-en un puis relance ce fichier.
  pause
  exit /b 1
)

set "PROFILE=%TEMP%\overlay-panel-app-profile"
if not exist "%PROFILE%" mkdir "%PROFILE%" >nul 2>&1

echo Navigateur : %BROWSER%
echo Mode       : fenetre application
echo.

start "" "%BROWSER%" --user-data-dir="%PROFILE%" --app="http://127.0.0.1:7689/panel" --window-size=580,820 --window-position=100,40 --disable-features=TranslateUI --no-first-run --no-default-browser-check --disable-extensions --disable-sync

echo Panneau lance.
pause
