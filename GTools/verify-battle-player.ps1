param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = Join-Path $root '.build-check/gameplay-validation'
$output = Join-Path $root ('.build-check/battle-player-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
New-Item -ItemType Directory -Path (Join-Path $copy 'Assets/Editor/BattleValidation') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Battle/BuildBattlePlayerSmoke.cs') -Destination (Join-Path $copy 'Assets/Editor/BattleValidation')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Battle/BattlePlayerSmokeDriver.cs') -Destination (Join-Path $copy 'Assets')
$editor = $null; $account = $null; $battle = $null; $players = @()
try {
    Write-Output '[阶段] 构建独立客户端回归测试'
    $buildLog = Join-Path $output 'build.log'
    $editorArgs = @('-batchmode', '-quit', '-projectPath', ('"' + $copy + '"'), '-executeMethod', 'BuildBattlePlayerSmoke.Run', '--test-output', ('"' + $output + '"'), '-logFile', ('"' + $buildLog + '"'))
    $editor = Start-Process $UnityPath -ArgumentList $editorArgs -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath $buildLog -TimeoutSeconds 900
    if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $buildLog -Pattern 'BATTLE_PLAYER_BUILD_PASS' -Quiet)) { throw "Player build failed: $output" }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start(); $accountPort = $listener.LocalEndpoint.Port; $listener.Stop()
    $udp = [Net.Sockets.UdpClient]::new(0); $battlePort = $udp.Client.LocalEndPoint.Port; $udp.Dispose()
    Set-Content -LiteralPath (Join-Path $output 'account-port.txt') -Value $accountPort
    $accountArgs = @(('"' + (Join-Path $root 'GServer/OperationBlacktide.Server/bin/Release/net8.0/OperationBlacktide.Server.dll') + '"'), '--no-console', '--port', $accountPort, '--battle-port', $battlePort, '--accounts', ('"' + (Join-Path $output 'accounts.json') + '"'), '--battle-key', ('"' + (Join-Path $output 'battle.key') + '"'), '--logs', ('"' + (Join-Path $output 'logs') + '"'))
    $account = Start-Process dotnet -ArgumentList $accountArgs -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(15)
    while (!(Test-Path -LiteralPath (Join-Path $output 'battle.key'))) { if ((Get-Date) -gt $deadline) { throw 'Account startup timed out.' }; Start-Sleep -Milliseconds 100 }
    $serverLog = Join-Path $output 'server-unity.log'
    $battleArgs = @('-batchmode', '-nographics', '--battle-server', '--port', $battlePort, '--battle-key', ('"' + (Join-Path $output 'battle.key') + '"'), '--logs', ('"' + (Join-Path $output 'logs') + '"'), '-logFile', ('"' + $serverLog + '"'))
    $battle = Start-Process (Join-Path $root 'Builds/BattleServer/Blacktide.BattleServer.exe') -ArgumentList $battleArgs -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    while (!(Test-Path -LiteralPath $serverLog) -or !(Select-String -LiteralPath $serverLog -Pattern 'BATTLE_SERVER_READY' -Quiet)) {
        if ($battle.HasExited -or (Get-Date) -gt $deadline) { throw 'Battle startup timed out.' }; Start-Sleep -Milliseconds 100
    }
    Write-Output '[阶段] 双独立客户端：登录、大厅入场、开火、返回与重新入场'
    foreach ($role in @('A', 'B')) {
        $arguments = @('-batchmode', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '--test-output', ('"' + $output + '"'), '--test-role', $role, '-logFile', ('"' + (Join-Path $output ($role + '-player.log')) + '"'))
        $players += Start-Process (Join-Path $output 'Player/Blacktide.Smoke.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    }
    $deadline = (Get-Date).AddSeconds(130); $disconnected = $false
    while (@($players | Where-Object { !$_.HasExited }).Count -gt 0) {
        if ((Get-Date) -gt $deadline) { throw "Player flow timed out: $output" }
        if (!$disconnected -and (Test-Path -LiteralPath (Join-Path $output 'A-rejoined.txt')) -and (Test-Path -LiteralPath (Join-Path $output 'B-rejoined.txt'))) {
            Write-Output '[阶段] 模拟战斗服断开，验证两个客户端均可返回大厅'
            $battle.Kill(); $battle.WaitForExit(); $disconnected = $true
        }
        if (Get-ChildItem -LiteralPath $output -Filter '*-failure.txt') { throw "Player regression failed: $output" }
        Start-Sleep -Milliseconds 250
    }
    foreach ($role in @('A', 'B')) { if (!(Test-Path -LiteralPath (Join-Path $output ($role + '-result.txt')))) { throw "Missing player $role result: $output" } }
    Write-Output "BATTLE_PLAYER_SMOKE_PASS $output"
}
finally {
    foreach ($process in @($editor, $battle, $account) + $players) {
        if ($null -ne $process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    }
}
