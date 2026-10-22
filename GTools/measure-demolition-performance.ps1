param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.47f1c1/Editor/Unity.exe',
    [ValidateSet('baseline','no-vision','no-cutaway','balanced','no-glass')][string[]]$Variants = @('baseline','no-vision','no-cutaway','balanced'),
    [ValidateSet('topdown','firstperson')][string]$View = 'topdown',
    [ValidateRange(10,60)][int]$SecondsPerPhase = 20,
    [ValidateRange(640,3840)][int]$Width = 1600,
    [ValidateRange(480,2160)][int]$Height = 900,
    [ValidateRange(1,5)][int]$Repeats = 1,
    [string]$BuildDirectory = '.build-check/battle-build',
    [switch]$ReuseBuild,
    [switch]$ProfilerTrace
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allowed = [IO.Path]::GetFullPath((Join-Path $root '.build-check')) + [IO.Path]::DirectorySeparatorChar
$copy = [IO.Path]::GetFullPath((Join-Path $root $BuildDirectory))
if (!$copy.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build copy must stay inside .build-check.' }
if ($View -eq 'firstperson' -and ($Variants -contains 'no-vision' -or $Variants -contains 'no-cutaway')) {
    throw 'First-person already disables vision and cutaway. Use -Variants baseline,balanced,no-glass.'
}
$build = Join-Path $root '.build-check/performance-players'
$output = Join-Path $root ('.build-check/performance-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Force -Path $build,$output | Out-Null
# Conservative source fingerprint: a changed source timestamp/size requires rebuilding both matched players.
$sourceFolders = @('Assets','Packages','ProjectSettings','Tests/Unity/Performance') | ForEach-Object { Join-Path $root $_ }
$sourceDescription = (Get-ChildItem -LiteralPath $sourceFolders -File -Recurse | Sort-Object FullName | ForEach-Object {
    $_.FullName.Substring($root.Length) + '|' + $_.Length + '|' + $_.LastWriteTimeUtc.Ticks
}) -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
try { $stamp = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceDescription))).Replace('-','') }
finally { $sha.Dispose() }
$stampFile = Join-Path $build 'source-stamp.txt'
$clientExe = Join-Path $build 'Client/Blacktide.Performance.exe'
$serverExe = Join-Path $build 'Server/Blacktide.PerformanceServer.exe'
$editor = $null; $client = $null; $server = $null
$results = [Collections.Generic.List[object]]::new()
. (Join-Path $PSScriptRoot 'Common/PerformanceReport.ps1')
try {
    if ($ReuseBuild) {
        if (!(Test-Path -LiteralPath $stampFile) -or (Get-Content -LiteralPath $stampFile -Raw).Trim() -ne $stamp -or
            !(Test-Path -LiteralPath $clientExe) -or !(Test-Path -LiteralPath $serverExe)) {
            throw 'Performance build is missing or source changed. Run again without -ReuseBuild.'
        }
    } else {
        if (!(Test-Path -LiteralPath $UnityPath)) { throw "Unity not found: $UnityPath" }
        . (Join-Path $PSScriptRoot 'Common/UnityProject.ps1')
        Sync-UnityProject -ProjectRoot $root -Destination $copy
        $editorFolder = Join-Path $copy 'Assets/Editor/PerformanceValidation'
        New-Item -ItemType Directory -Force -Path $editorFolder | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'Tests/Unity/Performance/BuildDemolitionPerformance.cs') -Destination $editorFolder
        foreach ($name in @('PerformanceCapture.cs','DemolitionPerformanceDriver.cs','DemolitionServerPerformanceDriver.cs')) {
            Copy-Item -LiteralPath (Join-Path $root "Tests/Unity/Performance/$name") -Destination (Join-Path $copy 'Assets')
        }
        $buildLog = Join-Path $output 'build.log'
        $editorArgs = @('-batchmode','-quit','-projectPath',('"'+$copy+'"'),'-executeMethod','BuildDemolitionPerformance.Run',
            '--test-output',('"'+$build+'"'),'-logFile',('"'+$buildLog+'"'))
        $editor = Start-Process $UnityPath -ArgumentList $editorArgs -WindowStyle Hidden -PassThru
        & (Join-Path $PSScriptRoot 'wait-unity-task.ps1') -Process $editor -LogPath $buildLog -TimeoutSeconds 1800
        if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $buildLog -Pattern 'DEMOLITION_PERFORMANCE_BUILD_PASS' -Quiet)) { throw "Performance build failed: $buildLog" }
        Set-Content -LiteralPath $stampFile -Value $stamp -Encoding ascii
    }
    for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
        foreach ($variant in $Variants) {
            $folder = Join-Path $output ("$variant-$repeat")
            New-Item -ItemType Directory -Force -Path $folder | Out-Null
            $udp = [Net.Sockets.UdpClient]::new(0); $port = $udp.Client.LocalEndPoint.Port; $udp.Dispose()
            $keyPath = Join-Path $folder 'match.key'
            $keyBytes = New-Object byte[] 32; $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($keyBytes); [IO.File]::WriteAllBytes($keyPath,$keyBytes) } finally { $rng.Dispose() }
            $seats = @(1..10 | ForEach-Object { [ordered]@{ Id=$_; Team=$(if($_ -le 5){0}else{1}); UserId="performance-$_"; Name="Perf $_"; IsBot=($_ -ne 1) } })
            $manifest = [ordered]@{ MatchId='performance-baseline-v1'; Port=$port; KeyFile=$keyPath; ReadyFile=(Join-Path $folder 'ready'); ResultFile=(Join-Path $folder 'match-result.json'); Seats=$seats }
            $manifestPath = Join-Path $folder 'manifest.json'
            $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
            Write-Output "[阶段] 跑测 $variant / $View / 第 $repeat 次；每阶段 $SecondsPerPhase 秒"
            $serverArgs = @('-batchmode','-nographics','--demolition-manifest',('"'+$manifestPath+'"'),
                '--perf-output',('"'+$folder+'"'),'-logFile',('"'+(Join-Path $folder 'server.log')+'"'))
            $server = Start-Process $serverExe -ArgumentList $serverArgs -WindowStyle Hidden -PassThru
            $deadline = (Get-Date).AddSeconds(90)
            while (!(Test-Path -LiteralPath $manifest.ReadyFile)) {
                if ($server.HasExited -or (Get-Date) -gt $deadline) { throw "Performance server failed to become ready: $folder" }
                Start-Sleep -Milliseconds 200
            }
            $clientArgs = @('-screen-fullscreen','0','-screen-width',$Width,'-screen-height',$Height,'-force-d3d11',
                '--demolition-manifest',('"'+$manifestPath+'"'),'--perf-output',('"'+$folder+'"'),'--perf-variant',$variant,
                '--perf-view',$View,'--perf-seconds',$SecondsPerPhase,'--perf-trace',$(if($ProfilerTrace){'1'}else{'0'}),
                '-logFile',('"'+(Join-Path $folder 'client.log')+'"'))
            # A real rendered game window is required: never use -batchmode/-nographics or minimize this client.
            $client = Start-Process $clientExe -ArgumentList $clientArgs -PassThru
            $deadline = (Get-Date).AddSeconds(3 * $SecondsPerPhase + 150)
            $lastPhase = ''; $lastStatus = Get-Date
            while (!$client.WaitForExit(500)) {
                if ($server.HasExited) { throw "Server exited during capture: $folder" }
                if ((Get-Date) -gt $deadline) { throw "Performance client timed out: $folder" }
                $log = Join-Path $folder 'client.log'
                if (Test-Path -LiteralPath $log) {
                    $phaseLine = Get-Content -LiteralPath $log -Tail 12 | Where-Object { $_ -match '^PERFORMANCE_PHASE_START' } | Select-Object -Last 1
                    if ($phaseLine -and $phaseLine -ne $lastPhase) { Write-Output $phaseLine; $lastPhase = $phaseLine }
                }
                if (((Get-Date) - $lastStatus).TotalSeconds -ge 30) { Write-Output "[阶段] $variant 正在采样，日志：$folder"; $lastStatus = Get-Date }
            }
            $client.Refresh()
            Set-Content -LiteralPath (Join-Path $folder 'finish-server') -Value 'finish'
            if (!$server.WaitForExit(15000)) { throw "Server metrics shutdown timed out: $folder" }
            $server.Refresh()
            if ($client.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $folder 'client-result.txt'))) {
                Write-PerformanceReport -Directory $output
                $failure = Join-Path $folder 'failure.txt'
                if (Test-Path -LiteralPath $failure) { Write-Output (Get-Content -LiteralPath $failure -Raw) }
                throw "Performance client failed: $folder"
            }
            if ($server.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $folder 'server-summary.json'))) { throw "Server capture failed: $folder" }
            $summary = Get-Content -LiteralPath (Join-Path $folder 'client-summary.json') -Raw | ConvertFrom-Json
            foreach ($phase in $summary.summaries) {
                $row = [ordered]@{ variant=$variant; repeat=$repeat; view=$View; phase=$phase.phase; frames=$phase.frames; fps=[Math]::Round($phase.fps,2); alive_fraction=$phase.aliveFraction }
                foreach ($metric in $phase.metrics) { $row[$metric.metric+'_mean'] = $metric.mean; $row[$metric.metric+'_p95'] = $metric.p95; $row[$metric.metric+'_p99'] = $metric.p99 }
                $results.Add([pscustomobject]$row)
            }
            $results | Export-Csv -LiteralPath (Join-Path $output 'comparison.csv') -NoTypeInformation -Encoding utf8
            Write-PerformanceReport -Directory $output
            Write-Output "DEMOLITION_PERFORMANCE_RUN_PASS $folder"
        }
    }
    @{
        created=(Get-Date).ToString('o'); sourceStamp=$stamp; width=$Width; height=$Height; view=$View;
        secondsPerPhase=$SecondsPerPhase; repeats=$Repeats; variants=$Variants;
        note='Client and authority run on the same computer. Development Mono / D3D11 / uncapped. Compare per-phase frame distributions and coverage; AI matches are not deterministic replays.'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'run.json') -Encoding utf8
    $results | Select-Object variant,repeat,phase,fps,frame_ms_p95,frame_ms_p99,vision_ms_mean,cutaway_ms_mean,gpu_frame_ms_mean | Format-Table -AutoSize
    Write-Output "DEMOLITION_PERFORMANCE_PASS $output"
} finally {
    # Only processes started by this invocation are eligible for cleanup.
    foreach ($process in @($client,$server,$editor)) {
        if ($null -ne $process -and !$process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) }
    }
}
