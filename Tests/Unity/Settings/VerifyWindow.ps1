param([string]$ClientPath = 'Builds/Client/Blacktide.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$client = [IO.Path]::GetFullPath((Join-Path $projectRoot $ClientPath))
$output = Join-Path $projectRoot '.build-check/display-settings'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SettingsWindowProbe {
    public delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wparam, IntPtr lparam);
    public static IntPtr Find(int pid) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((handle, unused) => { uint owner; GetWindowThreadProcessId(handle, out owner); Rect rect; GetClientRect(handle, out rect);
            if (owner == pid && rect.Right >= 640 && rect.Bottom >= 480) { result = handle; return false; } return true; }, IntPtr.Zero);
        return result;
    }
}
'@
$logPath = Join-Path $output 'window-smoke-player.log'
$process = Start-Process -FilePath $client -ArgumentList @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + $logPath + '"')) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $handle = [IntPtr]::Zero
    while ($handle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline -and !$process.HasExited) {
        Start-Sleep -Milliseconds 250
        $handle = [SettingsWindowProbe]::Find($process.Id)
    }
    if ($handle -eq [IntPtr]::Zero) { throw 'Client did not create a game window.' }
    Start-Sleep -Seconds 3
    $style = [SettingsWindowProbe]::GetWindowLong($handle, -16)
    if (($style -band 0x40000) -eq 0) { throw 'Native window is missing the resizable WS_THICKFRAME style.' }
    $before = New-Object SettingsWindowProbe+Rect
    [void][SettingsWindowProbe]::GetClientRect($handle, [ref]$before)
    if (![SettingsWindowProbe]::SetWindowPos($handle, [IntPtr]::Zero, 0, 0, 1060, 820, 0x16)) { throw 'Native window resize failed.' }
    Start-Sleep -Seconds 2
    $after = New-Object SettingsWindowProbe+Rect
    [void][SettingsWindowProbe]::GetClientRect($handle, [ref]$after)
    if ($after.Right -eq $before.Right -and $after.Bottom -eq $before.Bottom) { throw 'Client area remained fixed after resizing.' }
    [void][SettingsWindowProbe]::PostMessage($handle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
    if (!$process.WaitForExit(10000)) { throw 'Client failed to quit normally.' }
    if (Select-String -LiteralPath $logPath -Pattern 'NullReferenceException|MissingReferenceException|error CS|Failed to load' -Quiet) { throw "Client runtime error: $logPath" }
    $message = "WINDOW_SMOKE_PASS: resizable native border; client area $($before.Right)x$($before.Bottom) -> $($after.Right)x$($after.Bottom); normal process exit."
    Set-Content -LiteralPath (Join-Path $output 'window-smoke.txt') -Value $message
    Write-Output $message
}
finally {
    # Only the test process launched above is eligible for cleanup.
    if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    $process.Dispose()
}
