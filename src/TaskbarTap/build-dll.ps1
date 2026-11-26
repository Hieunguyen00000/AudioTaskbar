# Rebuild TaskbarTap.dll
# Prefers mingw-w64 (x86_64-w64-mingw32-g++), then MSVC (cl.exe from a Developer PowerShell).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here "TaskbarTap.dll"

function Build-Mingw {
    Push-Location $here
    try {
        & x86_64-w64-mingw32-g++ -shared -O2 -std=c++17 -s `
            -fno-exceptions -fno-rtti `
            -DWIN32_LEAN_AND_MEAN -DUNICODE -D_UNICODE `
            -o TaskbarTap.dll `
            TaskbarTap.cpp TaskbarTap.def `
            -static-libgcc -static-libstdc++ `
            -lole32 -loleaut32 -lruntimeobject -luuid -lshlwapi -lshell32 -ladvapi32
        if ($LASTEXITCODE -ne 0) { throw "mingw-w64 build failed with exit $LASTEXITCODE" }
    } finally { Pop-Location }
}

function Build-Msvc {
    Push-Location $here
    try {
        & cl.exe /nologo /LD /O2 /EHs- /GR- /DWIN32_LEAN_AND_MEAN /DUNICODE /D_UNICODE `
            TaskbarTap.cpp /Fe:TaskbarTap.dll `
            /link /DEF:TaskbarTap.def ole32.lib oleaut32.lib runtimeobject.lib uuid.lib shlwapi.lib shell32.lib advapi32.lib
        if ($LASTEXITCODE -ne 0) { throw "MSVC build failed with exit $LASTEXITCODE" }
        Get-ChildItem $here -Filter "TaskbarTap.exp","TaskbarTap.lib","TaskbarTap.obj","*.obj" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    } finally { Pop-Location }
}

$gcc = Get-Command x86_64-w64-mingw32-g++ -ErrorAction SilentlyContinue
$cl  = Get-Command cl.exe -ErrorAction SilentlyContinue

if ($gcc) {
    Write-Host "Building TaskbarTap.dll with mingw-w64 ($($gcc.Source)) ..."
    Build-Mingw
} elseif ($cl) {
    Write-Host "Building TaskbarTap.dll with MSVC ($($cl.Source)) ..."
    Build-Msvc
} else {
    Write-Host "Khong tim thay x86_64-w64-mingw32-g++ hoac cl.exe."
    Write-Host "Cai mingw-w64, mo 'Developer PowerShell for VS', hoac bien dich bang build.sh tren Linux/WSL."
    exit 1
}

if (-not (Test-Path $out)) { throw "Build reported success but $out is missing." }
Write-Host ("Da tao {0}  ({1:N0} bytes)" -f $out, (Get-Item $out).Length)
