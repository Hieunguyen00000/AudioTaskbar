#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
RES=()
if command -v x86_64-w64-mingw32-windres >/dev/null 2>&1; then
  WINDRES=x86_64-w64-mingw32-windres
elif command -v windres >/dev/null 2>&1; then
  WINDRES=windres
else
  WINDRES=
fi
if [[ -n "$WINDRES" && -f TaskbarTap.rc ]]; then
  "$WINDRES" -i TaskbarTap.rc -o TaskbarTap.res -O coff
  RES+=(TaskbarTap.res)
  echo "Embedded VERSIONINFO from TaskbarTap.rc ($WINDRES)"
else
  echo "WARNING: windres not found; TaskbarTap.dll will have no VERSIONINFO."
fi
x86_64-w64-mingw32-g++ -shared -O2 -std=c++17 -s \
  -fno-exceptions -fno-rtti \
  -DWIN32_LEAN_AND_MEAN -DUNICODE -D_UNICODE \
  -o TaskbarTap.dll \
  TaskbarTap.cpp \
  TaskbarTap.def \
  "${RES[@]}" \
  -static-libgcc -static-libstdc++ \
  -lole32 -loleaut32 -lruntimeobject -luuid -lshlwapi -lshell32 -ladvapi32
rm -f TaskbarTap.res
x86_64-w64-mingw32-objdump -p TaskbarTap.dll | grep -E 'DllGetClassObject|DllCanUnloadNow' || true
ls -la TaskbarTap.dll
