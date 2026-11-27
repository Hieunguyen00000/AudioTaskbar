# Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

**Status (November 2026):** we have applied to the [SignPath Foundation](https://signpath.org/) open-source program. Until that application is approved and a signed GitHub Release is published, `AudioTaskbar.exe`, `TaskbarTap.dll` and `AudioTaskbar-Setup-*.exe` are **not yet Authenticode-signed**. Windows 11 Smart App Control and SmartScreen may still treat downloads as an unknown publisher. This page will be updated once the first signed release ships.

> **Tiếng Việt:** Audio Taskbar đang xin chữ ký số miễn phí của SignPath Foundation. Khi được duyệt, mỗi bản phát hành trên GitHub Releases sẽ được ký bằng chứng chỉ của SignPath Foundation (người phát hành hiện trên Windows là *SignPath Foundation*). Cho đến lúc đó file tải về vẫn chưa có chữ ký Authenticode.

## What will be signed

After approval, only CI-built release artifacts from this repository are submitted to SignPath:

- `TaskbarTap.dll` — signed **before** it is embedded into the portable executable, because the app extracts this DLL to `%LOCALAPPDATA%\AudioTaskbar\` and asks Windows to load it into `explorer.exe`. Signing only the outer `.exe` would leave the extracted DLL unsigned, and Smart App Control would still block it.
- `AudioTaskbar.exe` — the portable, self-contained app (with the already-signed DLL embedded).
- `AudioTaskbar-Setup-*.exe` — the optional per-user NSIS installer.

Checksums in `SHA256SUMS.txt` and the updater manifest `latest.json` are computed from the **signed** files. Local or unofficial builds are never submitted for signing.

The private key never leaves SignPath (HSM-backed). This project does not store a code-signing certificate or private key in the repository or in GitHub Actions secrets — only a SignPath API token used to *request* a signature.

## How `TaskbarTap.dll` works (transparency)

`TaskbarTap.dll` is original code written for this project. It is a XAML Diagnostics TAP loaded through the documented Windows API `InitializeXamlDiagnosticsEx` (the same mechanism Visual Studio uses to inspect a XAML tree). It does **not** use `CreateRemoteThread`, process hollowing, or any other undocumented injection technique, and it does **not** attempt to bypass Windows security controls such as Smart App Control, SmartScreen, or Code Integrity.

The TAP only makes the Windows 11 taskbar background fill transparent so the in-app diorama can sit behind the icons. If Smart App Control or Code Integrity refuses to load the DLL into `explorer.exe`, Audio Taskbar automatically falls back to an icon-safe UIA overlay mode and leaves Explorer untouched. See [NOTICE.md](../NOTICE.md) and [PRIVACY.md](../PRIVACY.md).

## Team roles

This is a single-maintainer project. All three SignPath roles are held by the same person:

- **Authors** (commit access; may modify the source without additional reviews): [Hieu Nguyen Van](https://github.com/Hieunguyen00000)
- **Reviewers** (review changes proposed by non-committers, e.g. pull requests): [Hieu Nguyen Van](https://github.com/Hieunguyen00000). External pull requests are reviewed by the maintainer before merge.
- **Approvers** (approve each SignPath signing request): [Hieu Nguyen Van](https://github.com/Hieunguyen00000). Every release signing request requires explicit approval in the SignPath dashboard. GitHub Actions will wait for that approval (up to 60 minutes per request).

## Build and signing process

Release binaries are produced only by the public GitHub Actions workflow [`.github/workflows/release.yml`](../.github/workflows/release.yml) on GitHub-hosted `windows-latest` runners (never a self-hosted runner). After SignPath approval, set the repository variable `SIGNPATH_ENABLED` to `true` to turn on the two-phase signing flow:

1. Compile `TaskbarTap.dll` (with VERSIONINFO) → upload as a workflow artifact → SignPath signs it → the signed DLL is copied to `src/TaskbarTap/TaskbarTap.dll`.
2. Run tests, publish `AudioTaskbar.exe` (embedding the signed DLL), build the NSIS installer → upload exe + Setup → SignPath signs both → checksums and the GitHub Release are published from the signed files.

While `SIGNPATH_ENABLED` is unset or not `true`, the same workflow still publishes **unsigned** releases so development can continue during the SignPath review.

### Suggested SignPath artifact configurations

Use a ZIP root (GitHub `actions/upload-artifact` zips by default). Restrict PE metadata to this project’s name and publisher. Pass `version` from the workflow (for example `5.3.2`).

**Artifact configuration slug `taskbartap-dll`:**

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <parameters>
    <parameter name="version" required="true" />
  </parameters>
  <zip-file>
    <pe-file path="TaskbarTap.dll"
             product-name="Audio Taskbar"
             product-version="${version}"
             company-name="Hieunguyen00000"
             copyright="Copyright (c) 2026 Hieunguyen00000"
             original-filename="TaskbarTap.dll">
      <authenticode-sign/>
    </pe-file>
  </zip-file>
</artifact-configuration>
```

If SignPath compares `product-version` against the four-part `FileVersion` (`5.3.2.0`) rather than the three-part product string (`5.3.2`), relax or adjust that attribute to match the certificate policy you create.

**Artifact configuration slug `audiotaskbar-app`:**

```xml
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <parameters>
    <parameter name="version" required="true" />
  </parameters>
  <zip-file>
    <pe-file path="AudioTaskbar.exe"
             product-name="Audio Taskbar"
             company-name="Hieunguyen00000"
             copyright="Copyright (c) 2026 Hieunguyen00000"
             original-filename="AudioTaskbar.exe">
      <authenticode-sign/>
    </pe-file>
    <pe-file path="AudioTaskbar-Setup-${version}.exe"
             product-name="Audio Taskbar"
             company-name="Hieunguyen00000"
             copyright="Copyright (c) 2026 Hieunguyen00000"
             original-filename="AudioTaskbar-Setup-${version}.exe">
      <authenticode-sign/>
    </pe-file>
  </zip-file>
</artifact-configuration>
```

## Privacy policy

See [PRIVACY.md](../PRIVACY.md). Audio Taskbar does not include advertising or telemetry. It only contacts the network for optional weather/location data and for GitHub Releases updates, all of which can be limited or disabled as described there.
