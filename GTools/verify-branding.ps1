param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
Write-Output '[阶段] 准备验证工程；首次导入该工程可能需要数分钟。'
$projectRoot = Split-Path -Parent $PSScriptRoot
$validationRoot = Join-Path $projectRoot '.build-check/branding/project'
if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity editor not found: $UnityPath" }
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $projectRoot -Destination $validationRoot
$editorFolder = Join-Path $validationRoot 'Assets/Editor/BrandingValidation'
New-Item -ItemType Directory -Path $editorFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Branding/VerifyBranding.cs') -Destination $editorFolder
$logPath = Join-Path $validationRoot 'verification.log'
$arguments = @('-batchmode', '-projectPath', ('"' + $validationRoot + '"'), '-executeMethod', 'VerifyBranding.Run', '-logFile', ('"' + $logPath + '"'))
# Resolve the same locked package versions locally in the copy, including transitive
# dependencies, so validation does not depend on the package registry being online.
$packageLock = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/packages-lock.json') -Raw | ConvertFrom-Json
$localDependencies = [ordered]@{}
$allCached = $true
foreach ($package in $packageLock.dependencies.PSObject.Properties) {
    $cachedPath = Join-Path $projectRoot ('Library/PackageCache/' + $package.Name + '@' + $package.Value.version)
    if (!(Test-Path -LiteralPath (Join-Path $cachedPath 'package.json'))) { $allCached = $false; break }
    $localDependencies[$package.Name] = 'file:' + $cachedPath.Replace('\', '/')
}
if ($allCached) {
    $localManifest = @{ dependencies = $localDependencies } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $validationRoot 'Packages/manifest.json'), $localManifest)
}
$env:BLACKTIDE_MIGRATION_TEST_USER = 'branding-registry-test-' + [guid]::NewGuid().ToString('N')
$legacyRegistryPath = 'HKCU:\Software\Unity\UnityEditor\DefaultCompany\Test'
$fixtureName = 'CrossFire2048.Loadout.v1.' + $env:BLACKTIDE_MIGRATION_TEST_USER + '_h123456789'
$fixtureBytes = [System.Text.Encoding]::UTF8.GetBytes('{"version":1,"activeTeam":2}' + [char]0)
if (!(Test-Path -LiteralPath $legacyRegistryPath)) { New-Item -Path $legacyRegistryPath -Force | Out-Null }
New-ItemProperty -LiteralPath $legacyRegistryPath -Name $fixtureName -Value $fixtureBytes -PropertyType Binary | Out-Null
try {
    $verificationProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $verificationProcess -LogPath $logPath -TimeoutSeconds 900
}
finally {
    Remove-ItemProperty -LiteralPath $legacyRegistryPath -Name $fixtureName -ErrorAction SilentlyContinue
    Remove-Item Env:BLACKTIDE_MIGRATION_TEST_USER -ErrorAction SilentlyContinue
}
if ($verificationProcess.ExitCode -ne 0 -or !(Select-String -LiteralPath $logPath -Pattern 'BLACKTIDE_BRANDING_PASS' -Quiet)) {
    throw "Branding verification failed. See $logPath"
}
Write-Host "Branding verification passed. Captures and log: $validationRoot"
