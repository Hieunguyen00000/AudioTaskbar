$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe = Join-Path $root "release\AudioTaskbar.exe"
if (-not (Test-Path $exe)) { throw "Khong thay $exe — hay chay scripts\build.ps1 truoc." }
$startup = [Environment]::GetFolderPath("Startup")
$lnkPath = Join-Path $startup "Audio Taskbar.lnk"
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = Join-Path $root "release"
$lnk.WindowStyle = 7
$lnk.Description = "Audio Taskbar v4 — weather diorama (F8 to toggle)"
$lnk.Save()
Write-Host "Da tao shortcut tu khoi dong: $lnkPath"
Write-Host "Target: $exe"
