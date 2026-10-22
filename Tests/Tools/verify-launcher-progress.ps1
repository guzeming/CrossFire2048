param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$directory = Join-Path $root ('.build-check/launcher-progress-protocol-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$fixture = Join-Path $directory 'log writer.ps1'
$source = @'
param([string]$LogPath, [int]$ExitCode)
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($LogPath, "[Package Manager] ready`n", $encoding)
Start-Sleep -Milliseconds 1100
[IO.File]::AppendAllText($LogPath, "[1/4] Compile scripts`nLAUNCHER_PROGRESS 60 已检查 4/8 项`n", $encoding)
Start-Sleep -Milliseconds 1100
if ($ExitCode -eq 0) { [IO.File]::AppendAllText($LogPath, 'LAUNCHER_PROGRESS 95 检查完成', $encoding) }
else { [IO.File]::AppendAllText($LogPath, 'error CS1000: simulated failure', $encoding) }
exit $ExitCode
'@
[IO.File]::WriteAllText($fixture, $source, [Text.UTF8Encoding]::new($true))
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
foreach ($exitCode in @(0, 7)) {
    $log = Join-Path $directory ("unity-$exitCode.log")
    $child = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile', '-File', ('"' + $fixture + '"'), '-LogPath', ('"' + $log + '"'), '-ExitCode', $exitCode) -WindowStyle Hidden -PassThru
    try {
        $reports = @(& (Join-Path $root 'GTools/wait-unity-task.ps1') -Process $child -LogPath $log -TimeoutSeconds 15 -ProgressStart 20 -ProgressEnd 80)
        $reports | Set-Content -LiteralPath (Join-Path $directory ("reports-$exitCode.txt")) -Encoding UTF8
        $values = @($reports | Where-Object { $_ -match '^\[进度\] (\d+) ' } | ForEach-Object { [int]([regex]::Match($_, '^\[进度\] (\d+) ').Groups[1].Value) })
        if ($values -notcontains 56) { throw 'Structured progress did not reach the log reader.' }
        for ($i = 1; $i -lt $values.Count; $i++) { if ($values[$i] -lt $values[$i - 1]) { throw 'Progress moved backwards.' } }
        if ($child.ExitCode -ne $exitCode) { throw 'Fixture exit code was lost.' }
        if ($exitCode -eq 0) {
            if ($values -notcontains 77 -or $values[-1] -ne 80) { throw 'Final unterminated progress line or successful exit was lost.' }
        } else {
            if ($values[-1] -ne 56 -or !($reports -match 'error CS1000')) { throw 'Failure advanced progress or hid its final error.' }
        }
    }
    finally {
        if (!$child.HasExited) { $child.Kill(); [void]$child.WaitForExit(5000) }
        $child.Dispose()
    }
}
Write-Output "LAUNCHER_PROGRESS_PROTOCOL_PASS $directory"
