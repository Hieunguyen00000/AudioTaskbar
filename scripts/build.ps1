# Build the self-contained single-file AudioTaskbar.exe into ..\release
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root "src\AudioTaskbar.csproj"
$out = Join-Path $root "release"
$tapMingw = Join-Path $root "native\out\TaskbarTap.dll"
$tap = Join-Path $root "src\TaskbarTap\TaskbarTap.dll"

if (-not (Test-Path $tap) -and (Test-Path $tapMingw)) {
    Copy-Item $tapMingw $tap -Force
    Write-Host "Copied TaskbarTap.dll from native\out into src\TaskbarTap\"
}

if (-not (Test-Path $tap)) {
    Write-Host "WARNING: $tap not found. Build it first with src\TaskbarTap\build-dll.ps1 (or build.sh)."
    Write-Host "The published exe will start, but wallpaper-blocking TAP fusion will be unavailable."
}

foreach ($d in @((Join-Path $root "src\bin"), (Join-Path $root "src\obj"))) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}
if (Test-Path $out) {
    Get-ChildItem $out -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $out | Out-Null
}

Write-Host "Publishing Audio Taskbar v4 to $out ..."
dotnet publish $src -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out "AudioTaskbar.exe"
if (-not (Test-Path $exe)) { throw "Publish succeeded but $exe is missing." }
$vi = (Get-Item $exe).VersionInfo
Write-Host ("Xong: {0}  version={1}  size={2:N0} bytes" -f $exe, $vi.ProductVersion, (Get-Item $exe).Length)
Write-Host "Chay start.bat de khoi dong. Config/log nam o %LOCALAPPDATA%\AudioTaskbar\"
