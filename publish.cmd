@echo off
rem Local unsigned build to out\FacePresence-<version>-win-x64.exe. Signed builds are made by CI (build\windows\build.ps1 + secrets).
set VERSION=%1
if "%VERSION%"=="" set VERSION=0.1.0
pwsh -NoProfile -File "%~dp0build\windows\build.ps1" -Version %VERSION%
