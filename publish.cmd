@echo off
rem Lokální nepodepsaný build do out\FacePresence-<verze>-win-x64.exe. Podepsaný build dělá CI (build\windows\build.ps1 + secrets).
set VERSION=%1
if "%VERSION%"=="" set VERSION=0.1.0
pwsh -NoProfile -File "%~dp0build\windows\build.ps1" -Version %VERSION%
