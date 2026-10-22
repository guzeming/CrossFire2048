param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/gameplay-validation',
    [switch]$BuildPlayers
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = [IO.Path]::GetFullPath((Join-Path $root $ValidationDirectory))
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
$editor = Join-Path $copy 'Assets/Editor/ArchitectureValidation'
New-Item -ItemType Directory -Path $editor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Architecture/VerifyArchitecture.cs') -Destination $editor
$log = Join-Path $copy 'architecture-verification.log'
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log }
$arguments = @('-batchmode', '-projectPath', ('"' + $copy + '"'), '-executeMethod', 'VerifyArchitecture.Run', '-logFile', ('"' + $log + '"'))
if ($BuildPlayers) {
    $output = Join-Path $root ('.build-check/architecture-players-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $arguments += @('--verify-player-builds', '--output-root', ('"' + $output + '"'))
}
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $log -TimeoutSeconds 1800
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'ARCHITECTURE_VERIFY_PASS' -Quiet)) { throw "Architecture verification failed: $log" }
Write-Output "ARCHITECTURE_VERIFY_PASS $log"
