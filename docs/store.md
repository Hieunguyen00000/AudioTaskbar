# Microsoft Store (MSIX) packaging

Audio Taskbar has two distribution channels that share the same source:

| Channel | How it is built | Updates | Signing |
|---------|-----------------|---------|---------|
| Portable exe + optional NSIS Setup (GitHub Releases) | `scripts/build.ps1` (`PublishSingleFile=true`) | In-app GitHub updater | SignPath (pending) |
| Microsoft Store | `scripts/build-msix.ps1` (`-p:StoreBuild=true`, **not** single-file) | Store-managed only | Microsoft re-signs the package after certification |

The Store build is a compile-time edition (`STORE_BUILD`): it disables GitHub self-update, uses the MSIX `StartupTask` API instead of `HKCU\...\Run`, ships `TaskbarTap.dll` as a loose file in the package (copied at runtime to TempState), and never kills `explorer.exe`.

## Partner Center identity (do not invent new values)

These must stay in `packaging/msix/AppxManifest.xml`:

- Package/Identity/Name: `HubertNguyen.AudioTaskbar`
- Package/Identity/Publisher: `CN=826CBF8C-383C-4259-B039-4075F5205C32`
- Package/Properties/PublisherDisplayName: `Hubert Nguyen`
- Package Family Name: `HubertNguyen.AudioTaskbar_z52jp6qfzprfp`
- Store ID: `9PMQLC03S8Z6`
- Display name: `Audio Taskbar`

The package version uses four parts and the fourth must be `0` (for example `5.4.0.0`). `scripts/build-msix.ps1` stamps it from `<Version>` in `src/AudioTaskbar.csproj`.

## Build on Windows

```powershell
# From the repo root (needs .NET 8 SDK + TaskbarTap.dll toolchain)
powershell -ExecutionPolicy Bypass -File scripts\build-msix.ps1
```

Output: `packaging\msix\out\AudioTaskbar_<version>_x64.msix` (unsigned — upload this file to Partner Center).

Optional local sideload / WACK testing (not for Store upload):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-msix.ps1 -SignTestCert
```

If `makeappx.exe` is not already on the machine, the script restores the `Microsoft.Windows.SDK.BuildTools` NuGet package.

Regenerate logos from `src/app.ico` (optional):

```powershell
python scripts\generate-msix-assets.py
```

The 300×300 Partner Center tile is `packaging/msix/StoreListing/StoreTile300x300.png`.

Privacy policy URL for the listing: https://github.com/Hieunguyen00000/AudioTaskbar/blob/main/PRIVACY.md
