# Tham chiếu cấu hình

File cấu hình runtime:

```text
%LOCALAPPDATA%\AudioTaskbar\config.json
```

Ví dụ trên máy người dùng: `C:\Users\<user>\AppData\Local\AudioTaskbar\config.json`.

- Tạo tự động khi chạy lần đầu (giá trị mặc định của `AppConfig`).
- Có thể copy từ [`config.example.json`](../config.example.json) rồi chỉnh.
- JSON camelCase, cho phép comment (`//`, `/* */`) và dấu phẩy thừa.
- Sửa xong: chuột phải icon khay → **Tải lại cấu hình**, hoặc restart app. Một số khóa (hotkey, fusion) được áp dụng ngay khi reload.
- App **không** đọc `config.json` trong thư mục dự án một khi bản local đã tồn tại. Khóa vị trí / tùy chọn máy nằm ở `%LOCALAPPDATA%`, không commit lên git.

Màu dùng `#RRGGBB` hoặc `#AARRGGBB`. Giá trị ngoài khoảng cho phép sẽ bị `Normalize()` kẹp lại.

## Ví dụ tối thiểu (v4)

```json
{
  "mode": "diorama",
  "hotkey": "F8",
  "blockWallpaper": true,
  "backdropOpacity": 1.0,
  "enableAudio": false,
  "showSpectrum": false,
  "showWaveform": false,
  "showTrain": true,
  "showCyclist": true,
  "showAmbientDetails": true,
  "showWeatherEffects": true,
  "weatherPreview": "live",
  "showWeather": true,
  "weatherCity": "Bà Rịa",
  "weatherUnits": "celsius"
}
```

Khóa thành phố để IP geolocation không nhảy nhầm nơi khác:

```json
"weatherCity": "Bà Rịa"
```

Hoặc khóa tọa độ (ưu tiên hơn city):

```json
"weatherLatitude": 10.51,
"weatherLongitude": 107.18,
"weatherCity": "Bà Rịa"
```

Đổi hotkey khi F8 bị trùng:

```json
"hotkey": "Ctrl+Alt+F8"
```

Bật lại visualizer âm thanh (kiến trúc v1–v3):

```json
"enableAudio": true,
"showSpectrum": true,
"showWaveform": false,
"mode": "spectrum",
"preset": "aurora",
"sensitivity": 1.0
```

---

## Phím tắt, cửa sổ, nền taskbar

| Khóa | Kiểu | Mặc định | Ý nghĩa |
|------|------|----------|---------|
| `hotkey` | string | `"F8"` | Phím toàn cục. Tổ hợp `Ctrl`/`Alt`/`Shift`/`Win` + `F1`–`F24` hoặc một ký tự, nối bằng `+`. Ví dụ `"Ctrl+Alt+F8"`, `"Ctrl+F9"`. Để trống sẽ về `F8`. |
| `showOnAllMonitors` | bool | `true` | Vẽ overlay trên mọi taskbar (màn hình phụ). |
| `hideWhenFullscreen` | bool | `true` | Ẩn hiệu ứng khi có cửa sổ fullscreen trên màn đó. |
| `fps` | int | `30` | Khung hình/giây, kẹp 15–90. |
| `iconSafeFade` | bool | `true` | Khoét/mờ vùng Start, icon, khay khi **không** nằm chắc chắn phía sau taskbar. |
| `iconSafePadding` | int | `28` | Độ rộng vùng an toàn quanh icon (0–200 px, đã scale theo DPI). |
| `blockWallpaper` | bool | `true` | Chặn ảnh nền desktop: gắn TAP + vẽ nền đặc phía sau icon. Trùng menu *Chặn ảnh nền desktop*. |
| `backdropOpacity` | float | `1.0` | Độ đặc của nền visualizer (0.5–1.0). `1` = kín hoàn toàn, wallpaper không xuyên. |
| `fuseIntoTaskbar` | bool | `false` | Opt-in fusion vào nền XAML. Menu khay gán cùng `blockWallpaper` / `makeTaskbarTransparent`. Mặc định tắt khi migrate config cũ (sau sự cố Explorer lúc gỡ TAP). |
| `makeTaskbarTransparent` | bool | `false` | Yêu cầu TAP làm `BackgroundFill` trong suốt. Nên bật cùng `blockWallpaper`. |

`blockWallpaper || (fuseIntoTaskbar && makeTaskbarTransparent)` quyết định có thử gắn TAP hay không. Tắt từ menu khay sẽ `Detach` và khôi phục nền Explorer.

## Diorama và thời tiết (v4)

| Khóa | Kiểu | Mặc định | Ý nghĩa |
|------|------|----------|---------|
| `mode` | string | `"diorama"` | `diorama` (hoặc `scene`): cảnh vật. `spectrum` / `waveform` / `both`: bật lại visualizer âm thanh tương ứng nếu `showSpectrum`/`showWaveform` đang tắt. |
| `showTrain` | bool | `true` | Tàu hỏa chạy trái → phải (đầu máy, toa, đèn, khói). Menu: *Cảnh vật → Xe lửa*. |
| `showCyclist` | bool | `true` | Người đi xe đạp (đèn đêm, dù khi mưa). *Cảnh vật → Xe đạp*. |
| `showAmbientDetails` | bool | `true` | Chim, đom đóm, sao băng, khinh khí cầu, đèn phố. *Cảnh vật → Chim & đèn phố*. |
| `showWeatherEffects` | bool | `true` | Mưa, gợn nước, sấm chớp, mây nhiều lớp. *Cảnh vật → Thời tiết*. |
| `weatherPreview` | string | `"live"` | Scene xem trước: `live` (theo thời tiết thật + giờ hệ thống), `sunny`, `cloudy`, `rain`, `storm`, `night`. Chấp nhận alias tiếng Việt (`nắng đẹp`, `mây trôi`, `mưa rào`, `giông bão`, `đêm sao`, …). |
| `showWeather` | bool | `true` | Thẻ nhiệt độ + trạng thái trên taskbar. |
| `showWeatherAmbient` | bool | `true` | (Tương thích cũ) hiệu ứng ambient thời tiết trên overlay. |
| `weatherPosition` | string | `"left"` | Vị trí thẻ: `left` hoặc `right`. |
| `weatherUnits` | string | `"celsius"` | `celsius` hoặc `fahrenheit` (`f`, `°f` cũng được). |
| `weatherCity` | string | `""` | Khóa thành phố (geocode Open-Meteo, ngôn ngữ `vi`). Để trống = dò theo IP. |
| `weatherLatitude` | number \| null | `null` | Vĩ độ. Phải đi cùng `weatherLongitude`; ưu tiên hơn city. |
| `weatherLongitude` | number \| null | `null` | Kinh độ. |
| `weatherRefreshMinutes` | int | `15` | Chu kỳ gọi Open-Meteo (5–120). |
| `weatherOnPrimaryOnly` | bool | `true` | Chỉ vẽ thẻ thời tiết trên taskbar màn hình chính. |

## Khu vực, tàu lửa, thẻ thời tiết, TAP (v5)

| Khóa | Kiểu | Mặc định | Ý nghĩa |
|------|------|----------|---------|
| `trainCrossingMinutes` | float | `25` | Số phút để đoàn tàu đi hết chiều rộng taskbar, trái → phải (1–240). |
| `regionMode` | string | `"auto"` | `auto`, `hcmc`, `baria-vungtau`, `hanoi`, `danang-hoian`, `tokyo`, `paris`, `default`. `auto` suy ra từ `weatherCity` → tên địa điểm thời tiết → tọa độ. |
| `showWeatherHud` | bool | `true` | Hiện thẻ nhiệt độ / trạng thái / địa điểm trên nền taskbar. Tắt thì cảnh vật vẫn đổi theo thời tiết. |
| `weatherFollowsRegion` | bool | `false` | Khi chọn khu vực bằng tay: lấy thời tiết theo tọa độ khu vực đó thay vì vị trí máy. |
| `tapMode` | string | `"auto"` | `auto` thử gắn TAP (bỏ qua nếu có SAC); `off` luôn dùng chế độ an toàn. |
| `tapTimeoutSeconds` | float | `3.5` | Thời gian tối đa chờ TAP báo Ready (1.5–8). |

Ghi chú:

- `iconSafePadding` không còn là độ rộng cố định: v5 đo icon thật bằng UI Automation; vùng khoét = khung icon + 2 px·DPI, viền mờ 6 px·DPI nằm ngoài icon.
- Vị trí IP có chống nhảy: khi IP báo nơi cách hơn 80 km, cần 2 lần xác nhận liên tiếp mới đổi (log ghi `+stable`). Muốn cố định, đặt `weatherCity` hoặc `weatherLatitude`/`weatherLongitude`.

## Âm thanh / spectrum (tắt mặc định ở v4)

Chỉ có hiệu lực khi `enableAudio` là `true`.

| Khóa | Kiểu | Mặc định trong code | Ý nghĩa |
|------|------|---------------------|---------|
| `enableAudio` | bool | `false` | Bật WASAPI loopback. v4 ghi `false` khi start nếu config cũ còn bật. |
| `showSpectrum` | bool | `false` | Cột phổ. |
| `showWaveform` | bool | `false` | Đường waveform. |
| `barCount` | int | `72` | Số cột (8–256). |
| `sensitivity` | float | `1.0` | Độ nhạy (0.05–8). |
| `autoGain` | bool | `true` | Tự chỉnh gain theo mức âm thanh. |
| `opacity` | float | `0.52` | Độ mờ lớp nền visualizer (0–1), nhân với `intensity`. |
| `barOpacity` | float | `0.78` | Độ mờ cột (0–1), nhân với `intensity`. |
| `colorLow` | string | `"#071526"` | Màu vùng trầm / nền. |
| `colorMid` | string | `"#2EE6A6"` | Màu trung. |
| `colorHigh` | string | `"#7AE7FF"` | Màu cao. |
| `barColor` | string | `"#E8F4FF"` | Màu cột khi không tô theo tần số. |
| `barColorByFrequency` | bool | `true` | Cột đổi màu theo tần số. |
| `fftSize` | int | `2048` | Cỡ FFT, làm tròn lên lũy thừa 2, 512–8192. |
| `barHeightPercent` | float | `62` | Chiều cao cột tối đa (% chiều cao taskbar, 10–100). |
| `barGapPercent` | float | `32` | Khe giữa cột (0–80). |
| `smoothing` | float | `0.72` | Làm mượt biên độ (0–0.95). |
| `colorAttack` | float | `0.28` | Tốc độ màu khi tín hiệu tăng (0.01–1). |
| `colorRelease` | float | `0.07` | Tốc độ màu khi tín hiệu giảm (0.01–1). |
| `minFreq` | float | `30` | Tần số thấp (Hz). |
| `maxFreq` | float | `16000` | Tần số cao (Hz). |
| `peakHold` | bool | `true` | Giữ đỉnh cột. |
| `peakDecay` | float | `0.025` | Tốc độ hạ đỉnh (0.005–0.5). |
| `preset` | string | `"aurora"` | `aurora`, `night-ocean`, `sunset`, `minimal`, `windows-accent`, hoặc `custom`. |
| `intensity` | float | `1.0` | Hệ số cường độ tổng (0.25–2). |
| `idleFade` | bool | `false` (v4) | Mờ dần khi không có tiếng. (v2 từng mặc định `true`.) |
| `idleFadeSeconds` | float | `1.6` | Thời gian mờ khi idle (0.3–8). |
| `mirroredBars` | bool | `true` | Cột có phản chiếu nhẹ. |

## File runtime khác (không phải config)

| Đường dẫn | Nội dung |
|-----------|----------|
| `%LOCALAPPDATA%\AudioTaskbar\app.log` | Log chạy và crash. |
| `%LOCALAPPDATA%\AudioTaskbar\weather-cache.json` | Snapshot thời tiết gần nhất (dùng khi offline). |
| `%LOCALAPPDATA%\AudioTaskbar\tap-status.txt` | Trạng thái TAP (state, số element đã vá, lỗi). |
| `%LOCALAPPDATA%\AudioTaskbar\TaskbarTap.dll` | Bản DLL được host trích ra từ resource. |

Xóa `config.json` rồi chạy lại app để trở về mặc định v5.
