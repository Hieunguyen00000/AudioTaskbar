# Security policy

## Supported versions

Security fixes are applied to the latest published release of Audio Taskbar on [GitHub Releases](https://github.com/Hieunguyen00000/AudioTaskbar/releases) and to the `main` branch. Older portable builds are not patched in place; please update to the newest release.

## Reporting a vulnerability

Please **do not** open a public issue for a vulnerability that could be used to compromise a user's machine (for example a bug in `TaskbarTap.dll` that could affect `explorer.exe`).

Instead, report it privately:

1. Prefer a [GitHub Security Advisory](https://github.com/Hieunguyen00000/AudioTaskbar/security/advisories) on this repository, or
2. Contact the maintainer, [Hieu Nguyen Van](https://github.com/Hieunguyen00000), through GitHub.

You should receive an acknowledgement as soon as the maintainer can review the report. Please include the affected version, a clear description, and steps or a proof of concept if you have them.

## What this software does with Windows

Audio Taskbar is a per-user, non-admin app. `TaskbarTap.dll` is loaded into `explorer.exe` only through the documented `InitializeXamlDiagnosticsEx` XAML Diagnostics API (not `CreateRemoteThread`). It does not attempt to bypass Smart App Control, SmartScreen, or Code Integrity. If Windows refuses to load the DLL, the app falls back to an icon-safe UIA overlay. See [docs/CODE_SIGNING.md](docs/CODE_SIGNING.md) and [NOTICE.md](NOTICE.md).
