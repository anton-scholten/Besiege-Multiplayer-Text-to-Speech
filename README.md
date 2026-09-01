# Besiege Multiplayer Text to Speech

Reads multiplayer chat aloud, positioned at the speaking player's core block.
Each player gets their own voice, derived from their name, so you can tell who
is talking without looking at the chat window.

The voice is a **Klatt formant synthesiser** written from scratch in C#. That
is the same design DECtalk is built on — DECtalk descends directly from Dennis
Klatt's KlattTalk — so it lands on that flat, confident, faintly robotic
delivery on purpose rather than by accident.

## Why not DECtalk itself

Because a Besiege mod cannot load it, and this is worth stating plainly so
nobody spends a weekend trying.

DECtalk and DECtalk-mini are native code. There are exactly two ways to reach
native code from C#, and Besiege's mod loader refuses both:

| Route | What blocks it |
| --- | --- |
| `[DllImport]` on `libdectalk` | `InternalModding.Assemblies.AssemblyScanner` has a **dedicated** P/Invoke check that rejects the assembly outright — *"You are not allowed to use PInvoke!"* — on top of `System.Runtime.InteropServices` being on the namespace blacklist |
| Launching `say.exe` as a process | `System.Diagnostics` is blacklisted, with only `Stopwatch` exempted |

A rejected assembly does not produce an error the player can see. The mod
simply never appears, with the reason only in `Player.log`. `tools/build.sh`
runs the same checks the loader does, so this fails at build time instead —
see [tools/BlacklistCheck.cs](tools/BlacklistCheck.cs).

Two other routes exist and were rejected for reasons of their own:

- **A local HTTP TTS server.** `UnityEngine.Networking.UnityWebRequest` is the
  one network API the blacklist leaves alone, so a mod really can POST text to
  a localhost daemon wrapping real DECtalk. It works, and it means every player
  who wants to hear anything must install and run a server — which for a
  Workshop mod is no mod at all.
- **A DECtalk diphone bank.** Render ~1500 diphones offline with the real
  thing, ship the WAVs, concatenate at runtime. Genuinely DECtalk audio, but it
  needs a large asset payload and PSOLA re-pitching before it has any prosody
  at all, and it is choppy at the joins without a lot more work.

Synthesising from formants avoids all of it: no assets, no dependencies, no
network, full control of pitch and rate, and about 350× realtime.

## Installing

```sh
./tools/install.sh             # build, check, and symlink into Besiege_Data/Mods
./tools/install.sh --copy      # copy instead, for handing it to someone
./tools/install.sh --uninstall
```

`install.sh` builds first, so a mod the loader would refuse is never installed.
Set `BESIEGE_DIR` if the game is not found automatically.

Besiege reads its mod folders **once at startup**, so restart the game. The
symlink means later rebuilds need no reinstall — and it is the mode to develop
in for a second reason, below.

`<ID>` is a GUID the game writes into `Mod.xml` the first time it loads the
mod. With a symlink that write lands in this working copy, which is what you
want — commit it, and never change it afterwards.

## Using it

Speech starts working on its own in any multiplayer game.

### The options panel

A gear sits at the bottom-left of the chat window, just outside it, whenever
the chat is open. Clicking it opens a **UI Factory** window that grows upward
from the same corner — label on the left, slider, and a value box on the right
that can be typed into. Values are plain numbers, no unit:

- **Read chat aloud** — everything on or off;
- **Master volume** — all speech, 0–100;
- **Your messages** — your own lines read back to you. Zero, the default,
  means they are not spoken. It is a volume rather than a toggle because
  hearing yourself at everyone else's level is the thing nobody wants;
- **Players** — a scrolling list with a volume and a mute button for each
  person in the game, which follows people joining and leaving. Anyone you
  have actually heard stays on the list after they leave, so a volume set
  mid-game does not vanish from under the hand setting it;
- **Only my team** — restrict speech to your own team;
- **Speaking rate** (50–200) and **3D positioning** (0–100). Put 3D
  positioning at zero for flat, always-audible speech;
- **Range** (10–300 m) — how far a voice carries, for every speaker and for
  your own messages alike. It is one setting rather than one per player
  because it describes your own hearing, not any particular speaker's voice.
  The full-volume distance moves with it, so the shape of the falloff stays
  the same and only its scale changes.

Every value box takes typed input as well as the slider. A number outside the
range is clamped and written back, so it corrects itself visibly rather than
being silently ignored. While a box has focus the mod raises
`StatMaster.inMenu`, or the digits being typed would drive the camera and fire
block keys.

Per-player volumes are keyed by name, not network id, so a mute outlives the
lobby.

The gear appears and disappears with the chat window because it is a child of
it: `CanvasInputView.IsVisible` is literally `viewContainer.activeSelf`, so
parenting to that container gets the show/hide behaviour with no code.

### UI Factory is a soft dependency

The panel is built from [UI Factory 3](https://gitlab.com/dagriefaa/ui-factory-3)
(Workshop item `2913469777`), which ships Besiege's widgets as uGUI prefabs with
the game's own colours baked in — so the panel *is* the game's look rather than
a reproduction of it. Without UI Factory installed the mod still loads, still
reads chat aloud, and simply has no panel; every setting stays reachable from
the `tts` console command, and the mod says so once in the log.

That works because every mention of `Besiege.UI` lives in one file,
[UIF.cs](src/Ui/UIF.cs). A type that will not resolve fails when the method
mentioning it is compiled, so confining the mentions to one class means a
single guarded call decides whether the panel can exist. Two details in it: the
availability check caches only an affirmative answer, because UI Factory loads
its bundle a moment after the mod does and a single early ask answers "no"
wrongly; and construction is gated on `Make.OnReady`, because `Make.Prefab`
throws if the resources are not loaded yet.

Three things the prefabs give us that the hand-built panel had to do itself:
the `Window` arrives with a drag bar, title, close button and scroll view
already wired, so there is no layout arithmetic left in the panel at all; the
`Slider` is already the uniform-track, round-handle style Besiege uses; and the
`Input Field` carries `StopsHotkeysWhenInputFieldFocused`, so typing a number
no longer drives the camera and fires block keys.

Two traps it brings with it, both from note 04: every UI Factory `Text` carries
a `Translator` that will put the prefab's own wording back at the next language
change, so it comes off any label the mod writes into; and an `Input Field`'s
`Text` and placeholder come out with **no font**, and a `Text` with no font
draws nothing — a box that looks like it swallows typing rather than one that
failed to paint.

### The console

Everything the panel does is also under the `tts` console command (open the
console with `` ` `` and use `show_logs true` to see the mod's own logging):

```
tts                       show current settings
tts on | off              enable or disable speech
tts volume <0-100>        overall speech volume
tts speed <50-200>        speaking rate, percent
tts player <0-100> <name> one player's volume; 0 mutes
tts mute <name>           silence one player
tts unmute <name>         and put them back
tts own <0-100>           volume of your own messages; 0 is off
tts team on | off         only speak your own team
tts spatial <0-100>       how positional the voice is; 0 is flat 2D
tts range <metres>        how far speech carries (or <ref> <max> for both)
tts say <text>            hear a line in your own voice
tts test <name> <text>    hear a line in someone's voice
tts panel                 open or close the options panel
tts stop                  cut everything off now
tts status                is the chat hook working?
```

Settings persist to `Besiege_Data/Mods/Data/MultiplayerTextToSpeech_<guid>/settings.txt`.

## Moonbase Alpha markup

Moonbase Alpha's text-to-speech *was* DECtalk, so its famous syntax is
DECtalk's own inline command syntax — and this mod speaks it:

```
[:dial 6387657]The birth parents you are trying to call do not love you,
please hang up[:t 350,500][:t 1,500][:t 350,500]
```

| Markup | What it does |
| --- | --- |
| `[:t 350,500]`, `[:tone 350 500]` | a pure tone: frequency, then milliseconds |
| `[:t 1,500]` | a rest — anything under 20 Hz is silence, which is how these messages write pauses |
| `[:dial 6387657]` | the DTMF touch tones for a phone number |
| `[:phoneme arpabet speak on]` then `[dh<300,10>ax<200,12>]` | **singing**: each phoneme with `<duration in ms, pitch>` |
| `[:rate 300]` | speaking rate in words per minute; 200 is unchanged |
| `[:pitch N]`, `[:volume set N]` | pitch and loudness |
| `[:name paul]`, `[:nb]`, `[:nh]` … | the named voices — paul, betty, harry, frank, dennis, ursula, rita, wendy, kit, val |
| `[:comma N]`, `[:period N]` | a pause of N ms |

Everything outside brackets is ordinary text and takes exactly the path it
always did, so a message with no markup is unaffected — the parser is not even
entered unless the message contains a bracket pair.

The singing syntax fits this synthesiser unusually well, because duration and
pitch are already explicit per-segment tracks in it: a note's length and
frequency are written straight into the parameter arrays rather than being
approximated on top of a fixed contour.

**What is not exact.** DECtalk's pitch numbers index a table this code does not
have, so they are read as semitones — a tune comes out in tune with itself and
transposed as a whole. The named voices are approximations of pitch, apparent
speaker size and breathiness, not the original parameter sets. Commands that
are not implemented (`[:say]`, `[:mode]`, `[:punct]`, `[:index]`, `[:sync]`,
`[:pronounce]`) are **skipped rather than spoken**, which is what keeps a
message readable when it uses one.

Three caps stop a message holding the voice open: 4 s per tone, 4 s per
phoneme, and 20 s per message. A tone is also held out of the loudness
normalisation and written at a fixed level — a telephone tone has no correct
loudness relative to a sentence, and levelling the two together made the tone's
volume depend on how sibilant the neighbouring words happened to be.

Try it without anyone else online:

```
tts say [:dial 911]this is a test[:t 350,500]
```

## How it works

```
ChatController.HandleSayMessage        the one funnel every received message
  │                                     passes through, team and global alike
  ├─ logs "[ChatController] HandleSayMessage source=<name> <message>"
  ▼
ChatWatcher          Application.logMessageReceived, parsed back to a PlayerData
  ▼
DecTalk              [: commands], [phoneme<dur,pitch>] blocks   (only if present)
  ▼
TextNormaliser       rich text, numbers, chat shorthand, repeated letters
  ▼
LetterToSound        NRL rules (Elovitz et al. 1976) + an exception lexicon
  ▼
KlattSynth           cascade formants + parallel frication  [worker thread]
  ▼
SpeechVoice          own pan and distance gain              [audio thread]
  ▼
positioned at PlayerData.machine's starting block
```

### Reading chat through the log

There is no chat event. `HandleSayMessage` is private, `Modding.Events` exposes
nothing for chat, and `System.Reflection` is blacklisted — so Harmony and every
other patching approach is unavailable. What is available is that the method
logs every message it receives, unconditionally, before doing anything else.
`Application.logMessageReceived` reads that for free.

The cost is a dependency on a private diagnostic string. `tts status` reports
whether the hook has ever fired, so a Besiege update that changes the format
produces a clear answer instead of silence. [ChatWatcher.cs](src/ChatWatcher.cs)
is the only file that would need changing.

Splitting `source=<name> <message>` is not a split on the first space — plenty
of Steam names contain one. The known player names are matched against the
front of the string, longest first, which resolves it exactly and hands back
the `PlayerData` the audio needs to be positioned from.

**The message is already formatted for display when it arrives.** The sender
builds it in `PerformPlayerChat` as `"<color=#{0}>{1}:  </color>{2}"` — name,
then text — so stripping the rich-text tags leaves `Name:` in front of every
line, and the voice reads out who is speaking before what they said. Nothing in
`HandleSayMessage(PlayerData, string)` suggests this: it takes the sender as a
separate argument and looks for all the world as though the string were just
the message. `TextNormaliser.WithoutSpeaker` takes it back off.

### Docking to the chat window

`ChatView`'s parts are private serialised fields wired in the Unity editor, and
`System.Reflection` is blacklisted, so none of them can be read. The way in is
the hierarchy: `ChatView` sits on an always-active object, and the window it
toggles is a child called `ChatViewContainer` — a name read out of the
multiplayer scene, where the chat is

```
ChatViewContainer
  Scroll View / Viewport / Content    (t_TextEntry is the message row template)
  InputBar / InputParent / InputField, ChatMode, InviteFriend, Close
```

The panel's own look is *sampled* rather than reproduced: the font and
background colour are read off the live chat window. Note 04 of the modding
notes is right that Besiege's interface cannot be borrowed — the block mapper
is mesh UI and its materials are unreachable — but the chat window is ordinary
uGUI sitting right there, and a font read off it tracks the game through a
reskin for free. The font matters most: a uGUI `Text` with no font draws
nothing at all, which reads as a panel that failed to paint.

### Positioning

The audio is placed by hand, not by Unity. `OnAudioFilterRead` is inserted into
an `AudioSource`'s chain *downstream* of the 3D panner, so a filter that writes
the buffer — which is what playing your own samples means — throws away
everything the 3D stage did. The symptom is a voice heard at one volume, dead
centre, from anywhere on the map, with `spatialBlend = 1` set and looking
correct.

So the source is left **2D** and [SpeechVoice.cs](src/SpeechVoice.cs) computes
the distance gain and stereo pan itself, on the game thread, handing them to
the audio thread in `volatile` floats and sliding onto them across each buffer.

The core block is re-resolved every frame rather than cached, because a
simulation runs on a **clone** of the machine that is rebuilt from scratch on
every run — a `Transform` captured while building is destroyed the moment the
player hits go.

## Working on it

```sh
./tools/run-tests.sh               # 55 pipeline checks, no game launch
./tools/verify-build.sh            # compile only, leaves the shipped DLL alone
./tools/build.sh                   # build + blacklist + manifest checks
./tools/install.sh                 # build, then symlink into the game
./tools/say.sh "hello world" out.wav
./tools/say.sh "gg wp" out.wav 22050 98 1.1     # rate, pitch, speed
./tools/make_icon.py --preview p.png   # redraw the mod icon and look at it
```

The build needs UI Factory's assemblies to compile against — `build.sh` finds
them under the Workshop folder, or set `UIFACTORY_DIR`. It fails with an
explanation rather than a missing-namespace error if they are absent.

Everything under [src/Klatt/](src/Klatt/) is free of Unity and of Besiege on
purpose, so the whole text-to-audio path can be tested and tuned offline in
about a second. `tools/say.sh` writes a WAV you can listen to.

The build uses Besiege's own `mcs.dll` through its own embedded Mono, so a
compile failure here is a compile failure in game. It is **C# 4 and old**: no
interpolated strings, no `?.`, no `nameof`, and any `enum` declaration
segfaults it — which is why the phoneme classes in
[Phonemes.cs](src/Klatt/Phonemes.cs) are `const int`.

## Where the voice comes from

- Formant targets: Klatt's 1980 tables for an adult male tract.
- Letter-to-sound: the NRL rules (Elovitz et al., NRL Report 7948, 1976), the
  same family the small DECtalk-era systems used, plus an exception lexicon
  for the words the rules get wrong — including `machine`, which the rules
  render as `M AE CH IH N` and which is probably this game's most common noun.
- Per-player voices vary pitch, tract length, rate and breathiness within a
  narrow range, derived from an FNV-1a hash of the player's name. Deriving it
  means everyone computes the same voice for the same person with nothing
  synchronised or stored.

## Credits and licence

GPL-3.0, as the repository was set up. Nothing of Spiderling Studios' is
redistributed here.

The modding facts this depends on came from
[Besiege-Modding-AI-notes](../Besiege-Modding-AI-notes), in particular note 07
on audio, note 08 on the block lifecycle, and note 01 on the loader blacklist.
