# Xử lý sự cố

Log luôn là chỗ xem trước: `%LOCALAPPDATA%\AudioTaskbar\app.log`. Trạng thái TAP: `%LOCALAPPDATA%\AudioTaskbar\tap-status.txt`. Mở thư mục này từ menu khay → *Mở thư mục cấu hình*.

## Explorer bị đứng hoặc taskbar biến mất sau khi bật fusion

Việc gắn TAP chèn vào `explorer.exe`. Hiếm khi Explorer restart hoặc treo (đã gặp khi *gỡ* TAP giữa phiên).

**Khắc phục ngay:**

1. `Ctrl+Shift+Esc` → tìm *Windows Explorer* → *Restart task* (hoặc Chạy `explorer.exe` nếu taskbar đã mất).
2. Nếu app vẫn chạy, tắt bằng `stop.bat` hoặc End task `AudioTaskbar.exe`.
3. Tắt fusion: trong `%LOCALAPPDATA%\AudioTaskbar\config.json` đặt

```json
"blockWallpaper": false,
"fuseIntoTaskbar": false,
"makeTaskbarTransparent": false
```

rồi chạy lại app. Diorama vẫn hiện ở chế độ an toàn (khoét vùng icon).

Menu *Chặn ảnh nền desktop* chính là công tắc fusion. Nên để tắt nếu máy không ổn định với TAP.

F8 **không** gỡ TAP (chỉ ẩn overlay) đúng để tránh restart Explorer khi bật/tắt liên tục. Thoát hẳn app mới restore nền.

Không chạy đồng thời Windhawk Taskbar Styler, TranslucentTB, hoặc Visual Studio đang debug XAML Explorer — chỉ một consumer `InitializeXamlDiagnosticsEx` tại một thời điểm.

## Icon Start / ứng dụng / khay bị che hoặc mất

Đã sửa ở v3.2.1: overlay phải nằm *dưới* `Shell_TrayWnd`, không dùng `HWND_TOPMOST` như insert-after.

- Cập nhật lên bản build v4.0, restart app.
- Nếu vẫn che: tắt *Chặn ảnh nền desktop* (về overlay an toàn + `iconSafeFade`).
- Kiểm tra `app.log` có dòng TAP attach / `BehindOk` / fallback.
- Chưa bao giờ thay icon, chỉ vẽ nền. Nếu icon biến mất hẳn, đó là Explorer lỗi — restart Explorer như trên.

## Phím F8 không bật/tắt được

Thường do hotkey bị app khác đăng ký trước (`RegisterHotKey` thất bại). Icon khay sẽ báo; `app.log` có `Hotkey: RegisterHotKey(...) failed`.

Đổi trong `config.json`:

```json
"hotkey": "Ctrl+Alt+F8"
```

Rồi *Tải lại cấu hình* hoặc restart app. Cú pháp: `Ctrl`/`Alt`/`Shift`/`Win` + phím, nối bằng `+`.

Một số laptop dùng F8 cho Fn (độ sáng, v.v.) — giữ Fn hoặc đổi phím.

## Thời tiết sai thành phố / nhiệt độ nhảy (ví dụ 16°C thay vì Bà Rịa)

Khi `weatherCity` và tọa độ để trống, vị trí lấy theo IP (`ipapi.co` → `geojs` → `ip-api.com`). VPN, DNS, hoặc IP nhà mạng có thể trỏ nhầm vùng.

Khóa cứng trong `config.json`:

```json
"weatherCity": "Bà Rịa"
```

Hoặc:

```json
"weatherLatitude": 10.51,
"weatherLongitude": 107.18,
"weatherCity": "Bà Rịa"
```

Tọa độ được ưu tiên hơn tên thành phố. Menu *Thời tiết → Làm mới ngay* sau khi lưu. Xóa `weather-cache.json` nếu cache cũ còn hiện số liệu nơi khác.

## `AudioTaskbar.exe` cũ trên ổ C không xóa được (Access is denied)

Bản cài sớm từng nằm ở `C:\Users\hieunv1\AudioTaskbar\AudioTaskbar.exe`. Windows có thể giữ file lock (Image File Execution, antivirus, hoặc ACL) kể cả khi process đang chạy là bản ở ổ D.

- Xác nhận process đang chạy: Task Manager → đường dẫn Image. Chỉ cần một bản (mutex singleton).
- Thử xóa sau khi `stop.bat`, hoặc xóa bằng quyền admin, hoặc sau khi restart máy.
- Shortcut Desktop / Start Menu phải trỏ tới `release\AudioTaskbar.exe` trong thư mục dự án (ổ D), không còn trỏ ổ C.

## Đa màn hình và DPI

- App dùng *PerMonitor V2*. Mỗi taskbar có overlay riêng, bounds theo DPI từng màn.
- `showOnAllMonitors: false` nếu chỉ muốn màn hình chính.
- `weatherOnPrimaryOnly: true` (mặc định): thẻ nhiệt độ chỉ hiện trên taskbar chính, tránh lặp chữ.
- Kéo taskbar, đổi độ phân giải, cắm/rút màn hình: overlay được tạo lại; TAP gắn lại khi nhận `TaskbarCreated`.
- Taskbar dọc / trên cùng: diorama vẫn vẽ theo bounds, nhưng cảnh 48px được thiết kế chủ yếu cho taskbar ngang dưới.

## Hiệu ứng không thấy / wallpaper vẫn xuyên qua

- App đã chạy? Có icon khay không? (có thể bị gom trong overflow.)
- F8 đang ở trạng thái tắt? Bấm lại hoặc chọn *Bật hiệu ứng* trên menu.
- `hideWhenFullscreen`: cửa sổ fullscreen (game, video) sẽ ẩn overlay trên màn đó.
- Wallpaper xuyên vùng icon: *Chặn ảnh nền desktop* phải bật **và** TAP phải gắn thành công (`tap-status.txt` có số element patched > 0). Trên Windows 10 hoặc khi API XAML Diagnostics không có, chỉ vùng trống là nền đặc; khe icon có thể còn mica/wallpaper của Windows — đây là giới hạn, không phải lỗi config.
- Build thiếu `TaskbarTap.dll` (quên biên dịch trước `build.ps1`): exe không embed TAP. Xem [development.md](development.md).

## App không khởi động / báo đã chạy

Chỉ một instance (`Local\Hieunv1.AudioTaskbar.Singleton`). `start.bat` sẽ báo PID nếu đang chạy. Nếu process zombie: `stop.bat` hoặc End task, rồi chạy lại.

Crash: xem `app.log` (dòng `CRASH:`). Thiếu Visual C++ redistributable hầu như không phải vấn đề với bản mingw `-static-libgcc` hoặc self-contained .NET; vẫn kiểm tra nếu TAP fail lúc load DLL.

## Gỡ cài đặt

1. Tắt app (`stop.bat` hoặc *Thoát* trên khay) — bước này restore nền taskbar.
2. `scripts\disable-autostart.ps1` nếu đã bật tự khởi động (xóa `Audio Taskbar.lnk` trong thư mục Startup).
3. Xóa shortcut Desktop / Start Menu nếu có.
4. Xóa thư mục dự án (ví dụ `D:\999. Others\Agentic AI\Grok Bot\AudioTaskbar`).
5. (Tùy chọn) xóa dữ liệu user: `%LOCALAPPDATA%\AudioTaskbar\`.
6. (Tùy chọn) xóa bản cũ trên ổ C nếu còn: `C:\Users\<user>\AudioTaskbar\` và `C:\Users\<user>\audio-taskbar\`.

Không sửa registry hệ thống, không cài service, không đòi reboot (trừ khi Explorer đang treo và bạn muốn chắc chắn khóa file đã nhả).
