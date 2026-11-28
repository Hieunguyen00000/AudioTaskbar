# License notes (TaskbarTap / XAML TAP)

Audio Taskbar's managed app and the native `TaskbarTap.dll` are original
code written for this project.

The XAML-diagnostics approach (InitializeXamlDiagnosticsEx + a TAP
implementing IObjectWithSite / IVisualTreeServiceCallback2) is a
publicly documented Windows mechanism, also used by Visual Studio's
XAML debugger and discussed in public write-ups of ExplorerTAP /
Windhawk Taskbar Styler / TranslucentTB.

`windhawk-mods` and TranslucentTB are licensed under GPL-3.0. This
project does **not** copy their source. The TAP was written clean-room
from Microsoft headers (`xamlom.h`) and published API behavior:

- match elements by x:Name (`BackgroundFill`, `BackgroundStroke`) rather
  than a hard-coded parent path (needed on Windows 11 26300);
- set Fill to a transparent brush and collapse the stroke;
- restore with ClearProperty on detach;
- re-apply after Explorer restart / new monitors via tree callbacks
  and the application's TaskbarCreated handler.

Do not combine this binary with GPL-licensed Explorer TAP code in a
way that would create a derivative work unless you are prepared to
apply GPL-3 terms to the combination.

# Ambient sound (v5.4)

All ambient sound (rain, thunder, wind, traffic, horns, trains, birds,
insects, animals, waves) is generated at run time by original DSP code in
`src/AmbientSynth.cs` (filtered noise and synthesized tones). The app ships
**no recorded audio** and no third-party samples, so there are no sound
licences or attributions to carry. rainymood.com was used only as a
reference for the *feel* (layered, seamless, soft fades); none of its audio
was copied or used. Playback uses NAudio (MIT), already a dependency.
