# Modding notes from this mod

Findings from building this that are not in
[Besiege-Modding-AI-notes](../../Besiege-Modding-AI-notes) yet, in the style of
that repository: what was established, and how.

All checked against Besiege 5.4.0f3, September 2026, with
`peek.sh` from the notes repository.

## Native code is unreachable, and the loader says so specifically

The namespace blacklist in note 01 already rules out `System.Diagnostics`
(process launch) and `System.Runtime.InteropServices` (`DllImport`). What is
not in the note is that `InternalModding.Assemblies.AssemblyScanner` carries a
**separate, dedicated P/Invoke check** with its own message:

```
"You are not allowed to use PInvoke!"
```

Found by dumping the scanner's string literals:

```sh
./tools/peek.sh dump InternalModding.Assemblies.AssemblyScanner | grep ldstr
```

So a P/Invoke is refused on its own terms, not merely as a side effect of the
namespace test. This matters whenever a mod's obvious implementation is "wrap
the existing native library": there is no way in, and no partial way in either.
The practical consequence for this mod was that DECtalk, eSpeak, Festival, SAPI
and every other real speech engine are all equally unavailable, and the voice
had to be synthesised in managed code.

Worth building into a build script — `tools/BlacklistCheck.cs` here does the
namespace test, the exemption list, the four forbidden methods **and**
`MethodDefinition.IsPInvokeImpl`, and reports the declaring method for each.

## Chat: `HandleSayMessage` is the funnel, and it logs

`ChatController.HandleSayMessage(PlayerData, string)` is the single point every
received multiplayer chat message reaches. Both the team path and the global
path in `HandleSayCommand` converge on it, and the host's own messages echo
through it too.

It is **private**, `Modding.Events` has no chat event, and `System.Reflection`
is blacklisted, so it cannot be patched. But it opens with an unconditional

```csharp
Debug.Log("[ChatController] HandleSayMessage source=" + source.name + " " + message);
```

which `Application.logMessageReceived` delivers for free. That is currently the
only way for a mod to observe chat.

Two things to know if you use it:

- **The name/message separator is a single space, and names contain spaces.**
  Splitting on the first space is wrong for a great many Steam names. Match the
  names in `Playerlist.Players` against the front of the string, longest first
  — which also hands back the `PlayerData`.
- **Do not log anything matching your own parser.** The callback sees your own
  `Debug.Log` calls too.

The display path, for reference, is
`ICanvasInputView.AddTextEntry(string)` on `CanvasInputView`, whose message
list (`FixedSizedQueue textEntries`) is private. Polling the instantiated text
entry `GameObject`s in the scroll view is the reflection-free alternative to
the log, and strictly worse: it sees the formatted string with rich-text
markup and no `PlayerData`.

## Finding another player's core block

`PlayerData.machine` is a `ServerMachine`, which extends `Machine`. Both are
public, as is everything needed:

```csharp
List<BlockBehaviour> blocks = machine.isSimulating
    ? machine.SimulationBlocks       // the clone's blocks
    : machine.BuildingBlocks;
// starting block is BlockID == 0, checked against BlockType.StartingBlock
```

`Machine.FirstBlock` is a reasonable fallback and handles the sim/building
switch itself, but it is literally "element 0 of the current list" — the
starting block for a machine built the usual way, and not guaranteed. Scanning
for `BlockID == 0` is exact.

`(int)BlockType.StartingBlock == 0`, established by a probe compile rather than
assumed from its position in the enum:

```csharp
Console.WriteLine((int)BlockType.StartingBlock);   // 0
```

**Re-resolve it every frame.** A simulation runs on a clone rebuilt from
scratch each run (note 08), so a `Transform` cached while building is destroyed
the instant the player starts a simulation. This is the multiplayer instance of
the same trap note 08 describes for a block's own behaviours.

`GameObject.Find("StartingBlock")` — which `SmoothLookAtMachine` uses — is
useless here: it finds one object globally, and in multiplayer there are as
many starting blocks as there are players.

## Pre-rendering sidesteps note 07's latency trap

Note 07 lays out three ways to place generated audio and warns that the
streaming-`AudioClip` route sounds correct and is fatally late. There is a
fourth case it does not cover, because a block that synthesises live cannot use
it: **when the audio is known in advance, render it all up front.**

Speech is exactly that — the whole utterance exists the moment the message
arrives. So:

- synthesise into a `float[]` on a worker thread (a maximum-length chat message
  is ~35 ms of work, enough to be a visible hitch on the game thread);
- hand the array to the audio thread by a single `volatile` reference
  assignment, which needs no lock on the callback;
- `OnAudioFilterRead` then only copies and scales, and still applies its own
  pan and distance gain per note 07, because the 2D-source requirement is
  unchanged.

The result has none of the streaming route's latency and none of the live
route's cost.

## `Modding.ModConsole.RegisterCommand` takes a plain method group

Note 06 lists the delegate type as unconfirmed. It is `CommandHandler`, a
`void(string[])` delegate, and a method group converts to it implicitly:

```csharp
Modding.ModConsole.RegisterCommand("tts", OnCommand, "help text");
private static void OnCommand(string[] args) { ... }
```

Help text may not be empty and the command name may not contain a colon; both
throw. It also resolves the calling assembly against the manifest the same way
`ModIO` does, so a helper DLL cannot register commands.

## Docking a panel to the chat window

The chat window is ordinary uGUI, unlike the block mapper, so a mod can put its
own controls next to it. Three things make it easy and one makes it awkward.

**The window is a named child, and the name is readable from the scene.**
`ChatView`'s parts are private serialised fields — wired in the Unity editor,
not looked up at runtime — so with `System.Reflection` blacklisted none of them
can be read. But the hierarchy is stable and its names are in `level14`:

```sh
strings -t d -n 3 Besiege_Data/level14 | grep -i chat
```

```
ChatViewContainer
  Scroll View / Viewport / Content    (t_TextEntry is the message row template)
  InputBar / InputParent / InputField, ChatMode, InviteFriend, Close
```

`ChatView` itself is on an always-active object — its `LateUpdate` has to run —
so `FindObjectOfType<ChatView>()` finds it, and `ChatViewContainer` is then a
`GetComponentsInChildren<RectTransform>(true)` away. **Pass `true`**: the
container is inactive whenever the chat is closed, which is most of the time
and certainly the moment a mod first goes looking.

**Parenting to the container gets show/hide for free.**
`CanvasInputView.IsVisible` is literally `viewContainer.activeSelf`, so the
container is the object Besiege toggles as the chat opens and closes. A child
of it follows automatically, with no visibility code and nothing to keep in
sync.

**The look can be sampled rather than reproduced.** Note 04 is right that
Besiege's own UI cannot be borrowed — but that is about the *mapper*, which is
mesh UI whose materials need `InternalModding`. The chat window is uGUI and it
is on screen, so `GetComponentsInChildren<Text>(true)` yields a real `Font` and
`GetComponentsInChildren<Image>(true)` a real background colour. The font is
the one that matters: a `Text` with no font draws nothing at all, which reads
as a panel that failed to paint rather than one that failed to find a typeface.

**The awkward part: anything outside the container's rect can be clipped.** A
gear docked to the *left* of the chat window is outside it, so a `Mask` or
`RectMask2D` anywhere in the parent chain crops it away with no error and no
symptom. Nothing in the chat hierarchy is expected to clip — the only mask is
inside the message scroll view's viewport — but it is worth walking up to the
`Canvas` and logging the culprit if one is found, because otherwise the failure
looks exactly like a mod that did not load.

### Two uGUI traps this hit

**`Slider` and `Scrollbar` must be written out in full.** Note 01 lists the
four global names Besiege declares that collide with Unity's, and building a
settings panel walks straight into two of them. `UnityEngine.UI.Slider`, every
time; a bare `Slider` resolves to Besiege's type and fails with an error about
a missing `value`, against `Assembly-CSharp.dll`, which reads like anything but
a name collision.

**Build the panel's background onto the object the component lives on.** The
first version here created the panel as a *child* of the object it toggled, so
`SetActive(false)` hid an empty parent and left the panel on screen. Making the
component's own object be the panel removes the possibility.

## The manifest's required elements are five, and not the five you would guess

A manifest missing a required element is refused, and the mod does not appear:

```
[Mods] ModInfo (at line 1, column 2 in Mod.xml) must contain MultiplayerCompatible element!
[Mods] There was an error loading the mod manifest: .../Mod.xml
[Mods] Not loading MultiplayerTTS
```

Note 01 covers the same rule for **block** modules — a member without a
`[DefaultValue]` is mandatory — and `InternalModding.Common.Serialization.Validate`
is the same code applying it to `InternalModding.Mods.ModInfo`. So the required
set is every `[XmlElement]` property of `ModInfo` without a `[DefaultValue]`:

```
REQUIRED   Name  Author  Version  Description  MultiplayerCompatible
optional   Debug  Icon  WorkshopThumbnail  LoadOrder  LoadInTitleScreen
           Resources  ID
```

Two things worth knowing about that list.

**`MultiplayerCompatible` is required, and nothing suggests it.** It reads like
a declaration a single-player mod could leave out, and it is the one element a
hand-written manifest is most likely to miss.

**`Assemblies` is *not* required** — a blocks-only mod needs none — so a mod
whose entire content is an assembly can have an empty or absent `<Assemblies>`,
load without complaint, and do nothing at all. Worth a separate check of your
own; the loader will not make it for you.

`Debug` is optional, which is easy to get wrong in the other direction: every
shipped mod has one, so inferring the required set from what other mods happen
to contain marks it mandatory. Read the attributes instead — about twenty lines
of Cecil over `ModInfo`'s properties, looking for `XmlElementAttribute` without
`DefaultValueAttribute`:

```csharp
foreach (PropertyDefinition p in modInfoType.Properties)
    foreach (CustomAttribute c in p.CustomAttributes)
        // XmlElementAttribute + no DefaultValueAttribute => required
```

That is the same technique as the throwaway compile in note 06, applied to
attributes rather than to signatures, and it is the difference between a
build-time check that is correct and one that is merely superstitious.

## UI Factory: what is actually inside the prefabs

Note 04 lists the twenty-one prefab names UI Factory registers, and stops
there. Their internal structure matters as soon as you want to reach a part of
one, and it is readable in a second with the notes repo's own tool:

```sh
python3 tools/unbundle.py <UIFactory>/Resources/besiege-ui-prefabs Window
```

The four this mod's panel is built from:

```
Window          [Image, StopsZoomWhenHovered]        300x400
  Blur          [Image, BlurHandler]
  TopBar        [Image, Drag]                        50 tall, anchored to the top
    Text        [Text, LetterSpacing, Translator]
    CloseButton [Button, ScaleAnimation] -> Text
  ScrollView    [ScrollRect, Image]                  anchored below the TopBar
    Viewport    [Mask, Image] -> Content             Content is 500 tall in the prefab
    Scrollbar Horizontal / Vertical

Slider          [Slider]                             200x32
  Handle Slide Area [Image]                          <- this is the track
    Handle      [ScaleAnimation]
      Vis       [Image]                              <- the round knob

InputField      [Image, InputField, StopsHotkeysWhenInputFieldFocused]   85x30
  Placeholder   [Text, Translator, LetterSpacing]
  Text          [Text, LetterSpacing]

NormalToggle    [Toggle, ScaleAnimation]             167x38
  Background / Checkmark [Image]
  Text          [Text, LetterSpacing, Translator]
```

Three things worth taking from that.

**The Slider has no fill rect.** Its track is one image across the whole
control and the knob rides over it, which is exactly Besiege's own slider look
— a uniform track with no coloured portion to the left of the handle. A
hand-built uGUI slider gets a `fillRect` by default and looks foreign
immediately; leaving `fillRect` null is what matches the game.

**The InputField already holds Besiege's keyboard.** It carries
`StopsHotkeysWhenInputFieldFocused`, so none of the `StatMaster.SetInMenu`
counting that note 08 describes is needed — which also removes the chance of
getting the count wrong and leaving the game believing a menu is open. This is
the single best reason to use UI Factory's field rather than build one.

**`Window` removes the layout arithmetic, not just the styling.** It arrives
with a drag bar, a title, a close button and a working scroll view, so rows go
into `ScrollRect.content` under a `VerticalLayoutGroup` + `ContentSizeFitter`
and the panel sizes itself. The hand-built panel this replaced needed two
hand-maintained constants for the fixed height above and below a list, plus a
runtime assertion that they still added up.

### The XML docs ship with it

`Besiege.UI.xml` and `Besiege.UI.Bridge.xml` sit next to the DLLs in the
Workshop folder. They are ordinary C# doc comments and they answer most API
questions faster than reading IL — `Make.OnReady` is documented there as "call
a function when all resources are loaded... use this to safely load prefabs and
sprites", which is exactly the gate note 04 says to use.

### Finding it on disk

UI Factory is a Workshop subscription, so it is **not** in
`Besiege_Data/Mods/`. It lives at

```
steamapps/workshop/content/346010/2913469777/UIFactory/
```

Searching only the `Mods` folder, or only under `steamapps/common`, concludes
it is not installed when it is — which then argues for building the whole UI by
hand for no reason. The notes repo's `peek.sh -- UIFactory` already looks in
the right place; a build script needs to be told.

## The chat message is display-formatted before it is sent

Worth stating on its own, because it is invisible from the signature.
`ChatController.HandleSayMessage(PlayerData source, string message)` looks like
it hands you the sender and the message separately. It does not: the sender
built `message` in `PerformPlayerChat` as

```csharp
String.Format("<color=#{0}>{1}:  </color>{2}", teamColour, player.name, text)
```

so the string already carries the speaker's name and the colour tags around it.
A mod that reads chat and strips rich text is then left with `Name:  ` on the
front of every line — which for a text-to-speech mod means reading out who is
talking before what they said.

Two consequences:

- strip the name deliberately, matching the known sender against the front of
  the string, with the literal `:  ` (colon, **two** spaces) as the fallback
  for a sender that could not be resolved;
- do **not** blanket-strip `<...>` from the body. Only the name's tags are
  Besiege's; angle brackets later in the line belong to the player. That
  matters if you support DECtalk markup, where `[dh<300,10>]` carries a note's
  duration and pitch — a blanket strip silently removes every one of them.
