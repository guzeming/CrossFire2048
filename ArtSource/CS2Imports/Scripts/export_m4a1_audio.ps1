param(
    [string]$GameRoot='D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo',
    [string]$Exporter='D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe',
    [string]$Blender='D:/SteamLibrary/steamapps/common/Blender/blender.exe'
)
$ErrorActionPreference='Stop'
$assetRoot=Split-Path $PSScriptRoot -Parent
$audioRoot=Join-Path $assetRoot 'M4A1_S/Audio'
$archive=Join-Path $GameRoot 'pak01_dir.vpk'
New-Item -ItemType Directory -Force -Path (Join-Path $audioRoot 'Source'),(Join-Path $audioRoot 'Metadata') | Out-Null
& $Exporter -i $archive -f 'sounds/weapons/m4a1/' -o (Join-Path $audioRoot 'Source') -d *> (Join-Path $assetRoot 'Logs/m4a1_audio_export.log')
if($LASTEXITCODE -ne 0){throw 'Sound extraction failed'}
& $Exporter -i $archive -f 'soundevents/game_sounds_weapons.vsndevts_c' -o (Join-Path $audioRoot 'Metadata/game_sounds_weapons.vsndevts') -d *> (Join-Path $assetRoot 'Logs/m4a1_audio_events_export.log')
if($LASTEXITCODE -ne 0){throw 'Sound events extraction failed'}
& $Exporter -i $archive -f 'animation/anims/world/rifle/rifle_m4a1_silencer/' -o (Join-Path $audioRoot 'Metadata/WorldAnimations') -d *> (Join-Path $assetRoot 'Logs/m4a1_animation_metadata.log')
if($LASTEXITCODE -ne 0){throw 'Animation metadata extraction failed'}
& $Blender --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'inspect_m4a1_audio.py')
if($LASTEXITCODE -ne 0){throw 'Sound mapping failed'}
$dependencies=Get-Content -LiteralPath (Join-Path $audioRoot 'Metadata/additional_sound_resources.txt')
if($dependencies.Count -gt 0){
    & $Exporter -i $archive -f ($dependencies -join ',') -o (Join-Path $audioRoot 'Source') -d *> (Join-Path $assetRoot 'Logs/m4a1_audio_dependencies.log')
    if($LASTEXITCODE -ne 0){throw 'Sound dependency extraction failed'}
}
& $Blender --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'prepare_m4a1_audio.py')
if($LASTEXITCODE -ne 0){throw 'Sound preparation failed'}
Write-Output (Join-Path $audioRoot 'WAV')
