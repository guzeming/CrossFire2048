param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/gameplay-validation',
    [ValidateSet('All', 'TopDown', 'Throwables', 'Vision')][string]$Suite = 'All'
)
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $ValidationDirectory))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$validationRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must be inside .build-check.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
$assetCopy = Join-Path $validationRoot 'Assets'
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $validationRoot
$editorFolder = Join-Path $assetCopy 'Editor/GameplayValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Gameplay/VerifyGameplay.cs') -Destination $editorFolder
foreach ($driver in @('../Tests/Unity/Gameplay/GameplayVerificationDriver.cs', '../Tests/Unity/TopDown/TopDownVerificationDriver.cs',
    '../Tests/Unity/Throwable/ThrowableVerificationDriver.cs', '../Tests/Unity/Throwable/ThrowableActionVerification.cs',
    '../Tests/Unity/Vision/VisionVerificationDriver.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $driver) -Destination $assetCopy
}
$suites = if ($Suite -eq 'All') { @('TopDown', 'Throwables', 'Vision') } else { @($Suite) }
for ($suiteIndex = 0; $suiteIndex -lt $suites.Count; $suiteIndex++) {
    $name = $suites[$suiteIndex]
    Write-Output "[阶段] 执行回归项目：$name"
    $logPath = Join-Path $validationRoot ('gameplay-' + $name.ToLowerInvariant() + '.log')
    $failureName = switch ($name) { 'TopDown' { 'top-down-failure.txt' } 'Throwables' { 'throwables-failure.txt' } 'Vision' { 'vision-failure.txt' } }
    $failurePath = Join-Path $validationRoot $failureName
    if (Test-Path -LiteralPath $failurePath) { Remove-Item -LiteralPath $failurePath }
    $arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', ('VerifyGameplay.' + $name), '-logFile', ('"' + $logPath + '"'))
    $verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $progressStart = 10 + [int][Math]::Floor(88 * $suiteIndex / $suites.Count)
    $progressEnd = 10 + [int][Math]::Floor(88 * ($suiteIndex + 1) / $suites.Count)
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900 -ProgressStart $progressStart -ProgressEnd $progressEnd
    $marker = switch ($name) { 'TopDown' { 'TOP_DOWN_VERIFY_PASS' } 'Throwables' { 'THROWABLES_VERIFY_PASS' } 'Vision' { 'VISION_VERIFY_PASS' } }
    if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern $marker -Quiet)) { throw "Verification failed: $logPath" }
    Write-Output "Gameplay $name verification passed: $logPath"
}
