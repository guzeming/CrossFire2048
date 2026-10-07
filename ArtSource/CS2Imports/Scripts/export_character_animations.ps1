param([string]$GameRoot = 'D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo')
$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'CT_SAS/Animations'
$cliPath = 'D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe'
$inventoryPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'Logs/animation_inventory.txt'
$inventory = Get-Content -LiteralPath $inventoryPath | ForEach-Object { ($_ -split ' CRC:')[0] }
$sources = [System.Collections.Generic.List[string]]::new()
$basePath = 'animation/anims/world/rifle/_default_rifle/'
foreach ($pose in @('idle_rifle','idle_crouch_rifle','jump_stand_rifle','jump_n_rifle','jump_e_rifle','jump_s_rifle','jump_w_rifle')) {
    $sources.Add($basePath + $pose + '.vnmclip_c')
}
foreach ($movement in @('walk','run','crouch')) {
    foreach ($direction in @('n','ne','e','se','s','sw','w','nw')) {
        $sources.Add($basePath + $movement + '_' + $direction + '_rifle.vnmclip_c')
    }
}
foreach ($weapon in @('m4a4','m4a1s')) {
    $weaponFolder = if ($weapon -eq 'm4a1s') { 'rifle_m4a1_silencer' } else { 'rifle_m4a4' }
    foreach ($action in @('draw','draw_crouch','reload','reload_crouch','shoot')) {
        $suffix = if ($action -eq 'shoot') { '.vnmclip+non_additive.vnmclip_c' } else { '.vnmclip_c' }
        $sources.Add('animation/anims/world/rifle/' + $weaponFolder + '/' + $action + '_' + $weapon + $suffix)
    }
}
foreach ($action in @('defuse_enter_rifle','defuse_loop_rifle','defuse_crouch_enter_rifle','defuse_crouch_loop_rifle')) {
    $sources.Add('animation/anims/world/shared/defuse/' + $action + '.vnmclip_c')
}
foreach ($action in @('death_chest_a','death_chest_b','death_gut_a','death_gut_b','death_rknee_a','death_rknee_b','death_rshoulder')) {
    $sources.Add('animation/anims/world/shared/' + $action + '.vnmclip_c')
}
$sources.Add('animation/anims/world/shared/breathing.vnmclip+non_additive.vnmclip_c')
New-Item -ItemType Directory -Force -Path (Join-Path $assetRoot 'Clips'),(Join-Path $assetRoot 'Logs') | Out-Null
$manifest = @()
foreach ($sourcePath in $sources) {
    if ($sourcePath -notin $inventory) { throw "Clip missing in installed game: $sourcePath" }
    $clipName = [System.IO.Path]::GetFileName($sourcePath).Replace('.vnmclip+non_additive.vnmclip_c','').Replace('.vnmclip_c','')
    $outputPath = Join-Path $assetRoot ('Clips/' + $clipName + '.gltf')
    $logPath = Join-Path $assetRoot ('Logs/' + $clipName + '.log')
    if (-not (Test-Path -LiteralPath $outputPath)) {
        & $cliPath -i (Join-Path $GameRoot 'pak01_dir.vpk') -f $sourcePath -o $outputPath -d --gltf_export_format gltf --gltf_export_animations --gltf_compose_additive --game (Join-Path $GameRoot 'gameinfo.gi') *> $logPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) { throw "Export failed: $sourcePath; see $logPath" }
    }
    $gltf = Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
    if ($gltf.animations.Count -ne 1) { throw "Unexpected animation count: $clipName" }
    $duration = ($gltf.animations[0].samplers | ForEach-Object { $gltf.accessors[$_.input].max[0] } | Measure-Object -Maximum).Maximum
    $manifest += [ordered]@{ name=$clipName; source=$sourcePath; gltf=('Clips/' + $clipName + '.gltf'); duration_seconds=$duration; is_pose=($duration -eq 0) }
    Write-Output ('Exported {0}/{1}: {2} ({3:N2}s)' -f $manifest.Count,$sources.Count,$clipName,$duration)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $assetRoot 'manifest.json') -Encoding utf8
