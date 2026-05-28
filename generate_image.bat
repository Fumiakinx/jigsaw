@echo off
chcp 65001 > nul
title パズル画像一発生成＆1920x1080自動切り出しツール
powershell -ExecutionPolicy Bypass -File "%~dp0scratch\generate_jigsaw_image.ps1"
pause
