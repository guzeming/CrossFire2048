param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$ValidationDirectory) { $ValidationDirectory = '.build-check/loadout-regression-' + [Guid]::NewGuid().ToString('N').Substring(0,8) }
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must run inside .build-check.' }
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    & robocopy (Join-Path $projectRoot $folder) (Join-Path $validationRoot $folder) /E /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor/LoadoutValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LobbyValidation/VerifyLobbyLoadout.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LobbyValidation/LoadoutVerificationDriver.cs') -Destination (Join-Path $validationRoot 'Assets')
$logPath = Join-Path $validationRoot 'verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyLobbyLoadout.Run', '-logFile', ('"' + $logPath + '"'))
$verificationProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (!$verificationProcess.WaitForExit(900000)) {
    $verificationProcess.Kill()
    throw "Lobby verification timed out. See $logPath"
}
if ($verificationProcess.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'LOADOUT_VERIFY_PASS' -Quiet)) {
    throw "Lobby verification failed. See $logPath"
}
Write-Host "Lobby verification passed. Captures and log: $validationRoot"
