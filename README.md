# Audio Taskbar v4.0

Ứng dụng Windows nhỏ vẽ một **diorama thời tiết sống** (48px) ngay trên nền thanh taskbar: bầu trời đổi theo giờ trong ngày, mây/mưa/sấm, tàu hỏa chạy trái → phải, người đi xe đạp, chim, đèn phố, đom đóm… Hiệu ứng nằm **phía sau** icon nên nút Start, Search, app đang chạy và khay hệ thống vẫn sắc, bấm bình thường.

Phiên bản 4.0 tạm tắt spectrum / waveform và phần bắt âm thanh để máy nhẹ hơn. Kiến trúc visualizer (WASAPI loopback + FFT) vẫn còn trong mã nguồn và có thể bật lại bằng `config.json`.

Bật/tắt toàn bộ hiệu ứng mọi lúc bằng phím **F8** (hotkey toàn cục).

## Tính năng nổi bật

- **Diorama 48px** chạy dọc taskbar, theo nhịp ngày đêm (bình minh → trưa → hoàng hôn → đêm).
- **Thời tiết thật** từ [Open-Meteo](https://open-meteo.com/) (không cần API key): nhiệt độ, trạng thái tiếng Việt, hiệu ứng mưa/nắng/giông rất nhẹ. Vị trí tự dò theo IP, hoặc khóa thành phố / tọa độ trong config.
- **Cảnh vật sống:** tàu hỏa (đầu máy + toa, đèn, khói), xe đạp (đèn ban đêm, dù khi mưa), chim ban ngày, đom đóm / sao băng đêm quang, khinh khí cầu lúc nắng, dãy đồi / phố sáng đèn.
- **Chặn wallpaper:** nền taskbar dùng lớp tối đặc (`backdropOpacity: 1`) đặt sát phía sau `Shell_TrayWnd`. Ảnh nền desktop không xuyên qua hay ám màu lên thanh taskbar, kể cả vùng icon.
- **Clean-room XAML TAP** (`TaskbarTap.dll`): gọi `InitializeXamlDiagnosticsEx`, đi cây XAML, làm trong suốt `Rectangle#BackgroundFill` và ẩn `BackgroundStroke` — không cần cài Windhawk. Thất bại thì tự quay về chế độ an toàn (khoét mềm vùng icon).
- **Phím F8 toàn cục** + menu icon khay (cảnh vật, xem trước thời tiết, chặn ảnh nền, làm mới thời tiết).
- **Đa màn hình / DPI:** PerMonitor V2, tùy chọn hiện trên mọi màn hình; widget thời tiết mặc định chỉ trên màn hình chính.
- **Một file chạy:** `release/AudioTaskbar.exe` (single-file .NET 8, win-x64, self-contained).

## Yêu cầu

- Windows 11 (đã kiểm tra trên bản 24H2/26H2, build 26300). Windows 10 có thể chạy phần diorama nhưng TAP XAML của taskbar 11 sẽ không gắn được — app tự rơi về overlay an toàn.
- Không cần quyền Administrator (`asInvoker`).
- Kết nối mạng lần đầu để lấy thời tiết (có cache khi offline).

## Bắt đầu nhanh

1. Mở thư mục dự án rồi chạy `start.bat` (hoặc `scripts\start.bat`).
2. Tìm icon **Audio Taskbar** ở khay hệ thống (gần đồng hồ, có thể nằm trong mũi tên overflow).
3. Nhấn **F8** để ẩn/hiện hiệu ứng. Chuột phải icon khay để chỉnh cảnh vật hoặc xem trước kiểu thời tiết.
4. Tắt bằng *Thoát* trên menu khay, hoặc `stop.bat`.

Nếu chưa có `release\AudioTaskbar.exe`, build trước: `scripts\build.ps1` (cần .NET 8 SDK và `src\TaskbarTap\TaskbarTap.dll` — xem [docs/development.md](docs/development.md)).

Cấu hình, log và cache nằm ở `%LOCALAPPDATA%\AudioTaskbar\` (`config.json`, `app.log`, `weather-cache.json`, `tap-status.txt`). Không lưu config trong thư mục dự án. Mẫu: [`config.example.json`](config.example.json).

## Cài đặt và lối tắt

| Việc | Cách |
|------|------|
| Chạy từ dự án | `start.bat` / `stop.bat` |
| Tự khởi động cùng Windows | `scripts\enable-autostart.ps1` (tắt: `scripts\disable-autostart.ps1`). Mặc định **không** bật. |
| Shortcut Desktop / Start Menu | Tạo lối tắt trỏ tới `release\AudioTaskbar.exe` (working directory = thư mục `release`). |
| Gỡ | Xem [docs/troubleshooting.md](docs/troubleshooting.md#gỡ-cài-đặt). |

Chỉ một instance được chạy (mutex `Local\Hieunv1.AudioTaskbar.Singleton`).

## Phím tắt

- **F8** (mặc định, toàn cục): bật/tắt hiệu ứng. Tắt sẽ ẩn diorama; lớp TAP (nếu đang gắn) được **giữ nguyên** để tránh Explorer khởi động lại khi gỡ giữa chừng. Thoát app thì nền taskbar được khôi phục.
- Đổi phím trong `%LOCALAPPDATA%\AudioTaskbar\config.json`, ví dụ:

```json
"hotkey": "Ctrl+Alt+F8"
```

Hỗ trợ tổ hợp `Ctrl`, `Alt`, `Shift`, `Win` với phím F1–F24 hoặc một ký tự. Nếu phím bị app khác chiếm, icon khay báo và sự kiện được ghi vào `app.log`.

## Menu khay hệ thống

- **Bật/tắt hiệu ứng (F8)**
- **Cảnh vật:** Xe lửa, Xe đạp, Chim & đèn phố, Thời tiết (hiệu ứng mưa/nắng/sấm)
- **Xem trước thời tiết:** Thực tế, Nắng đẹp, Mây trôi, Mưa rào, Giông bão, Đêm sao
- **Thời tiết:** hiện thẻ nhiệt độ trên taskbar, *Làm mới ngay*
- **Chặn ảnh nền desktop:** bật/tắt TAP + nền đặc phía sau icon
- Tải lại cấu hình, mở thư mục cấu hình, Thoát

## Cấu trúc dự án

```
AudioTaskbar/
  src/                      # host .NET 8 WinForms / GDI+
    AudioTaskbar.csproj
    Program.cs, TrayApp.cs, Diorama.cs, OverlayWindow.cs, ...
    TaskbarTap/             # mã C++ của TaskbarTap.dll
      TaskbarTap.cpp, TaskbarTap.def
      build-dll.ps1, build.sh
  scripts/
    build.ps1               # publish single-file exe → release/
    start.ps1, stop.ps1, enable-autostart.ps1, disable-autostart.ps1
  tests/test_analyzer.py    # kiểm tra FFT/analyzer (Python)
  docs/                     # tài liệu chi tiết
  config.example.json
  NOTICE.md
  start.bat, stop.bat
  release/                  # output khi build (exe không đưa lên git)
```

## Tài liệu

- [Kiến trúc hệ thống](docs/architecture.md) — host .NET, TAP native, z-order, so sánh với Windhawk
- [Tham chiếu cấu hình](docs/configuration.md) — toàn bộ khóa trong `config.json`
- [Phát triển & build](docs/development.md) — biên dịch DLL, publish exe, chạy test
- [Xử lý sự cố](docs/troubleshooting.md) — Explorer/TAP, phím F8, sai vị trí thời tiết, đa màn hình, gỡ cài

## License / notices

Mã managed và `TaskbarTap.dll` là mã viết mới cho dự án này. Cách tiếp cận XAML Diagnostics (`InitializeXamlDiagnosticsEx` + TAP) là cơ chế có tài liệu của Windows, cũng được Visual Studio, ExplorerTAP / Windhawk Taskbar Styler / TranslucentTB dùng.

`windhawk-mods` và TranslucentTB phát hành theo GPL-3.0. Dự án này **không sao chép** mã nguồn của họ — TAP được viết clean-room từ header Microsoft (`xamlom.h`) và hành vi API đã công bố. Chi tiết: [NOTICE.md](NOTICE.md).
