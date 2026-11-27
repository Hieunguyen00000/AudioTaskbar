# Write SHA256SUMS.txt + latest.json for a release (used by publish-release.ps1 and CI).
#   .\scripts\make-release-manifest.ps1 -Version 5.3.0 -ExePath release\AudioTaskbar.exe [-SetupPath installer-output\AudioTaskbar-Setup-5.3.0.exe]
# v5.3: the primary asset is the single self-contained AudioTaskbar.exe (double-click to run, like UniKey);
# latest.json points the in-app updater at it. The optional Setup is listed in SHA256SUMS.txt too.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string]$SetupPath,
    [string]$OutDir = "installer-output",
    [string]$Repo = "Hieunguyen00000/AudioTaskbar",
    [string]$BaseUrl,
    [string]$Notes = ""
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $ExePath)) { throw "Missing $ExePath" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (-not $BaseUrl) { $BaseUrl = "https://github.com/$Repo/releases/download/v$Version" }

$exeHash = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
$lines = @("$exeHash  AudioTaskbar.exe")
if ($SetupPath -and (Test-Path -LiteralPath $SetupPath)) {
    $setupHash = (Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$setupHash  $(Split-Path -Leaf $SetupPath)"
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$sums = Join-Path $OutDir "SHA256SUMS.txt"
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $OutDir) "SHA256SUMS.txt"), (($lines -join "`n") + "`n"), $utf8)

$manifest = [ordered]@{
    version     = $Version
    fileName    = "AudioTaskbar.exe"
    downloadUrl = "$BaseUrl/AudioTaskbar.exe"
    sha256      = $exeHash
    size        = (Get-Item -LiteralPath $ExePath).Length
    pageUrl     = "https://github.com/$Repo/releases/tag/v$Version"
    notes       = $Notes
    published   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $OutDir) "latest.json"), ($manifest | ConvertTo-Json -Depth 3), $utf8)
Write-Host "SHA256SUMS.txt -> $sums"
Write-Host "latest.json    -> $(Join-Path $OutDir 'latest.json')"
Write-Host "sha256(AudioTaskbar.exe) = $exeHash"
