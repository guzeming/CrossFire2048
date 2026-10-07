param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/throwables-validation'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'ValidationDirectory must be inside .build-check.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThrowableValidation/VerifyThrowables.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThrowableValidation/ThrowableVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThrowableValidation/ThrowableActionVerification.cs') -Destination (Join-Path $validationRoot 'Assets')
$failurePath = Join-Path $validationRoot 'throwables-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
$logPath = Join-Path $validationRoot 'throwables-verification.log'
$arguments = @('-batchmode','-projectPath',('"'+$validationRoot+'"'),'-executeMethod','VerifyThrowables.BuildAndRun','-logFile',('"'+$logPath+'"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "Throwable verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'THROWABLES_VERIFY_PASS' -Quiet)) { throw "Throwable verification failed: $logPath" }
Write-Output "Throwable verification passed: $logPath"
