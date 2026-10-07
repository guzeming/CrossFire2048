param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/hud-validation'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'ValidationDirectory must be inside .build-check.' }
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
$failurePath = Join-Path $validationRoot 'hud-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HudValidation/VerifyTrainingHud.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HudValidation/HudVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'hud-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyTrainingHud.BuildAndRun', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "HUD verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'HUD_VERIFY_PASS' -Quiet)) { throw "HUD verification failed: $logPath" }
Write-Output "HUD verification passed: $logPath"
