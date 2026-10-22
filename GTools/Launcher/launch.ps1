param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $PSScriptRoot 'OperationBlacktide.Launcher.csproj'
$executable = Join-Path $PSScriptRoot 'bin/Release/net8.0-windows/Blacktide.Launcher.exe'
$assembly = Join-Path $PSScriptRoot 'bin/Release/net8.0-windows/Blacktide.Launcher.dll'
# Shortcuts contain local absolute paths; refresh them when this checkout moves.
& (Join-Path $PSScriptRoot 'create-shortcut.ps1')
$running = Get-Process -Name 'Blacktide.Launcher' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
if ($running -and !$BuildOnly) {
    Start-Process -FilePath $executable -ArgumentList @('--root', ('"' + $root + '"')) -WorkingDirectory $root
    exit 0
}
$rebuild = !(Test-Path -LiteralPath $assembly) -or !(Test-Path -LiteralPath $executable)
if (!$rebuild) {
    $builtAt = (Get-Item -LiteralPath $assembly).LastWriteTimeUtc
    $rebuild = [bool](Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.cs', '.xaml', '.csproj', '.ico') -and $_.LastWriteTimeUtc -gt $builtAt })
}
if ($rebuild) {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The launcher requires the .NET 8 SDK. Install it, then open this launcher again.' }
    dotnet build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed. See the output above.' }
}
if (!$BuildOnly) { Start-Process -FilePath $executable -ArgumentList @('--root', ('"' + $root + '"')) -WorkingDirectory $root }
