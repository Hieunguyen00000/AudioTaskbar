# Build AudioTaskbar-Setup-<version>.exe (per-user NSIS installer). Version = <Version> in src/AudioTaskbar.csproj.
# Requires: NSIS 3.x (makensis) and release\AudioTaskbar.exe (scripts\build.ps1).
#
# Linux / WSL (after `sudo apt-get install nsis`):
#   makensis -INPUTCHARSET UTF8 installer/AudioTaskbar.nsi
#
# Windows (NSIS from winget, scoop, or the official installer):
#   .\scripts\build-installer.ps1

[CmdletBinding()]
param(
    [string]$PayloadExe,
    [string]$OutFile,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $PayloadExe) { $PayloadExe = Join-Path $root "release\AudioTaskbar.exe" }
$nsi = Join-Path $root "installer\AudioTaskbar.nsi"
$outDirA = Join-Path $root "installer-output"
$outDirB = Join-Path $root "installer"
if (-not $Version) {
    $csproj = Get-Content -Raw (Join-Path $root "src\AudioTaskbar.csproj")
    if ($csproj -match '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') { $Version = $Matches[1] } else { throw "Cannot read <Version> from src\AudioTaskbar.csproj" }
}
$defaultName = "AudioTaskbar-Setup-$Version.exe"
if (-not $OutFile) { $OutFile = Join-Path $outDirA $defaultName }

if (-not (Test-Path -LiteralPath $nsi)) { throw "Missing $nsi" }
if (-not (Test-Path -LiteralPath $PayloadExe)) {
    throw "Missing $PayloadExe — run scripts\build.ps1 first."
}

# Refuse to pack a stale exe when FileVersion is available (Windows only).
try {
    $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($PayloadExe)
    if ($ver.FileVersion -and $ver.FileVersion -notlike "$Version*") {
        throw "Payload FileVersion is '$($ver.FileVersion)', expected $Version.x ($PayloadExe)"
    }
    Write-Host "Payload: $PayloadExe"
    Write-Host "  FileVersion    : $($ver.FileVersion)"
    Write-Host "  ProductVersion : $($ver.ProductVersion)"
    Write-Host "  Size           : $((Get-Item -LiteralPath $PayloadExe).Length) bytes"
} catch [System.Management.Automation.RuntimeException] {
    throw
} catch {
    Write-Host "Payload: $PayloadExe ($((Get-Item -LiteralPath $PayloadExe).Length) bytes) — could not read FileVersion here."
}

function Find-Makensis {
    $cmd = Get-Command makensis, makensis.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\NSIS\makensis.exe",
        "$env:ProgramFiles\NSIS\makensis.exe",
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "$env:USERPROFILE\scoop\apps\nsis\current\makensis.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    return $null
}

$makensis = Find-Makensis
if (-not $makensis) {
    throw @"
makensis not found. Install NSIS 3.x, then re-run this script.

  winget install --id NSIS.NSIS --scope user
  scoop install nsis
  sudo apt-get install nsis          # Linux / WSL
"@
}
Write-Host "makensis: $makensis"

New-Item -ItemType Directory -Force -Path $outDirA, $outDirB | Out-Null

# NSIS /D values must not be quoted; use forward slashes to avoid escaping issues.
$payloadDef = ($PayloadExe -replace "\\", "/")
$outDef = ($OutFile -replace "\\", "/")
$args = @(
    "-INPUTCHARSET", "UTF8",
    "-DPAYLOAD_EXE=$payloadDef",
    "-DAPP_VERSION=$Version",
    "-DOUTFILE=$outDef",
    $nsi
)
Write-Host "Running: $makensis $($args -join ' ')"
& $makensis @args
if ($LASTEXITCODE -ne 0) { throw "makensis failed with exit code $LASTEXITCODE" }

if (-not (Test-Path -LiteralPath $OutFile)) { throw "makensis reported success but $OutFile is missing." }

$copyTo = Join-Path $outDirB $defaultName
if ((Resolve-Path $OutFile).Path -ne (Join-Path $outDirB $defaultName)) {
    Copy-Item -LiteralPath $OutFile -Destination $copyTo -Force
}

$built = Get-Item -LiteralPath $OutFile
$hash = (Get-FileHash -LiteralPath $OutFile -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Built: $($built.FullName)"
Write-Host "Size : $($built.Length) bytes ($([Math]::Round($built.Length/1MB, 2)) MB)"
Write-Host "SHA256: $hash"
if (Test-Path -LiteralPath $copyTo) {
    Write-Host "Copy : $copyTo"
}
