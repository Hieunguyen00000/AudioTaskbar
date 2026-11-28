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
  "showAirplane": true,
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
| `blockWallpaper` | bool | `true` | **v5.3: luôn `true`, không tùy chỉnh** — giá trị khác trong file bị bỏ qua và ghi lại thành `true`. Gắn TAP + vẽ nền đặc phía sau icon. |
| `backdropOpacity` | float | `1.0` | **v5.3: cố định `1.0`** (kín hoàn toàn, wallpaper không xuyên); giá trị khác bị bỏ qua. |
| `fuseIntoTaskbar` | bool | `false` | Opt-in fusion vào nền XAML. Menu khay gán cùng `blockWallpaper` / `makeTaskbarTransparent`. Mặc định tắt khi migrate config cũ (sau sự cố Explorer lúc gỡ TAP). |
| `makeTaskbarTransparent` | bool | `false` | Yêu cầu TAP làm `BackgroundFill` trong suốt. Nên bật cùng `blockWallpaper`. |

`blockWallpaper || (fuseIntoTaskbar && makeTaskbarTransparent)` quyết định có thử gắn TAP hay không. Tắt từ menu khay sẽ `Detach` và khôi phục nền Explorer.

## Diorama và thời tiết (v4)

| Khóa | Kiểu | Mặc định | Ý nghĩa |
|------|------|----------|---------|
| `mode` | string | `"diorama"` | `diorama` (hoặc `scene`): cảnh vật. `spectrum` / `waveform` / `both`: bật lại visualizer âm thanh tương ứng nếu `showSpectrum`/`showWaveform` đang tắt. |
| `showTrain` | bool | `true` | Tàu hỏa vào ga từ trái, chậm dần, dừng ở sân ga nhỏ (khách xuống rồi lên), rời ga và tăng tốc về bên phải (đầu máy, toa, đèn, khói). v5.4.2: chạy lâu hơn, dừng ga ngắn hơn — trong một vòng, chạy 84 % (trước 76 %), dừng ga 12 % (trước 20 %); vòng 2 phút: chạy 100,8 s (trước 91,2 s), dừng 14,4 s (trước 24 s). Menu: *Cảnh vật → Tàu hỏa*. |
| `showAirplane` | bool | `true` | v5.4.2: không còn đường băng. Máy bay chỉ có ba trạng thái: xuất hiện đã ở trên không, thấp, ở mép trái và leo dần; bay ngang giữ độ cao qua giữa thanh; hạ dần và biến mất ở mép phải. Không có gì trên mặt đất, xe cộ và tàu không phải tránh chỗ nào. Menu: *Cảnh vật → Máy bay*. |
| `showCyclist` | bool | `true` | Người đi xe đạp (đèn đêm, dù khi mưa). *Cảnh vật → Xe đạp*. |
| `showAmbientDetails` | bool | `true` | Chim, đom đóm, sao băng, khinh khí cầu, đèn phố. *Cảnh vật → Chim & đèn phố*. |
| `showWeatherEffects` | bool | `true` | Mưa, gợn nước, sấm chớp, mây nhiều lớp. *Cảnh vật → Thời tiết*. |
| `weatherPreview` | string | `"live"` | Scene xem trước: `live` (theo thời tiết thật + giờ hệ thống), `random` (v5.4.2, *Ngẫu nhiên*), `sunny`, `cloudy`, `rain`, `storm`, `snow` (v5.2, *Tuyết rơi*), `night`. Chấp nhận alias tiếng Việt (`nắng đẹp`, `mây trôi`, `mưa rào`, `giông bão`, `tuyết rơi`/`tuyet`, `đêm sao`, …). v5.3.2: chọn `night` (*Đêm sao*) từ bảng điều khiển / menu khay / lệnh xem trước sẽ đặt luôn `timeMode = "night"`; các giá trị khác không đổi `timeMode`. Sửa tay trong file thì không tự đổi. |
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
| `airplaneCycleMinutes` | float | `3` | v5.4: số phút cho một vòng bay (vắng → xuất hiện thấp ở mép trái, leo → bay ngang → hạ dần, biến mất ở mép phải), 0.5–240. |
| `trainCrossingMinutes` | float | `2` | Số phút cho một vòng của đoàn tàu: vào ga chậm dần (36 %), dừng trả khách (10 %) và đón khách (10 %), rời ga tăng tốc (40 %), nghỉ ngắn (4 %) (0.25–240). v5.0 mặc định 25 (config cũ còn `25` được tự đổi một lần khi nâng lên v5.1); từ v5.4 mặc định là `2` phút cho một vòng đời. Giá trị bạn đã lưu (kể cả `1`) không bao giờ bị ghi đè. |
| `timeMode` | string | `"cycle"` | `cycle` = chu kỳ ngày–đêm tăng tốc; `real` = theo giờ thật của máy; `day` = cố định ban ngày (~10:30–12:00, dao động nhẹ); `night` = cố định ban đêm (~23:30–00:00). Giá trị lạ → `cycle`. Đổi trong portal hoặc menu khay *Ngày & đêm*. |
| `dayCycleMinutes` | float | `30` | Ở chế độ `cycle`: số phút thật cho 1 ngày ảo 24 giờ, **tùy chỉnh 0,5–10080** (30 giây – 7 ngày; v5.3.1 bỏ giới hạn cũ 2–1440). 30 → mỗi giờ ảo = 75 giây; 1440 → nhịp như giờ thật nhưng lệch pha. Giá trị ≤ 0 / NaN / vô hạn → `30`; ngoài khoảng bị kẹp về 0,5 hoặc 10080. Đổi trong portal (*Độ dài chu kỳ*) áp dụng ngay, không nhảy cảnh (app tự chỉnh `dayCycleOffsetHours`). |
| `dayCycleOffsetHours` | float | `0` | Dịch đồng hồ ảo (giờ, 0–24), ví dụ `6` để chu kỳ bắt đầu gần bình minh. |
| `configVersion` | int | `56` | Phiên bản schema để migrate một lần (app tự ghi, không cần sửa). |
| `regionMode` | string | `"auto"` | `auto`, `hcmc`, `baria-vungtau`, `hanoi`, `danang-hoian`, `tokyo`, `paris`, `sydney`, `newyork` (v5.4), `default`. v5.4.2: `rome`, `giza`, `santorini` đã bỏ — file cũ còn ghi các giá trị này tự chuyển về `auto`. `auto` suy ra từ `weatherCity` → tên địa điểm thời tiết → tọa độ. |
| `showWeatherHud` | bool | `false` | (Bỏ từ v5.4) Thẻ nhiệt độ trên taskbar không còn được vẽ; khóa này bị bỏ qua và tự về `false`. Vị trí, nhiệt độ, điều kiện thời tiết xem trong bảng điều khiển. |
| `weatherFollowsRegion` | bool | `false` | Khi chọn khu vực bằng tay: lấy thời tiết theo tọa độ khu vực đó thay vì vị trí máy. |
| `tapMode` | string | `"auto"` | Khóa ẩn để khắc phục sự cố (không có trên giao diện): `auto` thử gắn TAP (bỏ qua nếu có SAC); `off` luôn dùng chế độ an toàn. |
| `tapTimeoutSeconds` | float | `3.5` | Thời gian tối đa chờ TAP báo Ready (1.5–8). |

Ghi chú:

- `iconSafePadding` không còn là độ rộng cố định: v5 đo icon thật bằng UI Automation; vùng khoét = khung icon + 2 px·DPI, viền mờ 6 px·DPI nằm ngoài icon.
- Vị trí IP có chống nhảy: khi IP báo nơi cách hơn 80 km, cần 2 lần xác nhận liên tiếp mới đổi (log ghi `+stable`). Muốn cố định, đặt `weatherCity` hoặc `weatherLatitude`/`weatherLongitude`.

### Chu kỳ ngày–đêm (v5.1)

- Giờ ảo = `(thời gian Unix + dayCycleOffsetHours/24·chu kỳ) mod chu kỳ`, chạy liên tục 0,0 → 24,0; mọi máy cùng cấu hình sẽ thấy cùng giờ ảo.
- Bảng màu trời / mặt đất / bóng công trình nội suy mượt giữa các mốc 00:00, 04:24, 05:18, 06:06, 07:18, 09:00, 12:30, 15:36, 17:00, 18:06, 19:00, 20:00. Ánh sáng ngày, đèn nhân tạo, sao và độ ấm hoàng hôn là các hệ số liên tục (smoothstep), không nhảy cóc.
- Mặt trời: 05:30 → 18:30, mọc trái, cao nhất lúc 12:00, lặn phải. Mặt trăng: 18:30 → 05:30, cao nhất lúc 00:00. Ở chế độ an toàn (icon được khoét), cung chạy qua các khoảng trống giữa nhóm icon để không bị icon che hàng giờ; khi TAP gắn (cảnh nằm sau taskbar) cung chạy hết chiều rộng. Đi ngang thẻ thời tiết thì tự mờ đi.
- Thẻ thời tiết vẫn hiển thị thời tiết **thật**; chỉ cảnh vật đi theo giờ ảo.

### Tuyết rơi (v5.2)

- Tự bật khi Open-Meteo trả mã tuyết: `71` (Tuyết nhẹ), `73` (Tuyết rơi), `75` (Tuyết dày), `77` hạt tuyết (Tuyết nhẹ), `85` / `86` mưa tuyết rào (nhẹ / dày). Mật độ hạt theo mã: nhẹ ≈ 45 %, vừa ≈ 70 %, dày 100 %.
- Xem trước: menu khay *Xem trước thời tiết → Tuyết rơi*, hoặc `"weatherPreview": "snow"`, hoặc `AudioTaskbar.exe --render out.png --preview snow --hour 12 --region tokyo`.
- Tắt riêng hạt tuyết (giữ bầu trời / lớp tuyết trên mái) bằng `"showWeatherEffects": false`.
- Không có khóa config mới; `configVersion` giữ nguyên 51.

## Bảng điều khiển & tự động cập nhật (v5.3)

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `startMinimized` | `false` | Mở app không hiện bảng điều khiển (chỉ icon khay). Khởi động cùng Windows luôn dùng `--minimized`. |
| `autoUpdateEnabled` | `true` | Kiểm tra bản mới vài giây sau khi mở app và mỗi 6 giờ (v5.4: ~4 s). |
| `autoUpdateSilentInstall` | `true` | Có bản mới (đã khớp SHA-256) thì tự tải + cài im lặng rồi mở lại dưới khay. `false` = chỉ báo; bấm nút gộp *Kiểm tra & cập nhật* (bảng điều khiển hoặc menu khay) để cài. |
| `updateRepo` | `"Hieunguyen00000/AudioTaskbar"` | `owner/repo` GitHub để đọc `releases/latest`. Repo phải **Public** (người dùng không đăng nhập GitHub). Sai dạng → về mặc định. |
| `updateFeedUrl` | `""` | Thay cho GitHub API: URL `http(s)` tới `latest.json` tĩnh (GitHub Pages, R2/S3, máy chủ riêng). Không phải http(s) → bỏ qua. |

`latest.json`:

```json
{ "version": "5.3.1", "fileName": "AudioTaskbar-Setup-5.3.1.exe",
  "downloadUrl": "https://…/AudioTaskbar-Setup-5.3.1.exe", "sha256": "<64 hex>", "notes": "…" }
```

`downloadUrl` có thể là đường dẫn tương đối so với `latest.json`. Thiếu / sai `sha256` thì **không** tự cài.

Bảo vệ khi file config hỏng (v5.3): JSON lỗi / sai kiểu không làm app dừng — file cũ được giữ thành `config.json.corrupt-yyyyMMdd-HHmmss`, app chạy với cấu hình mặc định và ghi lại file sạch. Giá trị ngoài khoảng (âm, NaN, quá lớn) được kẹp về khoảng hợp lệ.

## Mùa, thời tiết tự đổi, vùng đệm icon, âm thanh nền (v5.4)

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `season` | `"summer"` | `spring`, `summer`, `autumn`, `winter` (chấp nhận `xuân`, `hạ`/`hè`, `thu`, `đông`), hoặc hai chế độ tự động (v5.4.2): `cycle` = *Tự đổi mùa* (xuân → hè → thu → đông, mỗi mùa `seasonCycleMinutes` phút, bắt đầu từ mùa thực tế hiện tại), `real` = *Thực tế* (mùa thật theo ngày và vị trí, xem bên dưới). Hè là giao diện cũ. Chọn thời tiết *Tuyết rơi* tự đặt `winter`; rời mùa đông khi đang chọn tuyết thì thời tiết về *Thực tế*. |
| `seasonCycleMinutes` | `30` | Số phút thật cho mỗi mùa ở chế độ `cycle` (0,5–10080). Bảng điều khiển chỉ hiện ô này khi chọn *Tự đổi mùa*. |
| `weatherPreview` = `"random"` | — | v5.4.2 *Ngẫu nhiên*: chờ ngẫu nhiên trong `[weatherCycleRandomMinMinutes, weatherCycleRandomMaxMinutes]` rồi chọn thời tiết hợp mùa (không lặp lại liền kề, tuyết chỉ vào mùa đông). Trước lần đổi đầu tiên cảnh theo thời tiết thật. Chỉ ảnh hưởng cảnh đang chạy. |
| `weatherCycleRandomMinMinutes` / `weatherCycleRandomMaxMinutes` | `5` / `20` | Khoảng chờ của *Ngẫu nhiên* (0,5–1440). Bảng điều khiển hiện dòng *Đổi sau mỗi* chỉ khi chọn *Ngẫu nhiên*. |
| `showIconPadding` | `true` | Vùng đệm (nền xám mờ + viền khoét) quanh icon ứng dụng trên taskbar. `false` = cảnh vật sát icon, không có dải xám. |
| `soundMasterVolume` | `0` | Âm lượng tổng 0–100. |
| `soundVolumes` | tất cả `0` | Âm lượng từng hiệu ứng 0–100: `rain`, `thunder`, `wind`, `traffic`, `horns`, `train`, `airplane`, `birds`, `insects`, `animals`, `waves`. Âm thực tế = tổng × từng hiệu ứng × mức cảnh (khu vực, thời tiết, mùa, giờ). Khi `soundAutoMix` bật, chỉ các hiệu ứng có trong `soundManual` dùng giá trị ở đây. |
| `soundAutoMix` | `true` | v5.4.2 *Tự phối theo cảnh*: mỗi hiệu ứng dùng mức chuẩn theo cảnh (bảng dưới). Tắt đi thì mức đang nghe trở thành giá trị của bạn trong `soundVolumes`. |
| `soundManual` | `[]` | v5.4.2: các hiệu ứng bạn đã kéo tay — tự phối không đụng tới. Nút *Phối lại theo cảnh* xóa danh sách này (âm lượng tổng giữ nguyên). |

### Khóa đã bỏ ở v5.4.2 (vẫn đọc được từ file cũ)

| Khóa cũ | Chuyển thành |
|---|---|
| `seasonCycleEnabled: true` | `season = "cycle"` |
| `weatherCycleMode: "random"` | `weatherPreview = "random"`, giữ khoảng min/max |
| `weatherCycleMode: "interval"` + `weatherCycleMinutes: N` | `weatherPreview = "random"` với min = max = N |
| `regionMode`: `rome` / `giza` / `santorini` | `auto` |

### Mùa *Thực tế* (v5.4.2)

Vị trí lấy theo khu vực đang chọn (hoặc tọa độ thời tiết khi *Tự động*); không rõ thì dùng TP. Hồ Chí Minh (10,78° B).

- **Vĩ độ ≥ 18°** (Hà Nội 21°, Tokyo, Paris, New York, Sydney …): mùa khí tượng. Bắc bán cầu: xuân tháng 3–5, hè 6–8, thu 9–11, đông 12–2.
- **Nhiệt đới, vĩ độ < 18°** (TP.HCM, Vũng Tàu, Đà Nẵng …): không có mùa đông thật, nên ánh xạ theo cảm nhận: **xuân tháng 1–2** (Tết, hoa mai), **hè tháng 3–7** (nắng nóng, đầu mùa mưa), **thu tháng 8–12** (mưa nhiều, dịu hơn). Không bao giờ ra mùa đông / tuyết.
- **Nam bán cầu** (Sydney, Jakarta …): lệch 6 tháng — Sydney tháng 1 là hè, tháng 7 là đông.

### Phối âm chuẩn theo cảnh (v5.4.2)

Âm lượng tổng (`soundMasterVolume`) **chỉ do bạn chỉnh** — mặc định 0 (im lặng), không preset hay migration nào thay đổi. Tự phối chỉ đặt mức của từng hiệu ứng:

| Hiệu ứng | Mức nền | Điều chỉnh theo cảnh |
|---|---|---|
| `rain` | 55 | +10 khi mưa, +20 khi giông bão |
| `thunder` | 60 | +20 khi giông bão |
| `wind` | 30 | +15 giông bão; +5 mùa thu, +10 mùa đông |
| `traffic` | 35 | tới +20 ở đô thị lớn (New York, TP.HCM, Hà Nội); −10 ban đêm; −5 khi mưa |
| `horns` | 15 | tới +15 ở đô thị; −10 ban đêm; −5 khi mưa |
| `train` | 45 | tới +5 ở đô thị |
| `airplane` | 40 | −10 khi giông bão |
| `birds` | 45 | tới +15 ban ngày (ít hơn ở đô thị); −20 ban đêm; −15 khi mưa |
| `insects` | 35 | dế ban đêm tới +25 (mạnh nhất mùa hè); −10 khi mưa; −15 mùa đông |
| `animals` | 30 | tới +15 ban đêm ở vùng quê; −5 khi mưa |
| `waves` | 35 | tới +30 ở vùng biển (Vũng Tàu, Sydney, Đà Nẵng); thêm tới +10 khi giông |

Kết quả kẹp trong 0–100 rồi vẫn nhân với âm lượng tổng và mức cảnh như trước. Kéo một thanh hiệu ứng = hiệu ứng đó thành "tay" (ghi vào `soundManual`), các hiệu ứng khác vẫn tự phối. Nhãn giá trị ghi `· tự` cho hiệu ứng đang tự phối.

Nâng cấp lên `configVersion` 56: `soundAutoMix = true`; nếu âm lượng tổng > 0 (bạn đã nghe với mix riêng) thì mọi hiệu ứng được giữ nguyên là "tay"; nếu tổng = 0 thì chỉ hiệu ứng nào > 0 được giữ, còn lại theo cảnh. `soundMasterVolume` không đổi. Chạy lại migration không thay đổi gì thêm.


Nâng cấp từ bản cũ: `configVersion` lên `54`, mọi âm lượng **luôn đặt 0** (kể cả file cũ có sẵn giá trị lạ), mùa = `summer`, các chu kỳ tắt, `showIconPadding = true`, `showWeatherHud = false`. Các lựa chọn khác giữ nguyên. Âm thanh là các bản thu CC0 nhúng sẵn trong exe (cộng gió/máy bay/tiếng ray tàu tổng hợp), không cần file ngoài; tạm dừng khi hiệu ứng bị tắt (F8) và dừng hẳn khi thoát.

Cấu hình chỉnh trong bảng điều khiển hoặc menu khay áp dụng ngay để xem trước; nhấn **Lưu cấu hình** để ghi vào `config.json`. Đóng bảng điều khiển hoặc thoát khi còn thay đổi chưa lưu sẽ hỏi Lưu / Không lưu / Hủy.

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

Xóa `config.json` rồi chạy lại app để trở về mặc định v5.1.
