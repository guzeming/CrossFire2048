param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [switch]$BuildClient,
    [string]$ClientOutputDirectory = 'Builds/Client',
    [string]$ValidationDirectory = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourceRoot = $projectRoot
$outputRoot = Join-Path $projectRoot '.build-check/display-settings'
if ($ValidationDirectory) {
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot $ValidationDirectory))
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot '.build-check')) + [IO.Path]::DirectorySeparatorChar
    if (!$projectRoot.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation must stay inside .build-check.' }
    if (Test-Path -LiteralPath (Join-Path $projectRoot 'Temp/UnityLockfile')) { throw 'The validation project is already open in Unity.' }
    . (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
    Sync-UnityProject -ProjectRoot $sourceRoot -Destination $projectRoot
}
$temporaryFolder = Join-Path $projectRoot 'Assets/Editor/DisplaySettingsValidation'
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
if (Test-Path -LiteralPath (Join-Path $projectRoot 'Temp/UnityLockfile')) { throw 'Close this project in Unity before running verification.' }
if (Test-Path -LiteralPath $temporaryFolder) { throw "Temporary validation folder already exists: $temporaryFolder" }
New-Item -ItemType Directory -Path $outputRoot, $temporaryFolder -Force | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Settings/VerifyDisplaySettings.cs') -Destination $temporaryFolder
    foreach ($name in @('verification.txt', 'failure.txt')) {
        $path = Join-Path $outputRoot $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    $logPath = Join-Path $outputRoot 'verify.log'
    Write-Output '[阶段] 编译设置界面并验证大厅菜单、保存、未保存退出和登录返回。'
    $arguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', 'VerifyDisplaySettings.Run', '--settings-output', ('"' + $outputRoot + '"'), '-logFile', ('"' + $logPath + '"'))
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $logPath -TimeoutSeconds 900
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'DISPLAY_SETTINGS_VERIFY_PASS' -Quiet)) { throw "Settings verification failed: $logPath" }
    if ($ValidationDirectory) {
        foreach ($name in @('LobbyPanel.prefab', 'SettingsPanel.prefab', 'SettingsPanel.prefab.meta', 'LobbyMenuPanel.prefab', 'LobbyMenuPanel.prefab.meta')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot ('Assets/Resources/UI/Panels/' + $name)) -Destination (Join-Path $sourceRoot 'Assets/Resources/UI/Panels')
        }
        foreach ($name in @('PerformanceStatsOverlay.prefab', 'PerformanceStatsOverlay.prefab.meta')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot ('Assets/Resources/UI/' + $name)) -Destination (Join-Path $sourceRoot 'Assets/Resources/UI')
        }
    }
    if ($BuildClient) {
        Write-Output '[阶段] 构建 Windows 客户端。'
        $clientOutput = [IO.Path]::GetFullPath((Join-Path $sourceRoot $ClientOutputDirectory))
        $logPath = Join-Path $outputRoot 'build-client.log'
        $arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', 'VerifyDisplaySettings.BuildClient', '--settings-client-output', ('"' + $clientOutput + '"'), '-logFile', ('"' + $logPath + '"'))
        $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
        & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $logPath -TimeoutSeconds 1800
        if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'DISPLAY_SETTINGS_CLIENT_BUILD_PASS' -Quiet)) { throw "Client build failed: $logPath" }
    }
    Write-Output "Display settings verification passed. Results: $outputRoot"
}
finally {
    $expected = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Assets/Editor/DisplaySettingsValidation'))
    $resolved = [IO.Path]::GetFullPath($temporaryFolder)
    if ($resolved -ne $expected -or !$resolved.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup path escaped workspace.' }
    foreach ($name in @('VerifyDisplaySettings.cs', 'VerifyDisplaySettings.cs.meta')) {
        $path = Join-Path $resolved $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved }
    if (Test-Path -LiteralPath ($resolved + '.meta')) { Remove-Item -LiteralPath ($resolved + '.meta') }
}
