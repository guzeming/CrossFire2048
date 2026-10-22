param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$outputRoot = Join-Path $projectRoot '.build-check/black-gold-ui'
$validationScripts = Join-Path $projectRoot 'Assets/Editor/BlackGoldValidation'
$editorParent = Join-Path $projectRoot 'Assets/Editor'
$editorParentExisted = Test-Path -LiteralPath $editorParent
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
if (Test-Path -LiteralPath (Join-Path $projectRoot 'Temp/UnityLockfile')) {
    throw 'Close this project in Unity before running the batch visual verification.'
}
if (Test-Path -LiteralPath $validationScripts) { throw "Temporary validation directory already exists: $validationScripts" }
New-Item -ItemType Directory -Path $outputRoot, $validationScripts -Force | Out-Null
$logPath = Join-Path $outputRoot 'verify.log'
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/UI/VerifyBlackGoldUI.cs') -Destination $validationScripts
    foreach ($name in @('verification.txt', 'failure.txt')) {
        $resultPath = Join-Path $outputRoot $name
        if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
    }
    $arguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', 'VerifyBlackGoldUI.Run', '-logFile', ('"' + $logPath + '"'))
    $verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
    if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'BLACK_GOLD_VERIFY_PASS' -Quiet)) {
        throw "Black Gold UI verification failed: $logPath"
    }
    Write-Output "Black Gold UI verification passed. Screenshots: $outputRoot"
}
finally {
    # Only remove the exact temporary directory created by this invocation.
    $expected = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Assets/Editor/BlackGoldValidation'))
    $resolved = [IO.Path]::GetFullPath($validationScripts)
    if ($resolved -ne $expected -or !$resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Validation cleanup path escaped the workspace.'
    }
    foreach ($file in @('VerifyBlackGoldUI.cs', 'VerifyBlackGoldUI.cs.meta')) {
        $temporaryFile = Join-Path $resolved $file
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved }
    if (Test-Path -LiteralPath ($resolved + '.meta')) { Remove-Item -LiteralPath ($resolved + '.meta') }
    if (!$editorParentExisted -and (Test-Path -LiteralPath $editorParent) -and @(Get-ChildItem -LiteralPath $editorParent -Force).Count -eq 0) {
        Remove-Item -LiteralPath $editorParent
        if (Test-Path -LiteralPath ($editorParent + '.meta')) { Remove-Item -LiteralPath ($editorParent + '.meta') }
    }
}
