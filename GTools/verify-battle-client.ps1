param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$root = Split-Path -Parent $PSScriptRoot
$copy = Join-Path $root '.build-check/gameplay-validation'
$output = Join-Path $root ('.build-check/battle-client-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
New-Item -ItemType Directory -Path (Join-Path $copy 'Assets/Editor/BattleValidation') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Battle/VerifyBattleClient.cs') -Destination (Join-Path $copy 'Assets/Editor/BattleValidation')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Battle/BattleClientVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start(); $accountPort = $listener.LocalEndpoint.Port; $listener.Stop()
$udp = [Net.Sockets.UdpClient]::new(0); $battlePort = $udp.Client.LocalEndPoint.Port; $udp.Dispose()
Set-Content -LiteralPath (Join-Path $output 'account-port.txt') -Value $accountPort
$account = $null; $battle = $null; $editor = $null
try {
    $accountArgs = @(('"' + (Join-Path $root 'GServer/OperationBlacktide.Server/bin/Release/net8.0/OperationBlacktide.Server.dll') + '"'), '--no-console', '--port', $accountPort, '--battle-port', $battlePort, '--accounts', ('"' + (Join-Path $output 'accounts.json') + '"'), '--battle-key', ('"' + (Join-Path $output 'battle.key') + '"'), '--logs', ('"' + (Join-Path $output 'logs') + '"'))
    $account = Start-Process dotnet -ArgumentList $accountArgs -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(15)
    while (!(Test-Path -LiteralPath (Join-Path $output 'battle.key'))) { if ((Get-Date) -gt $deadline) { throw 'Account startup timed out.' }; Start-Sleep -Milliseconds 100 }
    $battleArgs = @('-batchmode', '-nographics', '--battle-server', '--port', $battlePort, '--battle-key', ('"' + (Join-Path $output 'battle.key') + '"'), '--logs', ('"' + (Join-Path $output 'logs') + '"'), '-logFile', ('"' + (Join-Path $output 'server-unity.log') + '"'))
    $battle = Start-Process (Join-Path $root 'Builds/BattleServer/Blacktide.BattleServer.exe') -ArgumentList $battleArgs -WindowStyle Hidden -PassThru
    $editorArgs = @('-batchmode', '-projectPath', ('"' + $copy + '"'), '-executeMethod', 'VerifyBattleClient.Run', '--test-output', ('"' + $output + '"'), '-logFile', ('"' + (Join-Path $output 'client-unity.log') + '"'))
    $editor = Start-Process $UnityPath -ArgumentList $editorArgs -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath (Join-Path $output 'client-unity.log') -TimeoutSeconds 300
    if ($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'client-result.txt'))) { throw "Client verification failed: $output" }
    Write-Output "BATTLE_CLIENT_VERIFY_PASS $output"
}
finally { foreach ($process in @($editor, $battle, $account)) { if ($null -ne $process -and !$process.HasExited) { $process.Kill() } } }
