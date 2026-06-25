@echo off
setlocal

pushd "%~dp0.."

echo Starting CrossFire2048 server...
echo.
echo Default command:
echo dotnet run --project "GServer\CrossFire2048.Server\CrossFire2048.Server.csproj" -- --port 7777 %*
echo.

dotnet run --project "GServer\CrossFire2048.Server\CrossFire2048.Server.csproj" -- --port 7777 %*

echo.
echo Server stopped.
popd
pause
exit /b 0
