$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$stageRoot = Join-Path $projectRoot '.build-check/dustii-import'
$reportPath = Join-Path $stageRoot 'Validation/import_report.json'
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if (!$report.passed -or $report.meshes -ne 3599 -or $report.missingOrDownscaledTextures -ne 0 -or
    $report.hiddenEffectMeshes -ne 45 -or $report.visibleMeshes -ne 3554) {
    throw 'Dust II validation did not pass.'
}
if (!(Select-String -LiteralPath (Join-Path $stageRoot 'import.log') -Pattern 'DUSTII_IMPORT_PASS' -Quiet)) {
    throw 'Dust II Unity build did not finish.'
}
$mapTarget = Join-Path $projectRoot 'Assets/Art/Maps/DustII'
$copyTarget = $mapTarget
if (!(Test-Path -LiteralPath $mapTarget)) {
    $copyTarget = Join-Path $stageRoot 'Publish/DustII'
}
New-Item -ItemType Directory -Path $copyTarget -Force | Out-Null
& robocopy (Join-Path $stageRoot 'Assets/Art/Maps/DustII') $copyTarget /E /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw 'Copying map resources failed.' }
if ($copyTarget -ne $mapTarget) {
    # Publish the complete directory at once so Unity sees all material dependencies together.
    $workspacePrefix = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    foreach ($targetPath in @($copyTarget, $mapTarget)) {
        if (![IO.Path]::GetFullPath($targetPath).StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Directory move is outside the project: $targetPath"
        }
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $mapTarget) -Force | Out-Null
    Move-Item -LiteralPath $copyTarget -Destination $mapTarget
}
foreach ($relative in @('Assets/Art/Maps/DustII.meta', 'Assets/Scenes/DustII.unity', 'Assets/Scenes/DustII.unity.meta')) {
    Copy-Item -LiteralPath (Join-Path $stageRoot $relative) -Destination (Join-Path $projectRoot $relative)
}
$reportTarget = Join-Path $projectRoot 'ArtSource/CS2Imports/DustII/UnityExport'
Copy-Item -LiteralPath $reportPath -Destination (Join-Path $reportTarget 'unity_import_report.json')
Copy-Item -LiteralPath (Join-Path $stageRoot 'Validation/DustII_Unity_Overview.png') -Destination $reportTarget
Write-Host 'Dust II map resources and verified scene copied into Assets.'
