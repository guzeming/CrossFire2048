param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [string]$UnityPath,
    [string]$Target,
    [switch]$NoViewer
)
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $encoding
$OutputEncoding = $encoding
$arguments = @{}
if ($UnityPath) { $arguments.UnityPath = $UnityPath }
if ($Target) { $arguments.Target = $Target }
if ($NoViewer) { $arguments.NoViewer = $true }
try { & $ScriptPath @arguments; exit 0 }
catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
