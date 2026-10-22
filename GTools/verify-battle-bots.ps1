param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [switch]$Client, [string]$ServerExecutable)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = Join-Path $root '.build-check/bot-validation'
$output = Join-Path $root ('.build-check/battle-bots-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
$editorFolder = Join-Path $copy 'Assets/Editor/BotValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Bot/VerifyBattleBots.cs') -Destination $editorFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Bot/BattleBotVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Bot/BotClientVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
$log = Join-Path $output 'unity.log'
$server = $null
try {
    if ($Client) {
        if (!$ServerExecutable) { $ServerExecutable = Join-Path $root 'Builds/BattleServer/Blacktide.BattleServer.exe' }
        $udp = [Net.Sockets.UdpClient]::new(0); $port = $udp.Client.LocalEndPoint.Port; $udp.Dispose()
        $bytes = New-Object byte[] 32; $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes) } finally { $random.Dispose() }
        $key = Join-Path $output 'test.key'; [IO.File]::WriteAllBytes($key, $bytes)
        Set-Content -LiteralPath (Join-Path $output 'port.txt') -Value $port
        $serverArgs = @('-batchmode','-nographics','--battle-server','--bots','7','--port',$port,'--battle-key',('"' + $key + '"'),
            '--logs',('"' + (Join-Path $output 'logs') + '"'),'-logFile',('"' + (Join-Path $output 'server.log') + '"'))
        $server = Start-Process -FilePath $ServerExecutable -ArgumentList $serverArgs -WindowStyle Hidden -PassThru
    }
    $method = if ($Client) { 'VerifyBattleBots.RunClient' } else { 'VerifyBattleBots.Run' }
    $resultFile = if ($Client) { 'client-result.txt' } else { 'bot-result.txt' }
    $arguments = @('-batchmode', '-projectPath', ('"' + $copy + '"'), '-executeMethod', $method, '--test-output', ('"' + $output + '"'), '-logFile', ('"' + $log + '"'))
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $log -TimeoutSeconds 600
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output $resultFile))) { throw "Bot verification failed: $output" }
    Write-Output "BATTLE_BOTS_VERIFY_PASS $output"
}
finally { if ($null -ne $server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit(5000) | Out-Null } }
