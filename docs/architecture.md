# Kiến trúc Audio Taskbar v5.2.0

Audio Taskbar gồm hai phần:

1. **Host managed** — `AudioTaskbar.exe`, .NET 8, WinForms + GDI+. Vẽ diorama, quản lý khay hệ thống, hotkey, thời tiết, và điều khiển lớp nền taskbar.
2. **Native TAP** — `TaskbarTap.dll` (C++), được host nhúng như embedded resource rồi trích ra `%LOCALAPPDATA%\AudioTaskbar\TaskbarTap.dll`. Windows tự nạp DLL vào `explorer.exe` qua API XAML Diagnostics.

Không dùng `CreateRemoteThread`, không hook Win32 của Explorer, và không cần quyền Administrator.

```
┌─────────────────────────────────────────────────────────────┐
│  AudioTaskbar.exe  (.NET 8, STA, PerMonitorV2)              │
│  TrayApp  →  OverlayWindow (WS_EX_LAYERED, click-through)   │
│           →  Diorama / (tùy chọn) Analyzer + AudioCapture   │
│           →  WeatherService (Open-Meteo)                    │
│           →  TaskbarTapHost  ──InitializeXamlDiagnosticsEx──┐
└─────────────────────────────────────────────────────────────┘ │
                                                                ▼
                                           explorer.exe  (Shell_TrayWnd)
                                           TaskbarTap.dll  (IObjectWithSite
                                             + IVisualTreeServiceCallback2)
                                           Rectangle#BackgroundFill → trong suốt
                                           BackgroundStroke → Collapsed
```

## Host .NET 8

| Thành phần | Vai trò |
|------------|---------|
| `Program.cs` | Entry point, mutex singleton, DPI, log crash. |
| `TrayApp.cs` | Vòng đời app, menu khay, F8, gắn/gỡ TAP, reload config. |
| `OverlayWindow.cs` | Cửa sổ layered phủ đúng bounds từng taskbar; vẽ frame (diorama hoặc spectrum). |
| `Transit.cs` / `DioramaTransit.cs` | v5.4: máy trạng thái tàu hỏa và máy bay (thuần, có test) và phần vẽ sân ga, hành khách, đường băng, nhà ga, máy bay. |
| `Diorama.cs` | Cảnh 48px: trời theo giờ, mây, mưa, sấm, tàu, xe đạp, chim, đom đóm, đèn phố. |
| `TaskbarService.cs` | Tìm `Shell_TrayWnd` / `Shell_SecondaryTrayWnd`, đo vùng Start / icon / khay. |
| `TaskbarTapHost.cs` | Trích DLL, gọi `InitializeXamlDiagnosticsEx`, đọc `tap-status.txt`, detach/restore. |
| `TaskbarTransparency.cs` | Thử API composition cũ (không còn tác dụng trên taskbar XAML 26300) — lớp dự phòng lịch sử. |
| `WeatherService.cs` | Geocode / IP locate + forecast Open-Meteo, cache offline. |
| `Cycles.cs` | v5.4: mùa (`Seasons`, `SeasonCycler`) và thời tiết tự đổi (`WeatherCycle`, `WeatherScheduler`), thuần logic, có test. |
| `Sounds.cs` / `AmbientSynth.cs` / `AmbientMixer.cs` | v5.4: âm thanh nền tổng hợp trong app (mưa, sấm, gió, xe cộ, còi, tàu, chim, côn trùng, động vật, sóng). `SoundScape` tính mức từng âm theo khu vực/thời tiết/mùa/giờ; `AmbientMixer` trộn theo khối 256 mẫu (kết quả không phụ thuộc kích thước buffer), `AmbientEngine` mở thiết bị âm thanh khi cần và đóng khi im lặng. Không có file âm thanh. |
| `TrayMenuModel.cs` / `PortalHost.cs` / `UpdatePolicy.cs` | v5.4: cấu trúc menu khay, giao diện `IPortalHost` giữa `TrayApp` và `PortalForm` (có thể giả lập khi test / `--render-portal`), chính sách kiểm tra/cài cập nhật. |
| `Hotkey.cs` | Parse `"Ctrl+Alt+F8"` và `RegisterHotKey`. |
| `Config.cs` | Đọc/ghi `%LOCALAPPDATA%\AudioTaskbar\config.json` (camelCase). |
| `AudioCapture.cs` / `Analyzer.cs` / `Presets.cs` | WASAPI loopback + FFT. Tắt mặc định ở v4 (`enableAudio: false`). |

`OverlayWindow` dùng `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` và `ShowWithoutActivation`, nên click xuyên qua tới icon taskbar bên dưới.

## Z-order: nằm sát phía sau taskbar

Khi TAP đã làm nền XAML trong suốt, visualizer phải **ở dưới** `Shell_TrayWnd` — nếu không, nền đặc sẽ che icon (lỗi đã gặp ở v3.2).

`PlaceBehindTaskbar()` gọi:

```text
SetWindowPos(viz, taskbarHwnd, ..., SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
```

`hwndInsertAfter = taskbarHwnd` chèn cửa sổ hiệu ứng *ngay sau* taskbar trong z-order, **không** dùng `HWND_TOPMOST` làm insert-after (cờ đó sẽ đẩy visualizer lên trên icon). App kiểm tra định kỳ (`IsBelowTaskbar` / `ReassertZ`). Nếu không giữ được vị trí phía sau, host tự chuyển overlay sang chế độ an toàn: nền vẫn vẽ ở khoảng trống, vùng Start / icon / khay được khoét mềm (`iconSafeFade`).

`blockWallpaper` + `backdropOpacity` (mặc định `1.0`) vẽ một lớp tối **đặc**, nên wallpaper desktop không xuyên qua taskbar.

## TaskbarTap.dll — clean-room XAML TAP

Host lấy địa chỉ `InitializeXamlDiagnosticsEx` từ `Windows.UI.Xaml.dll` rồi gọi với CLSID của TAP. Windows nạp DLL vào process Explorer (cùng cơ chế Visual Studio dùng để debug XAML).

DLL triển khai:

- `DllGetClassObject` / `DllCanUnloadNow` (export trong `TaskbarTap.def`)
- `IObjectWithSite` — nhận site, `QueryInterface` ra `IVisualTreeService` / `IXamlDiagnostics`
- `IVisualTreeServiceCallback2` — đi cây, theo dõi element mới (màn hình mới, Explorer vẽ lại)

Ghép phần tử theo **`x:Name`**, không theo đường dẫn cha–con cứng (cần thiết trên cây Windows 11 26H2 / build 26300 khi `TaskbarFrame` bị bọc thêm lớp):

| Element | Hành động | Khôi phục |
|---------|-----------|-----------|
| `BackgroundFill` | Đặt `Fill` = brush trong suốt | `IVisualTreeService::ClearProperty` |
| `BackgroundStroke` | `Visibility = Collapsed` (fallback: fill trong suốt) | `ClearProperty` |

Host và DLL đồng bộ qua named events (`Local\AudioTaskbar.Tap.Ready`, `.Apply`, `.Applied`, `.Restore`, `.Stop`, `.Stopped`) và file `%LOCALAPPDATA%\AudioTaskbar\tap-status.txt`.

Khi gắn thất bại (timeout, không vá được phần tử, Explorer không còn), host **gỡ TAP** (nếu đã advise) và quay về overlay an toàn. Nếu Explorer chết ngay sau khi gắn, host có thể khởi động lại Explorer rồi tắt fusion.

**F8** chỉ ẩn/hiện overlay; không `Detach` TAP giữa phiên để tránh Explorer restart. Thoát app (`ExitApp`) mới restore + detach.

### Giới hạn quan trọng

Chỉ **một** consumer XAML Diagnostics được gắn vào Explorer tại một thời điểm. Không chạy đồng thời với Windhawk Taskbar Styler, TranslucentTB (ExplorerTAP), hoặc Visual Studio đang debug XAML của Explorer.

## Chế độ an toàn (fallback)

Bật khi:

- `blockWallpaper` / fusion tắt, hoặc
- TAP không gắn được, hoặc
- z-order không giữ được vị trí phía sau taskbar.

Hành vi: cửa sổ hiệu ứng có thể nằm trên taskbar nhưng `iconSafeFade` làm mờ/khoét vùng Start, cụm icon và khay (`iconSafePadding`, mặc định 28px). Icon luôn còn và bấm được. Vùng trống vẫn hiện diorama / thời tiết.

## v5: đo bố cục icon (TaskbarLayout.cs)

Nguyên nhân icon bị che ở v4: trên taskbar XAML của Windows 11, các HWND con cũ (`ReBarWindow32`, `MSTaskSwWClass`…) có khung không khớp với icon thật (ví dụ ReBar 516–1073 px trong khi icon kéo tới 1403 px), cộng thêm phần đoán 40% chiều rộng.

v5 chạy `TaskbarLayoutProbe` trên một luồng MTA nền, mỗi ~400 ms:

1. UI Automation (`System.Windows.Automation`, CacheRequest + FindAll Descendants) từ `Shell_TrayWnd` / `Shell_SecondaryTrayWnd`: lấy khung `StartButton`, `SearchButton`, `TaskViewButton`, `WidgetsButton`, `Taskbar.TaskListButtonAutomationPeer`, `SystemTray.*`, đồng hồ… Bỏ các container rộng hơn 60% thanh.
2. Bổ sung HWND cũ (`TrayNotifyWnd`, `Start`, `MSTaskSwWClass`/`MSTaskListWClass`) qua EnumChildWindows.
3. Gộp các khung chạm nhau → danh sách cutout; nếu UIA không trả gì → dự phòng bảo thủ (chỉ vẽ dải trái hẹp).

`OverlayWindow` vẽ cảnh 48 px logic rồi scale `H/48` (PerMonitorV2), nhân alpha theo từng cột: alpha = 0 trong (icon + pad), smoothstep 6 px·DPI ra ngoài. Thẻ thời tiết, danh lam được đặt **chỉ** vào các khoảng trống (`FreeSpans`). Bố cục thay đổi (mở app, đổi DPI, ẩn/hiện khay) được cập nhật trong vòng ~1 s.

## v5: TAP và Smart App Control

`TaskbarTapHost` kiểm tra trước `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` (1 = SAC đang chặn) và log CodeIntegrity. Nếu bị chặn → chế độ an toàn ngay. Ngược lại TAP gắn trên Task nền, chờ Ready tối đa `tapTimeoutSeconds` (3,5 s); thất bại thì nhớ PID Explorer để F8 không thử lại. `InitializeXamlDiagnosticsEx` trả `hr=0` **không** có nghĩa DLL đã nạp: v5 xóa `tap-status.txt` trước và chỉ coi là gắn khi DLL ghi Ready.

Ở chế độ an toàn, `BackdropWindow` (lớp tối đặc, click-through) được đặt bằng `SetWindowPos(hwnd, Shell_TrayWnd)` ngay dưới taskbar, kiểm tra lại z-order; acrylic của taskbar hiện lớp này thay vì wallpaper.

## v5: khu vực và cảnh vật

`Regions.cs` chứa 7 hồ sơ (`RegionCatalog`) với tọa độ, danh lam, skyline nền, bảng giao thông đường bộ / đường thủy. `Resolve` ưu tiên: chọn tay → `weatherCity` → tên địa điểm thời tiết (bỏ dấu) → hộp tọa độ. `Scenery.cs` dựng skyline + danh lam vào khoảng trống theo độ ưu tiên và vẽ: nền xa, mặt nước & thuyền, danh lam (đèn, cửa sổ, hiệu ứng như hải đăng quét, cầu Rồng phun lửa mỗi 45 s, Eiffel lấp lánh), làn xe gần (trái → phải) / xa (phải → trái), cánh hoa.

Đoàn tàu và máy bay (v5.4) là máy trạng thái thuần trong `Transit.cs`: `TrainCycle` (vào ga → dừng, khách xuống → khách lên → rời ga → nghỉ) và `PlaneCycle` (bay vào/hạ độ cao → chạm đường băng → chạy vào bãi đỗ → trả khách → đón khách → ra đường băng → cất cánh → bay đi, hoặc chỉ bay ngang trời khi không đủ chỗ cho sân bay). Vị trí là hàm của vị trí chu kỳ `0..1 = frac(phase0 + t / chu kỳ)`, `phase0` đặt để lúc khởi động tàu đang ở sân ga và máy bay đang ở bãi đỗ; bánh xe quay theo quãng đường, khói nhẹ 0,45 s/lần (thưa khi tàu đứng). `AirportPlan` chọn khoảng trống rộng nhất (≥ 84 px) cho sân bay, sân ga của tàu ở khoảng trống rộng nhì. `DioramaTransit.cs` chỉ vẽ trạng thái đó. Mọi thứ vẽ nằm dưới lớp cắt icon nên không che icon / vùng đệm.

## v5.1.1: TAP thường trú, tái sử dụng (giao thức v3)

| Sự kiện (Local\\) | Tạo bởi | Ý nghĩa |
|---|---|---|
| `AudioTaskbar.Tap.Alive3` | DLL (worker) | Dịch vụ TAP v3 còn sống (site XAML còn giữ) → chỉ tái sử dụng, không inject lại |
| `AudioTaskbar.Tap.Loaded3` | DLL (DllMain) | Một bản v3 đã nạp trong Explorer này (sống tới khi Explorer thoát); nếu `Alive3` mất mà `Loaded3` còn thì inject mới là an toàn |
| `AudioTaskbar.Tap.Apply` / `Applied` | DLL / host | Bật lại nền trong suốt (ReapplyAll) / xác nhận |
| `AudioTaskbar.Tap.Restore` / `Restored` | DLL / host | Trả nền taskbar gốc (RestoreAll, tắt patch phần tử mới) / xác nhận |
| `AudioTaskbar.Tap.Stop` | DLL | Bị bỏ qua ở v3 (coi như Restore); host v5.1.1 không gửi |

Khi đang ở trạng thái Restore, watcher vẫn theo dõi cây XAML và **ghi nhớ** `BackgroundFill`/`BackgroundStroke` mới (không patch), để lần Apply sau áp dụng đúng phần tử hiện tại; phần tử bị gỡ khỏi cây được đánh dấu `removed` và không bao giờ bị chạm lại. Host: `Attach()` → nếu `Alive3` tồn tại thì `ReuseExisting()`; nếu Explorer có module `TaskbarTap*` mà không có `Loaded3` (bản v1/v2 cũ) thì từ chối (`stale-legacy-tap`); còn lại mới gọi `InitializeXamlDiagnosticsEx`.

**Luồng (quan trọng):** `IVisualTreeService`/`SetProperty` không an toàn đa luồng — gọi từ luồng worker làm Explorer crash (`Windows.UI.Xaml.dll` / `TaskbarTap` access violation, đã gặp với giao thức v2 khi phát triển). Ở v3, `SetSite` (chạy trên luồng UI XAML) tạo một cửa sổ message-only `AudioTaskbarTapMsg3`; worker chỉ chờ sự kiện rồi `PostMessage` `WM_APP+0x41` (Restore) / `WM_APP+0x42` (Apply), và `RestoreAll`/`ReapplyAll` luôn chạy trên luồng UI. Worker giữ một tham chiếu `TapSite`, không bao giờ gọi vào watcher; `Teardown` (SetSite(null), luồng UI) là nơi duy nhất `Shutdown` watcher.

## v5.1: đồng hồ cảnh và thiên thể

`SceneClock.VirtualHour(cfg, now)` trả giờ cảnh 0–24 (chu kỳ `dayCycleMinutes` hoặc giờ thật). `SceneClock.Compose` nội suy bảng màu theo các keyframe và tính các hệ số liên tục `DayK`, `LightsK`, `StarsK`, `WarmK`, `SunT`, `MoonT` trong `SceneMood`. `DioramaRenderer` vẽ mặt trời / mặt trăng theo cung `ArcPoint` (sin của tiến độ), chìm sau skyline ở hai đầu; ở chế độ an toàn `ArcX` ánh xạ cung qua các khoảng trống. Mọi lớp đèn (cửa sổ, đèn phố, đèn tàu, phản chiếu trên nước, quầng sáng thành phố), sao, đom đóm, chim, khinh khí cầu đều nhân alpha theo các hệ số này nên chuyển cảnh mượt.

## Visualizer âm thanh (tùy chọn, tắt ở v4)

Khi `enableAudio: true` (và `showSpectrum` / `showWaveform`):

- `AudioCapture` bắt **toàn bộ** output hệ thống bằng WASAPI loopback (nhạc, trình duyệt, RainyMood, …) — không cần biết nguồn phát.
- `Analyzer` FFT (kích thước mặc định 2048), smoothing, peak-hold, preset màu (Aurora, Night Ocean, Sunset, Minimal, Windows Accent).

v4 đặt `mode: "diorama"` và tắt audio để tiết CPU/RAM; bật lại bất cứ lúc nào trong config rồi *Tải lại cấu hình*.

## Thời tiết

1. Vị trí, theo thứ tự ưu tiên: `weatherLatitude` + `weatherLongitude` → `weatherCity` (geocoding Open-Meteo, `language=vi`) → IP (`ipapi.co`, `geojs`, `ip-api.com`).
2. Forecast: `https://api.open-meteo.com/v1/forecast` (`temperature_2m`, `weather_code`, `is_day`, `precipitation`, `cloud_cover`, `timezone=auto`).
3. Cache: `%LOCALAPPDATA%\AudioTaskbar\weather-cache.json`. Làm mới theo `weatherRefreshMinutes` (5–120, mặc định 15).

`weatherPreview` khác `live` thì diorama dùng scene cố định (sunny / cloudy / rain / storm / night) — tiện xem thử, không ghi đè dữ liệu forecast đã cache.

## So sánh với Windhawk Windows 11 Taskbar Styler

| | Windhawk Taskbar Styler | Audio Taskbar v4 |
|--|--|--|
| Cài đặt thêm | Cần Windhawk (thường cần admin) + mod | Không; DLL đi kèm / embed trong exe |
| Cơ chế | XAML Diagnostics TAP trong `explorer.exe` | Cùng họ API, tự viết clean-room |
| Mục tiêu | Theme/styler đầy đủ (màu, blur, nhiều selector) | Chỉ làm nền trong suốt + khôi phục, rồi vẽ diorama phía sau |
| Đa màn hình / Explorer restart | Có | Có (gắn lại khi `TaskbarCreated`) |
| Bật/tắt / restore | Tắt mod trong Windhawk | Menu khay **Chặn ảnh nền desktop**, restore khi thoát |
| Xung đột | Chỉ 1 consumer XAML Diagnostics tại một thời điểm | Cùng giới hạn đó |
| Mã nguồn | Mods GPL-3.0 | Không sao chép mã Windhawk / TranslucentTB (xem `NOTICE.md`) |

Audio Taskbar **không** thay thế Windhawk: không có bộ chọn style tùy ý, không chỉnh màu từng phần taskbar ngoài việc làm nền trong suốt. Bù lại, không cần cài thêm công cụ và chỉ chạm đúng hai element nền.
