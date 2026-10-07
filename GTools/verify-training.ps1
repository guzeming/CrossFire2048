param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/training-validation',
    [switch]$UseExistingAssets
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ValidationDirectory must be inside this project .build-check directory.'
}
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor/TrainingValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TrainingValidation/VerifyTraining.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TrainingValidation/TrainingVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'verification.log'
$method = if ($UseExistingAssets) { 'VerifyTraining.RunExisting' } else { 'VerifyTraining.BuildAndRun' }
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', $method, '-logFile', ('"' + $logPath + '"'))
$verificationProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verificationProcess.WaitForExit(900000)) {
    $verificationProcess.Kill()
    throw "Training verification timed out. See $logPath"
}
if ($verificationProcess.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'TRAINING_VERIFY_PASS' -Quiet)) {
    throw "Training verification failed. See $logPath"
}
Write-Host "Training verification passed. Captures and log: $validationRoot"
