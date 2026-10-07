param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/training-menu-validation'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must be inside .build-check.' }
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
$failurePath = Join-Path $validationRoot 'training-menu-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor/TrainingMenuValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TrainingMenuValidation/VerifyTrainingMenu.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TrainingMenuValidation/TrainingMenuVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'training-menu-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyTrainingMenu.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "Training menu verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'TRAINING_MENU_VERIFY_PASS' -Quiet)) { throw "Training menu verification failed: $logPath" }
Write-Output "Training menu verification passed: $logPath"
