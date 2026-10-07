param(
    [ValidateSet('ct', 'dust2')][string]$Asset = 'ct',
    [switch]$OverwriteGeneratedFiles,
    [string]$Blender = 'D:/SteamLibrary/steamapps/common/Blender/blender.exe',
    [string]$Exporter = 'D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe',
    [string]$Game = 'D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo'
)
$ErrorActionPreference = 'Stop'
$assetRoot = Split-Path $PSScriptRoot -Parent
if ($Asset -eq 'ct') {
    $folder = 'CT_SAS'
    $basename = 'ctm_sas'
    $archive = Join-Path $Game 'pak01_dir.vpk'
    $resource = 'agents/models/ctm_sas/ctm_sas.vmdl_c'
} else {
    $folder = 'DustII'
    $basename = 'de_dust2'
    $archive = Join-Path $Game 'maps/de_dust2.vpk'
    $resource = 'maps/de_dust2/world.vwrld_c'
}
$outputDir = Join-Path $assetRoot $folder
$blendFile = Join-Path $outputDir "$basename.blend"
if ((Test-Path -LiteralPath $blendFile) -and -not $OverwriteGeneratedFiles) {
    throw "The output already exists. Save any edits elsewhere, then use -OverwriteGeneratedFiles to rebuild: $blendFile"
}
$exportArguments = @('-i', $archive, '-f', $resource, '-o', (Join-Path $outputDir "$basename.gltf"),
    '-d', '--gltf_export_format', 'gltf', '--gltf_export_materials', '--gltf_textures_adapt',
    '--game', (Join-Path $Game 'gameinfo.gi'))
if ($Asset -eq 'ct') {
    # This exporter emits skinning only when animation export is enabled.
    # An intentionally unmatched clip filter retains the rig without exporting clips.
    $exportArguments += @('--gltf_export_animations', '--gltf_animation_list', '__rig_only__')
}
& $Exporter @exportArguments *> (Join-Path $assetRoot "Logs/$($Asset)_export.log")
if ($LASTEXITCODE -ne 0) { throw "Source 2 Viewer failed: $LASTEXITCODE" }
& $Blender --background --factory-startup --python-exit-code 1 --python (Join-Path $PSScriptRoot 'import_to_blender.py') -- --asset $Asset --preview *> (Join-Path $assetRoot "Logs/$($Asset)_blender.log")
if ($LASTEXITCODE -ne 0) { throw "Blender import failed: $LASTEXITCODE" }
Write-Output $blendFile
