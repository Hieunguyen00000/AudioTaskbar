# Phát triển và build

Repo chứa mã nguồn. File chạy `release/AudioTaskbar.exe` và `TaskbarTap.dll` **không** đưa lên git (xem `.gitignore`) — phải build lại trên máy Windows.

## Môi trường

- Windows 10/11 x64 (publish target: `win-x64`).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (desktop / Windows workload, vì dự án dùng WinForms).
- Để biên dịch TAP: **mingw-w64** (`x86_64-w64-mingw32-g++` và `windres` trong PATH) hoặc **MSVC** (`cl.exe` / `rc.exe` từ *Developer PowerShell for VS*). `TaskbarTap.rc` nhúng VERSIONINFO (bắt buộc khi ký SignPath) — giữ số phiên bản trùng với `<Version>` trong `src/AudioTaskbar.csproj`.
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
rc /fo TaskbarTap.res TaskbarTap.rc
cl /LD /O2 /EHs- /GR- /DWIN32_LEAN_AND_MEAN /DUNICODE /D_UNICODE ^
   TaskbarTap.cpp /Fe:TaskbarTap.dll /link /DEF:TaskbarTap.def TaskbarTap.res ^
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

Kết quả: `release\AudioTaskbar.exe` (single-file, self-contained, khoảng 70 MB, phiên bản assembly `5.2.0`). Không cần cài .NET Runtime trên máy chạy.

Chạy nhanh khi đang dev (không single-file):

```powershell
dotnet run --project src\AudioTaskbar.csproj -c Debug
```

## Xem trước không cần màn hình (`--render`, v5)

```powershell
release\AudioTaskbar.exe --render out.png --size 1920x60 --cutouts 8-198,516-1403,1653-1920 `
  --region auto --place "Ho Chi Minh City" --temp 27 --code 3 --day 0 --hud 1
```

Tham số: `--hour <0–24>` (ghim giờ cảnh, ví dụ `17.75` = 17:45; v5.1), `--timemode cycle|real`, `--size WxH`, `--cutouts l-r,...` (px vật lý, vùng icon), `--region`, `--preview` (sunny/cloudy/rain/storm/night), `--hud 0|1`, `--train <phút>`, `--airplane 0|1`, `--airplane-min <phút>`, `--train-phase <0..1>` / `--plane-phase <0..1>` (ghim vị trí trong vòng đời để xem từng giai đoạn; v5.4), `--place`, `--temp`, `--code` (WMO), `--day 0|1`, `--cloud`, `--t <giây>`, `--frames`, `--fused 0|1`. Ghi kèm `out.png.txt` (khu vực, khoảng trống, vị trí thẻ, scale). Dùng để kiểm tra layout trên máy bị khóa màn hình hoặc bị SAC chặn chạy app.

Lưu ý: dự án bật `UseWPF` (để dùng UI Automation); `GlobalUsings.cs` thêm lại `System.IO` / `System.Net.Http`.

## 3. Chạy tests

```powershell
dotnet test tests\AudioTaskbar.Tests\AudioTaskbar.Tests.csproj -c Release
python tests\test_analyzer.py
```

`tests/AudioTaskbar.Tests` (xUnit, .NET 8, v5.3) ràng buộc các lỗi có thể gặp khi dùng phần mềm. Không cần mạng (mạng được giả lập bằng `HttpMessageHandler` và một HTTP server cục bộ 127.0.0.1), không đụng Explorer/taskbar thật:

| File | Bao phủ |
|---|---|
| `ConfigTests` | JSON hỏng / sai kiểu / rỗng → mặc định + bản sao `.corrupt-*`; thiếu khóa; giá trị âm, NaN, quá lớn; migrate tàu 25 → 1 phút đúng một lần; khóa v5.3 round-trip |
| `WeatherTests` | offline, timeout, HTTP 500/502/429/404, JSON lỗi → không ném lỗi, giữ thời tiết cũ; bảng mã WMO (mọi mã tuyết, mưa, giông, sương, quang, mã lạ, ban đêm); chống nhảy vị trí > 80 km (cần 2 lần liên tiếp); khớp khu vực (Hồ Chí Minh → hcmc, Bà Rịa → baria-vungtau, lạ → default) |
| `SceneTests` | giờ ảo chu kỳ 30 phút quay vòng 0..24, ngày trước 1970, ghi đè giờ; cung mặt trời/mặt trăng luôn trong khung ở 100/125/150/300 %; tiến độ tàu không NaN/âm, mượt, quay vòng 1 lần/chu kỳ; thanh kích thước 0/âm; render đủ khung cho 7 khu vực × thời tiết |
| `LayoutTests` | khoét icon chồng nhau / trùng / ra ngoài thanh / phủ kín; màn hình phụ; DPI 100/125/150/200 %; thẻ thời tiết không bao giờ đè icon, ẩn khi không đủ chỗ |
| `HotkeyTests` | F8/F9/Ctrl+Alt+F8/Ctrl+Shift+F8…, phím sai, rỗng → F8, 3000 chuỗi ngẫu nhiên không ném lỗi |
| `UpdaterTests` | SemVer (5.3.0 < 5.3.1, 5.10.0 > 5.2.0, tiền tố v, pre-release, chuỗi sai); 404 (repo Private) / 401 / 403 / 429 / 5xx / offline / timeout; SHA-256 khớp → giữ, sai / file cụt → xóa và từ chối; thiếu asset Setup; SHA256SUMS thiếu dòng; tên file không phải .exe / path traversal; tham số NSIS bản cài vs portable; bản dev không tự cài; end-to-end qua HTTP server cục bộ |
| `PortableUpdateTests` | Ưu tiên asset `AudioTaskbar.exe` hơn Setup; tải về tên `AudioTaskbar-<ver>.exe` + kiểm SHA; file giả bị xóa; thông báo lỗi không lộ repo/URL (404/403/429/5xx/offline/JSON hỏng); nội dung `apply-update.cmd` (chờ PID, `.old`, rollback, `--minimized --updated`, thoát `%`); chạy thật script: đổi exe sau khi process thoát, rollback khi thiếu file mới; kiểm quyền ghi |
| `ModeTests` | `timeMode` cycle/real/day/night + alias tiếng Việt; *Cố định ban ngày* luôn 10:30–12:00 (Day), *ban đêm* 23:30–00:00 (Night, trăng giữa trời) bất kể giờ máy; render được; `blockWallpaper=false` / `backdropOpacity=0.5` trong config bị ép về `true` / `1.0` |
| `TapSafetyTests` | chống chèn TAP hai lần (tái dùng v3, từ chối khi có TAP cũ), cầu dao 90 s khi Explorer thoát sau khi gắn |

CI (`.github/workflows/release.yml`) chạy bộ test này trên mọi pull request và **trước** khi đóng gói release — test đỏ thì không phát hành.

Test tái hiện thuật toán FFT/analyzer (Python), gồm:

- đỉnh sine đúng tần số,
- silence ≈ 0,
- so khớp `numpy.fft` nếu có numpy (bỏ qua nếu chưa cài).

Không có test UI / TAP tự động (cần Explorer trên Windows 11 thật). Khi đổi TAP, kiểm tra tay: gắn fusion, bấm Start, F8 vài lần, thoát app — Explorer không được restart, icon không bị che.

## 4. Đóng gói trình cài đặt (NSIS)

File phát hành chính (v5.3) là **một** `AudioTaskbar.exe` tự chứa — người dùng tải về và double-click. Tùy chọn kèm `AudioTaskbar-Setup-<version>.exe` (version đọc từ `<Version>` trong `src\AudioTaskbar.csproj`) (cài theo user, không UAC, không cần .NET SDK).

Yêu cầu: [NSIS 3.x](https://nsis.sourceforge.io/) (`makensis` trong PATH). Trên Linux/WSL: `sudo apt-get install nsis`. Trên Windows: `winget install --id NSIS.NSIS --scope user` hoặc `scoop install nsis`.

```powershell
# 1) Publish exe (nếu chưa có release\AudioTaskbar.exe đúng version)
.\scripts\build.ps1

# 2) Đóng gói Setup
.\scripts\build-installer.ps1
```

Tương đương thủ công:

```text
makensis -INPUTCHARSET UTF8 installer\AudioTaskbar.nsi
```

Kết quả (không commit file exe):

- `installer-output\AudioTaskbar-Setup-<version>.exe`
- bản sao tại `installer\AudioTaskbar-Setup-<version>.exe`

Setup đóng gói `release\AudioTaskbar.exe` (self-contained, đã embed `TaskbarTap.dll` + .NET 8), `README.md`, `NOTICE.md`, `config.example.json` và icon đa độ phân giải. **Không** đóng gói `config.json` cá nhân. Người dùng mới nhận mặc định sạch: `regionMode=auto`, `showWeatherHud=false`, `trainCrossingMinutes=1`, `timeMode=cycle`, `dayCycleMinutes=30`, `tapMode=auto`.

Cài mặc định vào `%LOCALAPPDATA%\Programs\AudioTaskbar`, tạo shortcut Desktop/Start Menu (tùy chọn), đăng ký gỡ cài chuẩn tại `HKCU\...\Uninstall\AudioTaskbar`. Trước khi ghi đè hoặc gỡ, Setup/Uninstall chỉ tắt `AudioTaskbar.exe` đang chạy **từ thư mục cài** (sự kiện `Local\AudioTaskbar.Cmd.Exit`) — không kill Explorer, không đụng bản đang chạy từ thư mục khác (ví dụ bản dev trên ổ D:).

Cờ silent hữu ích khi kiểm tra (không đụng shortcut/app đang mở của máy dev):

```text
AudioTaskbar-Setup-5.3.0.exe /S /NORUN /NODESKTOP /NOSTARTMENU /D=%TEMP%\AudioTaskbar-InstallTest
%TEMP%\AudioTaskbar-InstallTest\Uninstall.exe /S
```

`/D=` phải là tham số cuối và không đặt trong dấu ngoặc kép.

Cờ của trình cài (v5.3): `/S` im lặng; `/NORUN` không mở app sau khi cài; `/UPDATED` (bộ tự cập nhật dùng) mở lại bằng `--minimized --updated` để hiện thông báo *Đã cập nhật*; cài im lặng không có `/NORUN` luôn mở lại bằng `--minimized`. Nếu máy đã bật *Khởi động cùng Windows*, bản nâng cấp giữ nguyên và ghi lại lệnh kèm `--minimized`.

## 5. Đóng gói / phát hành

- Đưa lên git: mã nguồn, scripts (kể cả `installer\AudioTaskbar.nsi`), tests, docs — **không** commit `release\*.exe`, `*.dll`, hay `AudioTaskbar-Setup-*.exe`.
- Phát hành cho người dùng: gửi `release\AudioTaskbar.exe` (một file), hoặc phát hành GitHub Release (mục 6) để mọi máy tự cập nhật. Setup là tùy chọn.
- Config người dùng luôn ở `%LOCALAPPDATA%\AudioTaskbar\`, tách khỏi bản phân phối.
- Tự khởi động: tùy chọn trong Setup (HKCU Run, mặc định tắt), hoặc `scripts\enable-autostart.ps1` tạo shortcut trong thư mục Startup của user.

## 6. Phát hành & tự động cập nhật

Mọi bản cài v5.3+ tự cập nhật lên **đúng version bạn phát hành**:

1. Nâng `<Version>` trong `src\AudioTaskbar.csproj` (ví dụ `5.3.1`), commit.
2. Chọn một cách phát hành:
   - **GitHub Actions (khuyên dùng):** `git tag v5.3.1` rồi `git push origin main v5.3.1`. Workflow `release.yml` (windows-latest) chạy `dotnet test`, build `TaskbarTap.dll` (MSYS2 mingw-w64), `scripts\build.ps1`, NSIS, tạo `SHA256SUMS.txt` + `latest.json` và đăng Release `v5.3.1` với `AudioTaskbar.exe` (file chính), `SHA256SUMS.txt`, `latest.json` và Setup (tùy chọn). Tag phải trùng `<Version>`, nếu không workflow dừng.
   - **Máy dev:** `.\scripts\publish-release.ps1` (test → build `AudioTaskbar.exe` → SHA256SUMS + latest.json → `gh release create`, hoặc REST API với `$env:GITHUB_TOKEN`). `-NoUpload` chỉ build; `-WithSetup` kèm Setup.
3. App của người dùng: 10 s sau khi mở và mỗi 6 giờ gọi `https://api.github.com/repos/<updateRepo>/releases/latest` → so SemVer với version đang chạy → ưu tiên asset `AudioTaskbar.exe`, tải vào `%LOCALAPPDATA%\AudioTaskbar\updates\AudioTaskbar-<ver>.exe` → kiểm SHA-256 (sai thì xóa file, không cài) → kiểm quyền ghi cạnh exe → ghi `updates\apply-update.cmd` rồi app tự thoát sạch (TAP Restore, không đụng Explorer). Script chờ PID thoát (tối đa 60 s), `move` exe cũ → `AudioTaskbar.exe.old`, `copy` bản mới vào đúng `Environment.ProcessPath` (lỗi → khôi phục `.old`), mở lại `--minimized --updated` (thông báo) và tự xóa; lần chạy sau xóa `.old`. Nếu Release chỉ có Setup, app dùng đường cũ `Setup.exe /S /UPDATED`. Thông báo cho người dùng chỉ là câu tiếng Việt thân thiện (*Hiện chưa có bản phát hành mới.* / *Không thể kiểm tra cập nhật lúc này.*), không lộ repo/URL; chi tiết (HTTP code, Private repo, rate limit) ghi vào `app.log`.
4. Bản chạy từ thư mục dev (cạnh `.git`) chỉ báo có bản mới, không tự cài. Người dùng tắt được ở *Hệ thống → Tự động kiểm tra cập nhật* hoặc `autoUpdateSilentInstall: false`.

**Repo đang Private:** API GitHub trả 404 cho người không đăng nhập, app báo *“Không tìm thấy bản phát hành… Repo có thể đang Private”* và không làm gì thêm. Để người dùng nhận bản mới, chọn một trong hai:

- (a) Chuyển repo sang **Public**, hoặc tạo repo **Public chỉ chứa Release** (ví dụ `Hieunguyen00000/AudioTaskbar-releases`) rồi đặt `updateRepo` trỏ tới đó (đổi mặc định trong `Config.cs` cho bản sau). Workflow có thể đăng lên repo khác bằng `softprops/action-gh-release` + `repository:` và một token có quyền ghi.
- (b) Host `latest.json` + `AudioTaskbar.exe` ở chỗ công khai (GitHub Pages, Cloudflare R2, S3, máy chủ riêng) và đặt `updateFeedUrl` = URL của `latest.json`. `scripts\make-release-manifest.ps1 -BaseUrl https://…` tạo đúng file.

GitHub API ẩn danh giới hạn 60 lượt/giờ/IP; app chỉ gọi 4 lần/ngày, 403/429 được báo nhẹ và thử lại lần sau.

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
src/app.ico                 # multi-size 16–256 (shared with the Setup wizard)
scripts/build.ps1
scripts/build-installer.ps1
scripts/build-msix.ps1
packaging/msix/
installer/AudioTaskbar.nsi
installer/stop-installed.ps1
installer/assets/
src/TaskbarTap/build-dll.ps1
src/TaskbarTap/build.sh
```

## Microsoft Store (MSIX)

Gói Store dùng `-p:StoreBuild=true` (không single-file) và script `scripts/build-msix.ps1`. Chi tiết identity, cập nhật Store, và cách nộp Partner Center: [store.md](store.md). Kênh GitHub portable/NSIS (`scripts/build.ps1`) không đổi.
