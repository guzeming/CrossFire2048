param([int]$AccountPort = 7777, [int]$BattlePort = 7778, [ValidateRange(0,7)][int]$BotCount = 7, [switch]$NoViewer, [switch]$Rebuild, [switch]$ArenaOnly,
    [string]$DataDirectory = 'data', [string]$LogDirectory = 'Logs/Server',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
if (Get-NetTCPConnection -LocalPort $AccountPort -State Listen -ErrorAction SilentlyContinue) {
    throw "Account port $AccountPort is occupied. Close the old account server first, then run this launcher again."
}
if (Get-NetUDPEndpoint -LocalPort $BattlePort -ErrorAction SilentlyContinue) { throw "Battle port $BattlePort is occupied." }
$serverExe = Join-Path $root 'Builds/BattleServer/Blacktide.BattleServer.exe'
if ($Rebuild -or !(Test-Path -LiteralPath $serverExe)) { & (Join-Path $PSScriptRoot 'build-battle.ps1') -UnityPath $UnityPath -ArenaOnly:$ArenaOnly }
dotnet build GServer/OperationBlacktide.Server -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Account build failed.' }
if (!$NoViewer) {
    dotnet build GTools/LogViewer -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Log viewer build failed.' }
}
$data = [IO.Path]::GetFullPath((Join-Path $root $DataDirectory)); $logs = [IO.Path]::GetFullPath((Join-Path $root $LogDirectory))
$diagnostics = Split-Path -Parent $logs
New-Item -ItemType Directory -Path $data, $logs -Force | Out-Null
$accounts = Join-Path $data 'accounts.json'
$legacyAccounts = Join-Path $root 'GServer/OperationBlacktide.Server/bin/Debug/net8.0/data/accounts.json'
if ($DataDirectory -eq 'data' -and !(Test-Path -LiteralPath $accounts) -and (Test-Path -LiteralPath $legacyAccounts)) { Copy-Item -LiteralPath $legacyAccounts -Destination $accounts }
$stopFile = Join-Path $data 'local-server.stop'
if (Test-Path -LiteralPath $stopFile) { Remove-Item -LiteralPath $stopFile }
$keyFile = Join-Path $data 'battle.key'
$started = @()
try {
    $accountArgs = @(('"' + (Join-Path $root 'GServer/OperationBlacktide.Server/bin/Release/net8.0/OperationBlacktide.Server.dll') + '"'), '--no-console', '--port', $AccountPort, '--battle-port', $BattlePort, '--accounts', ('"' + $accounts + '"'), '--battle-key', ('"' + $keyFile + '"'), '--logs', ('"' + $logs + '"'), '--stop-file', ('"' + $stopFile + '"'))
    $account = Start-Process dotnet -ArgumentList $accountArgs -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $diagnostics 'account-console.log') -RedirectStandardError (Join-Path $diagnostics 'account-error.log')
    $started += $account
    $deadline = (Get-Date).AddSeconds(15)
    while (!(Get-NetTCPConnection -LocalPort $AccountPort -State Listen -ErrorAction SilentlyContinue)) {
        if ($account.HasExited -or (Get-Date) -gt $deadline) { throw 'Account server did not start; see Logs/account-error.log.' }
        Start-Sleep -Milliseconds 100
    }
    $unityLog = Join-Path $diagnostics 'battle-unity.log'
    if (Test-Path -LiteralPath $unityLog) { Remove-Item -LiteralPath $unityLog }
    $battleArgs = @('-batchmode', '-nographics', '--battle-server', '--port', $BattlePort, '--bots', $BotCount, '--battle-key', ('"' + $keyFile + '"'), '--logs', ('"' + $logs + '"'), '--stop-file', ('"' + $stopFile + '"'), '-logFile', ('"' + $unityLog + '"'))
    $battle = Start-Process $serverExe -ArgumentList $battleArgs -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $diagnostics 'battle-console.log') -RedirectStandardError (Join-Path $diagnostics 'battle-error.log')
    $started += $battle
    $deadline = (Get-Date).AddSeconds(30)
    while (!(Test-Path -LiteralPath $unityLog) -or !(Select-String -LiteralPath $unityLog -Pattern 'BATTLE_SERVER_READY' -Quiet)) {
        if ($battle.HasExited -or (Get-Date) -gt $deadline) { throw 'Battle server did not start; see Logs/battle-unity.log.' }
        Start-Sleep -Milliseconds 100
    }
    $started | ForEach-Object { @{ Id = $_.Id; Started = $_.StartTime.ToUniversalTime().Ticks; Path = $_.Path } } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'local-server-processes.json') -Encoding UTF8
    if (!$NoViewer) {
        # Interactive tool requested by the user; only server helpers run hidden.
        Start-Process (Join-Path $root 'GTools/LogViewer/bin/Release/net8.0-windows/OperationBlacktide.LogViewer.exe') -ArgumentList @('--logs', ('"' + $logs + '"')) -WorkingDirectory $root | Out-Null
    }
    Write-Output "Account TCP $AccountPort and battle UDP $BattlePort ready. Logs: $logs"
    Write-Output 'Stop with GTools/stop-local-server.bat.'
}
catch { foreach ($process in $started) { if (!$process.HasExited) { $process.Kill() } }; throw }
