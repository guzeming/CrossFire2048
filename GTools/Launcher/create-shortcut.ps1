$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut((Join-Path $root '游戏启动面板.lnk'))
    $shortcut.TargetPath = Join-Path $root '游戏启动面板.bat'
    $shortcut.WorkingDirectory = $root
    $shortcut.IconLocation = (Join-Path $PSScriptRoot 'Blacktide.ico') + ',0'
    $shortcut.Description = 'Operation Blacktide - Game and Server Launcher'
    $shortcut.WindowStyle = 1
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
