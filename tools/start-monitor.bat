@echo off
chcp 65001 >nul
:: ScreenClipMonitor - launch screenshot path auto-copy
title Screenshot Monitor
powershell -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0ScreenClipMonitor.ps1"
pause
