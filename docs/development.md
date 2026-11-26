# Phát triển và build

Repo chứa mã nguồn. File chạy `release/AudioTaskbar.exe` và `TaskbarTap.dll` **không** đưa lên git (xem `.gitignore`) — phải build lại trên máy Windows.

## Môi trường

- Windows 10/11 x64 (publish target: `win-x64`).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (desktop / Windows workload, vì dự án dùng WinForms).
- Để biên dịch TAP: **mingw-w64** (`x86_64-w64-mingw32-g++` trong PATH) hoặc **MSVC** (`cl.exe` từ *Developer PowerShell for VS*).
- Python 3 để chạy `tests/test_analyzer.py` (numpy tùy chọn, chỉ để so sánh FFT).

Kiểm tra:

```powershell
dotnet --version          # >= 8.0
x86_64-w64-mingw32-g++ --version   # hoặc: cl
python --version
```

## 1. Biên dịch `TaskbarTap.dll`

DLL được embed vào exe qua `AudioTaskbar.csproj` (`LogicalName=AudioTaskbar.TaskbarTap.dll`) nếu file tồn tại tại:

- `src/TaskbarTap/TaskbarTap.dll` (ưu tiên), hoặc
- `native/out/TaskbarTap.dll` (đường dẫn legacy trên máy build Linux/WSL).

### mingw-w64 trên Windows

```powershell
cd src\TaskbarTap
.\build-dll.ps1
```

### MSVC

Mở *Developer PowerShell for Visual Studio*, rồi chạy cùng script (tự chọn `cl.exe` nếu không có mingw):

```powershell
cd src\TaskbarTap
.\build-dll.ps1
```

Tương đương thủ công:

```text
cl /LD /O2 /EHs- /GR- /DWIN32_LEAN_AND_MEAN /DUNICODE /D_UNICODE ^
   TaskbarTap.cpp /Fe:TaskbarTap.dll /link /DEF:TaskbarTap.def ^
   ole32.lib oleaut32.lib runtimeobject.lib uuid.lib shlwapi.lib shell32.lib advapi32.lib
```

### Linux / WSL (cross-compile)

```bash
cd src/TaskbarTap   # hoặc native/ nếu đang dùng tree legacy
./build.sh
```

`build.sh` gọi `x86_64-w64-mingw32-g++ -shared ... -o out/TaskbarTap.dll` (trong `native/`) hoặc đặt DLL cạnh file `.cpp` tùy script. Copy kết quả thành `src/TaskbarTap/TaskbarTap.dll` trước khi publish nếu cần.

Thiếu DLL thì `dotnet publish` vẫn thành công, nhưng exe không embed TAP — *Chặn ảnh nền desktop* sẽ thất bại và app rơi về overlay an toàn. `scripts/build.ps1` in cảnh báo nếu không thấy DLL.

## 2. Publish `AudioTaskbar.exe`

Từ thư mục gốc dự án:

```powershell
.\scripts\build.ps1
```

Script sẽ:

1. Copy `native\out\TaskbarTap.dll` → `src\TaskbarTap\` nếu bản kia chưa có.
2. Xóa `src\bin`, `src\obj` và file cũ trong `release\`.
3. Chạy:

```powershell
dotnet publish src\AudioTaskbar.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o release
```

Kết quả: `release\AudioTaskbar.exe` (single-file, self-contained, khoảng 70 MB, phiên bản assembly `4.0.0`). Không cần cài .NET Runtime trên máy chạy.

Chạy nhanh khi đang dev (không single-file):

```powershell
dotnet run --project src\AudioTaskbar.csproj -c Debug
```

## 3. Chạy tests

```powershell
python tests\test_analyzer.py
```

Test tái hiện thuật toán FFT/analyzer (Python), gồm:

- đỉnh sine đúng tần số,
- silence ≈ 0,
- so khớp `numpy.fft` nếu có numpy (bỏ qua nếu chưa cài).

Không có test UI / TAP tự động (cần Explorer trên Windows 11 thật). Khi đổi TAP, kiểm tra tay: gắn fusion, bấm Start, F8 vài lần, thoát app — Explorer không được restart, icon không bị che.

## 4. Đóng gói / phát hành

- Đưa lên git: mã nguồn, scripts, tests, docs — **không** commit `release\*.exe` hay `*.dll`.
- Phát hành cho người dùng: zip `release\AudioTaskbar.exe` (+ tùy chọn `start.bat` / `stop.bat` / `config.example.json`), hoặc trỏ shortcut tới exe.
- Config người dùng luôn ở `%LOCALAPPDATA%\AudioTaskbar\`, tách khỏi bản phân phối.
- Tự khởi động: `scripts\enable-autostart.ps1` tạo shortcut trong thư mục Startup của user.

## Gợi ý khi sửa mã

| Muốn đổi | File chính |
|----------|------------|
| Cảnh tàu / xe / chim / trời | `src/Diorama.cs` |
| Menu khay, F8, vòng đời TAP | `src/TrayApp.cs` |
| Z-order, nền đặc, khoét icon | `src/OverlayWindow.cs` |
| Khóa config mới | `src/Config.cs` + `docs/configuration.md` + `config.example.json` |
| Thời tiết / geocode | `src/WeatherService.cs` |
| Làm trong suốt nền Explorer | `src/TaskbarTap/TaskbarTap.cpp`, `src/TaskbarTapHost.cs` |
| Spectrum / WASAPI | `src/Analyzer.cs`, `src/AudioCapture.cs`, `src/Presets.cs` |

Sau khi sửa C++, nhớ build lại DLL rồi `scripts\build.ps1` — exe chỉ embed DLL có mặt tại thời điểm publish.

## Cấu trúc build liên quan

```
src/AudioTaskbar.csproj     # net8.0-windows, WinForms, NAudio 2.2.1, embed TAP
src/app.manifest            # asInvoker, PerMonitorV2, supportedOS Win10/11
src/app.ico
scripts/build.ps1
src/TaskbarTap/build-dll.ps1
src/TaskbarTap/build.sh
```
