@echo off
setlocal

pushd "%~dp0.."

echo Building OperationBlacktide server...
dotnet build "GServer\OperationBlacktide.Server\OperationBlacktide.Server.csproj"

if errorlevel 1 (
    echo.
    echo Build failed.
    popd
    pause
    exit /b 1
)

echo.
echo Build succeeded.
popd
pause
exit /b 0
