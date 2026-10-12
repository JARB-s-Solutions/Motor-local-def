@echo off
title Probar instalador HEXODUS BioSync
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\probar-instalador-exus.ps1"
if errorlevel 1 pause
