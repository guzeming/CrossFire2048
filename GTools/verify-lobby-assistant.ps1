param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$validationRoot = Join-Path $projectRoot '.build-check/lobby-assistant-validation'
if (Test-Path -LiteralPath (Join-Path $validationRoot 'Temp/UnityLockfile')) { throw 'Close the assistant validation project before running.' }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
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
$editorFolder = Join-Path $validationRoot 'Assets/Editor/LobbyAssistantValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Tests/Unity/Lobby/VerifyLobbyAssistant.cs') -Destination $editorFolder
$logPath = Join-Path $validationRoot 'verify.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyLobbyAssistant.Run', '-logFile', ('"' + $logPath + '"'))
$verification = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verification -LogPath $logPath -TimeoutSeconds 900
if ($verification.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'LOBBY_ASSISTANT_UI_PASS' -Quiet)) { throw "Assistant UI verification failed: $logPath" }
# Only install the newly generated assistant prefab. Existing lobby assets are preserved.
foreach ($file in @('LobbyAssistant.prefab', 'LobbyAssistant.prefab.meta')) {
    Copy-Item -LiteralPath (Join-Path $validationRoot ('Assets/Resources/UI/' + $file)) -Destination (Join-Path $projectRoot ('Assets/Resources/UI/' + $file)) -Force
}
Get-Content -LiteralPath (Join-Path $projectRoot '.build-check/lobby-assistant/ui-verification.txt')
