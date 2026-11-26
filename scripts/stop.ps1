$procs = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host "Audio Taskbar khong dang chay."
    exit 0
}
foreach ($p in $procs) {
    try { $p.CloseMainWindow() | Out-Null } catch {}
}
Start-Sleep -Milliseconds 800
$left = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
if ($left) { $left | Stop-Process -Force }
Write-Host "Da tat Audio Taskbar."
