@echo off
title JIGSAW LOCAL SERVER
echo ==================================================
echo   Starting JIGSAW Local Test Server...
echo ==================================================

start http://localhost:8080/
node server.js
pause
