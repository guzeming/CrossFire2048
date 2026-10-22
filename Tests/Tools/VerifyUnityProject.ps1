$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $root 'GTools/Common/UnityProject.ps1')
$fixture = Join-Path $root ('.build-check/project-sync-test-' + [Guid]::NewGuid().ToString('N'))
$copy = Join-Path $fixture '.build-check/copy'
New-Item -ItemType Directory -Path (Join-Path $fixture 'Assets'), (Join-Path $fixture 'Packages'), (Join-Path $fixture 'ProjectSettings') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $fixture 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: test'
Set-Content -LiteralPath (Join-Path $fixture 'Assets/Before.cs') -Value 'class Example {}'
Set-Content -LiteralPath (Join-Path $fixture 'Assets/Before.cs.meta') -Value 'guid: 0123456789abcdef0123456789abcdef'
Sync-UnityProject -ProjectRoot $fixture -Destination $copy
if (!(Test-Path -LiteralPath (Join-Path $copy 'Assets/Before.cs'))) { throw 'Initial source was not copied.' }
New-Item -ItemType Directory -Path (Join-Path $copy 'Library') -Force | Out-Null
Set-Content -LiteralPath (Join-Path $copy 'Library/keep.txt') -Value 'cache'
Set-Content -LiteralPath (Join-Path $copy 'Assets/PreviousTest.cs') -Value 'class OldDriver {}'
Rename-Item -LiteralPath (Join-Path $fixture 'Assets/Before.cs') -NewName 'After.cs'
Rename-Item -LiteralPath (Join-Path $fixture 'Assets/Before.cs.meta') -NewName 'After.cs.meta'
Sync-UnityProject -ProjectRoot $fixture -Destination $copy
foreach ($stale in @('Before.cs', 'Before.cs.meta', 'PreviousTest.cs')) {
    if (Test-Path -LiteralPath (Join-Path $copy ('Assets/' + $stale))) { throw "Stale script/GUID survived: $stale" }
}
if (!(Test-Path -LiteralPath (Join-Path $copy 'Library/keep.txt'))) { throw 'Library cache was removed.' }
if ((Get-Content -LiteralPath (Join-Path $copy 'Assets/After.cs.meta') -Raw).Trim() -ne 'guid: 0123456789abcdef0123456789abcdef') { throw 'Moved GUID changed.' }
foreach ($invalid in @($fixture, (Join-Path $fixture '.build-check'), (Join-Path $root '.build-check/outside-fixture'))) {
    $rejected = $false
    try { Sync-UnityProject -ProjectRoot $fixture -Destination $invalid } catch { $rejected = $true }
    if (!$rejected) { throw "Unsafe copy destination accepted: $invalid" }
}
Write-Output 'UNITY_PROJECT_SYNC_PASS: renamed files, stale drivers, GUIDs, cache preservation, destination guards'
