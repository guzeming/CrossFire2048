param([Parameter(Mandatory = $true)][string]$Target)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = [IO.Path]::GetFullPath((Join-Path $root $Target))
Write-Output '[进度] 0 正在还原 .NET 依赖'
dotnet restore $project --nologo
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
Write-Output '[进度] 30 依赖已还原，正在编译 .NET 程序'
dotnet build $project -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET build failed.' }
Write-Output '[进度] 99 .NET 编译完成，正在检查任务结果'
