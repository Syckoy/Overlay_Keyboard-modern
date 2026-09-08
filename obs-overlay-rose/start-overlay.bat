@echo off
cd /d "%~dp0"
chcp 65001 >nul
title Yeshua
mode con cols=78 lines=32
color 0F
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0yeshua.ps1"

set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Compilateur C# introuvable.
  pause
  exit /b 1
)
if not exist "bridge.exe" (
  "%CSC%" /nologo /t:exe /out:bridge.exe /optimize+ bridge.cs
  if errorlevel 1 (
    echo Echec compilation.
    pause
    exit /b 1
  )
)
bridge.exe
pause
