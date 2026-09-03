using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerTTS.Ui
{
    /// <summary>
    /// The options panel, built from UI Factory's prefabs so it looks like part
    /// of the game rather than like a mod's approximation of it.
    ///
    /// The whole panel is one UI Factory <c>Window</c>: it arrives with a drag
    /// bar, a title, a close button and a scroll view already wired, so the
    /// rows go into <c>ScrollRect.content</c> under a vertical layout group and
    /// the window sizes and scrolls itself. There is no layout arithmetic here
    /// at all, which is most of the reason for using it -- the hand-built
    /// version this replaced needed two hand-maintained constants and a runtime
    /// assertion that they still added up.
    ///
    /// Rows are built once and rebound as the lobby changes, rather than being
    /// torn down and rebuilt: a row holds a player's <b>name</b>, not a
    /// captured slider-to-player binding, and every caption is written on every
    /// refresh. Note 04 of the modding notes records what the other way round
    /// costs -- a reused row that kept pointing at whoever it was built for,
    /// silently writing one player's volume onto another.
    /// </summary>
    public class OptionsPanel : MonoBehaviour
    {
        public TtsManager Manager;

        public const float Width = 330f;

        /// <summary>The Window prefab's title bar, which the scroll view sits under.</summary>
        private const float TopBarHeight = 50f;

        /// <summary>
        /// Bounds on the window's height. Within these it is sized to fit its
        /// contents, because the Window prefab's background is translucent and
        /// its Blur shows the game through it: any part of the window the rows
        /// do not reach is a pane of blurred scenery, which reads as the panel
        /// leaking rather than as an empty panel.
        /// </summary>
        private const float MinHeight = 180f;
        private const float MaxHeight = 560f;

        /// <summary>
        /// How far left of the chat window the window's right edge sits, which
        /// reserves a column for the gear beside it.
        /// </summary>
        public const float RightOffset = 44f;

        private const float RowHeight = 30f;
        private const float LabelWidth = 116f;
        private const float FieldWidth = 52f;
        private const float MuteWidth = 46f;

        /// <summary>
        /// How many frames Besiege's hotkeys stay stopped after a value box
        /// loses focus. Two would do -- see <c>HoldHotkeys</c> for why one is
        /// not enough -- and three costs nothing.
        /// </summary>
        private const int HotkeyHoldFrames = 3;
        private const float PlayerNameWidth = 84f;
        private const float PlayerFieldWidth = 42f;
        private const float Pad = 10f;

        /// <summary>
        /// Lettering size on a toggle. The prefab's own is small for a row read
        /// at a glance; this matches the value boxes, as the Music mod's panels
        /// do.
        /// </summary>
        private const int ToggleFont = 16;

        /// <summary>
        /// How opaque the window's own plate is, standing in for the blur that
        /// is switched off above.
        /// </summary>
        private const float BackdropAlpha = 0.93f;

        private const float SpeedMin = 0.5f;
        private const float SpeedMax = 2.0f;

        // Range's own limits, and the coupling between the two distances behind
        // it, live in TtsSettings so this panel and `tts range` cannot drift
        // apart on either. See TtsSettings.SetRange.
        private const float RangeMin = TtsSettings.RangeMin;
        private const float RangeMax = TtsSettings.RangeMax;

        private RectTransform content;
        private RectTransform playersHeader;
        private RectTransform windowRect;

        /// <summary>
        /// The Window itself, which is what has to be positioned. The object
        /// this component lives on is only a container for it.
        /// </summary>
        public RectTransform Window { get { return windowRect; } }

        private Row master, own, speed, spatial, range;
        private Toggle enabledToggle;
        private Toggle teamToggle;

        private readonly List<PlayerRow> rows = new List<PlayerRow>();
        private readonly List<string> currentNames = new List<string>();
        private float nextPlayerScan;

        // Set while a control is written from code, so its change callback does
        // not treat it as the player moving the control and write the value
        // back -- which, with a clamp in between, is how a setting drifts a
        // little every time a panel opens.
        private bool binding;

        // The hotkey hold, and how many frames of it are left. See HoldHotkeys.
        private bool holdingHotkeys;
        private int hotkeyHold;

        private class Row
        {
            public RectTransform Root;
            public Text Label;
            public UnityEngine.UI.Slider Slider;
            public InputField Field;
        }

        private class PlayerRow
        {
            public Row Control;
            public GameObject Mute;
            public Text MuteCaption;
            public string PlayerName;
        }

        // ---------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------

        /// <summary>
        /// Spawn the window under <paramref name="parent"/> and put an
        /// OptionsPanel on it. Returns null if UI Factory will not give us the
        /// prefab.
        ///
        /// The component goes on the Window itself rather than on a wrapper
        /// object. A wrapper is the obvious arrangement and it broke both
        /// dragging and placement: a bare <c>new GameObject</c> has a
        /// zero-sized RectTransform, so everything that measured the window
        /// against its parent -- the on-screen clamp especially -- was
        /// measuring against a 0x0 rect, decided the window could never fit,
        /// and pinned it to a corner every frame.
        /// </summary>
        public static OptionsPanel Create(Transform parent, TtsManager manager)
        {
            GameObject window = UIF.Spawn(UIF.WindowPrefab, parent);
            if (window == null) return null;

            window.name = "MpTtsOptions";

            OptionsPanel panel = window.AddComponent<OptionsPanel>();
            panel.Manager = manager;
            if (!panel.Build(window)) { Destroy(window); return null; }
            return panel;
        }

        private bool Build(GameObject window)
        {
            // Do NOT add a Drag or a StopsZoomWhenHovered of our own: the
            // Window prefab already carries both.
            // Sized here; ChatDock.Follow does the positioning, because where
            // it goes depends on the chat window and this does not know about
            // that.
            windowRect = UIF.Rect(window);
            windowRect.sizeDelta = new Vector2(Width, MinHeight);

            // ---- the backdrop ---------------------------------------------
            //
            // The Window prefab's frosting is switched off here, and the plate
            // behind it made opaque instead.
            //
            // That frosting is an Image running Besiege's own
            // "Custom/TooltipBlur (Larger)" shader, which samples the frame
            // behind it. It is built for a tooltip: something small and
            // short-lived on Besiege's own canvas. On a large window on a
            // canvas of its own, with its own sorting order, what it grabs is
            // not the composition it assumes, and it draws a displaced copy of
            // other things on screen -- the chat's buttons inside the panel,
            // and pieces of the panel's own title outside it. The shader is the
            // game's, and a mod cannot patch it.
            //
            // Switching it off is not a workaround around the edge of the API:
            // BlurHandler already enables and disables exactly this Image every
            // frame from Besiege's own blur graphics option, so a UI Factory
            // window with no blur is a state the game ships. It has to be the
            // GameObject rather than the Image, because that same Update would
            // switch the Image straight back on.
            Transform blur = window.transform.Find("Blur");
            if (blur != null) blur.gameObject.SetActive(false);

            // With nothing frosted behind it the prefab's own near-transparent
            // plate would leave the panel unreadable over a bright level, so it
            // is the game's panel colour at an alpha that stands on its own.
            Image background = window.GetComponent<Image>();
            if (background != null)
            {
                Color panel = UIF.PanelBlack;
                panel.a = BackdropAlpha;
                background.color = panel;
            }

            Transform topBar = window.transform.Find("TopBar");
            if (topBar != null)
            {
                Transform titleObject = topBar.Find("Text");
                if (titleObject != null)
                {
                    Text title = titleObject.GetComponent<Text>();
                    if (title != null)
                    {
                        UIF.Untranslate(title);
                        title.text = "SPEECH";
                    }
                }

                Transform close = topBar.Find("CloseButton");
                if (close != null) UIF.OnClick(close.gameObject, Close);
            }

            ScrollRect scroll = window.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null || scroll.content == null)
            {
                Log.Warn("the UI Factory Window prefab has no scroll view, so "
                         + "the options panel cannot be laid out.");
                return false;
            }

            // Rows go into ScrollRect.content, not onto the window. Rows put on
            // the window leave the scroll view holding the prefab's own
            // 500-unit placeholder, which is taller than any panel -- so the
            // scrollbar sits there permanently beside an empty scroll area.
            content = scroll.content;

            VerticalLayoutGroup layout =
                content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter =
                content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildRows();
            FitToContent();

            gameObject.SetActive(false);
            return true;
        }

        private void BuildRows()
        {
            enabledToggle = BuildToggle("Read chat aloud", OnEnabledChanged);

            master = BuildRow("MASTER VOLUME", OnMasterChanged, OnMasterTyped);
            own = BuildRow("YOUR MESSAGES", OnOwnChanged, OnOwnTyped);

            playersHeader = BuildHeader("PLAYERS");

            teamToggle = BuildToggle("Only my team", OnTeamChanged);

            speed = BuildRow("SPEAKING RATE", OnSpeedChanged, OnSpeedTyped);
            spatial = BuildRow("3D POSITIONING", OnSpatialChanged, OnSpatialTyped);
            range = BuildRow("RANGE", OnRangeChanged, OnRangeTyped);

            Refresh();
            RefreshPlayers(true);
        }

        /// <summary>
        /// Shrink or grow the window to exactly what the rows need.
        ///
        /// The vertical layout group sizes the scroll view's *content*, not the
        /// window, so without this the window keeps whatever height it was
        /// given and the surplus shows the game through the prefab's
        /// translucent background and blur. Beyond MaxHeight the scroll view
        /// takes over, which is what it is there for.
        /// </summary>
        private void FitToContent()
        {
            if (content == null || windowRect == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            float needed = LayoutUtility.GetPreferredHeight(content) + TopBarHeight;
            float height = Mathf.Clamp(needed, MinHeight, MaxHeight);

            if (Mathf.Abs(windowRect.sizeDelta.y - height) < 0.5f) return;
            windowRect.sizeDelta = new Vector2(Width, height);
        }

        private RectTransform BuildHeader(string caption)
        {
            GameObject go = UIF.Spawn(UIF.TextPrefab, content);
            if (go == null) return null;

            Text text = UIF.Caption(go, caption);
            if (text != null) text.alignment = TextAnchor.MiddleLeft;

            RectTransform rect = UIF.Rect(go);
            AddHeight(rect, 22f);
            return rect;
        }

        private Toggle BuildToggle(string caption,
                                   UnityEngine.Events.UnityAction<bool> onChanged)
        {
            GameObject go = UIF.Spawn(UIF.TextToggle, content);
            if (go == null) return null;

            Text label = UIF.Caption(go, caption);
            AddHeight(UIF.Rect(go), RowHeight);

            // Styled the way the Music mod styles the toggles in its block
            // panels, so the two mods' menus match: the prefab's own swell off,
            // the lettering at the same size as the value boxes rather than the
            // prefab's smaller default, and only the lettering growing under
            // the pointer.
            UIF.NoSwell(go);
            if (label != null)
            {
                label.fontSize = ToggleFont;
                label.resizeTextForBestFit = false;
                UIF.EnsureFont(label);

                Swell swell = go.AddComponent<Swell>();
                swell.grows = label.transform;
                swell.grown = 1.15f;
            }

            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        /// <summary>
        /// A label, a slider and a typeable value box on one line: the shape
        /// every row in the panel has, whether it is a setting or a player.
        ///
        /// The slider is UI Factory's prefab, which is already the style
        /// Besiege uses in its own settings: a uniform track with no coloured
        /// fill, and a round handle. It always runs 0..1 and the caller maps
        /// that onto whatever the setting's own units are, so a row knows
        /// nothing about what it is showing.
        ///
        /// The two callers differ only in their widths and in what they hang
        /// off the result -- a player row also gets a mute button in the gap
        /// <paramref name="fieldInset"/> leaves for it.
        /// </summary>
        private Row NewRow(string caption, float labelWidth, float fieldInset,
                           float fieldWidth, float sliderRight, int digits)
        {
            Row row = new Row();

            GameObject holder = UIF.Spawn(UIF.Empty, content);
            if (holder == null) return row;
            row.Root = UIF.Rect(holder);
            AddHeight(row.Root, RowHeight);

            GameObject label = UIF.Spawn(UIF.TextPrefab, row.Root);
            row.Label = UIF.Caption(label, caption);
            if (row.Label != null) row.Label.alignment = TextAnchor.MiddleLeft;
            Left(UIF.Rect(label), 0f, labelWidth);

            GameObject field = UIF.Spawn(UIF.InputFieldPrefab, row.Root);
            row.Field = field != null ? field.GetComponent<InputField>() : null;
            SetUpField(row.Field, digits);
            Right(UIF.Rect(field), fieldInset, fieldWidth);

            GameObject slider = UIF.Spawn(UIF.SliderPrefab, row.Root);
            row.Slider = slider != null
                ? slider.GetComponent<UnityEngine.UI.Slider>() : null;
            if (row.Slider != null)
            {
                row.Slider.minValue = 0f;
                row.Slider.maxValue = 1f;
                row.Slider.wholeNumbers = false;
            }
            Between(UIF.Rect(slider), labelWidth + 8f, sliderRight);

            return row;
        }

        /// <summary>One of the fixed settings rows, bound to its two handlers.</summary>
        private Row BuildRow(string caption,
                             UnityEngine.Events.UnityAction<float> onChanged,
                             UnityEngine.Events.UnityAction<string> onTyped)
        {
            Row row = NewRow(caption, LabelWidth, 0f, FieldWidth,
                             FieldWidth + 8f, 5);

            if (row.Slider != null) row.Slider.onValueChanged.AddListener(onChanged);
            if (row.Field != null) row.Field.onEndEdit.AddListener(onTyped);
            return row;
        }

        /// <summary>
        /// Common setup for a value box.
        ///
        /// The Input Field prefab carries UI Factory's
        /// <c>StopsHotkeysWhenInputFieldFocused</c>, which is one of the better
        /// reasons to use its field rather than a hand-built one: without it,
        /// typing "100" into a box drives the camera and fires block keys.
        ///
        /// It is not quite enough on its own, because it gives the keyboard
        /// back one Update too early and Enter then reaches the chat window.
        /// See <c>HoldHotkeys</c>.
        /// </summary>
        private static void SetUpField(InputField field, int limit)
        {
            if (field == null) return;

            UIF.FixFont(field);
            UIF.Untranslate(field.placeholder as Text);

            field.contentType = InputField.ContentType.DecimalNumber;
            field.characterLimit = limit;

            Text inner = field.textComponent;
            if (inner != null) inner.alignment = TextAnchor.MiddleCenter;
        }

        // ---------------------------------------------------------------
        // Player rows
        // ---------------------------------------------------------------

        private void Update()
        {
            HoldHotkeys();

            if (Time.unscaledTime < nextPlayerScan) return;
            nextPlayerScan = Time.unscaledTime + 0.5f;
            RefreshPlayers(false);
        }

        /// <summary>
        /// Keep Besiege's hotkeys stopped for a few frames after a value box
        /// loses focus.
        ///
        /// Without this, pressing Enter in a value box shuts the chat window
        /// and takes this panel down with it. The chain is worth writing down,
        /// because nothing about it is a race that "usually" happens -- it
        /// fires every single time:
        ///
        /// <list type="number">
        /// <item><c>ChatView</c> closes on its toggle key, and the key is only
        ///   read when hotkeys are live: <c>InputManager.ToggleChat()</c>
        ///   returns false outright while <c>StatMaster.stopHotkeys</c> is
        ///   set. That check happens in <c>CanvasInputView.LateUpdate</c>.</item>
        /// <item>UI Factory's own <c>StopsHotkeysWhenInputFieldFocused</c>
        ///   holds the stop while a field has focus and releases it the moment
        ///   focus goes -- from <c>Update</c>.</item>
        /// <item>Enter deactivates the field. So on that one frame the release
        ///   runs in Update, and <c>ChatView</c> reads the key in LateUpdate,
        ///   by which time the guard is already gone.</item>
        /// </list>
        ///
        /// Update always precedes LateUpdate, so the release always lands
        /// before the check. Holding the stop a few frames longer closes the
        /// gap and costs nothing: hotkeys stay dead for about 50 ms after the
        /// player finishes typing, which is not perceptible.
        ///
        /// <c>StatMaster.StopHotKeys</c> is a counter, not a flag -- true
        /// increments and false decrements, and it logs "stopHotCounter &lt; 0!"
        /// if it goes negative -- so every hold here is matched by exactly one
        /// release, including the one in <see cref="OnDisable"/>. Holding it
        /// alongside UI Factory's own hold is fine and is the point of its
        /// being a counter.
        /// </summary>
        private void HoldHotkeys()
        {
            bool typing = AnyFieldFocused();

            if (typing) hotkeyHold = HotkeyHoldFrames;
            else if (hotkeyHold > 0) hotkeyHold--;

            bool want = typing || hotkeyHold > 0;
            if (want == holdingHotkeys) return;

            holdingHotkeys = want;
            StatMaster.StopHotKeys(want);
        }

        /// <summary>
        /// Give the hold back. Closing the panel disables this object and
        /// <see cref="Update"/> stops running with it, so without this the
        /// counter never comes back down and the player loses every hotkey in
        /// the game until they restart it.
        /// </summary>
        private void ReleaseHotkeys()
        {
            if (!holdingHotkeys) return;

            holdingHotkeys = false;
            hotkeyHold = 0;
            StatMaster.StopHotKeys(false);
        }

        private bool AnyFieldFocused()
        {
            if (Focused(master) || Focused(own) || Focused(speed)
                || Focused(spatial) || Focused(range)) return true;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && Focused(rows[i].Control)) return true;
            }
            return false;
        }

        private static bool Focused(Row row)
        {
            return row != null && row.Field != null && row.Field.isFocused;
        }

        /// <summary>
        /// Rebind the list to whoever is in the game now.
        ///
        /// This polls rather than subscribing to
        /// <c>Modding.Events.OnPlayerJoin</c> and <c>OnPlayerLeave</c>. Half a
        /// second of latency on a name appearing is imperceptible, and polling
        /// also covers what those two events do not name: a player changing
        /// their name, and the panel being opened when the lobby already has
        /// people in it.
        /// </summary>
        public void RefreshPlayers(bool force)
        {
            if (content == null) return;

            List<string> names = new List<string>();
            List<PlayerData> players = Playerlist.Players;
            PlayerData local = PlayerData.localPlayer;
            string localName = local != null ? local.name : null;

            if (players != null)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerData p = players[i];
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;
                    if (p.isLocalPlayer) continue;          // has its own row
                    if (names.Contains(p.name)) continue;
                    names.Add(p.name);
                }
            }

            // Anyone we have actually heard stays listed after they leave, so a
            // volume set mid-game does not vanish from under the hand setting it.
            if (Manager != null)
            {
                List<string> heard = Manager.KnownSpeakers();
                for (int i = 0; i < heard.Count; i++)
                {
                    if (string.IsNullOrEmpty(heard[i])) continue;
                    if (heard[i] == localName) continue;
                    if (names.Contains(heard[i])) continue;
                    names.Add(heard[i]);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);

            if (!force && SameAs(names)) { UpdateRowValues(); return; }

            currentNames.Clear();
            currentNames.AddRange(names);

            while (rows.Count < names.Count) rows.Add(NewPlayerRow());

            for (int i = 0; i < rows.Count; i++)
            {
                PlayerRow row = rows[i];
                bool used = i < names.Count;
                row.Control.Root.gameObject.SetActive(used);
                if (!used) continue;

                row.PlayerName = names[i];
                if (row.Control.Label != null)
                {
                    row.Control.Label.text = Shorten(names[i]);
                }
            }

            UpdateRowValues();
            FitToContent();
        }

        private bool SameAs(List<string> names)
        {
            if (names.Count != currentNames.Count) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] != currentNames[i]) return false;
            }
            return true;
        }

        private PlayerRow NewPlayerRow()
        {
            PlayerRow row = new PlayerRow();
            row.Control = NewRow("", PlayerNameWidth, MuteWidth + 6f,
                                 PlayerFieldWidth,
                                 MuteWidth + PlayerFieldWidth + 14f, 3);
            if (row.Control.Root == null) return row;

            // Keep the player rows together under the PLAYERS header rather
            // than at the end of the layout group, where they were spawned.
            if (playersHeader != null)
            {
                row.Control.Root.SetSiblingIndex(
                    playersHeader.GetSiblingIndex() + 1 + rows.Count);
            }

            row.Mute = UIF.Spawn(UIF.TextButton, row.Control.Root);
            row.MuteCaption = UIF.Caption(row.Mute, MuteWord(1f));
            Right(UIF.Rect(row.Mute), 0f, MuteWidth);

            // The row is captured, the player name is not: the handlers read
            // row.PlayerName at the moment they fire, so a rebound row writes to
            // whoever it is showing now rather than to whoever it was built for.
            PlayerRow captured = row;
            if (row.Control.Slider != null)
            {
                row.Control.Slider.onValueChanged.AddListener(delegate(float v)
                {
                    OnPlayerVolumeChanged(captured, v);
                });
            }
            if (row.Control.Field != null)
            {
                row.Control.Field.onEndEdit.AddListener(delegate(string t)
                {
                    OnPlayerVolumeTyped(captured, t);
                });
            }
            UIF.OnClick(row.Mute, delegate { OnMuteClicked(captured); });

            return row;
        }

        private void UpdateRowValues()
        {
            if (Manager == null || Manager.Settings == null) return;

            binding = true;
            for (int i = 0; i < rows.Count; i++)
            {
                PlayerRow row = rows[i];
                if (row.Control.Root == null) continue;
                if (!row.Control.Root.gameObject.activeSelf) continue;

                float v = Manager.Settings.GetPlayerVolume(row.PlayerName);
                if (row.Control.Slider != null) row.Control.Slider.value = v;
                SetField(row.Control.Field, v * 100f);
                if (row.MuteCaption != null) row.MuteCaption.text = MuteWord(v);
            }
            binding = false;
        }

        // ---------------------------------------------------------------
        // Values
        // ---------------------------------------------------------------

        /// <summary>
        /// Pull every control back into line with the settings. Called on open,
        /// so a change made from the console shows up here.
        /// </summary>
        public void Refresh()
        {
            if (Manager == null || Manager.Settings == null) return;
            TtsSettings s = Manager.Settings;

            binding = true;

            if (enabledToggle != null) enabledToggle.isOn = s.Enabled;
            if (teamToggle != null) teamToggle.isOn = s.SpeakTeamOnly;

            SetRow(master, s.Volume, s.Volume * 100f);
            SetRow(own, s.OwnVolume, s.OwnVolume * 100f);
            SetRow(speed, Mathf.InverseLerp(SpeedMin, SpeedMax, s.Speed),
                   s.Speed * 100f);
            SetRow(spatial, s.Spatialisation, s.Spatialisation * 100f);
            SetRow(range, Mathf.InverseLerp(RangeMin, RangeMax, s.MaxDistance),
                   s.MaxDistance);

            binding = false;

            UpdateRowValues();
        }

        private static void SetRow(Row row, float sliderValue, float shown)
        {
            if (row == null) return;
            if (row.Slider != null) row.Slider.value = Mathf.Clamp01(sliderValue);
            SetField(row.Field, shown);
        }

        /// <summary>
        /// Write a value box, unless it has focus. Writing to an InputField
        /// mid-edit moves the caret out from under whoever is typing.
        /// </summary>
        private static void SetField(InputField field, float shown)
        {
            if (field == null || field.isFocused) return;
            field.text = Mathf.RoundToInt(shown).ToString();
        }

        /// <summary>
        /// What the button beside a player does next. Silent is the only state
        /// worth a different word, and the threshold is the same one the mute
        /// button and the console both treat as off.
        /// </summary>
        private static string MuteWord(float volume)
        {
            return volume <= 0.0001f ? "unmute" : "mute";
        }

        private static bool ReadNumber(string text, out float value)
        {
            return float.TryParse(text,
                                  System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out value);
        }

        // ---------------------------------------------------------------
        // Handlers
        // ---------------------------------------------------------------

        private void OnEnabledChanged(bool on)
        {
            if (binding || Manager == null) return;
            Manager.SetEnabled(on);
        }

        private void OnTeamChanged(bool on)
        {
            if (binding || Manager == null) return;
            Manager.Settings.SpeakTeamOnly = on;
            Save();
        }

        private void OnMasterChanged(float v)
        {
            if (binding || Manager == null) return;
            Manager.Settings.Volume = v;
            SetField(master.Field, v * 100f);
            Save();
        }

        private void OnOwnChanged(float v)
        {
            if (binding || Manager == null) return;
            Manager.Settings.OwnVolume = v;
            SetField(own.Field, v * 100f);
            Save();
        }

        private void OnSpeedChanged(float v)
        {
            if (binding || Manager == null) return;
            float rate = Mathf.Lerp(SpeedMin, SpeedMax, v);
            Manager.Settings.Speed = rate;
            SetField(speed.Field, rate * 100f);
            Save();
        }

        private void OnSpatialChanged(float v)
        {
            if (binding || Manager == null) return;
            Manager.Settings.Spatialisation = v;
            SetField(spatial.Field, v * 100f);
            Save();
        }

        private void OnRangeChanged(float v)
        {
            if (binding || Manager == null) return;
            Manager.Settings.SetRange(Mathf.Lerp(RangeMin, RangeMax, v));
            SetField(range.Field, Manager.Settings.MaxDistance);
            Save();
        }

        // A typed number is clamped and then written back, so an out-of-range
        // entry visibly corrects itself rather than being silently ignored.

        private void OnMasterTyped(string text)
        {
            float v;
            if (ReadNumber(text, out v))
            {
                Manager.Settings.Volume = Mathf.Clamp01(v / 100f);
            }
            Commit();
        }

        private void OnOwnTyped(string text)
        {
            float v;
            if (ReadNumber(text, out v))
            {
                Manager.Settings.OwnVolume = Mathf.Clamp01(v / 100f);
            }
            Commit();
        }

        private void OnSpeedTyped(string text)
        {
            float v;
            if (ReadNumber(text, out v))
            {
                Manager.Settings.Speed = Mathf.Clamp(v / 100f, SpeedMin, SpeedMax);
            }
            Commit();
        }

        private void OnSpatialTyped(string text)
        {
            float v;
            if (ReadNumber(text, out v))
            {
                Manager.Settings.Spatialisation = Mathf.Clamp01(v / 100f);
            }
            Commit();
        }

        private void OnRangeTyped(string text)
        {
            float v;
            if (ReadNumber(text, out v)) Manager.Settings.SetRange(v);
            Commit();
        }

        private void Commit()
        {
            Refresh();
            if (Manager != null && Manager.Settings != null) Manager.Settings.Save();
        }

        private void OnPlayerVolumeChanged(PlayerRow row, float v)
        {
            if (binding || Manager == null || row.PlayerName == null) return;
            Manager.Settings.SetPlayerVolume(row.PlayerName, v);
            SetField(row.Control.Field, v * 100f);
            if (row.MuteCaption != null) row.MuteCaption.text = MuteWord(v);
            Save();
        }

        private void OnPlayerVolumeTyped(PlayerRow row, string text)
        {
            if (Manager == null || row.PlayerName == null) return;

            float v;
            if (ReadNumber(text, out v))
            {
                Manager.Settings.SetPlayerVolume(row.PlayerName,
                                                 Mathf.Clamp01(v / 100f));
                Manager.Settings.Save();
            }
            UpdateRowValues();
        }

        private void OnMuteClicked(PlayerRow row)
        {
            if (Manager == null || row.PlayerName == null) return;

            float current = Manager.Settings.GetPlayerVolume(row.PlayerName);
            Manager.Settings.SetPlayerVolume(row.PlayerName,
                                             current <= 0.0001f ? 1f : 0f);
            UpdateRowValues();
            Manager.Settings.Save();
        }

        // ---------------------------------------------------------------
        // Open, close, save
        // ---------------------------------------------------------------

        public void Open()
        {
            gameObject.SetActive(true);
            Refresh();
            RefreshPlayers(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        public bool IsOpen()
        {
            return gameObject.activeSelf;
        }

        /// <summary>
        /// Writing the settings file on every drag frame would be a file write
        /// per frame, so the save is deferred until the control settles.
        /// </summary>
        private float saveDue;

        private void Save()
        {
            saveDue = Time.unscaledTime + 0.4f;
        }

        private void LateUpdate()
        {
            if (saveDue <= 0f || Time.unscaledTime < saveDue) return;
            Flush();
        }

        /// <summary>
        /// Closing the panel disables this object, and neither Update nor
        /// LateUpdate runs on a disabled one -- so a slider moved and then
        /// immediately closed would leave its save pending forever, and the
        /// hotkey hold would never be given back.
        /// </summary>
        private void OnDisable()
        {
            ReleaseHotkeys();
            Flush();
        }

        /// <summary>
        /// The panel going away with the chat window is a destroy, not a
        /// disable, and an unbalanced hold would outlive it.
        /// </summary>
        private void OnDestroy()
        {
            ReleaseHotkeys();
        }

        private void Flush()
        {
            if (saveDue <= 0f) return;
            saveDue = 0f;
            if (Manager != null && Manager.Settings != null) Manager.Settings.Save();
        }

        // ---------------------------------------------------------------
        // Layout helpers
        // ---------------------------------------------------------------

        /// <summary>Fix a row's height inside the vertical layout group.</summary>
        private static void AddHeight(RectTransform rect, float height)
        {
            if (rect == null) return;
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void Left(RectTransform rect, float inset, float width)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(inset, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
        }

        private static void Right(RectTransform rect, float inset, float width)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-inset, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
        }

        private static void Between(RectTransform rect, float left, float right)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 22f);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0f);
        }

        /// <summary>
        /// Steam names can be far wider than the row. Truncating keeps the mute
        /// button where the eye expects it.
        /// </summary>
        private static string Shorten(string name)
        {
            if (name == null) return "";
            if (name.Length <= 12) return name;
            return name.Substring(0, 11) + "…";
        }
    }
}
