param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationRoot
)
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$ValidationRoot) { $ValidationRoot = Join-Path $projectRoot '.build-check/firing-validation' }
New-Item -ItemType Directory -Path $ValidationRoot -Force | Out-Null
$ValidationRoot = (Resolve-Path -LiteralPath $ValidationRoot).Path
if ($ValidationRoot -eq $projectRoot) { throw 'Validation must run in a separate project copy.' }
$failurePath = Join-Path $ValidationRoot 'firing-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $ValidationRoot
$editorFolder = Join-Path $ValidationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Firing/VerifyTrainingWeapons.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Firing/FiringVerificationDriver.cs') -Destination (Join-Path $ValidationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Firing/UpperBodyAimVerification.cs') -Destination (Join-Path $ValidationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Firing/BulletEffectsVerification.cs') -Destination (Join-Path $ValidationRoot 'Assets')
$logPath = Join-Path $ValidationRoot 'firing-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $ValidationRoot + '"'), '-executeMethod', 'VerifyTrainingWeapons.BuildAndRun', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'FIRING_VERIFY_PASS' -Quiet)) {
    throw "Firing verification failed: $logPath"
}
Write-Output "Firing verification passed: $logPath"
