# Gracefully stop AudioTaskbar.exe only when it is running FROM $InstallDir.
# Never signals the exit event (and never kills a process) for a copy running
# from somewhere else — e.g. a dev build on another drive must be left alone.
# Also never touches Explorer.
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDir
)

$ErrorActionPreference = "Continue"
try {
    $InstallDir = [System.IO.Path]::GetFullPath($InstallDir.TrimEnd("\", "/"))
} catch {
    Write-Host "Invalid install dir."
    exit 0
}

function Get-InstalledCopies {
    Get-CimInstance Win32_Process -Filter "Name='AudioTaskbar.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            if (-not $_.ExecutablePath) { return $false }
            try {
                $path = [System.IO.Path]::GetFullPath($_.ExecutablePath)
            } catch {
                return $false
            }
            $prefix = $InstallDir.TrimEnd("\", "/") + [System.IO.Path]::DirectorySeparatorChar
            return $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        }
}

$targets = @(Get-InstalledCopies)
if ($targets.Count -eq 0) {
    Write-Host "No AudioTaskbar process is running from $InstallDir."
    exit 0
}

Write-Host ("Stopping {0} AudioTaskbar process(es) from {1}..." -f $targets.Count, $InstallDir)
try {
    $ev = [System.Threading.EventWaitHandle]::OpenExisting("Local\AudioTaskbar.Cmd.Exit")
    [void]$ev.Set()
    $ev.Dispose()
} catch {
    # Older builds may not expose the clean-exit event.
}

$deadline = (Get-Date).AddSeconds(8)
while ((Get-Date) -lt $deadline -and (Get-InstalledCopies)) {
    Start-Sleep -Milliseconds 200
}

$left = @(Get-InstalledCopies)
if ($left.Count -gt 0) {
    foreach ($p in $left) {
        try {
            $proc = Get-Process -Id $p.ProcessId -ErrorAction Stop
            [void]$proc.CloseMainWindow()
        } catch {}
    }
    Start-Sleep -Milliseconds 800
    $left = @(Get-InstalledCopies)
    foreach ($p in $left) {
        try { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue } catch {}
    }
}

if (Get-InstalledCopies) {
    Write-Host "Warning: AudioTaskbar is still running from the install folder."
    exit 1
}
Write-Host "Audio Taskbar has exited cleanly."
exit 0
