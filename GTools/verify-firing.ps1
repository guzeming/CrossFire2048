param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationRoot
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$ValidationRoot) { $ValidationRoot = Join-Path $projectRoot '.build-check/firing-validation' }
New-Item -ItemType Directory -Path $ValidationRoot -Force | Out-Null
$ValidationRoot = (Resolve-Path -LiteralPath $ValidationRoot).Path
if ($ValidationRoot -eq $projectRoot) { throw 'Validation must run in a separate project copy.' }
$failurePath = Join-Path $ValidationRoot 'firing-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $ValidationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $ValidationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FiringValidation/VerifyTrainingWeapons.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FiringValidation/FiringVerificationDriver.cs') -Destination (Join-Path $ValidationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FiringValidation/UpperBodyAimVerification.cs') -Destination (Join-Path $ValidationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FiringValidation/BulletEffectsVerification.cs') -Destination (Join-Path $ValidationRoot 'Assets')
$logPath = Join-Path $ValidationRoot 'firing-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $ValidationRoot + '"'), '-executeMethod', 'VerifyTrainingWeapons.BuildAndRun', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "Verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'FIRING_VERIFY_PASS' -Quiet)) {
    throw "Firing verification failed: $logPath"
}
Write-Output "Firing verification passed: $logPath"
