@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stop-local-server.ps1"
if errorlevel 1 pause
