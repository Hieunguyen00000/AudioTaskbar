# Privacy policy

Audio Taskbar is a local Windows desktop app. It does **not** create user accounts, show advertising, or collect analytics, telemetry, crash reports, or usage statistics. Nothing is uploaded to a server operated by the maintainer.

The only network activity is described below. You can limit or disable each of these features.

## 1. IP geolocation (optional, auto region only)

**When:** only if the app needs to guess your city for live weather *and* you have not already given it a place. That happens when:

- *Region* is set to **Auto** (`regionMode: "auto"`), or the chosen region is the generic *Default* profile, **and**
- *Weather follows selected region* is **off** (`weatherFollowsRegion: false`), **and**
- you have not set a city (`weatherCity`) or coordinates (`weatherLatitude` / `weatherLongitude`) in `config.json`.

**Where:** the app tries these public endpoints, in order, and stops at the first success:

- `https://ipapi.co/json/`
- `https://get.geojs.io/v1/ip/geo.json`
- `http://ip-api.com/json/?fields=status,country,regionName,city,lat,lon`

These services see your public IP address in the ordinary way any HTTPS request does. The app keeps only the resulting city / coordinates locally (in memory and, if saved, in `%LOCALAPPDATA%\AudioTaskbar\config.json`). It does not send your IP to the maintainer.

**How to disable:** in the control panel or tray menu, pick a **fixed region** (for example Ho Chi Minh City or Tokyo) and turn on **Weather follows selected region** (`weatherFollowsRegion: true`). You can also set `weatherCity` or `weatherLatitude` / `weatherLongitude` in `config.json`. After that, no IP geolocation request is made.

## 2. Weather and geocoding (Open-Meteo)

**When:** Audio Taskbar fetches a weather forecast so the taskbar diorama can follow real conditions (and so the optional weather HUD can show the temperature). This runs at startup and then on a timer (`weatherRefreshMinutes`, default 15 minutes).

**Where:**

- Forecast: `https://api.open-meteo.com/v1/forecast?...` (latitude, longitude, and the current weather fields only).
- City lookup (only if you typed a city name in `weatherCity`): `https://geocoding-api.open-meteo.com/v1/search?...`.

Open-Meteo sees the coordinates (or city name) you configured or that were obtained from the step above. A short local cache is kept so the app can still show the last forecast offline.

**How to limit this:**

- Pick a **fixed region** with **Weather follows selected region** (or set a city / coordinates) so the forecast is for a place you chose, not an IP-guessed location.
- A **manual weather preview** in the control panel or tray (Clear, Rain, Starry night, and so on; `weatherPreview` other than `live`) only overrides how the diorama is drawn. It does **not** by itself stop the background forecast request. There is currently no separate “fully offline / never contact Open-Meteo” switch; leave the machine disconnected, or contact the maintainer if you need a dedicated toggle.

## 3. Update checks (GitHub Releases) — portable and NSIS installer editions only

**Microsoft Store edition:** if you installed Audio Taskbar from the Microsoft Store, the app does **not** contact GitHub to check for or download updates. Updates are delivered exclusively through the Microsoft Store (Windows checks for package updates on its usual schedule). There is no in-app GitHub updater in that edition.

**Portable / NSIS installer editions:** if automatic updates are enabled (default: `autoUpdateEnabled: true`), the app checks for a newer release about 10 seconds after start and then every 6 hours. You can also click **Check for updates**. If silent install is also on (default: `autoUpdateSilentInstall: true`) and a newer signed checksum-verified release exists, the app may download and install it, then relaunch.

**Where (portable / NSIS only):**

- `https://api.github.com/repos/Hieunguyen00000/AudioTaskbar/releases/latest`
- `https://github.com/Hieunguyen00000/AudioTaskbar/releases/download/...` (the published `AudioTaskbar.exe` or Setup, plus `latest.json` / `SHA256SUMS.txt` when used)

GitHub sees a normal HTTPS request (including a short `AudioTaskbar-Updater/...` user-agent). No GitHub account is required and no token is sent.

**How to disable (portable / NSIS only):** in the control panel, turn off **Automatically check for updates**, or set `"autoUpdateEnabled": false` in `%LOCALAPPDATA%\AudioTaskbar\config.json`. You can still check manually from the tray or the Update tab. To prevent silent installs but still be notified, set `"autoUpdateSilentInstall": false`.

## What stays on your PC

Settings and logs live under `%LOCALAPPDATA%\AudioTaskbar\` (in the Microsoft Store edition this is the package's local data folder). The portable/NSIS edition may also extract `TaskbarTap.dll` there; the Store edition copies that DLL bit-identically into the package TempState folder so Explorer can load a Store-signed file. The optional NSIS installer lives under `%LOCALAPPDATA%\Programs\AudioTaskbar\`. Uninstall can keep or delete your settings; see the uninstaller prompt and [docs/troubleshooting.md](docs/troubleshooting.md).

## Source and questions

The complete source, including every URL above, is in this public repository. See also the [Code signing policy](docs/CODE_SIGNING.md). Questions or privacy reports: contact the maintainer at [https://github.com/Hieunguyen00000](https://github.com/Hieunguyen00000).
