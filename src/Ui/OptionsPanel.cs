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
        public const float Height = 470f;

        /// <summary>
        /// How far left of the chat window the window's right edge sits, which
        /// reserves a column for the gear beside it.
        /// </summary>
        public const float RightOffset = 44f;

        private const float RowHeight = 30f;
        private const float LabelWidth = 116f;
        private const float FieldWidth = 52f;
        private const float MuteWidth = 46f;
        private const float PlayerNameWidth = 84f;
        private const float PlayerFieldWidth = 42f;
        private const float Pad = 10f;

        private const float SpeedMin = 0.5f;
        private const float SpeedMax = 2.0f;
        private const float RangeMin = 10f;
        private const float RangeMax = 300f;

        /// <summary>
        /// The distance at which a voice is still at full volume, as a fraction
        /// of the distance at which it becomes inaudible. Kept proportional so
        /// range is one control: the falloff keeps its shape and only its scale
        /// moves, which is what "range" means to the person dragging it.
        /// </summary>
        private const float ReferenceFraction = 8f / 90f;

        private RectTransform content;
        private RectTransform playersHeader;

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
        /// Build the window under <paramref name="parent"/>. Returns false if
        /// UI Factory would not give us its prefabs.
        /// </summary>
        public bool Build(Transform parent)
        {
            GameObject window = UIF.Spawn(UIF.WindowPrefab, parent);
            if (window == null) return false;

            window.transform.SetParent(transform, false);

            // Do NOT add a Drag or a StopsZoomWhenHovered of our own: the
            // Window prefab already carries both.
            RectTransform rect = UIF.Rect(window);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-RightOffset, 0f);
            rect.sizeDelta = new Vector2(Width, Height);

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

            UIF.Caption(go, caption);
            AddHeight(UIF.Rect(go), RowHeight);

            // The hover swell is right for a button and wrong for a full-width
            // row, so it is aimed at the checkmark instead of at the whole row.
            Transform check = go.transform.Find("Checkmark");
            if (check != null)
            {
                UIF.RetargetHover(go, check.GetComponent<RectTransform>());
            }

            Toggle toggle = go.GetComponent<Toggle>();
            if (toggle != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        /// <summary>
        /// A label, a slider and a typeable value box on one line.
        ///
        /// The slider is UI Factory's prefab, which is already the style
        /// Besiege uses in its own settings: a uniform track with no coloured
        /// fill, and a round handle.
        /// </summary>
        private Row BuildRow(string caption,
                             UnityEngine.Events.UnityAction<float> onChanged,
                             UnityEngine.Events.UnityAction<string> onTyped)
        {
            Row row = new Row();

            GameObject holder = UIF.Spawn(UIF.Empty, content);
            if (holder == null) return row;
            row.Root = UIF.Rect(holder);
            AddHeight(row.Root, RowHeight);

            GameObject label = UIF.Spawn(UIF.TextPrefab, row.Root);
            row.Label = UIF.Caption(label, caption);
            if (row.Label != null) row.Label.alignment = TextAnchor.MiddleLeft;
            Left(UIF.Rect(label), 0f, LabelWidth);

            GameObject field = UIF.Spawn(UIF.InputFieldPrefab, row.Root);
            row.Field = field != null ? field.GetComponent<InputField>() : null;
            SetUpField(row.Field, 5);
            if (row.Field != null) row.Field.onEndEdit.AddListener(onTyped);
            Right(UIF.Rect(field), 0f, FieldWidth);

            GameObject slider = UIF.Spawn(UIF.SliderPrefab, row.Root);
            row.Slider = slider != null
                ? slider.GetComponent<UnityEngine.UI.Slider>() : null;
            if (row.Slider != null)
            {
                row.Slider.minValue = 0f;
                row.Slider.maxValue = 1f;
                row.Slider.wholeNumbers = false;
                row.Slider.onValueChanged.AddListener(onChanged);
            }
            Between(UIF.Rect(slider), LabelWidth + 8f, FieldWidth + 8f);

            return row;
        }

        /// <summary>
        /// Common setup for a value box.
        ///
        /// Nothing here holds Besiege's keyboard: the Input Field prefab
        /// carries <c>StopsHotkeysWhenInputFieldFocused</c> already, which is
        /// one of the better reasons to use UI Factory's field rather than a
        /// hand-built one. Without that behaviour, typing "100" into a box
        /// drives the camera and fires block keys.
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
            if (Time.unscaledTime < nextPlayerScan) return;
            nextPlayerScan = Time.unscaledTime + 0.5f;
            RefreshPlayers(false);
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
            row.Control = new Row();

            GameObject holder = UIF.Spawn(UIF.Empty, content);
            if (holder == null) return row;

            row.Control.Root = UIF.Rect(holder);
            AddHeight(row.Control.Root, RowHeight);

            // Keep the player rows together under the PLAYERS header rather
            // than at the end of the layout group, where they were spawned.
            if (playersHeader != null)
            {
                row.Control.Root.SetSiblingIndex(
                    playersHeader.GetSiblingIndex() + 1 + rows.Count);
            }

            GameObject label = UIF.Spawn(UIF.TextPrefab, row.Control.Root);
            row.Control.Label = UIF.Caption(label, "");
            if (row.Control.Label != null)
            {
                row.Control.Label.alignment = TextAnchor.MiddleLeft;
            }
            Left(UIF.Rect(label), 0f, PlayerNameWidth);

            row.Mute = UIF.Spawn(UIF.TextButton, row.Control.Root);
            row.MuteCaption = UIF.Caption(row.Mute, "mute");
            Right(UIF.Rect(row.Mute), 0f, MuteWidth);

            GameObject field = UIF.Spawn(UIF.InputFieldPrefab, row.Control.Root);
            row.Control.Field = field != null ? field.GetComponent<InputField>() : null;
            SetUpField(row.Control.Field, 3);
            Right(UIF.Rect(field), MuteWidth + 6f, PlayerFieldWidth);

            GameObject slider = UIF.Spawn(UIF.SliderPrefab, row.Control.Root);
            row.Control.Slider = slider != null
                ? slider.GetComponent<UnityEngine.UI.Slider>() : null;
            if (row.Control.Slider != null)
            {
                row.Control.Slider.minValue = 0f;
                row.Control.Slider.maxValue = 1f;
                row.Control.Slider.wholeNumbers = false;
            }
            Between(UIF.Rect(slider), PlayerNameWidth + 8f,
                    MuteWidth + PlayerFieldWidth + 14f);

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
                if (row.MuteCaption != null)
                {
                    row.MuteCaption.text = v <= 0.0001f ? "unmute" : "mute";
                }
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

        /// <summary>
        /// Range applies to everyone -- every other player and your own
        /// messages alike. It is one setting rather than one per speaker
        /// because it describes the listener's own hearing, not any particular
        /// speaker's voice.
        /// </summary>
        private void OnRangeChanged(float v)
        {
            if (binding || Manager == null) return;
            SetRange(Mathf.Lerp(RangeMin, RangeMax, v));
            SetField(range.Field, Manager.Settings.MaxDistance);
            Save();
        }

        private void SetRange(float metres)
        {
            metres = Mathf.Clamp(metres, RangeMin, RangeMax);
            Manager.Settings.MaxDistance = metres;
            Manager.Settings.ReferenceDistance =
                Mathf.Max(1f, metres * ReferenceFraction);
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
            if (ReadNumber(text, out v)) SetRange(v);
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
            if (row.MuteCaption != null)
            {
                row.MuteCaption.text = v <= 0.0001f ? "unmute" : "mute";
            }
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
        /// immediately closed would leave its save pending forever.
        /// </summary>
        private void OnDisable()
        {
            Flush();
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
