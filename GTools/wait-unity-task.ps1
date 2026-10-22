# Shared by all Unity tasks. Consume new bytes so bursts do not hide errors.
param(
    [Parameter(Mandatory = $true)][Diagnostics.Process]$Process,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [int]$TimeoutSeconds = 900,
    [ValidateRange(0, 99)][int]$ProgressStart = 10,
    [ValidateRange(1, 99)][int]$ProgressEnd = 98
)
$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$lastOutput = 0.0
$lastActivity = 0.0
$lastSummary = ''
$phase = '启动 Unity'
$asset = ''
$imported = 0
$stream = $null
$pending = ''
$decoder = [Text.Encoding]::UTF8.GetDecoder()
$buffer = New-Object byte[] 65536
$characters = New-Object char[] 65540
$startedAt = $Process.StartTime.ToUniversalTime()
if ($ProgressEnd -le $ProgressStart) { throw 'ProgressEnd must be greater than ProgressStart.' }
# These are workflow milestones; no progress is inferred from the clock.
$progressState = @{ Percent = -1; Stage = ''; Phase = $phase; Imported = 0; Asset = ''; Custom = $false; Building = $false }
function Write-UnityProgress([int]$Percent, [string]$Stage) {
    $value = [Math]::Max($progressState.Percent, [Math]::Min(100, $Percent))
    if ($value -ne $progressState.Percent -or $Stage -ne $progressState.Stage) {
        $progressState.Percent = $value; $progressState.Stage = $Stage
        $mapped = $ProgressStart + [int][Math]::Floor(($ProgressEnd - $ProgressStart) * $value / 100)
        Write-Output ('[进度] {0} {1}' -f $mapped, $Stage)
    }
}
function Read-UnityProgressLine([string]$Line) {
    if ($Line -match '^LAUNCHER_PROGRESS (\d{1,3}) (.+)$') {
        $value = [int]$Matches[1]; $label = $Matches[2]
        if ($value -le 100) {
            $progressState.Custom = $true; $progressState.Building = $label.StartsWith('正在编译并打包：')
            $progressState.Phase = $label; Write-UnityProgress $value $label
        }
    } elseif ($Line -match '\b[A-Z][A-Z0-9_]*_(PASS|FAIL|START|PROGRESS)\b|error CS\d|Exception:|Aborting batchmode') {
        Write-Output ('[Unity] ' + $Line)
        if ($Line -match '_FAIL|error CS|Exception:|Aborting') { $progressState.Phase = '检测到错误，查看下方日志' }
        elseif ($Line -match '\bARCHITECTURE_VERIFY_PASS\b') {
            $progressState.Phase = '检查结果并收尾'; Write-UnityProgress 95 $progressState.Phase
        } else {
            $progressState.Phase = '运行验证'; Write-UnityProgress 55 $progressState.Phase
        }
    } elseif ($Line -match '^Start importing (.*?) using Guid') {
        $progressState.Imported++; $progressState.Asset = $Matches[1]; $progressState.Phase = '导入资源'
        Write-UnityProgress 35 $progressState.Phase
    } elseif ($Line -match '^\[\s*(\d+)/(\d+)(?:\s+[^\]]*)?\]') {
        $done = [int]$Matches[1]; $total = [int]$Matches[2]
        if ($total -gt 0 -and $done -le $total -and (!$progressState.Custom -or $progressState.Building)) {
            $building = $progressState.Building
            $start = if ($building) { 75 } else { 15 }
            $progressState.Phase = "编译任务 $done/$total"
            Write-UnityProgress ($start + [int][Math]::Floor(15 * $done / $total)) $progressState.Phase
        }
    } elseif ($Line -match 'ScriptCompilation|Tundra|script compilation time') {
        $progressState.Phase = '编译脚本'; Write-UnityProgress 15 $progressState.Phase
    } elseif ($Line -match 'ReloadAssembly|Loaded All Assemblies') {
        $progressState.Phase = '加载脚本程序集'; Write-UnityProgress 30 $progressState.Phase
    } elseif ($Line -match '\[Package Manager\]|PackageManager') {
        $progressState.Phase = '准备依赖包'; Write-UnityProgress 5 $progressState.Phase
    } elseif ($Line -match 'Initialize engine version:') {
        $progressState.Phase = '初始化 Unity'; Write-UnityProgress 10 $progressState.Phase
    } elseif ($Line -match 'ShaderCompiler|Compiling shader') {
        $progressState.Phase = '编译着色器'; Write-UnityProgress 40 $progressState.Phase
    } elseif ($Line -match 'Application.Shutdown|Exiting batchmode') {
        $progressState.Phase = '收尾并退出 Unity'
        # Shutdown also occurs on failures. Only a successful process exit advances the final milestone.
    }
}
Write-UnityProgress 0 '启动 Unity'
Write-Output "[阶段] Unity 已启动（进程 $($Process.Id)）。首次导入该验证工程可能需要数分钟。"
Write-Output "完整 Unity 日志：$LogPath"
try {
    do {
        $exited = $Process.WaitForExit(1000)
        if ($null -eq $stream -and (Test-Path -LiteralPath $LogPath)) {
            $info = Get-Item -LiteralPath $LogPath
            if ($info.LastWriteTimeUtc -ge $startedAt) {
                try { $stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite) }
                catch [IO.IOException] { }
            }
        }
        if ($null -ne $stream) {
            if ($stream.Length -lt $stream.Position) { $stream.Position = 0; $decoder.Reset(); $pending = '' }
            $readThrough = $stream.Length
            while ($stream.Position -lt $readThrough) {
                $count = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $readThrough - $stream.Position))
                if ($count -eq 0) { break }
                $lastActivity = $clock.Elapsed.TotalSeconds
                $charCount = $decoder.GetChars($buffer, 0, $count, $characters, 0, $false)
                $text = $pending + [string]::new($characters, 0, $charCount)
                $lines = $text -split "`n"
                $pending = $lines[-1]
                for ($i = 0; $i -lt $lines.Length - 1; $i++) {
                    $line = $lines[$i].TrimEnd("`r")
                    Read-UnityProgressLine $line
                }
                if ($pending.Length -gt 65536) { $pending = $pending.Substring($pending.Length - 65536) }
            }
        }
        if ($exited -and $pending.Length -gt 0) {
            Read-UnityProgressLine $pending.TrimEnd("`r")
            $pending = ''
        }
        $phase = $progressState.Phase; $asset = $progressState.Asset; $imported = $progressState.Imported
        if ($exited) { $phase = 'Unity 已退出，正在检查任务结果' }
        if ($exited -and $Process.ExitCode -eq 0) { Write-UnityProgress 100 $phase }
        $summary = $phase
        if ($phase -eq '导入资源') {
            $summary += " · 本次已开始导入 $imported 项"
            if ($asset) { $summary += ' · ' + $asset }
        }
        if ($summary.Length -gt 240) { $summary = $summary.Substring(0, 240) + '…' }
        if ($summary -ne $lastSummary -and ($clock.Elapsed.TotalSeconds - $lastOutput -ge 2 -or $exited)) {
            Write-Output ('[阶段] {0} · 已用时 {1:mm\:ss}' -f $summary, $clock.Elapsed)
            $lastSummary = $summary; $lastOutput = $clock.Elapsed.TotalSeconds
        } elseif (!$exited -and $clock.Elapsed.TotalSeconds - $lastOutput -ge 15) {
            $quiet = [int]($clock.Elapsed.TotalSeconds - $lastActivity)
            Write-Output ('[阶段] {0} · 进程尚未退出 · {1} 秒无新日志 · 已用时 {2:mm\:ss}' -f $phase, $quiet, $clock.Elapsed)
            $lastOutput = $clock.Elapsed.TotalSeconds
        }
        if (!$exited -and $clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $Process.Kill(); [void]$Process.WaitForExit(10000)
            throw "Unity 任务超过 $TimeoutSeconds 秒，已请求停止。日志：$LogPath"
        }
    } while (!$exited)
}
finally { if ($null -ne $stream) { $stream.Dispose() } }
Write-Output "Unity 已退出，退出码 $($Process.ExitCode)。"
