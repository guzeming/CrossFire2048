@echo off
setlocal

pushd "%~dp0.."

echo Building CrossFire2048 server...
dotnet build "GServer\CrossFire2048.Server\CrossFire2048.Server.csproj"

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
