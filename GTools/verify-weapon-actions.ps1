param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/weapon-actions-validation'
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WeaponActionValidation/VerifyWeaponActions.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WeaponActionValidation/WeaponActionVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WeaponActionValidation/SidearmVerification.cs') -Destination (Join-Path $validationRoot 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WeaponActionValidation/PrimaryWeaponVerification.cs') -Destination (Join-Path $validationRoot 'Assets')
$failurePath = Join-Path $validationRoot 'weapon-actions-failure.txt'
if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
$logPath = Join-Path $validationRoot 'weapon-actions-verification.log'
$arguments = @('-batchmode','-projectPath',('"'+$validationRoot+'"'),'-executeMethod','VerifyWeaponActions.BuildAndRun','-logFile',('"'+$logPath+'"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verification.WaitForExit(900000)) { $verification.Kill(); throw "Weapon action verification timed out: $logPath" }
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'WEAPON_ACTIONS_VERIFY_PASS' -Quiet)) { throw "Weapon action verification failed: $logPath" }
Write-Output "Weapon action verification passed: $logPath"
