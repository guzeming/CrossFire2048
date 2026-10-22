param(
    [ValidateSet('Server', 'Client', 'Dedicated', 'Demolition')][string]$Target = 'Server',
    [switch]$ArenaOnly,
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$BuildDirectory = '.build-check/battle-build',
    [string]$OutputDirectory = 'Builds'
)
$ErrorActionPreference = 'Stop'
if ($ArenaOnly -and $Target -eq 'Demolition') { throw 'Demolition requires the full map; omit -ArenaOnly.' }
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
Write-Output "[阶段] 准备 $Target 构建工程"
$root = Split-Path -Parent $PSScriptRoot
$copy = [IO.Path]::GetFullPath((Join-Path $root $BuildDirectory))
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.build-check')) + [IO.Path]::DirectorySeparatorChar
if (!$copy.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build copy must stay inside .build-check.' }
New-Item -ItemType Directory -Path $copy -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
$method = switch ($Target) { 'Server' { 'BuildHeadless' } 'Dedicated' { 'BuildDedicated' } 'Client' { 'BuildClient' } 'Demolition' { 'BuildDemolition' } }
if ($ArenaOnly) { $method += 'ArenaOnly' }
$log = Join-Path $root ('Logs/battle-build-' + $Target.ToLowerInvariant() + '.log')
New-Item -ItemType Directory -Path (Split-Path $log -Parent) -Force | Out-Null
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log }
$arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $copy + '"'), '-executeMethod', ('OperationBlacktide.EditorTools.BuildBattleArena.' + $method), '--output-root', ('"' + [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) + '"'), '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
& (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $process -LogPath $log -TimeoutSeconds 1800
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'BATTLE_BUILD_PASS' -Quiet)) { throw "Build failed; see $log" }
Write-Output '[进度] 98 正在同步构建结果'
foreach ($name in @('BattleArena.unity', 'BattleArena.unity.meta', 'BattleServer.unity', 'BattleServer.unity.meta')) {
    Copy-Item -LiteralPath (Join-Path $copy ('Assets/Scenes/' + $name)) -Destination (Join-Path $root 'Assets/Scenes')
}
Write-Output "Built $Target. Log: $log"
foreach ($name in @('DemolitionPanel.prefab', 'DemolitionPanel.prefab.meta')) {
    Copy-Item -LiteralPath (Join-Path $copy ('Assets/Resources/UI/Panels/' + $name)) -Destination (Join-Path $root 'Assets/Resources/UI/Panels')
}
$navigation = 'Assets/Art/Maps/DustII/Navigation'
New-Item -ItemType Directory -Path (Join-Path $root $navigation) -Force | Out-Null
foreach ($name in @('Navigation.meta', 'Navigation/BattleNavMesh.asset', 'Navigation/BattleNavMesh.asset.meta')) {
    Copy-Item -LiteralPath (Join-Path $copy ('Assets/Art/Maps/DustII/' + $name)) -Destination (Join-Path $root ('Assets/Art/Maps/DustII/' + $name))
}
foreach ($name in @('NetworkTracer.mat', 'NetworkTracer.mat.meta')) {
    Copy-Item -LiteralPath (Join-Path $copy ('Assets/Art/Combat/' + $name)) -Destination (Join-Path $root 'Assets/Art/Combat')
}
Write-Output '[进度] 99 构建结果同步完成'
