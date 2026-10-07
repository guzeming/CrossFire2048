@echo off
setlocal

pushd "%~dp0.."

echo Starting OperationBlacktide server...
echo.
echo Default command:
echo dotnet run --project "GServer\OperationBlacktide.Server\OperationBlacktide.Server.csproj" -- --port 7777 %*
echo.

dotnet run --project "GServer\OperationBlacktide.Server\OperationBlacktide.Server.csproj" -- --port 7777 %*

echo.
echo Server stopped.
popd
pause
exit /b 0
