# Audio Taskbar v5.3.2

Ứng dụng Windows nhỏ vẽ một **diorama thời tiết sống** (48px) ngay trên nền thanh taskbar: bầu trời đổi theo giờ trong ngày, mây/mưa/sấm, tàu hỏa chạy trái → phải, người đi xe đạp, chim, đèn phố, đom đóm… Hiệu ứng nằm **phía sau** icon nên nút Start, Search, app đang chạy và khay hệ thống vẫn sắc, bấm bình thường.

**Mới ở v5.3.2:** nút **Bật / Tắt hiệu ứng (F8)** chuyển xuống hàng nút dưới cùng của bảng điều khiển (bên phải, cạnh *Đóng (Ẩn xuống khay)* và *Kết thúc (Thoát)*), cửa sổ gọn hơn. Nút vẫn màu xanh nổi bật: nền xanh khi đang bật, viền xanh khi đang tắt.

**Mới ở v5.3.1 — Tự chỉnh độ dài chu kỳ ngày–đêm:** không còn giới hạn 30 phút. Trong bảng điều khiển chọn *Ngày & đêm → Theo chu kỳ tùy chỉnh* rồi nhập **Độ dài chu kỳ** bất kỳ từ **0,5 phút đến 10080 phút (7 ngày)** — ví dụ 15, 30, 90 hoặc 1440 phút/ngày (`dayCycleMinutes`). Ô nhập chỉ bật khi ở chế độ chu kỳ; đổi giá trị áp dụng ngay, bầu trời **không nhảy** (giữ nguyên giờ cảnh hiện tại, chỉ đổi tốc độ) và được lưu vào `config.json`. Menu khay hiện *Theo chu kỳ (N phút/ngày)*.

**Mới ở v5.3.0 — Bảng điều khiển kiểu UniKey + tự động cập nhật:**

- **Bảng điều khiển (portal):** nhấp đúp icon khay (hoặc mở `AudioTaskbar.exe` lần nữa) để mở cửa sổ gọn giống UniKey. Có nút **Bật / Tắt** ở hàng nút dưới cùng (đồng bộ với F8 và menu khay), thẻ *Cảnh vật & Thời tiết* (khu vực, xem trước thời tiết, ngày & đêm, thẻ thời tiết, tàu hỏa + số phút/lượt, phương tiện, chi tiết nền, hiệu ứng thời tiết), thẻ *Hệ thống* (phím tắt F8 / F9 / Ctrl+Alt+F8 / Ctrl+Shift+F8, Khởi động cùng Windows, Mở ẩn xuống khay, Tự động kiểm tra cập nhật) và thẻ *Cập nhật* (chỉ hiện *Phiên bản hiện tại*).
- **Một file duy nhất:** phát hành dạng **một** `AudioTaskbar.exe` tự chứa (đã gồm .NET 8 + `TaskbarTap.dll`) — tải về, double-click là chạy, không cần cài.
- **Chặn ảnh nền desktop luôn bật** (không còn tùy chọn): nền đặc phía sau icon, wallpaper không xuyên qua taskbar.
- **Ngày & đêm có 4 chế độ:** *Theo chu kỳ tùy chỉnh* (mặc định 30 phút/ngày), *Theo giờ thật*, *Cố định ban ngày* (~10:30–12:00, mặt trời cao), *Cố định ban đêm* (~23:30–00:00, trăng, sao, đèn phố). Chọn trong portal, menu khay hoặc `timeMode` trong config. Mọi thay đổi áp dụng ngay và được lưu.
- **Đóng (✕) chỉ ẩn xuống khay**, ứng dụng vẫn chạy; nút **Kết thúc (Thoát)** mới thoát hẳn và trả lại taskbar gốc. Chỉ chạy **một** bản: mở lần hai sẽ đưa bảng điều khiển lên. `--minimized` (dùng khi khởi động cùng Windows) chạy im lặng dưới khay.
- **Tự động cập nhật:** 10 giây sau khi mở và mỗi 6 giờ, app đọc GitHub Release mới nhất (hoặc `updateFeedUrl`), so SemVer, tải `AudioTaskbar.exe` mới vào `%LOCALAPPDATA%\AudioTaskbar\updates`, **bắt buộc khớp SHA-256** (`SHA256SUMS.txt` / `latest.json`), rồi `apply-update.cmd` chờ app thoát sạch (trả taskbar gốc, không đụng Explorer), sao lưu file cũ thành `AudioTaskbar.exe.old`, thay **đúng file đang chạy** (lỗi thì khôi phục), mở lại dưới khay kèm thông báo *“Đã cập nhật lên Audio Taskbar vX.Y.Z”*. Thông báo cho người dùng không hiển thị địa chỉ repo; chi tiết kỹ thuật ghi trong `app.log`. Xem [Phát hành & tự cập nhật](docs/development.md#6-phát-hành--tự-động-cập-nhật).
- **Bộ test hồi quy** `tests/AudioTaskbar.Tests` (xUnit, `dotnet test`) chặn các lỗi khi dùng: config hỏng, mạng lỗi, nhảy vị trí, toán khoét icon / DPI, phím tắt, cập nhật giả mạo, TAP chèn hai lần.

**Mới ở v5.2.0 — Tuyết rơi ❄:** thời tiết tuyết thật (mã WMO 71/73/75/77/85/86 → *Tuyết nhẹ* / *Tuyết rơi* / *Tuyết dày*, thẻ thời tiết có biểu tượng bông tuyết 6 cánh) hoặc chọn *Xem trước thời tiết → Tuyết rơi* trong menu khay. Tuyết rơi 3 lớp chiều sâu (hạt xa li ti trôi chậm, hạt giữa đung đưa, hạt gần sắc nét lắc theo gió và thỉnh thoảng lấp lánh, ửng vàng ấm khi bay qua đèn phố / đèn pha tàu ban đêm); bầu trời mùa đông xanh xám nhạt ban ngày, chàm sâu ban đêm, hồng phấn lúc bình minh/hoàng hôn; lớp tuyết mỏng trên mái nhà, skyline, danh lam, mặt đất và nóc tàu, sương mờ sát chân trời. Màu trời giữ ở mức trung tính để icon sáng vẫn dễ đọc. Chạy với cả chu kỳ ngày–đêm 30 phút và 7 khu vực.

**Mới ở v5.1.1:** `TaskbarTap.dll` thành dịch vụ thường trú tái sử dụng trong Explorer — tắt/bật app bao nhiêu lần cũng hòa nền liền mạch phía sau toàn bộ taskbar (không còn dải xám quanh icon), không inject lại, không làm Explorer khởi động lại. Xem [troubleshooting](docs/troubleshooting.md).

**Mới ở v5.1:** tàu chạy nhanh hơn — 1 phút/lượt (`trainCrossingMinutes: 1`); **chu kỳ ngày–đêm tăng tốc** mặc định 30 phút thật = 1 ngày 24 giờ (`timeMode: "cycle"`, `dayCycleMinutes: 30`), bầu trời, ánh sáng, đèn cửa sổ / đèn phố / đèn tàu, sao và đom đóm chuyển mượt liên tục (đêm → bình minh → sáng → trưa → chiều → hoàng hôn → chạng vạng → đêm); **mặt trời và mặt trăng đi theo cung** từ trái (đông) lên đỉnh rồi xuống phải (tây) — mặt trời 05:30–18:30 (cam vàng lúc mọc/lặn, trắng ấm buổi trưa), mặt trăng 18:30–05:30 (đỉnh lúc 00:00, có quầng sáng). Chuyển giữa *Chu kỳ 30 phút* và *Theo giờ thật* trong menu khay.

**Mới ở v5.0:** icon taskbar không bao giờ bị che (đo vị trí icon thật bằng UI Automation mỗi ~0,4 s, khoét chính xác theo DPI), một đoàn tàu cố định chạy trái → phải trong 25 phút, cảnh vật + giao thông theo khu vực (TP.HCM, Bà Rịa – Vũng Tàu, Hà Nội, Đà Nẵng – Hội An, Tokyo, Paris, mặc định), bật/tắt thẻ thời tiết & địa điểm, TAP gắn nhanh (≤ 3,5 s) và tự nhận biết Smart App Control.

Phiên bản 4.0 tạm tắt spectrum / waveform và phần bắt âm thanh để máy nhẹ hơn. Kiến trúc visualizer (WASAPI loopback + FFT) vẫn còn trong mã nguồn và có thể bật lại bằng `config.json`.

Bật/tắt toàn bộ hiệu ứng mọi lúc bằng phím **F8** (hotkey toàn cục).

## Tính năng nổi bật

- **Diorama 48px** chạy dọc taskbar, theo nhịp ngày đêm (bình minh → trưa → hoàng hôn → đêm).
- **Thời tiết thật** từ [Open-Meteo](https://open-meteo.com/) (không cần API key): nhiệt độ, trạng thái tiếng Việt, hiệu ứng mưa/nắng/giông/tuyết rơi. Vị trí tự dò theo IP, hoặc khóa thành phố / tọa độ trong config.
- **Một đoàn tàu cố định** (đầu máy + 3 toa) chạy từ trái sang phải, đúng `trainCrossingMinutes` (mặc định 1 phút từ v5.1; v5.0 là 25 phút) cho một lượt; vị trí là hàm của thời gian nên mượt và luôn thấy ngay khi bật.
- **Cảnh vật theo khu vực:** mỗi khu vực có danh lam + skyline + giao thông đặc trưng, ví dụ TP.HCM (Landmark 81, Bitexco, Nhà thờ Đức Bà, cầu Ba Son; xe máy, xe buýt, xích lô), Vũng Tàu (Tượng Chúa, hải đăng quét đèn, hàng dừa Bãi Sau; tàu cá), Hà Nội (Tháp Rùa, cầu Long Biên, Chùa Một Cột, cầu Thê Húc), Đà Nẵng – Hội An (cầu Rồng phun lửa, Cầu Vàng, đèn lồng, thuyền hoa đăng), Tokyo (Phú Sĩ, Tokyo Tower, torii, hoa anh đào; kei truck, mamachari), Paris (Eiffel lấp lánh đầu giờ, Khải Hoàn Môn; 2CV, taxi cổ).
- **Khu vực tự động hoặc chọn tay:** `regionMode: "auto"` lấy theo vị trí thời tiết (IP / thành phố / tọa độ, có chống nhảy vị trí), hoặc chọn trong menu *Khu vực & Cảnh vật*.
- **Thẻ thời tiết & địa điểm bật/tắt được** (`showWeatherHud`), thẻ gọn 2 dòng, chỉ đặt vào khoảng trống không có icon.
- **Không che icon trên mọi máy / DPI:** đo nút Start, Search, Task View, Widgets, app đang chạy, khay hệ thống và đồng hồ bằng UI Automation (dự phòng EnumChildWindows), khoét sắc theo DPI, viền mờ 6 px chỉ nằm ngoài icon.
- **Chặn wallpaper (luôn bật, không tùy chỉnh):** nền taskbar dùng lớp tối đặc (`backdropOpacity` cố định 1) đặt sát phía sau `Shell_TrayWnd`. Ảnh nền desktop không xuyên qua hay ám màu lên thanh taskbar, kể cả vùng icon.
- **Clean-room XAML TAP** (`TaskbarTap.dll`): gọi `InitializeXamlDiagnosticsEx`, đi cây XAML, làm trong suốt `Rectangle#BackgroundFill` và ẩn `BackgroundStroke` — không cần cài Windhawk. Thất bại thì tự quay về chế độ an toàn (khoét mềm vùng icon).
- **Phím F8 toàn cục** + menu icon khay (hoạt ảnh, khu vực & cảnh vật, thẻ thời tiết, xem trước thời tiết, chặn ảnh nền, làm mới thời tiết & vị trí).
- **Đa màn hình / DPI:** PerMonitor V2, tùy chọn hiện trên mọi màn hình; widget thời tiết mặc định chỉ trên màn hình chính.
- **Một file chạy:** `release/AudioTaskbar.exe` (single-file .NET 8, win-x64, self-contained).

## Smart App Control (SAC) và SmartScreen

File Setup và `AudioTaskbar.exe` hiện **chưa ký số**, nên Windows có thể cảnh báo hoặc hạn chế:

- **SmartScreen** khi mở `AudioTaskbar-Setup-5.2.0.exe`: bấm **More info** → **Run anyway**. Đây là cảnh báo bình thường với phần mềm mới, chưa có "reputation", chứ không phải Setup bị hỏng.
- Trên máy bật **Smart App Control** (Windows Security → App & browser control), Windows chặn file exe/DLL chưa ký số và chưa có "reputation". Ảnh hưởng:
  - `TaskbarTap.dll` không được nạp vào Explorer → v5 phát hiện SAC ngay khi khởi động và dùng chế độ an toàn (khoét chính xác vùng icon, không chờ, không treo F8).
  - Bản exe mới build (chưa ký) có thể bị chặn chạy hoàn toàn (Event Viewer → *Microsoft-Windows-CodeIntegrity/Operational*, sự kiện 3077/3033).

Cách xử lý: ký số exe bằng chứng chỉ code-signing tin cậy, hoặc tắt SAC (Windows không cho bật lại SAC nếu không cài lại / reset Windows). App không tìm cách lách SAC.

## Yêu cầu

- Windows 11 64-bit (đã kiểm tra trên bản 24H2/26H2, build 26300). Windows 10 có thể chạy phần diorama nhưng TAP XAML của taskbar 11 sẽ không gắn được — app tự rơi về overlay an toàn.
- Không cần quyền Administrator (`asInvoker`). File Setup cũng cài **theo user**, không hiện UAC.
- Không cần cài .NET SDK / Runtime (exe self-contained .NET 8).
- Kết nối mạng lần đầu để lấy thời tiết (có cache khi offline).

## Cài đặt nhanh bằng file Setup

Gửi cho người khác **một file** `AudioTaskbar-Setup-5.2.0.exe` (khoảng 25–30 MB, nén LZMA). Họ chỉ cần double-click, không cần .NET SDK, không cần quyền admin, không cần clone repo.

1. Mở `AudioTaskbar-Setup-5.2.0.exe`.
2. **Windows SmartScreen** (file chưa ký số) có thể hiện *Windows protected your PC*. Bấm **More info** → **Run anyway**.
3. Chọn thư mục cài (mặc định `%LOCALAPPDATA%\Programs\AudioTaskbar`) và các tùy chọn:
   - Tạo shortcut trên Desktop (mặc định bật)
   - Tạo shortcut trên Start Menu (mặc định bật)
   - Tự khởi động cùng Windows (mặc định **tắt**, đăng ký `HKCU\...\Run` nếu bật)
   - Chạy Audio Taskbar ngay sau khi cài xong (mặc định bật)
4. Tìm icon **Audio Taskbar** ở khay hệ thống (gần đồng hồ, có thể nằm trong mũi tên overflow). Nhấn **F8** để bật/tắt hiệu ứng.
5. **Gỡ cài đặt:** *Settings → Apps → Installed apps → Audio Taskbar* (hoặc *Add or remove programs*). Trình gỡ tắt app nhẹ nhàng qua sự kiện thoát (khôi phục taskbar, **không** kill Explorer), rồi xóa file cài, shortcut và mục tự khởi động. Có hỏi trước khi xóa cấu hình/log trong `%LOCALAPPDATA%\AudioTaskbar` (mặc định giữ lại).

Người dùng mới nhận cấu hình mặc định sạch — `regionMode: "auto"`, `showWeatherHud: true`, `trainCrossingMinutes: 1`, `timeMode: "cycle"`, `dayCycleMinutes: 30`, `tapMode: "auto"` — không dùng config cá nhân của máy build.

**Smart App Control:** trên máy bật SAC, Windows có thể chặn file exe/DLL chưa ký. Nếu app vẫn chạy được, nó **tự chuyển sang chế độ khoét icon an toàn** (không hòa nền XAML phía sau icon). Xem mục [Smart App Control](#smart-app-control-sac) bên dưới.

Tự tạo file Setup từ nguồn: `scripts\build-installer.ps1` (cần [NSIS](https://nsis.sourceforge.io/) 3.x và `release\AudioTaskbar.exe`). Script nằm ở `installer/AudioTaskbar.nsi`.

## Bắt đầu nhanh (từ mã nguồn)

1. Mở thư mục dự án rồi chạy `start.bat` (hoặc `scripts\start.bat`).
2. Tìm icon **Audio Taskbar** ở khay hệ thống (gần đồng hồ, có thể nằm trong mũi tên overflow).
3. Nhấn **F8** để ẩn/hiện hiệu ứng. Chuột phải icon khay để chỉnh cảnh vật hoặc xem trước kiểu thời tiết.
4. Tắt bằng *Thoát* trên menu khay, hoặc `stop.bat`.

Nếu chưa có `release\AudioTaskbar.exe`, build trước: `scripts\build.ps1` (cần .NET 8 SDK và `src\TaskbarTap\TaskbarTap.dll` — xem [docs/development.md](docs/development.md)).

Cấu hình, log và cache nằm ở `%LOCALAPPDATA%\AudioTaskbar\` (`config.json`, `app.log`, `weather-cache.json`, `tap-status.txt`). Không lưu config trong thư mục dự án. Mẫu: [`config.example.json`](config.example.json).

## Cài đặt và lối tắt

| Việc | Cách |
|------|------|
| Cài cho người khác | `AudioTaskbar-Setup-5.2.0.exe` (xem mục trên) |
| Chạy từ dự án (máy dev) | `start.bat` / `stop.bat` |
| Tự khởi động cùng Windows | Trong Setup, hoặc `scripts\enable-autostart.ps1` (tắt: `scripts\disable-autostart.ps1`). Mặc định **không** bật. |
| Shortcut Desktop / Start Menu | Setup tạo sẵn; hoặc lối tắt trỏ tới `release\AudioTaskbar.exe` (working directory = thư mục `release`). |
| Gỡ | *Settings → Apps → Audio Taskbar*, hoặc xem [docs/troubleshooting.md](docs/troubleshooting.md#gỡ-cài-đặt). |

Chỉ một instance được chạy (mutex `Local\Hieunv1.AudioTaskbar.Singleton`).

## Bảng điều khiển (v5.3)

| Thao tác | Kết quả |
|---|---|
| Nhấp đúp icon khay / menu *Mở bảng điều khiển* / chạy lại `AudioTaskbar.exe` | Mở bảng điều khiển |
| Nút lớn **Đang bật / Đang tắt**, F8, menu khay | Bật/tắt hiệu ứng (đồng bộ cả 3 nơi) |
| ✕ hoặc **Đóng (Ẩn xuống khay)** | Ẩn cửa sổ, app vẫn chạy |
| **Kết thúc (Thoát)** | Thoát hẳn, khôi phục taskbar gốc |
| *Khởi động cùng Windows* | Ghi `HKCU\…\Run\AudioTaskbar = "…\AudioTaskbar.exe" --minimized` |

Dòng trạng thái: *Đang bật · Hòa nền sau icon* (TAP đang hoạt động), *Đang bật · Chế độ an toàn* (không TAP, ví dụ Smart App Control bật) hoặc *Đang tắt*.

## Phím tắt

- **F8** (mặc định, toàn cục): bật/tắt hiệu ứng. Tắt sẽ ẩn diorama và trả lại nền taskbar gốc (TAP v3 thường trú chỉ khôi phục nền, không gỡ DLL nên Explorer không khởi động lại). Thoát app thì nền taskbar được khôi phục.
- Đổi phím trong `%LOCALAPPDATA%\AudioTaskbar\config.json`, ví dụ:

```json
"hotkey": "Ctrl+Alt+F8"
```

Hỗ trợ tổ hợp `Ctrl`, `Alt`, `Shift`, `Win` với phím F1–F24 hoặc một ký tự. Nếu phím bị app khác chiếm, icon khay báo và sự kiện được ghi vào `app.log`.

## Menu khay hệ thống

- **Mở bảng điều khiển** (in đậm, = nhấp đúp icon)
- **Bật/tắt hiệu ứng (F8)**
- **Hoạt ảnh:** Tàu lửa, Giao thông địa phương, Chim & đèn phố, Hiệu ứng thời tiết
- **Ngày & đêm (mặt trời / mặt trăng):** Theo chu kỳ (N phút/ngày), Theo giờ thật, Cố định ban ngày, Cố định ban đêm
- **Khu vực & Cảnh vật:** Tự động theo máy, TP. Hồ Chí Minh, Bà Rịa – Vũng Tàu, Hà Nội, Đà Nẵng – Hội An, Tokyo / Nhật Bản, Paris / Pháp, Mặc định; *Thời tiết theo khu vực đã chọn*
- **Hiện thông tin thời tiết & địa điểm** (thẻ trên nền taskbar)
- **Xem trước thời tiết:** Thực tế, Nắng đẹp, Mây trôi, Mưa rào, Giông bão, Tuyết rơi, Đêm sao
- **Làm mới thời tiết & vị trí**
- **Kiểm tra cập nhật**
- Tải lại cấu hình, **Kết thúc (Thoát)**

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
    build-installer.ps1     # đóng gói AudioTaskbar-Setup-5.2.0.exe (NSIS)
    start.ps1, stop.ps1, enable-autostart.ps1, disable-autostart.ps1
  installer/
    AudioTaskbar.nsi        # script NSIS (cài theo user, không cần admin)
    stop-installed.ps1      # tắt nhẹ bản đang chạy trong thư mục cài
    assets/                 # icon đa độ phân giải + ảnh wizard
  tests/test_analyzer.py    # kiểm tra FFT/analyzer (Python)
  docs/                     # tài liệu chi tiết
  config.example.json
  NOTICE.md
  start.bat, stop.bat
  release/                  # output khi build (exe không đưa lên git)
  installer-output/         # file Setup đã đóng gói (không đưa lên git)
```

## Tài liệu

- [Kiến trúc hệ thống](docs/architecture.md) — host .NET, TAP native, z-order, so sánh với Windhawk
- [Tham chiếu cấu hình](docs/configuration.md) — toàn bộ khóa trong `config.json`
- [Phát triển & build](docs/development.md) — biên dịch DLL, publish exe, chạy test
- [Xử lý sự cố](docs/troubleshooting.md) — Explorer/TAP, phím F8, sai vị trí thời tiết, đa màn hình, gỡ cài

## License / notices

Mã managed và `TaskbarTap.dll` là mã viết mới cho dự án này. Cách tiếp cận XAML Diagnostics (`InitializeXamlDiagnosticsEx` + TAP) là cơ chế có tài liệu của Windows, cũng được Visual Studio, ExplorerTAP / Windhawk Taskbar Styler / TranslucentTB dùng.

`windhawk-mods` và TranslucentTB phát hành theo GPL-3.0. Dự án này **không sao chép** mã nguồn của họ — TAP được viết clean-room từ header Microsoft (`xamlom.h`) và hành vi API đã công bố. Chi tiết: [NOTICE.md](NOTICE.md).
