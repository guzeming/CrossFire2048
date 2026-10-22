param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/impact-audio-validation'
)
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must be inside .build-check.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
Write-Output '[1/3] Preparing impact audio verification project.'
$assetCopy = Join-Path $validationRoot 'Assets'
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $validationRoot
$editorFolder = Join-Path $assetCopy 'Editor/ImpactAudioValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/ImpactAudio/VerifyImpactAudio.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/ImpactAudio/ImpactAudioVerificationDriver.cs') -Destination $assetCopy
$logPath = Join-Path $validationRoot 'impact-audio-verification.log'
if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath }
Write-Output '[2/3] Starting Unity: import resources, compile scripts, then verify impact audio.'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyImpactAudio.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath
Write-Output '[3/3] Checking verification results.'
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'IMPACT_AUDIO_VERIFY_PASS' -Quiet)) {
    throw "Impact audio verification failed: $logPath"
}
Write-Output "Impact audio verification passed: $logPath"
