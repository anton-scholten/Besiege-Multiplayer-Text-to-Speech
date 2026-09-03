# Working on this repository

Notes for anyone — human or AI — changing this mod. The [README](README.md) is
for people who just want to use it; nothing here needs repeating there.

What this mod established about Besiege that was not already written down is in
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md).

## Layout

The folder Besiege loads is `MultiplayerTTS/`, because that subfolder is the
whole of what gets uploaded to the Workshop. Everything beside it is not part of
the mod.

```
MultiplayerTTS/Mod.xml               manifest: assembly, resources
MultiplayerTTS/MultiplayerTTS.dll    built by tools/build.sh (checked in, the game loads it)
MultiplayerTTS/Resources/            gear icon, and the two images Mod.xml names
Thumbnail.png, Thumbnail.xcf         the Workshop thumbnail, drawn by hand, copied into Resources/
Background.jpg, TTS.png              what tools/make_icon.py composes the icon from
src/ModEntry.cs                      entry point, console commands
src/ChatWatcher.cs                   the log hook that is the only way to read chat
src/TtsManager.cs                    worker thread, voice assignment, core-block lookup
src/SpeechVoice.cs                   playback, hand-rolled pan and distance gain
src/TtsSettings.cs                   persisted settings
src/VoiceBank.cs                     per-player voices from a hash of the name
src/Klatt/                           the synthesiser; free of Unity and of Besiege
src/Ui/                              the options panel and its dock to the chat window
tools/build.sh                       compiles with Besiege's own compiler, and checks
tools/verify-build.sh                the check to run after editing any .cs
tools/install.sh                     builds and installs into the game
tools/run-tests.sh                   the offline pipeline checks
tools/say.sh                         render a phrase to a WAV without launching the game
docs/                                notes; not loaded by anything
```

Both images carry the same white page-and-speaker mark, `TTS.png`, over the
same `Background.jpg`, and they differ in exactly one thing: the thumbnail has
the mod's name lettered across it and the icon does not. At 256px a mod name is
a smear, and Besiege's mods list is a row of small squares.

`tools/make_icon.py` composes the icon from those two files -- crop, vignette,
glyph, rim, rounded corners, the same recipe and the same constants the Git
View mod uses, so the tiles sit together in the list. The thumbnail is
drawn by hand in `Thumbnail.xcf` and copied into `Resources/`; `make_icon.py`
deliberately does not write it, having once overwritten it.

`MultiplayerTTS/MultiplayerTTS.dll` is committed on purpose. `Mod.xml` names it
as an `<Assembly>`, so a checkout has to carry a built one or the mod does not
load.

`<ID>` is a GUID the game writes into `Mod.xml` the first time it loads the mod.
With a symlink install that write lands in this working copy, which is what you
want — commit it, and never change it afterwards.

## Hard rules

- **Everything under `src/Klatt/` stays free of Unity and of Besiege.** That is
  what lets the whole text-to-audio path be tested and tuned offline in about a
  second, and it is why `tools/say.sh` can exist at all.
- **`src/Ui/UIF.cs` is the only file that may mention `Besiege.UI`.** A type
  that will not resolve fails when the method mentioning it is compiled, so
  confining every mention to one class means a single guarded call decides
  whether the panel can be built. That is what makes UI Factory a *soft*
  dependency rather than a hard one.
- **Run `./tools/verify-build.sh` after editing any `.cs`.** It compiles with
  Besiege's own `mcs.dll` through its own embedded Mono, so a compile failure
  here is a compile failure in the game, and it leaves the shipped DLL alone.
- **Never add a namespace the loader blacklists.** `tools/build.sh` runs the
  same check `InternalModding.Assemblies.AssemblyScanner` does; a rejected
  assembly produces no error the player can see, the mod simply never appears.
- **Measure before changing a synthesiser constant.** Nearly every number in
  `src/Klatt/` is either published or measured off a recording, and the comment
  beside it says which. Replacing one with a guess is how the voice table ended
  up wrong the first time.

## The compiler is C# 4 and old

No interpolated strings, no `?.`, no `nameof`. Any `enum` declaration
**segfaults it** — which is why the phoneme classes in
[Phonemes.cs](src/Klatt/Phonemes.cs) are `const int`. `UnityEngine.UI.Slider`
has to be fully qualified, because Besiege has a global `Slider` of its own.

## Why not DECtalk itself

Worth stating plainly so nobody spends a weekend on it. DECtalk and
DECtalk-mini are native code, there are exactly two ways to reach native code
from C#, and the mod loader refuses both:

| Route | What blocks it |
| --- | --- |
| `[DllImport]` on `libdectalk` | `AssemblyScanner` has a **dedicated** P/Invoke check — *"You are not allowed to use PInvoke!"* — on top of `System.Runtime.InteropServices` being blacklisted |
| Launching `say.exe` as a process | `System.Diagnostics` is blacklisted, with only `Stopwatch` exempted |

Two other routes exist and were rejected on their own merits. **A local HTTP TTS
server**: `UnityEngine.Networking.UnityWebRequest` is the one network API the
blacklist leaves alone, so a mod really can POST to a localhost daemon wrapping
real DECtalk — and then every player who wants to hear anything must install and
run a server, which for a Workshop mod is no mod at all. **A diphone bank**:
render ~1500 diphones offline with the real thing and concatenate at runtime;
genuinely DECtalk audio, but it needs a large asset payload and PSOLA
re-pitching before it has any prosody, and it is choppy at the joins.

Synthesising from formants avoids all of it: no assets, no dependencies, no
network, full control of pitch and rate, and about 350× realtime.

## Where the DECtalk numbers came from

The source is public: [dectalk/463](https://github.com/dectalk/463) is DECtalk
4.63's source, [dectalk/dectalk](https://github.com/dectalk/dectalk) builds it,
and [dectalk.github.io/dectalk](https://dectalk.github.io/dectalk/toclist.htm)
is the reference manual. Moonbase Alpha runs DECtalk 5.0.

The 4.63 share is missing the prosody module, so the speaker tables are not in
it; they come from the manual instead. Two things about them:

- **`ap` is not the pitch you measure.** It is where the baseline lands.
  `f0' = ap + (f0 - 120) * pr / 100`, so Harry's published `ap` of 89 renders
  around 105 Hz with his `pr` of 80. Reading a measured peak as `ap` is what put
  six of the nine voices wrong the first time round. See `KlattSynth.FillPitch`.
- **The 5.01 user guide and the SDK docs disagree** on Paul: `ap` 112 against
  122, `sm` 30 against 3, `sr` 25 against 32. Genuine version drift. The SDK
  table is used here because it is complete and self-consistent across all nine;
  a recording of Paul *speaking* rather than singing would settle it.

`[:nv]` is not a tenth voice. Val is the user-defined slot `[:dv save]` writes
into, and starts as a copy of Paul; the tenth built-in is Chris, which has no
shorthand. `NUMSPEAKERS` in `hlsynapi.h` names all ten.

## Known divergences from DECtalk

- **`[:comma N]` and `[:period N]` emit a pause here; DECtalk *sets* the pause
  length used at the next comma or full stop.** The markup is rare enough in
  copypastas that this has not been worth the state, but it is a real
  difference and not a rounding one.
- **Stress is positional, not lexical.** The hat pattern and the stress rises
  are placed on the first and last vowel of each run of text, because there is
  no stress dictionary here. DECtalk places them from the lexical stress
  pattern and the syntactic structure of the sentence.
- **One clause per message.** DECtalk breaks a sentence into clauses at
  punctuation and clause-introducing words and gives each its own hat.

## Things that will bite

**The chat message is already formatted for display when it arrives.** The
sender builds it in `PerformPlayerChat` as `"<color=#{0}>{1}:  </color>{2}"`, so
stripping the rich-text tags leaves `Name:` in front of every line and the voice
reads out who is speaking before what they said. Nothing in
`HandleSayMessage(PlayerData, string)` suggests this — it takes the sender as a
separate argument. `TextNormaliser.WithoutSpeaker` takes it back off.

**Splitting `source=<name> <message>` is not a split on the first space.**
Plenty of Steam names contain one. Known player names are matched against the
front of the string, longest first.

**`OnAudioFilterRead` runs downstream of Unity's 3D panner.** A filter that
writes the buffer throws away everything the 3D stage did — the symptom is a
voice heard at one volume, dead centre, from anywhere on the map, with
`spatialBlend = 1` set and looking correct. So the source is left **2D** and
`SpeechVoice` computes distance gain and pan itself, on the game thread, handing
them to the audio thread in `volatile` floats.

**The core block has to be re-resolved every frame.** A simulation runs on a
*clone* of the machine, rebuilt from scratch on every run, so a `Transform`
captured while building is destroyed the moment the player hits go.

**`ModResource.AllResourcesLoaded` resolves the *calling* assembly.** Asking it
from this mod reports whether *our* icon has loaded, which has nothing to do
with UI Factory. It cost a session of the panel never appearing, because a
mistyped texture path left our own resources permanently unloaded and the panel
was gated on it. What actually gates the prefabs is `Make.OnReady`.

**`<Texture path="...">` in `Mod.xml` resolves against `Resources/`,** not the
mod root. `Resources/icon.png` becomes `Resources/Resources/icon.png`.

**A `--` inside an XML comment** breaks the manifest. `tools/check-manifest.py`
catches it.

**`StatMaster.StopHotKeys` is a counter, not a flag.** True increments, false
decrements, and it logs `stopHotCounter < 0!` if it underflows. Every hold needs
exactly one release, including from `OnDisable` and `OnDestroy` — a stranded
hold kills every hotkey in the game until restart.

**Enter in a value box used to close the chat window,** every time. UI Factory's
`StopsHotkeysWhenInputFieldFocused` releases the hotkey stop from `Update` the
moment focus goes, and `ChatView` reads its toggle key in `LateUpdate`, which
always runs later. `OptionsPanel.HoldHotkeys` keeps the stop up for three more
frames to close the gap.

**A UI Factory `Text` carries a `Translator`** that will put the prefab's own
wording back at the next language change, so it comes off any label the mod
writes into. And an `Input Field`'s `Text` and placeholder come out with **no
font**; a `Text` with no font draws nothing, which reads as a box that swallows
typing rather than one that failed to paint.

**The gear is a child of `ChatViewContainer`.** `CanvasInputView.IsVisible` is
literally `viewContainer.activeSelf`, so parenting to that container gets the
show/hide behaviour with no code — and takes the panel down with it.

## Testing

```sh
./tools/run-tests.sh                            # 123 offline checks, no game launch
./tools/say.sh "hello world" out.wav            # render a phrase and listen
./tools/say.sh "gg wp" out.wav 22050 98 1.1     # rate, pitch, speed
./tools/make_icon.py --preview                  # redraw the mod icon
```

`tools/say.sh` prints the parsed markup as well as the audio stats, which is
usually enough to see what a copypasta is actually doing before listening to it.

The build needs UI Factory's assemblies to compile against. `build.sh` finds
them under the Workshop folder — note **`steamapps/workshop`**, not
`steamapps/common`, which cost a session of wrongly concluding it was not
installed — or set `UIFACTORY_DIR`.

## Style

Comments say *why*, not *what*. A constant that was measured says what it was
measured against and what the wrong value did; that is what stops the next
person replacing it with a rounder number. Match the density of the file you are
in rather than the density of this list.
