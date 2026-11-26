#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
x86_64-w64-mingw32-g++ -shared -O2 -std=c++17 -s \
  -fno-exceptions -fno-rtti \
  -DWIN32_LEAN_AND_MEAN -DUNICODE -D_UNICODE \
  -o TaskbarTap.dll \
  TaskbarTap.cpp \
  TaskbarTap.def \
  -static-libgcc -static-libstdc++ \
  -lole32 -loleaut32 -lruntimeobject -luuid -lshlwapi -lshell32 -ladvapi32
x86_64-w64-mingw32-objdump -p TaskbarTap.dll | grep -E 'DllGetClassObject|DllCanUnloadNow' || true
ls -la TaskbarTap.dll
