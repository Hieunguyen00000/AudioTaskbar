# Partner Center listing drafts (ready to paste)

Privacy policy URL:

```
https://github.com/Hieunguyen00000/AudioTaskbar/blob/main/PRIVACY.md
```

Store ID: `9PMQLC03S8Z6` · Display name: `Audio Taskbar` · Publisher: `Hubert Nguyen`

Upload the package `AudioTaskbar_5.3.3.0_x64.msix` on the Packages page, and the 300×300 tile `packaging/msix/StoreListing/StoreTile300x300.png` in Store logos (1:1 App tile icon). Desktop screenshots must be PNG, 1366×768 or larger.

---

## Short description (English, ≤255 characters if needed)

A living weather diorama for the Windows 11 taskbar: regional scenery, day-and-night light, and optional audio-reactive motion — sitting safely behind your icons.

## Short description (Tiếng Việt)

Diorama thời tiết sống trên thanh taskbar Windows 11: cảnh vùng miền, ngày–đêm, chuyển động theo nhạc (tuỳ chọn) — nằm an toàn phía sau các icon.

## Description (English)

Audio Taskbar turns the Windows 11 taskbar into a small living scene.

It paints a regional weather diorama behind the taskbar icons — trains, harbour lights, rain, snow, and a day-to-night cycle — while keeping the icons, Start button, and system tray fully clickable. The app can follow live weather (Open-Meteo) or a manual preview, and an optional audio visualizer can react to system playback.

Highlights:

- Regions including Ho Chi Minh City, Hanoi, Da Nang–Hoi An, Tokyo, and Paris, plus automatic location
- Live weather and a 30-minute (adjustable) day/night cycle, or follow the real clock
- Icon-safe rendering at any DPI; press F8 to toggle the effect
- Optional “start with Windows” (off by default)
- No account, no ads, no telemetry

Updates for the Microsoft Store edition are delivered exclusively through the Microsoft Store. A privacy policy is published at https://github.com/Hieunguyen00000/AudioTaskbar/blob/main/PRIVACY.md

Audio Taskbar is a Windows 11 desktop companion. It does not replace Explorer or take over the shell. If the behind-icon effect cannot be applied, the app automatically falls back to a safe overlay that never modifies Explorer.

## Description (Tiếng Việt)

Audio Taskbar biến thanh taskbar Windows 11 thành một cảnh nhỏ đang sống.

Ứng dụng vẽ diorama thời tiết theo vùng miền phía sau các icon trên taskbar — tàu hoả, đèn cảng, mưa, tuyết, chu kỳ ngày–đêm — trong khi icon, nút Start và khay hệ thống vẫn bấm bình thường. Có thể theo thời tiết thật (Open-Meteo) hoặc xem trước thủ công, và bật visualizer theo nhạc hệ thống nếu muốn.

Điểm chính:

- Các vùng: TP. Hồ Chí Minh, Hà Nội, Đà Nẵng – Hội An, Tokyo, Paris, và tự động theo máy
- Thời tiết thật và chu kỳ ngày–đêm (mặc định 30 phút, chỉnh được), hoặc theo đồng hồ thật
- An toàn với icon ở mọi DPI; bấm F8 để bật/tắt hiệu ứng
- Tuỳ chọn khởi động cùng Windows (mặc định tắt)
- Không tài khoản, không quảng cáo, không telemetry

Bản Microsoft Store chỉ nhận cập nhật qua Microsoft Store. Chính sách quyền riêng tư: https://github.com/Hieunguyen00000/AudioTaskbar/blob/main/PRIVACY.md

Audio Taskbar là ứng dụng đồng hành cho Windows 11; không thay Explorer hay chiếm shell. Nếu không gắn được hiệu ứng phía sau icon, ứng dụng tự chuyển sang chế độ overlay an toàn và không chỉnh Explorer.

---

## runFullTrust justification (English, Submission options)

This is a packaged Win32 (.NET 8) desktop app that must run at full trust (medium IL / Desktop Bridge). It draws a weather diorama on the Windows 11 taskbar and, when possible, makes the taskbar background transparent so the scene can sit behind the icons.

To do that it loads a small in-proc helper (TaskbarTap.dll) through the documented Windows API InitializeXamlDiagnosticsEx — the same XAML Diagnostics mechanism Visual Studio uses to inspect a running XAML tree. The helper does not use CreateRemoteThread, process hollowing, or any technique that bypasses Windows security (Smart App Control, SmartScreen, or Code Integrity).

If that helper cannot be loaded into Explorer, the app automatically falls back to an icon-safe UI Automation overlay and leaves Explorer unmodified. The app runs asInvoker (no administrator elevation), does not install kernel drivers, and does not change system-wide settings other than an optional, user-controlled startup task (windows.startupTask, off by default).

---

## Notes for certification testers (English)

Thank you for reviewing Audio Taskbar (Win32 packaged as MSIX, runFullTrust).

What the app does:
- After launch, a diorama is drawn along the Windows 11 taskbar. Icons, Start, and the system tray stay clickable.
- F8 (configurable) toggles the effect on or off. “Đóng” hides the control window to the tray; “Kết thúc” exits and restores the taskbar.
- “Khởi động cùng Windows” uses the declared windows.startupTask (TaskId: AudioTaskbarStartup). It is off by default. Please do not expect a HKCU Run key.
- This Store build does not check GitHub for updates and cannot self-replace its install. Updates come only from the Microsoft Store.

Behind-icon mode:
- The app may load TaskbarTap.dll via InitializeXamlDiagnosticsEx (documented XAML Diagnostics TAP). This is optional. If Smart App Control or Code Integrity refuses the load, or if attach fails, the app stays in UIA icon-safe mode and does not modify Explorer.
- The Store build never kills explorer.exe. If the taskbar window is missing and no Explorer process is running, it may start explorer.exe once.

How to test quickly:
1. Install and launch Audio Taskbar. The control portal should appear (unless started from the startup task).
2. Confirm the diorama is visible on the taskbar and icons remain clickable.
3. Press F8 to turn the effect off and on.
4. Optionally change region / weather preview in the portal; changes apply live.
5. Leave “Khởi động cùng Windows” unchecked unless you are specifically testing the startup task.
6. Close with “Kết thúc” and confirm the taskbar returns to normal.

No Microsoft account, no in-app purchase, no internet account is required. Optional network use: Open-Meteo for weather and, only when region is Auto, public IP-geolocation. Details are in the privacy policy.
