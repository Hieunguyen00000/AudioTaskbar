# Rebuild TaskbarTap.dll (with VERSIONINFO from TaskbarTap.rc).
# Prefers mingw-w64 (x86_64-w64-mingw32-g++ / windres), then MSVC (cl.exe / rc.exe).
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here "TaskbarTap.dll"
$rc   = Join-Path $here "TaskbarTap.rc"

function Build-Mingw {
    Push-Location $here
    try {
        $windres = Get-Command windres, x86_64-w64-mingw32-windres, llvm-windres -ErrorAction SilentlyContinue | Select-Object -First 1
        $resArgs = @()
        if ($windres) {
            $res = Join-Path $here "TaskbarTap.res"
            & $windres.Source -i TaskbarTap.rc -o $res -O coff
            if ($LASTEXITCODE -ne 0) { throw "windres failed with exit $LASTEXITCODE" }
            $resArgs += $res
            Write-Host "Embedded VERSIONINFO from TaskbarTap.rc ($($windres.Source))"
        } else {
            Write-Host "WARNING: windres not found; TaskbarTap.dll will have no VERSIONINFO."
        }
        & x86_64-w64-mingw32-g++ -shared -O2 -std=c++17 -s `
            -fno-exceptions -fno-rtti `
            -DWIN32_LEAN_AND_MEAN -DUNICODE -D_UNICODE `
            -o TaskbarTap.dll `
            TaskbarTap.cpp TaskbarTap.def @resArgs `
            -static-libgcc -static-libstdc++ `
            -lole32 -loleaut32 -lruntimeobject -luuid -lshlwapi -lshell32 -ladvapi32
        if ($LASTEXITCODE -ne 0) { throw "mingw-w64 build failed with exit $LASTEXITCODE" }
        if ($resArgs) { Remove-Item $resArgs -Force -ErrorAction SilentlyContinue }
    } finally { Pop-Location }
}

function Build-Msvc {
    Push-Location $here
    try {
        $resArgs = @()
        $rcExe = Get-Command rc.exe -ErrorAction SilentlyContinue
        if ($rcExe -and (Test-Path $rc)) {
            & rc.exe /nologo /fo TaskbarTap.res TaskbarTap.rc
            if ($LASTEXITCODE -ne 0) { throw "rc.exe failed with exit $LASTEXITCODE" }
            $resArgs += "TaskbarTap.res"
            Write-Host "Embedded VERSIONINFO from TaskbarTap.rc (rc.exe)"
        } else {
            Write-Host "WARNING: rc.exe not found; TaskbarTap.dll will have no VERSIONINFO."
        }
        & cl.exe /nologo /LD /O2 /EHs- /GR- /DWIN32_LEAN_AND_MEAN /DUNICODE /D_UNICODE `
            TaskbarTap.cpp /Fe:TaskbarTap.dll `
            /link /DEF:TaskbarTap.def @resArgs ole32.lib oleaut32.lib runtimeobject.lib uuid.lib shlwapi.lib shell32.lib advapi32.lib
        if ($LASTEXITCODE -ne 0) { throw "MSVC build failed with exit $LASTEXITCODE" }
        Get-ChildItem $here -Filter "TaskbarTap.exp","TaskbarTap.lib","TaskbarTap.obj","*.obj","TaskbarTap.res" -ErrorAction SilentlyContinue |
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
