$startup = [Environment]::GetFolderPath("Startup")
$lnkPath = Join-Path $startup "Audio Taskbar.lnk"
if (Test-Path $lnkPath) {
    Remove-Item $lnkPath -Force
    Write-Host "Da tat tu khoi dong (xoa $lnkPath)."
} else {
    Write-Host "Khong co shortcut tu khoi dong."
}
