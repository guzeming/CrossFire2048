param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$copy=Join-Path $root '.build-check/demolition-build'
$output=Join-Path $root ('.build-check/c4-presentation-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
New-Item -ItemType Directory -Force -Path (Join-Path $copy 'Assets/Editor/C4Validation') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Demolition/VerifyDemolitionPresentation.cs') -Destination (Join-Path $copy 'Assets/Editor/C4Validation')
Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Demolition/DemolitionPresentationVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
$editor=$null
try {
    $arguments=@('-batchmode','-projectPath',('"'+$copy+'"'),'-executeMethod','VerifyDemolitionPresentation.Run','--c4-output',('"'+$output+'"'),'-logFile',('"'+(Join-Path $output 'unity.log')+'"'))
    $editor=Start-Process $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath (Join-Path $output 'unity.log') -TimeoutSeconds 600
    if($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'result.txt'))) { throw "C4 verification failed: $output" }
    foreach($folder in @('Assets/Art/Demolition','Assets/Resources/Gameplay/Demolition')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root $folder) | Out-Null
        Copy-Item -LiteralPath (Join-Path $copy ($folder+'.meta')) -Destination (Join-Path $root ($folder+'.meta'))
        Get-ChildItem -LiteralPath (Join-Path $copy $folder) | Copy-Item -Destination (Join-Path $root $folder) -Recurse -Force
    }
    # The ScriptableObject's script GUID must match the source project as well as the validation copy.
    foreach($script in @('Assets/Scripts/Editor/Build/BuildDemolitionPresentation.cs',
        'Assets/Scripts/GClient/Runtime/Features/Demolition/DemolitionPresentationAssets.cs',
        'Assets/Scripts/GClient/Runtime/Features/Demolition/DemolitionActorPresentation.cs',
        'Assets/Scripts/GClient/Runtime/Features/Demolition/DemolitionBombPresentation.cs')) {
        Copy-Item -LiteralPath (Join-Path $copy ($script+'.meta')) -Destination (Join-Path $root ($script+'.meta')) -Force
    }
    Write-Output "DEMOLITION_PRESENTATION_PASS $output"
} finally { if($null-ne $editor -and !$editor.HasExited) { $editor.Kill() } }
