# Audio Taskbar v5.1.1

Ứng dụng Windows nhỏ vẽ một **diorama thời tiết sống** (48px) ngay trên nền thanh taskbar: bầu trời đổi theo giờ trong ngày, mây/mưa/sấm, tàu hỏa chạy trái → phải, người đi xe đạp, chim, đèn phố, đom đóm… Hiệu ứng nằm **phía sau** icon nên nút Start, Search, app đang chạy và khay hệ thống vẫn sắc, bấm bình thường.

**Mới ở v5.1.1:** `TaskbarTap.dll` thành dịch vụ thường trú tái sử dụng trong Explorer — tắt/bật app bao nhiêu lần cũng hòa nền liền mạch phía sau toàn bộ taskbar (không còn dải xám quanh icon), không inject lại, không làm Explorer khởi động lại. Xem [troubleshooting](docs/troubleshooting.md).

**Mới ở v5.1:** tàu chạy nhanh hơn — 1 phút/lượt (`trainCrossingMinutes: 1`); **chu kỳ ngày–đêm tăng tốc** mặc định 30 phút thật = 1 ngày 24 giờ (`timeMode: "cycle"`, `dayCycleMinutes: 30`), bầu trời, ánh sáng, đèn cửa sổ / đèn phố / đèn tàu, sao và đom đóm chuyển mượt liên tục (đêm → bình minh → sáng → trưa → chiều → hoàng hôn → chạng vạng → đêm); **mặt trời và mặt trăng đi theo cung** từ trái (đông) lên đỉnh rồi xuống phải (tây) — mặt trời 05:30–18:30 (cam vàng lúc mọc/lặn, trắng ấm buổi trưa), mặt trăng 18:30–05:30 (đỉnh lúc 00:00, có quầng sáng). Chuyển giữa *Chu kỳ 30 phút* và *Theo giờ thật* trong menu khay.

**Mới ở v5.0:** icon taskbar không bao giờ bị che (đo vị trí icon thật bằng UI Automation mỗi ~0,4 s, khoét chính xác theo DPI), một đoàn tàu cố định chạy trái → phải trong 25 phút, cảnh vật + giao thông theo khu vực (TP.HCM, Bà Rịa – Vũng Tàu, Hà Nội, Đà Nẵng – Hội An, Tokyo, Paris, mặc định), bật/tắt thẻ thời tiết & địa điểm, TAP gắn nhanh (≤ 3,5 s) và tự nhận biết Smart App Control.

Phiên bản 4.0 tạm tắt spectrum / waveform và phần bắt âm thanh để máy nhẹ hơn. Kiến trúc visualizer (WASAPI loopback + FFT) vẫn còn trong mã nguồn và có thể bật lại bằng `config.json`.

Bật/tắt toàn bộ hiệu ứng mọi lúc bằng phím **F8** (hotkey toàn cục).

## Tính năng nổi bật

- **Diorama 48px** chạy dọc taskbar, theo nhịp ngày đêm (bình minh → trưa → hoàng hôn → đêm).
- **Thời tiết thật** từ [Open-Meteo](https://open-meteo.com/) (không cần API key): nhiệt độ, trạng thái tiếng Việt, hiệu ứng mưa/nắng/giông rất nhẹ. Vị trí tự dò theo IP, hoặc khóa thành phố / tọa độ trong config.
- **Một đoàn tàu cố định** (đầu máy + 3 toa) chạy từ trái sang phải, đúng `trainCrossingMinutes` (mặc định 1 phút từ v5.1; v5.0 là 25 phút) cho một lượt; vị trí là hàm của thời gian nên mượt và luôn thấy ngay khi bật.
- **Cảnh vật theo khu vực:** mỗi khu vực có danh lam + skyline + giao thông đặc trưng, ví dụ TP.HCM (Landmark 81, Bitexco, Nhà thờ Đức Bà, cầu Ba Son; xe máy, xe buýt, xích lô), Vũng Tàu (Tượng Chúa, hải đăng quét đèn, hàng dừa Bãi Sau; tàu cá), Hà Nội (Tháp Rùa, cầu Long Biên, Chùa Một Cột, cầu Thê Húc), Đà Nẵng – Hội An (cầu Rồng phun lửa, Cầu Vàng, đèn lồng, thuyền hoa đăng), Tokyo (Phú Sĩ, Tokyo Tower, torii, hoa anh đào; kei truck, mamachari), Paris (Eiffel lấp lánh đầu giờ, Khải Hoàn Môn; 2CV, taxi cổ).
- **Khu vực tự động hoặc chọn tay:** `regionMode: "auto"` lấy theo vị trí thời tiết (IP / thành phố / tọa độ, có chống nhảy vị trí), hoặc chọn trong menu *Khu vực & Cảnh vật*.
- **Thẻ thời tiết & địa điểm bật/tắt được** (`showWeatherHud`), thẻ gọn 2 dòng, chỉ đặt vào khoảng trống không có icon.
- **Không che icon trên mọi máy / DPI:** đo nút Start, Search, Task View, Widgets, app đang chạy, khay hệ thống và đồng hồ bằng UI Automation (dự phòng EnumChildWindows), khoét sắc theo DPI, viền mờ 6 px chỉ nằm ngoài icon.
- **Chặn wallpaper:** nền taskbar dùng lớp tối đặc (`backdropOpacity: 1`) đặt sát phía sau `Shell_TrayWnd`. Ảnh nền desktop không xuyên qua hay ám màu lên thanh taskbar, kể cả vùng icon.
- **Clean-room XAML TAP** (`TaskbarTap.dll`): gọi `InitializeXamlDiagnosticsEx`, đi cây XAML, làm trong suốt `Rectangle#BackgroundFill` và ẩn `BackgroundStroke` — không cần cài Windhawk. Thất bại thì tự quay về chế độ an toàn (khoét mềm vùng icon).
- **Phím F8 toàn cục** + menu icon khay (hoạt ảnh, khu vực & cảnh vật, thẻ thời tiết, xem trước thời tiết, chặn ảnh nền, làm mới thời tiết & vị trí).
- **Đa màn hình / DPI:** PerMonitor V2, tùy chọn hiện trên mọi màn hình; widget thời tiết mặc định chỉ trên màn hình chính.
- **Một file chạy:** `release/AudioTaskbar.exe` (single-file .NET 8, win-x64, self-contained).

## Smart App Control (SAC)

Trên máy bật **Smart App Control** (Windows Security → App & browser control), Windows chặn file exe/DLL chưa ký số và chưa có "reputation". Ảnh hưởng:

- `TaskbarTap.dll` không được nạp vào Explorer → v5 phát hiện SAC ngay khi khởi động và dùng chế độ an toàn (không chờ, không treo F8).
- Bản exe mới build (chưa ký) có thể bị chặn chạy hoàn toàn (Event Viewer → *Microsoft-Windows-CodeIntegrity/Operational*, sự kiện 3077/3033).

Cách xử lý: ký số exe bằng chứng chỉ code-signing tin cậy, hoặc tắt SAC (Windows không cho bật lại SAC nếu không cài lại / reset Windows). App không tìm cách lách SAC.

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
- **Hoạt ảnh:** Tàu lửa, Giao thông địa phương, Chim & đèn phố, Hiệu ứng thời tiết
- **Ngày & đêm (mặt trời / mặt trăng):** Chu kỳ 30 phút (1 ngày), Theo giờ thật
- **Khu vực & Cảnh vật:** Tự động theo máy, TP. Hồ Chí Minh, Bà Rịa – Vũng Tàu, Hà Nội, Đà Nẵng – Hội An, Tokyo / Nhật Bản, Paris / Pháp, Mặc định; *Thời tiết theo khu vực đã chọn*
- **Hiện thông tin thời tiết & địa điểm** (thẻ trên nền taskbar)
- **Xem trước thời tiết:** Thực tế, Nắng đẹp, Mây trôi, Mưa rào, Giông bão, Đêm sao
- **Làm mới thời tiết & vị trí**
- **Chặn ảnh nền desktop:** bật/tắt TAP + nền đặc phía sau icon
- Tải lại cấu hình, mở thư mục cấu hình, Thoát

## Cấu trúc dự án

```
AudioTaskbar/
  src/                      # host .NET 8 WinForms / GDI+
    AudioTaskbar.csproj
    Program.cs, TrayApp.cs, Diorama.cs, OverlayWindow.cs, ...
    TaskbarLayout.cs        # đo icon bằng UI Automation (v5)
    Regions.cs, Scenery.cs  # hồ sơ khu vực, danh lam, giao thông (v5)
    BackdropWindow.cs       # nền tối phía sau taskbar ở chế độ an toàn (v5)
    RenderPreview.cs        # --render: xuất ảnh PNG không cần màn hình (v5)
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
