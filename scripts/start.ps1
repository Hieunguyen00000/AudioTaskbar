$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root "release\AudioTaskbar.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Chua tim thay $exe"
    Write-Host "Hay chay scripts\build.ps1 truoc."
    exit 1
}
$existing = Get-Process -Name AudioTaskbar -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Audio Taskbar dang chay (PID $($existing.Id -join ', '))."
    exit 0
}
Start-Process -FilePath $exe -WorkingDirectory (Join-Path $root "release")
Write-Host "Da bat Audio Taskbar tu $exe"
Write-Host "Tim icon o khay he thong (gan dong ho) de bat/tat (F8) hoac thoat."
Write-Host "Config/log: $env:LOCALAPPDATA\AudioTaskbar\"
