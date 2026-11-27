# Build an unsigned (or optionally test-signed) MSIX for Microsoft Store upload.
# Usage (from repo root, on Windows):
#   powershell -ExecutionPolicy Bypass -File scripts\build-msix.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\build-msix.ps1 -SignTestCert
# Output:
#   packaging\msix\out\AudioTaskbar_<version>_x64.msix
# The Microsoft Store re-signs the package after certification - a CA cert is not required for upload.
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SignTestCert,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root "src\AudioTaskbar.csproj"
$tapScript = Join-Path $root "src\TaskbarTap\build-dll.ps1"
$tap = Join-Path $root "src\TaskbarTap\TaskbarTap.dll"
$manifestSrc = Join-Path $root "packaging\msix\AppxManifest.xml"
$assetsSrc = Join-Path $root "packaging\msix\Assets"
$payload = Join-Path $root "packaging\msix\payload"
$defaultOut = Join-Path $root "packaging\msix\out"
if (-not $OutputDir) { $OutputDir = $defaultOut }

function Get-AppVersion {
    [xml]$proj = Get-Content $src
    $ver = $proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $ver) { throw "Could not read <Version> from $src" }
    return $ver.Trim()
}

function Find-MakeAppx {
    $found = @()
    $kitRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )
    foreach ($kit in $kitRoots) {
        if (Test-Path $kit) {
            $found += Get-ChildItem -Path $kit -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
                Where-Object { $_.DirectoryName -match '\\x64$' -or $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending
        }
    }
    $tools = Join-Path $root ".tools"
    if (Test-Path $tools) {
        $found += Get-ChildItem -Path $tools -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' -or $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending
    }
    if ($found.Count -gt 0) { return $found[0].FullName }
    return $null
}

function Install-SdkBuildTools {
    $ver = "10.0.26100.6584"
    $id = "microsoft.windows.sdk.buildtools"
    $tools = Join-Path $root ".tools"
    $dest = Join-Path $tools ("Microsoft.Windows.SDK.BuildTools." + $ver)
    $already = Get-ChildItem -Path $dest -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($already) { return }
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    $nupkg = Join-Path $tools ($id + "." + $ver + ".nupkg")
    $url = "https://api.nuget.org/v3-flatcontainer/$id/$ver/$id.$ver.nupkg"
    Write-Host "Downloading Microsoft.Windows.SDK.BuildTools $ver (provides makeappx.exe)..."
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    $zip = $nupkg + ".zip"
    Copy-Item $nupkg $zip -Force
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}

function Find-SignTool {
    $kitRoots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )
    foreach ($kit in $kitRoots) {
        if (Test-Path $kit) {
            $hit = Get-ChildItem -Path $kit -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
                Where-Object { $_.DirectoryName -match '\\x64$' -or $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    $tools = Join-Path $root ".tools"
    if (Test-Path $tools) {
        $hit = Get-ChildItem -Path $tools -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -match '\\x64$' -or $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

# --- TaskbarTap.dll ---
if (-not (Test-Path $tap)) {
    if (Test-Path $tapScript) {
        Write-Host "Building TaskbarTap.dll..."
        & $tapScript
        if (-not (Test-Path $tap)) { throw "TaskbarTap.dll was not produced by $tapScript" }
    } else {
        throw "Missing $tap - build it first with src\TaskbarTap\build-dll.ps1"
    }
}

$version = Get-AppVersion
$packageVersion = "$version.0"
$msixName = "AudioTaskbar_${packageVersion}_x64.msix"
Write-Host "Packaging Audio Taskbar $packageVersion (Store build, win-x64, not single-file)..."

# --- publish (not single-file; TaskbarTap.dll must remain a loose PE in the package) ---
$publishDir = Join-Path $root "packaging\msix\publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

dotnet publish $src -c $Configuration -r win-x64 --self-contained true `
    -p:StoreBuild=true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (StoreBuild) failed." }

$exe = Join-Path $publishDir "AudioTaskbar.exe"
if (-not (Test-Path $exe)) { throw "Publish succeeded but AudioTaskbar.exe is missing." }
$publishedTap = Join-Path $publishDir "TaskbarTap.dll"
if (-not (Test-Path $publishedTap)) {
    Copy-Item $tap $publishedTap -Force
    Write-Host "Copied TaskbarTap.dll into the publish folder."
}
if (-not (Test-Path $publishedTap)) { throw "TaskbarTap.dll is missing from the publish folder." }

# --- stage payload ---
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null
Get-ChildItem $publishDir | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $payload $_.Name) -Recurse -Force
}
Copy-Item $manifestSrc (Join-Path $payload "AppxManifest.xml") -Force
# Stamp Identity/@Version from the csproj if it drifted.
$manifestPath = Join-Path $payload "AppxManifest.xml"
[xml]$manifest = Get-Content $manifestPath
$manifest.Package.Identity.Version = $packageVersion
$manifest.Save($manifestPath)
$assetsDest = Join-Path $payload "Assets"
New-Item -ItemType Directory -Path $assetsDest -Force | Out-Null
Copy-Item (Join-Path $assetsSrc "*") $assetsDest -Force

# --- makeappx ---
$makeappx = Find-MakeAppx
if (-not $makeappx) {
    Install-SdkBuildTools
    $makeappx = Find-MakeAppx
}
if (-not $makeappx) { throw "makeappx.exe not found. Install the Windows SDK or allow the script to restore Microsoft.Windows.SDK.BuildTools." }
Write-Host "Using makeappx: $makeappx"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$msix = Join-Path $OutputDir $msixName
if (Test-Path $msix) { Remove-Item $msix -Force }

& $makeappx pack /d $payload /p $msix /o
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msix)) { throw "makeappx pack failed." }

if ($SignTestCert) {
    $signtool = Find-SignTool
    if (-not $signtool) { throw "-SignTestCert was set but signtool.exe was not found (install the Windows SDK)." }
    $certDir = Join-Path $root ".tools\certs"
    New-Item -ItemType Directory -Path $certDir -Force | Out-Null
    $pfx = Join-Path $certDir "AudioTaskbar-Test.pfx"
    $cer = Join-Path $certDir "AudioTaskbar-Test.cer"
    $pwdPlain = "AudioTaskbar-Test"
    $pwd = ConvertTo-SecureString $pwdPlain -AsPlainText -Force
    if (-not (Test-Path $pfx)) {
        Write-Host "Creating a local code-signing test certificate (not for Store upload)..."
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Audio Taskbar Test" -CertStoreLocation Cert:\CurrentUser\My -HashAlgorithm SHA256 -KeyExportPolicy Exportable
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Write-Host "Test cert exported to $cer - import it as Trusted People / Trusted Root on this machine to sideload."
    }
    & $signtool sign /fd SHA256 /a /f $pfx /p $pwdPlain $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed." }
    Write-Host "Test-signed $msix"
} else {
    Write-Host "Unsigned package (ready to upload to Partner Center; the Store will re-sign it)."
}

$item = Get-Item $msix
Write-Host ("Xong: {0}  size={1:N0} bytes" -f $item.FullName, $item.Length)
Write-Host $item.FullName
