param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/top-down-validation',
    [switch]$UseExistingWeapons
)
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must be inside .build-check.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $validationRoot
$editorFolder = Join-Path $validationRoot 'Assets/Editor/TopDownValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/TopDown/VerifyTopDown.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/TopDown/TopDownVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'top-down-verification.log'
$method = if ($UseExistingWeapons) { 'VerifyTopDown.RunWithExistingWeapons' } else { 'VerifyTopDown.Run' }
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', $method, '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'TOP_DOWN_VERIFY_PASS' -Quiet)) { throw "Top-down verification failed: $logPath" }
Write-Output "Top-down verification passed: $logPath"
