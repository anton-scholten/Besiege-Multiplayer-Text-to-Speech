# Besiege Multiplayer Text to Speech

Multiplayer chat, read aloud out of the speaker's own machine, in
[Besiege](https://store.steampowered.com/app/346010/Besiege/).

Everyone gets their own voice, derived from their name, so you can tell who is
talking without looking at the chat window. The voice is a Klatt formant
synthesiser written from scratch — the same design DECtalk is built on — so it
lands on that flat, faintly robotic delivery on purpose, and it speaks
Moonbase Alpha's markup because that markup *is* DECtalk's.

**[UI Factory](https://steamcommunity.com/sharedfiles/filedetails/?id=2913469777)**
(Workshop item `2913469777`) is what the options panel is built from. Without it
the mod still loads and still reads chat aloud; every setting stays reachable
from the `tts` console command.

## Install

Either subscribe to the mod on Steam, or if you don't use Steam you can clone
the repo then:

```sh
./tools/install.sh              # symlink into Besiege_Data/Mods
./tools/install.sh --copy       # copy instead
./tools/install.sh --uninstall
```

Set `BESIEGE_DIR` if your install isn't found automatically. Start Besiege,
enable **Multiplayer Text to Speech** in the mods menu, and join or host a game.
Besiege reads its mod folders once at startup, so restart the game after
installing; later rebuilds need no reinstall. No C# toolchain is needed, the
build uses Besiege's own compiler.

## Using it

Speech starts working on its own in any multiplayer game. A gear sits at the
bottom-left of the chat window whenever the chat is open, and opens the panel.

| Setting | Range | What it does |
| --- | --- | --- |
| **Read chat aloud** | on / off | everything at once |
| **Master volume** | 0–100 | all speech |
| **Your messages** | 0–100 | your own lines read back to you; 0 is off, and the default |
| **Players** | 0–100 each | one volume and one mute per person, following joins and leaves |
| **Only my team** | on / off | restrict speech to your own team |
| **Speaking rate** | 50–200 | percent |
| **3D positioning** | 0–100 | 0 is flat, always-audible speech |
| **Range** | 10–300 m | how far a voice carries |

Every box takes a typed number as well as the slider, and one outside the range
is clamped and written back rather than ignored. Volumes are keyed by name, so a
mute outlives the lobby, and anyone you have actually heard stays on the list
after they leave.

## Moonbase Alpha markup

Moonbase Alpha's text-to-speech *was* DECtalk, so its famous syntax is DECtalk's
own — and this mod speaks it. Type it straight into chat:

```
[:dial 6387657]The birth parents you are trying to call do not love you,
please hang up[:t 350,500][:t 1,500][:t 350,500]
```

| Markup | What it does |
| --- | --- |
| `[:t 350,500]` | a pure tone: frequency, then milliseconds |
| `[:t 1,500]` | a rest — anything under 20 Hz is silence |
| `[:dial 6387657]` | the DTMF touch tones for a phone number |
| `[dh<300,10>ax<200,12>]` | **singing**: each phoneme with `<milliseconds, note>` |
| `[_<1,29>]words` | put ordinary words on a note instead of spelling them out |
| `[:rate 300]` | words per minute; 200 is DECtalk's own default |
| `[:pitch N]`, `[:volume set N]` | pitch and loudness |
| `[:np]`, `[:nb]`, `[:nh]` … | the voices — paul, betty, harry, frank, dennis, ursula, rita, wendy, kit, val |
| `[:dv ap 90 pr 0]` | the voice designer: 19 options, same names DECtalk uses |
| `[:comma N]`, `[:period N]` | a pause of N ms |

Notes are 1–37, C2 to C5, the same numbers DECtalk's tone table uses. A message
can change speaker part way through, which is the whole point of the format:

```
[:nh]why? [:nv]cuz you are john madden![:np]
```

The parser is not entered unless there is a bracket pair, so a message with no
markup takes the path it always did. Try it without anyone else online:

```
tts say [:dial 911]this is a test[:t 350,500]
```

## The console

Everything the panel does is also under `tts` (open the console with `` ` ``,
and `show_logs true` shows the mod's own logging):

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

Settings are saved in
`Besiege_Data/Mods/Data/MultiplayerTextToSpeech_<mod-id>/settings.txt`.

## How close is it to DECtalk?

Close on the things that are written down, and measured off recordings for the
things that are not.

- **The nine voices** are DECtalk's published speaker definitions — average
  pitch, pitch range, head size, breathiness, the higher formants.
- **The intonation** is DECtalk's own model: a baseline falling at 16 Hz per
  second, a hat pattern across the stressed syllables, a rise on each stress, a
  terminal fall, and a larynx lagging behind all of it. DECtalk's own worked
  example, `[:nh][:dv ap 90 pr 0] I am a robot.`, comes out monotone at 89.9 Hz.
- **Tone and note timing** were measured sample by sample: every `[:t]` tone is
  followed by 64.58 ms of silence, which is what makes a tune a row of notes
  rather than one long slide.

It is a different synthesiser, so it does not match sample for sample and is not
trying to. Commands it does not implement (`[:say]`, `[:mode]`, `[:punct]`,
`[:sync]`, `[:pronounce]`) are skipped rather than spoken.

## Notes

There is no chat event in Besiege and `System.Reflection` is blacklisted, so
this reads chat out of the log line `ChatController.HandleSayMessage` writes for
every message it receives. `tts status` says whether that hook has ever fired,
which is the thing to check first if speech is silent.

Real DECtalk cannot be used here at all — it is native code, and the mod loader
refuses both P/Invoke and `System.Diagnostics`.

Runtime behaviour hasn't been fully confirmed yet — if something misbehaves, the
details land in `Player.log` and in the in-game console with `show_logs true`.

AI agent? see [AGENTS.md](AGENTS.md) for layout, build, and any relevant info.
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md) has what this mod had to work out
about Besiege's modding API — including reading chat with no chat event, and
placing audio downstream of Unity's 3D panner. The general notes, for a mod that
is not this one, are collected in
[Besiege-Modding-AI-notes](https://github.com/anton-scholten/Besiege-Modding-AI-notes).

## Credits

Formant targets from Klatt's 1980 tables; letter-to-sound from the NRL rules
(Elovitz et al., NRL Report 7948, 1976). Voice parameters and the tone table
from DECtalk's own documentation.

## Licence

GPL-3.0. Besiege is Spiderling Studios'; nothing of theirs is redistributed
here.
