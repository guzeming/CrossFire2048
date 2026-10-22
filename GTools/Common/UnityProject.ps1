# Shared preparation for disposable Unity build and validation projects.
function Sync-UnityProject {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot, [Parameter(Mandatory)][string]$Destination)
    $source = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $target = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $allowed = Join-Path $source '.build-check'
    if (!$target.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unity copies must be inside this project .build-check directory.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $source 'ProjectSettings/ProjectVersion.txt'))) { throw "Not a Unity project: $source" }
    if (Test-Path -LiteralPath (Join-Path $target 'Temp/UnityLockfile')) { throw "Close the destination Unity project before synchronizing: $target" }
    # Do not mirror through junctions or symlinks, even inside the allowed directory.
    for ($ancestor = $target; $ancestor -and $ancestor -ne $source; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Unity copy path contains a link: $ancestor"
        }
    }
    if (Test-Path -LiteralPath $target) {
        $links = Get-ChildItem -LiteralPath $target -Force -Recurse -Attributes ReparsePoint | Select-Object -First 1
        if ($links) { throw "Unity copy contains a link: $($links.FullName)" }
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $synced = 0
    Write-Output '[进度] 0 正在准备并同步工程'
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        $from = Join-Path $source $folder
        $to = [IO.Path]::GetFullPath((Join-Path $target $folder))
        if (!(Test-Path -LiteralPath $from) -or !$to.StartsWith($target + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Invalid Unity copy folder: $folder"
        }
        Write-Output "[阶段] 同步资源：$folder"
        # Mirror only these three verified disposable folders. Library remains available.
        # Removes renamed scripts/assets and previous test drivers before a suite installs its own.
        & robocopy $from $to /MIR /XJ /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copy failed: $folder (robocopy $LASTEXITCODE)" }
        $synced++
        Write-Output ('[进度] {0} 工程目录已同步 {1}/3：{2}' -f [int][Math]::Floor(10 * $synced / 3), $synced, $folder)
    }
}
