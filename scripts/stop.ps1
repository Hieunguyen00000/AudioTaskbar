$procs = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host "Audio Taskbar khong dang chay."
    exit 0
}
# Ask the app to exit cleanly first: it detaches TaskbarTap.dll from Explorer.
# Killing it while the TAP is attached can leave the DLL orphaned inside Explorer.
$signaled = $false
try {
    $ev = [System.Threading.EventWaitHandle]::OpenExisting("Local\AudioTaskbar.Cmd.Exit")
    [void]$ev.Set(); $ev.Dispose(); $signaled = $true
} catch {
    # Older builds (<= v5.1.0) have no exit event: ask them to detach the TAP first.
    try {
        $ev = [System.Threading.EventWaitHandle]::OpenExisting("Local\AudioTaskbar.Cmd.FusionOff")
        [void]$ev.Set(); $ev.Dispose(); Start-Sleep -Seconds 3
    } catch {}
}
$deadline = (Get-Date).AddSeconds(8)
while ($signaled -and (Get-Date) -lt $deadline -and (Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 200
}
$left = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
if ($left) {
    foreach ($p in $left) { try { $p.CloseMainWindow() | Out-Null } catch {} }
    Start-Sleep -Milliseconds 800
    $left = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
    if ($left) { $left | Stop-Process -Force }
}
Write-Host "Da tat Audio Taskbar."
