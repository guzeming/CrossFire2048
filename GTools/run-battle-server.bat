@echo off
cd /d "%~dp0.."
if not exist "Builds\BattleServer\Blacktide.BattleServer.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "GTools\build-battle.ps1"
  if errorlevel 1 exit /b 1
)
if not exist "data\battle.key" (
  echo Start the account server first: GTools\run-server.bat
  pause
  exit /b 1
)
"Builds\BattleServer\Blacktide.BattleServer.exe" -batchmode -nographics --battle-server --battle-key "%CD%\data\battle.key" --logs "%CD%\Logs\Server" -logFile "%CD%\Logs\battle-unity.log" %*
