@echo off
cd /d "%~dp0.."
dotnet build GTools\LogViewer -c Release --nologo
if errorlevel 1 (pause & exit /b 1)
start "" "GTools\LogViewer\bin\Release\net8.0-windows\OperationBlacktide.LogViewer.exe" --logs "%CD%\Logs\Server"
