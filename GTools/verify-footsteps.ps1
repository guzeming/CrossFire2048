param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationRoot
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$ValidationRoot) { $ValidationRoot = Join-Path $projectRoot '.build-check/footstep-validation' }
New-Item -ItemType Directory -Path $ValidationRoot -Force | Out-Null
$ValidationRoot = (Resolve-Path -LiteralPath $ValidationRoot).Path
if ($ValidationRoot -eq $projectRoot) { throw 'Validation must run in a separate project copy.' }
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $ValidationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $ValidationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FootstepValidation/VerifyFootsteps.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FootstepValidation/FootstepVerificationDriver.cs') -Destination (Join-Path $ValidationRoot 'Assets')
$logPath = Join-Path $ValidationRoot 'footstep-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $ValidationRoot + '"'), '-executeMethod', 'VerifyFootsteps.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "Verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'FOOTSTEP_VERIFY_PASS' -Quiet)) {
    throw "Footstep verification failed: $logPath"
}
Write-Output "Footstep verification passed: $logPath"
