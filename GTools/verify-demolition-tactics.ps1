param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = Join-Path $root '.build-check/bot-validation'
$output = Join-Path $root ('.build-check/demolition-tactics-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
$editor = Join-Path $copy 'Assets/Editor/TacticsValidation'
New-Item -ItemType Directory -Path $editor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Bot/VerifyDemolitionTactics.cs') -Destination $editor
$log = Join-Path $output 'unity.log'
$arguments = @('-batchmode','-quit','-projectPath',('"'+$copy+'"'),'-executeMethod','VerifyDemolitionTactics.Run',
    '--test-output',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $log -TimeoutSeconds 600
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'result.txt'))) { throw "Tactical route validation failed: $output" }
Write-Output "DEMOLITION_TACTICS_PASS $output"
