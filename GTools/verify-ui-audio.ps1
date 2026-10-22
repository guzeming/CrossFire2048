param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$validationRoot = Join-Path $projectRoot '.build-check/ui-audio-validation'
$lockPath = Join-Path $validationRoot 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    try { $lockCheck = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lockCheck.Dispose() }
    catch { throw 'UI audio validation is already running.' }
    Remove-Item -LiteralPath $lockPath
}
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
# This suite needs production scripts, UI prefabs and audio, not world meshes or map textures.
foreach ($relative in @('Packages', 'ProjectSettings', 'Assets/Scripts', 'Assets/Resources/UI', 'Assets/UI/Fonts')) {
    $target = [IO.Path]::GetFullPath((Join-Path $validationRoot $relative))
    if (!$target.StartsWith($validationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid validation path.' }
    for ($ancestor = $target; $ancestor -and $ancestor -ne $projectRoot; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Validation path must not contain links.' }
    }
    if ((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint | Select-Object -First 1)) { throw 'Validation contents must not contain links.' }
    & robocopy (Join-Path $projectRoot $relative) $target /MIR /XJ /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $relative" }
}
$editorFolder = Join-Path $validationRoot 'Assets/Editor/UIAudioValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Unity/UI/VerifyUIAudio.cs') -Destination $editorFolder
foreach ($name in @('ui-audio-verification.txt', 'ui-audio-failure.txt')) {
    $result = Join-Path $validationRoot $name
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
}
$logPath = Join-Path $validationRoot 'verify.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyUIAudio.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'UI_AUDIO_VERIFY_PASS' -Quiet)) { throw "UI audio verification failed: $logPath" }
Get-Content -LiteralPath (Join-Path $validationRoot 'ui-audio-verification.txt')
