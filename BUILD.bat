@echo off
chcp 65001 >nul
title "DeceiverFusionSlots20 - Build and Package"
cd /d "%~dp0"

echo ============================================================
echo  DeceiverFusionSlots20 : compile Main.cs and pack UMM zip
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"

echo.
echo --- window will stay open, check the message above ---
pause
