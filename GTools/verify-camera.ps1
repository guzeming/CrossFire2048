param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/camera-validation'
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
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor/CameraValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CameraValidation/VerifyCameraCutaway.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CameraValidation/CameraCutawayVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'camera-verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyCameraCutaway.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) {
    $verification.Kill()
    throw "Camera verification timed out: $logPath"
}
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'CAMERA_VERIFY_PASS' -Quiet)) {
    throw "Camera verification failed: $logPath"
}
Write-Output "Camera verification passed. Captures and log: $validationRoot"
