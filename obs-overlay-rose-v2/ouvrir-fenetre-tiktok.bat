@echo off
cd /d "%~dp0"
title Overlay TikTok - fenetre application
echo 1. start-overlay.bat doit etre ouvert (serveur bridge)
echo 2. Une FENETRE APPLICATION separee va s'ouvrir (fond vert)
echo    - ce n'est PAS un onglet Brave/Chrome
echo    - TikTok capture cette fenetre (source Fenetre), pas une URL
echo 3. Dans TikTok Live Studio : source Fenetre / Window
echo 4. Active le fond vert (chroma key / green screen)
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

rem Profil isole : obligatoire pour que --app ouvre une vraie fenetre app
rem meme si Brave/Chrome/Edge est deja ouvert (sinon ca devient un onglet web).
set "PROFILE=%TEMP%\overlay-tiktok-app-profile"
if not exist "%PROFILE%" mkdir "%PROFILE%" >nul 2>&1

echo Navigateur : %BROWSER%
echo Mode       : fenetre application ^(TikTok Window Capture^)
echo.

start "" "%BROWSER%" --user-data-dir="%PROFILE%" --app="http://127.0.0.1:7689/?tiktok=1" --window-size=720,340 --window-position=80,80 --disable-features=TranslateUI --no-first-run --no-default-browser-check --disable-extensions --disable-sync

echo Fenetre application lancee.
echo Si tu ne vois qu'un onglet dans Brave : ferme Brave puis relance ce .bat
pause
