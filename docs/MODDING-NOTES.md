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

## A UI Factory Window must not be parented into Besiege's own UI

Docking a panel next to the chat window by making it a *child* of the chat
window is the obvious arrangement, and it is what note 19's "parenting to the
container gets show/hide for free" leads you to. For a plain button that is
right. For a UI Factory `Window` it is not.

`Window`'s scroll view clips with a stencil `Mask` on its `Viewport`, and so
does the chat window's own message list. uGUI assigns stencil bits by depth, so
two masks at the same depth under one parent get the same bit and cut holes in
each other. What that looks like in game is the **chat's own text drawing
outside its viewport and across the panel** — which reads as a transparency or
draw-order bug and is neither.

Parent a Window to `Besiege.UI.Make.ScreenCanvas` instead, which is what it is
for. Two things then have to be done by hand that came free before:

- **position**, from the anchor's world corners:

  ```csharp
  Vector3[] corners = new Vector3[4];
  anchor.GetWorldCorners(corners);            // 0 = bottom-left
  Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
  RectTransformUtility.ScreenPointToLocalPointInRectangle(
      parentRect, screen, camera, out local);
  ```

  with `camera` null for a `ScreenSpaceOverlay` canvas.

- **visibility**, mirrored from the anchor's `activeInHierarchy`.

Position it when the panel opens, **not** every frame: the `Window` prefab
carries a `Drag` on its top bar, and repositioning continuously drags it back
out of the player's hand.

The gear button beside it can stay a child of the chat window. It is an
`Icon Button` with no mask of its own, so it has nothing to conflict with, and
it keeps the free show-and-hide.

## A UI Factory Window is translucent, so it must fit its contents

`Window`'s background is an `Image` at alpha 0.39 with a `Blur` child at alpha
0.40 over it, and the blur really does show the game through the panel — that
is the frosted look it is for.

The consequence is easy to miss: **any part of the window your rows do not
reach is a pane of blurred scenery.** With a fixed window height and a shorter
list of rows, the empty band at the bottom shows a blurred copy of whatever is
behind it — the chat input bar, the block toolbar — and reads as the panel
leaking other UI through itself. It is not a leak, a draw-order bug or a mask
problem; the window is simply bigger than what was put in it.

`VerticalLayoutGroup` plus `ContentSizeFitter` sizes the scroll view's
**content**, not the window, so the window has to be told:

```csharp
LayoutRebuilder.ForceRebuildLayoutImmediate(content);
float needed = LayoutUtility.GetPreferredHeight(content) + 50f;   // 50 = TopBar
windowRect.sizeDelta = new Vector2(width, Mathf.Clamp(needed, min, max));
```

Re-run it whenever the row count changes. With the window's pivot at the bottom
it then grows upwards and keeps its lower edge where it was put.

`BlurHandler` is worth knowing about while you are here: it assigns one shared
static material to the Image and enables or disables it every frame from
`BlurHandler.BlurActive`, which is Besiege's own graphics option. So "blur off"
is a state every UI Factory window already handles — turning the `Blur` child
off for one window is safe if you ever need to — but it is not the fix for
this, and it costs the panel its intended look.

## Speech next to instruments: a peak ceiling is the wrong control

This mod's speech sat far quieter than the Music mod's instrument blocks from
the same author, at settings that looked equivalent. The cause is crest factor,
not gain.

Speech runs a long way above its own average on plosives and sibilants -- a
factor of four or five is ordinary -- so normalising with a **peak ceiling**
lets one or two samples decide the level of the whole line, and everything else
sits well under it. A sustained instrument note has a far lower crest factor, so
the same ceiling leaves it much louder. Measured here: speech landed at about
0.09 RMS where the instruments run near full scale.

The fix is the one the Music mod already uses on every block: set the level from
**RMS** and round the peaks off with a soft knee, rather than scaling the whole
signal down until the loudest transient fits.

```csharp
// linear to 0.7, then a curve that approaches 1 and never reaches it
if (s > 0.7f)
    s = 0.7f + 0.3f * (1f - 1f / (1f + (s - 0.7f) * 3f));
```

Speech went from ~0.09 RMS to ~0.23 at the same peak, which is about 8 dB, with
nothing clipped -- the knee cannot produce a sample outside ±1 by construction,
so the peak test in the offline suite still passes.

Worth knowing about `Master.cs` in that mod too: sixty blocks each peaking near
one sum to a signal peaking near sixty, and no block can see the mix it is part
of. Its answer is for every block to report its own peak and read back one
shared gain, summed as **power** (`sqrt(sum of squares)`) rather than as peaks,
because separate notes are not in phase. A mod that adds one more voice to that
mix should either join that scheme or, as here, stay well inside its own
headroom.

## Give a UI Factory Window a canvas of your own

Three separate bugs here all had one root: the window was not on a full-screen
canvas of this mod's own.

**Do not parent it into Besiege's UI.** The Window prefab clips with a stencil
`Mask`, and so does the chat window's message list. Two masks at the same depth
under one parent share a stencil bit and cut holes in each other, which shows up
as Besiege's own text drawing across the panel.

**Do not wrap it in a bare `GameObject` either.** `new GameObject(name,
typeof(RectTransform))` has a **zero-sized** rect. Anything that measures the
window against its parent then measures against 0x0 — an on-screen clamp
concludes the window can never fit and pins it to a corner every frame, which
also makes it undraggable. Put the component on the Window itself.

**`Besiege.UI.Make.ScreenCanvas` is not the answer either**, even though it is
public and `Make.Prefab` falls back to it. It is UI Factory's own canvas, shared
with whatever else is on it, and it is assigned by `Besiege.UI.Mod` rather than
by `Make` — so its lifetime is not obviously yours.

Make one:

```csharp
canvas = go.AddComponent<Canvas>();
canvas.renderMode = RenderMode.ScreenSpaceOverlay;
canvas.sortingOrder = 2400;
CanvasScaler scaler = go.AddComponent<CanvasScaler>();
scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
scaler.referenceResolution = new Vector2(1920f, 1080f);
scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
scaler.matchWidthOrHeight = 1f;
go.AddComponent<GraphicRaycaster>();
```

That is what the Music mod does for its block panels, and it settles the stencil
question, the measuring question and the draw order in one. Put it on a
`DontDestroyOnLoad` object so it survives the scene change that takes the chat
window away.

**And clamp the window on screen.** The prefab's top bar is a `Drag` with no
bounds of its own, so the panel can be dragged off the edge and lost — there is
no way back if the only control is a button that toggles it.

## The Window's blur is a tooltip shader, and it misbehaves on a big window

The frosting on UI Factory's `Window` is an Image running Besiege's own
`Custom/TooltipBlur (Larger)`, which `Besiege.UI.Mod` finds by name and hands to
`BlurHandler`. The name is the warning: it is written for a **tooltip** — small,
short-lived, on Besiege's own canvas.

On a large window on a canvas of its own, with its own `sortingOrder`, what that
grab captures is not the composition the shader assumes. The result is a
displaced copy of other screen content: the chat window's buttons drawn inside
the panel, and pieces of the panel's own title drawn outside it. Everything
wrong in the picture is blurred and everything sharp is in its right place,
which is how to tell this apart from a layout or masking fault.

There is nothing to patch — the shader is the game's, and a mod cannot reach it.
Switch the blur off:

```csharp
Transform blur = window.transform.Find("Blur");
if (blur != null) blur.gameObject.SetActive(false);   // the object, not the Image
```

It must be the **GameObject**. `BlurHandler.Update` writes `image.enabled` every
frame from `BlurHandler.BlurActive`, so disabling the Image lasts one frame; an
inactive object stops the handler running at all. And this is not going around
the API: that same handler turns this Image off whenever the player switches
blur off in Besiege's graphics options, so a UI Factory window without it is a
state the game ships.

Then raise the window's own plate. It is alpha 0.39 and was drawn expecting the
frosting behind it, so on its own it leaves the panel unreadable over a bright
level.
