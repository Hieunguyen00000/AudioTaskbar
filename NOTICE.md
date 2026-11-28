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

# Ambient sound (v5.4.2)

Rain, thunder, traffic, car horns, the train horn, birds, insects, animals and
waves are **real recordings**, all released under **CC0 1.0** (public domain
dedication, https://creativecommons.org/publicdomain/zero/1.0/), so no
attribution is legally required; they are credited here anyway. They are
embedded in the exe as OGG Vorbis (about 3.8 MiB in total, `src/Sounds/`)
and decoded at run time with NAudio.Vorbis 1.5.0 and NVorbis (both MIT).
Thunder is tied to the lightning flash of the diorama: each flash is one strike with a distance, the crack follows the flash after 0.04-2.8 s, quieter, duller (low-pass 20 kHz down to 1.4 kHz) and slightly slower the farther the strike. The thunder clips themselves are not filtered; they start about 0.03 s before their crack, are high-passed at 20 Hz, 2.5 s fade-out and normalised to about -16 LUFS.
The wind, the airplane and the train rumble / rail clack are still generated
by original DSP code (`src/AmbientSynth.cs`). rainymood.com was used only as
a reference for the *feel*; none of its audio was used.

All files come from Freesound (https://freesound.org/). Freesound only serves
the **original** uploads to signed-in users, so these assets were cut from the
site's high-quality MP3 *previews* (about 180 kbps) and re-encoded to Vorbis;
re-encoding the originals is a possible later quality upgrade. Every loop was
cross-faded so that it repeats seamlessly, and loudness-normalised (EBU R128,
about -18 LUFS for loops, -20 LUFS for one-shots).

| Embedded file(s) | Title - author (Freesound id) | Use | Processing | Size |
|---|---|---|---|---|
| `rain.ogg` | "Heavy Rain Regen Starkregen Raining Atmosphere Ambient Forest Wald" - Borgory ([651189](https://freesound.org/people/Borgory/sounds/651189/)) | rain loop (heavy rain outdoors, forest) | 40 s window, 3 s cross-fade, 40 Hz high-pass | 602 KiB |
| `thunder1.ogg` | "Closeup Thunder Strike 01" - loganzsound ([840628](https://freesound.org/people/loganzsound/sounds/840628/)) | thunder one-shot (close strike) | 11 s from the crack, full band | 158 KiB |
| `thunder2.ogg` | "Thunder big crack explosive close ms 77mel 190903.flac" - TRP ([574412](https://freesound.org/people/TRP/sounds/574412/)) | thunder one-shot (explosive close crack) | 12 s, full band | 153 KiB |
| `thunder3.ogg` | "Close lightning strike 2018 07 07" - csengeri ([434359](https://freesound.org/people/csengeri/sounds/434359/)) | thunder one-shot (close strike) | 12 s, full band | 170 KiB |
| `thunder4.ogg` | "Thunderstorm lightning strike" - foad ([243614](https://freesound.org/people/foad/sounds/243614/)) | thunder one-shot (strike with a long rumble) | 14 s, mono, full band | 117 KiB |
| `thunder5.ogg` | "230812 Thunder, med crack, rain stops dry rolling, Stratford 9am 2am" - TRP ([717888](https://freesound.org/people/TRP/sounds/717888/)) | thunder one-shot (sharp crack, dry rolling) | 8 s, full band | 117 KiB |
| `thunder6.ogg`, `thunder7.ogg` | "mountain thunder" - bruno.auzet ([647420](https://freesound.org/people/bruno.auzet/sounds/647420/)) | thunder one-shots (deep rolling) | two excerpts, 13 s and 10 s, full band | 340 KiB |
| `thunder8.ogg` | "Scary Thunder" - steffcaffrey ([527186](https://freesound.org/people/steffcaffrey/sounds/527186/)) | thunder one-shot (deep, scary) | 11 s, full band | 147 KiB |
| `birds.ogg` | "Birds singing, Dawn chorus" - SamsterBirdies ([578523](https://freesound.org/people/SamsterBirdies/sounds/578523/)) | bird loop | 36 s window, 2.5 s cross-fade, 120 Hz high-pass | 539 KiB |
| `waves.ogg` | "Calm ocean waves" - SamsterBirdies ([578524](https://freesound.org/people/SamsterBirdies/sounds/578524/)) | waves loop | 40 s window, 3 s cross-fade | 489 KiB |
| `traffic.ogg` | "Barcelona city light traffic from terrace" - Juno24 ([679517](https://freesound.org/people/Juno24/sounds/679517/)) | traffic loop | 40 s window, 3 s cross-fade, 60 Hz high-pass | 530 KiB |
| `horn.ogg` | "Ambience_City_Horn_01.wav" - BenDrain ([488065](https://freesound.org/people/BenDrain/sounds/488065/)) | car horn honk (1-3 honks, random pitch +-15 %, distance, pan) | one honk cut out, mono, 120 Hz high-pass | 13 KiB |
| `crickets.ogg` | "Cricket chirping" - Breviceps ([583074](https://freesound.org/people/Breviceps/sounds/583074/)) | night insects loop (two layers) | the two continuous sections (the file has silent gaps) cross-faded together, 22 s loop, mono | 213 KiB |
| `cicadas.ogg` | "cicada insect loop.wav" - soundofsong ([640718](https://freesound.org/people/soundofsong/sounds/640718/)) | daytime insects loop | 6 s loop, 2.5 s cross-fade | 74 KiB |
| `rooster.ogg` | "Rooster crow" - arelitron ([582624](https://freesound.org/people/arelitron/sounds/582624/)) | rooster at daybreak | one crow, mono | 21 KiB |
| `frogs1.ogg`, `frogs2.ogg` | "Cicadas and frogs - Antioquia Suroccidental - Bolombolo Colombia" - fenixevendir ([502204](https://freesound.org/people/fenixevendir/sounds/502204/)) | frog chorus at night | two 8 s excerpts, mono, 150 Hz high-pass | 140 KiB |
| `dog.ogg` | "Thuir Distant dog bark - 2025 03 14 00h01 - XY H2N" - Sadiquecat ([792440](https://freesound.org/people/Sadiquecat/sounds/792440/)) | distant dog at night (1-3 barks) | one bark, mono, 100 Hz high-pass | 11 KiB |
| `trainhorn.ogg` | "Train Horn 71 (Distant, Clean).mp3" - FunWithSound ([591873](https://freesound.org/people/FunWithSound/sounds/591873/)) | horn of the passing train | 12.8 s, mono, 80 Hz high-pass | 101 KiB |

Libraries: NAudio (MIT), NAudio.Vorbis (MIT), NVorbis (MIT).
