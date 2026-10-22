param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [string]$ValidationDirectory = '.build-check/tactical-hud'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$copy = [IO.Path]::GetFullPath((Join-Path $root $ValidationDirectory))
$output = Join-Path $root ('.build-check/tactical-hud-results-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
New-Item -ItemType Directory -Force -Path $output,(Join-Path $copy 'Assets/Editor/TacticalValidation') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Hud/VerifyTacticalHud.cs') -Destination (Join-Path $copy 'Assets/Editor/TacticalValidation')
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Hud/TacticalHudVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
$log = Join-Path $output 'unity.log'
$arguments = @('-batchmode','-projectPath',('"'+$copy+'"'),'-executeMethod','VerifyTacticalHud.Run','--test-output',('"'+$output+'"'),'-logFile',('"'+$log+'"'))
$editor = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath $log -TimeoutSeconds 900
    if ($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'result.txt'))) { throw "Tactical HUD validation failed: $output" }
    foreach ($name in @('DustIIRadar.asset','DustIIRadar.asset.meta','DustIIRadar.png','DustIIRadar.png.meta')) {
        Copy-Item -LiteralPath (Join-Path $copy ('Assets/Resources/Gameplay/'+$name)) -Destination (Join-Path $root ('Assets/Resources/Gameplay/'+$name))
    }
    Write-Output "TACTICAL_HUD_VERIFY_PASS $output"
} finally { if (!$editor.HasExited) { $editor.Kill() } }
