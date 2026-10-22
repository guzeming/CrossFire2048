param([string]$DataDirectory = 'data')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$data = [IO.Path]::GetFullPath((Join-Path $root $DataDirectory))
$state = Join-Path $data 'local-server-processes.json'
if (!(Test-Path -LiteralPath $state)) { Write-Output 'No managed local servers recorded.'; exit 0 }
# Windows PowerShell 5.1 returns the JSON array as one pipeline object.
# Assign it directly so foreach visits each server rather than a nested array.
$records = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
Set-Content -LiteralPath (Join-Path $data 'local-server.stop') -Value 'stop'
foreach ($record in $records) {
    $process = Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    if ($null -ne $process -and $process.StartTime.ToUniversalTime().Ticks -eq $record.Started -and $process.Path -eq $record.Path) {
        if (!$process.WaitForExit(10000)) {
            $process.Kill()
            if (!$process.WaitForExit(5000)) { throw "Server process $($record.Id) has not stopped. Process record retained." }
        }
        Write-Output "Stopped server process $($record.Id)."
    } elseif ($null -ne $process) {
        Write-Output "Skipped process $($record.Id): identity does not match the managed server."
    }
    if ($null -ne $process) { $process.Dispose() }
}
Remove-Item -LiteralPath $state
Write-Output 'Local servers stopped. Logs retained.'
