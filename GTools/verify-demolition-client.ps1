param([string]$UnityPath='C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$copy=Join-Path $root '.build-check/demolition-build'
$output=Join-Path $root ('.build-check/demolition-client-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $output,(Join-Path $copy 'Assets/Editor/DemolitionValidation') | Out-Null
. (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
Sync-UnityProject -ProjectRoot $root -Destination $copy
New-Item -ItemType Directory -Force -Path (Join-Path $copy 'Assets/Editor/DemolitionValidation') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Demolition/VerifyDemolitionClient.cs') -Destination (Join-Path $copy 'Assets/Editor/DemolitionValidation')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../Tests/Unity/Demolition/DemolitionVerificationDriver.cs') -Destination (Join-Path $copy 'Assets')
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
Set-Content -LiteralPath (Join-Path $output 'account-port.txt') -Value $port
dotnet build (Join-Path $root 'GServer/OperationBlacktide.Server') -c Release -o (Join-Path $output 'Account') -v:q -p:WarningLevel=0
if($LASTEXITCODE -ne 0){throw 'Account server build failed.'}
$account=$null;$editor=$null
try{
    $accountArguments=@(('"'+(Join-Path $output 'Account/OperationBlacktide.Server.dll')+'"'),'--no-console','--port',$port,'--accounts',('"'+(Join-Path $output 'accounts.json')+'"'),'--battle-key',('"'+(Join-Path $output 'key')+'"'),'--logs',('"'+(Join-Path $output 'logs')+'"'),'--stop-file',('"'+(Join-Path $output 'stop-account')+'"'))
    $account=Start-Process dotnet -ArgumentList $accountArguments -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'account.log') -RedirectStandardError (Join-Path $output 'account-error.log')
    $editorArguments=@('-batchmode','-projectPath',('"'+$copy+'"'),'-executeMethod','VerifyDemolitionClient.Run','--test-output',('"'+$output+'"'),'-logFile',('"'+(Join-Path $output 'unity.log')+'"'))
    $editor=Start-Process $UnityPath -ArgumentList $editorArguments -WindowStyle Hidden -PassThru
    & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath (Join-Path $output 'unity.log') -TimeoutSeconds 300
    if($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $output 'result.txt'))){throw "Demolition UI validation failed: $output"}
    Write-Output "DEMOLITION_CLIENT_PASS $output"
    foreach($name in @('DemolitionPanel.prefab','DemolitionPanel.prefab.meta')){
        Copy-Item -LiteralPath (Join-Path $copy ('Assets/Resources/UI/Panels/'+$name)) -Destination (Join-Path $root ('Assets/Resources/UI/Panels/'+$name))
    }
    $art='Assets/UI/Art/Demolition'
    New-Item -ItemType Directory -Force -Path (Join-Path $root $art) | Out-Null
    Copy-Item -LiteralPath (Join-Path $copy ($art+'.meta')) -Destination (Join-Path $root ($art+'.meta'))
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $copy $art) -File){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $root $art)}
}finally{
    if($null-ne $editor-and !$editor.HasExited){$editor.Kill()}
    if($null-ne $account-and !$account.HasExited){Set-Content -LiteralPath (Join-Path $output 'stop-account') -Value 'stop';if(!$account.WaitForExit(5000)){$account.Kill()}}
}
