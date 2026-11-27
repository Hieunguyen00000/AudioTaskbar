# Write SHA256SUMS.txt + latest.json for a release (used by publish-release.ps1 and CI).
#   .\scripts\make-release-manifest.ps1 -Version 5.3.0 -SetupPath installer-output\AudioTaskbar-Setup-5.3.0.exe
# latest.json is what the in-app updater reads (also usable on any static host via updateFeedUrl).
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$SetupPath,
    [string]$ExePath,
    [string]$OutDir,
    [string]$Repo = "Hieunguyen00000/AudioTaskbar",
    [string]$BaseUrl,
    [string]$Notes = ""
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $SetupPath)) { throw "Missing $SetupPath" }
if (-not $OutDir) { $OutDir = Split-Path -Parent (Resolve-Path $SetupPath) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$setupName = Split-Path -Leaf $SetupPath
if (-not $BaseUrl) { $BaseUrl = "https://github.com/$Repo/releases/download/v$Version" }

$lines = @()
$setupHash = (Get-FileHash -LiteralPath $SetupPath -Algorithm SHA256).Hash.ToLowerInvariant()
$lines += "$setupHash  $setupName"
if ($ExePath -and (Test-Path -LiteralPath $ExePath)) {
    $exeHash = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$exeHash  AudioTaskbar.exe"
}
$sums = Join-Path $OutDir "SHA256SUMS.txt"
# LF line endings, no BOM (sha256sum -c compatible)
[System.IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

$manifest = [ordered]@{
    version     = $Version
    fileName    = $setupName
    downloadUrl = "$BaseUrl/$setupName"
    sha256      = $setupHash
    size        = (Get-Item -LiteralPath $SetupPath).Length
    pageUrl     = "https://github.com/$Repo/releases/tag/v$Version"
    notes       = $Notes
    published   = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$latest = Join-Path $OutDir "latest.json"
[System.IO.File]::WriteAllText($latest, ($manifest | ConvertTo-Json -Depth 3), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "SHA256SUMS.txt -> $sums"
Write-Host "latest.json    -> $latest"
Write-Host "sha256($setupName) = $setupHash"
