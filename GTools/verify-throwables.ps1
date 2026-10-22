param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/throwables-validation'
)
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'ValidationDirectory must be inside .build-check.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $validationRoot
$editorFolder = Join-Path $validationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Throwable/VerifyThrowables.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Throwable/ThrowableVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Throwable/ThrowableActionVerification.cs') -Destination (Join-Path $validationRoot 'Assets')
$failurePath = Join-Path $validationRoot 'throwables-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
$logPath = Join-Path $validationRoot 'throwables-verification.log'
$arguments = @('-batchmode','-projectPath',('"'+$validationRoot+'"'),'-executeMethod','VerifyThrowables.BuildAndRun','-logFile',('"'+$logPath+'"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'THROWABLES_VERIFY_PASS' -Quiet)) { throw "Throwable verification failed: $logPath" }
Write-Output "Throwable verification passed: $logPath"
