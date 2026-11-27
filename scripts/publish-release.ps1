# One-command local release (run on the dev PC from the repo root):
#   .\scripts\publish-release.ps1                 # build + test + installer + SHA256SUMS + latest.json, then upload
#   .\scripts\publish-release.ps1 -NoUpload       # everything except creating the GitHub Release
#
# Version = <Version> in src\AudioTaskbar.csproj (bump it first and commit).
# Upload uses `gh release create` when GitHub CLI is installed and logged in, otherwise the GitHub
# REST API with $env:GITHUB_TOKEN (fine-grained token, "Contents: Read and write" on the repo).
# Alternative: just push a tag (git tag v5.3.0; git push origin v5.3.0) and let
# .github/workflows/release.yml build and publish the same assets on GitHub Actions.
[CmdletBinding()]
param(
    [string]$Repo = "Hieunguyen00000/AudioTaskbar",
    [switch]$NoUpload,
    [switch]$SkipTests,
    [switch]$Draft,
    [string]$Notes = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root
$csproj = Get-Content -Raw "src\AudioTaskbar.csproj"
if ($csproj -notmatch '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') { throw "No <Version> in src\AudioTaskbar.csproj" }
$version = $Matches[1]
$tag = "v$version"
Write-Host "== Audio Taskbar release $tag ($Repo) =="

if (-not (Test-Path "src\TaskbarTap\TaskbarTap.dll")) {
    Write-Host "TaskbarTap.dll missing - building it"
    & powershell -ExecutionPolicy Bypass -File "src\TaskbarTap\build-dll.ps1"
    if ($LASTEXITCODE -ne 0) { throw "TaskbarTap.dll build failed" }
}
if (-not $SkipTests) {
    dotnet test "tests\AudioTaskbar.Tests\AudioTaskbar.Tests.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed - release aborted." }
}
& powershell -ExecutionPolicy Bypass -File "scripts\build.ps1"
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
& powershell -ExecutionPolicy Bypass -File "scripts\build-installer.ps1" -Version $version
if ($LASTEXITCODE -ne 0) { throw "build-installer.ps1 failed" }

$out = Join-Path $root "installer-output"
$setup = Join-Path $out "AudioTaskbar-Setup-$version.exe"
$exe = Join-Path $root "release\AudioTaskbar.exe"
if ([string]::IsNullOrWhiteSpace($Notes)) { $Notes = "Audio Taskbar $tag" }
& powershell -ExecutionPolicy Bypass -File "scripts\make-release-manifest.ps1" -Version $version -SetupPath $setup -ExePath $exe -OutDir $out -Repo $Repo -Notes $Notes
$assets = @($setup, (Join-Path $out "SHA256SUMS.txt"), (Join-Path $out "latest.json"))

if ($NoUpload) {
    Write-Host "`nBuilt (not uploaded):"; $assets | ForEach-Object { Write-Host "  $_" }
    exit 0
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($gh) {
    & gh release view $tag --repo $Repo *> $null
    if ($LASTEXITCODE -eq 0) {
        & gh release upload $tag @assets --repo $Repo --clobber
    } else {
        $flags = @("--repo", $Repo, "--title", "Audio Taskbar $tag", "--notes", $Notes)
        if ($Draft) { $flags += "--draft" }
        & gh release create $tag @assets @flags
    }
    if ($LASTEXITCODE -ne 0) { throw "gh release failed" }
} else {
    if (-not $env:GITHUB_TOKEN) { throw "Install GitHub CLI (winget install GitHub.cli; gh auth login) or set GITHUB_TOKEN." }
    $h = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = "application/vnd.github+json"; "X-GitHub-Api-Version" = "2022-11-28" }
    try { $rel = Invoke-RestMethod -Headers $h "https://api.github.com/repos/$Repo/releases/tags/$tag" }
    catch {
        $body = @{ tag_name = $tag; name = "Audio Taskbar $tag"; body = $Notes; draft = [bool]$Draft; prerelease = $false } | ConvertTo-Json
        $rel = Invoke-RestMethod -Method Post -Headers $h -ContentType "application/json" -Body $body "https://api.github.com/repos/$Repo/releases"
    }
    foreach ($a in $assets) {
        $name = Split-Path -Leaf $a
        $existing = $rel.assets | Where-Object { $_.name -eq $name }
        if ($existing) { Invoke-RestMethod -Method Delete -Headers $h $existing.url | Out-Null }
        $up = ($rel.upload_url -replace '\{.*\}$', '') + "?name=$([uri]::EscapeDataString($name))"
        Invoke-RestMethod -Method Post -Headers $h -ContentType "application/octet-stream" -InFile $a $up | Out-Null
        Write-Host "uploaded $name"
    }
}
Write-Host "`nRelease $tag published: https://github.com/$Repo/releases/tag/$tag"
Write-Host "Note: anonymous users can only auto-update from a PUBLIC repo (or set updateFeedUrl to a public latest.json)."
