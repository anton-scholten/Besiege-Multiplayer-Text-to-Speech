using System;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerTTS.Ui
{
    /// <summary>
    /// Finds Besiege's chat window and hangs the gear button — and the options
    /// panel behind it — off its bottom-left corner.
    ///
    /// <b>Finding the window.</b> <c>ChatView</c>'s parts are private
    /// serialised fields, wired in the Unity editor rather than looked up by
    /// name at runtime, and <c>System.Reflection</c> is blacklisted, so none
    /// of them can be read. What can be done is walk the hierarchy:
    /// <c>ChatView</c> itself sits on an always-active object (its
    /// <c>LateUpdate</c> has to run), and the window it toggles is a child
    /// named <c>ChatViewContainer</c>. That name was read out of the
    /// multiplayer scene, where the chat hierarchy is
    ///
    ///     ChatViewContainer
    ///       Scroll View / Viewport / Content   (t_TextEntry is the row template)
    ///       InputBar / InputParent / InputField, ChatMode, InviteFriend, Close
    ///
    /// <b>Why parent to the container.</b> <c>CanvasInputView.IsVisible</c> is
    /// literally <c>viewContainer.activeSelf</c>, so the container is what
    /// gets switched on and off as the chat opens. Making the gear a child of
    /// it means the gear appears and disappears with the chat window for free,
    /// with no visibility code and nothing to keep in sync.
    ///
    /// <b>UI Factory.</b> Both the gear and the panel are UI Factory prefabs,
    /// so this waits for UI Factory to be present and ready before building
    /// anything. Without it the mod is fully functional except that there is
    /// no panel; every setting is still reachable from the <c>tts</c> console
    /// command.
    /// </summary>
    public class ChatDock : MonoBehaviour
    {
        // GearInset + GearSize must stay within OptionsPanel.RightOffset, which
        // is the column the panel leaves clear for this button; otherwise the
        // panel opens underneath it and puts a slider under a button. 8 + 32
        // against 44 leaves 4px. There is no way to assert that in this
        // compiler -- both are constants, so any runtime check is dead code the
        // compiler warns about -- so it is written down here instead.
        private const float GearSize = 32f;
        private const float GearInset = 8f;

        /// <summary>
        /// Sort order for this mod's canvas, and the reference it scales
        /// against. Both match the Music mod's block panels, so the two mods'
        /// windows sit at the same size and in the same layer.
        /// </summary>
        private const int CanvasOrder = 2400;
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        public TtsManager Manager;

        private Canvas canvas;
        private ChatView chatView;
        private RectTransform container;
        private GameObject gear;
        private OptionsPanel panel;
        private Sprite gearIcon;

        private float nextScan;
        private bool loggedDock;
        private bool warnedMissing;

        private void Update()
        {
            // Rebuild if the chat window has gone -- it is recreated with the
            // multiplayer scene, which takes our children with it.
            if (container != null && gear != null) return;

            if (Time.unscaledTime < nextScan) return;

            // While UI Factory is genuinely absent, each availability check
            // costs a caught TypeLoadException, so this asks on a timer rather
            // than every frame.
            nextScan = Time.unscaledTime + 2f;

            TryDock();
        }

        private void TryDock()
        {
            if (!UIF.Available)
            {
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    Log.Info("UI Factory is not loaded (yet). Without it there is "
                             + "no options panel; 'tts' in the console reaches "
                             + "every setting either way.");
                }
                return;
            }

            if (chatView == null)
            {
                chatView = FindObjectOfType<ChatView>();
                if (chatView == null) return;
            }

            RectTransform found = FindContainer(chatView.transform);
            if (found == null)
            {
                Log.Warn("found the chat view but no ChatViewContainer under it; "
                         + "the options gear cannot be docked. The chat hierarchy "
                         + "has probably changed -- see ChatDock.cs.");
                chatView = null;
                nextScan = Time.unscaledTime + 30f;
                return;
            }

            container = found;
            WarnIfClipped(container);

            // Make.Prefab throws if UI Factory's resources are not loaded, so
            // construction is gated on its own readiness callback rather than
            // on any guess about when the game is ready.
            RectTransform target = container;
            if (!UIF.WhenReady(delegate { Build(target); }))
            {
                container = null;
                nextScan = Time.unscaledTime + 10f;
            }
        }

        /// <summary>
        /// The gear and the panel sit <em>outside</em> the chat window's own
        /// rect, to its left. That is fine unless something in the parent
        /// chain clips to its rect, in which case both are cropped away
        /// entirely and the mod looks like it simply did not load.
        ///
        /// Nothing in the chat hierarchy is expected to clip -- the only mask
        /// in it is inside the message scroll view's viewport, well below us --
        /// but this is the one failure here that produces no symptom at all,
        /// so it is worth naming the culprit if it ever appears.
        /// </summary>
        private static void WarnIfClipped(RectTransform from)
        {
            Transform t = from;
            while (t != null)
            {
                if (t.GetComponent<Mask>() != null || t.GetComponent<RectMask2D>() != null)
                {
                    Log.Warn("'" + t.name + "' clips its children, and the options "
                             + "gear sits outside the chat window's rect, so it may "
                             + "be invisible. Parent the gear to the canvas instead "
                             + "-- see WarnIfClipped in ChatDock.cs.");
                    return;
                }
                if (t.GetComponent<Canvas>() != null) return;   // reached the top
                t = t.parent;
            }
        }

        /// <summary>
        /// The chat window under a <c>ChatView</c>. Inactive children are
        /// included, because the container is switched off whenever the chat
        /// is closed — which is most of the time, and certainly the moment we
        /// first go looking.
        /// </summary>
        private static RectTransform FindContainer(Transform root)
        {
            RectTransform[] all = root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "ChatViewContainer") return all[i];
            }
            return null;
        }

        /// <summary>
        /// This mod's own full-screen canvas, made once and kept.
        ///
        /// It lives on a child of the manager, which is DontDestroyOnLoad, so
        /// it survives the scene changes that take the chat window away.
        /// </summary>
        private bool EnsureCanvas()
        {
            if (canvas != null) return true;

            GameObject go = new GameObject("MpTtsCanvas");
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            go.AddComponent<GraphicRaycaster>();
            return true;
        }

        private void Build(RectTransform parent)
        {
            if (parent == null || gear != null) return;

            // ---- the panel, hidden until the gear is clicked --------------
            //
            // On a canvas of this mod's own, never inside Besiege's chat
            // window. Two separate reasons, both of which cost a round of
            // debugging:
            //
            //   * the Window prefab clips with a stencil Mask and so does the
            //     chat's message list; two of those at the same depth under one
            //     parent share a stencil bit and cut holes in each other, which
            //     shows up as the chat's own text drawing across the panel;
            //   * everything that measures the window -- placement, and the
            //     on-screen clamp especially -- needs a full-screen parent rect
            //     to measure against.
            //
            // Make.ScreenCanvas would do for the first but is UI Factory's, and
            // shared. The Music mod makes its own canvas for its block panels
            // for the same reasons; this does the same.
            if (!EnsureCanvas()) return;

            panel = OptionsPanel.Create(canvas.transform, Manager);
            if (panel == null)
            {
                Log.Warn("the options panel could not be built from UI Factory's "
                         + "prefabs; carrying on without it.");
                return;
            }

            // ---- the gear -------------------------------------------------
            // Bottom-left: outside the chat window's left edge, level with its
            // bottom. The panel grows upward from the same corner, so the
            // button and the thing it opens share an edge.
            gear = UIF.Spawn(UIF.IconButton, parent);
            if (gear == null)
            {
                Log.Warn("could not spawn the gear button.");
                return;
            }
            gear.name = "MpTtsGear";

            RectTransform rect = UIF.Rect(gear);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-GearInset, GearInset);
            rect.sizeDelta = new Vector2(GearSize, GearSize);

            SetGearIcon(gear);
            UIF.OnClick(gear, Toggle);

            if (!loggedDock)
            {
                loggedDock = true;
                // Report what was measured, not just what was decided. A panel
                // in the wrong place is otherwise a guessing game, and this
                // one has been guessed at twice.
                RectTransform canvasRect = canvas.transform as RectTransform;
                Log.Info("gear docked to the chat window at " + Describe(parent)
                         + "; panel on '" + canvas.name + "' at "
                         + Describe(canvasRect)
                         + ", window " + Describe(panel.Window) + ".");
            }
        }

        private void SetGearIcon(GameObject button)
        {
            if (gearIcon == null) gearIcon = LoadGear();
            if (gearIcon == null) return;

            // The Icon Button prefab's artwork lives on a child called "Icon".
            Transform icon = button.transform.Find("Icon");
            if (icon == null) return;

            Image image = icon.GetComponent<Image>();
            if (image == null) return;

            image.sprite = gearIcon;
            image.preserveAspect = true;
        }

        /// <summary>
        /// Hide the panel and the gear when the chat window closes.
        ///
        /// That came free when the panel was a child of the chat window, and
        /// stopped once it had to live on UI Factory's canvas to keep its
        /// stencil mask out of Besiege's.
        /// </summary>
        private void LateUpdate()
        {
            if (panel == null || container == null) return;

            bool chatOpen = container.gameObject.activeInHierarchy;
            if (!chatOpen)
            {
                if (panel.IsOpen()) panel.Close();
                if (gear != null && gear.activeSelf) gear.SetActive(false);
                return;
            }
            if (gear != null && !gear.activeSelf) gear.SetActive(true);

            if (panel.IsOpen()) ClampToScreen();
        }

        /// <summary>
        /// Keep the window inside its canvas.
        ///
        /// The Window prefab's top bar is a <c>Drag</c>, and nothing in it
        /// stops the player dragging the panel off the edge of the screen and
        /// losing it -- there is no way to get it back short of restarting,
        /// because the gear only toggles it and does not move it.
        ///
        /// This runs every frame rather than only while dragging, which costs
        /// nothing: when the window is already inside, every clamp is a
        /// no-op and the position is written back unchanged. Only pushing it
        /// out moves it, so it does not fight an ordinary drag.
        /// </summary>
        private void ClampToScreen()
        {
            RectTransform rect = panel.Window;
            if (rect == null) return;

            RectTransform parentRect = rect.parent as RectTransform;
            if (parentRect == null) return;

            Rect area = parentRect.rect;
            Vector2 size = rect.rect.size;
            Vector2 pivot = rect.pivot;

            // anchoredPosition is measured from the anchor, which Follow puts
            // at the parent's centre, so the limits are half the parent either
            // side less whatever of the window sits beyond its own pivot.
            float minX = area.xMin + size.x * pivot.x;
            float maxX = area.xMax - size.x * (1f - pivot.x);
            float minY = area.yMin + size.y * pivot.y;
            float maxY = area.yMax - size.y * (1f - pivot.y);

            // A window larger than the screen has no valid range; pin it to the
            // top-left rather than letting the clamp invert.
            if (minX > maxX) maxX = minX;
            if (minY > maxY) minY = maxY;

            Vector2 at = rect.anchoredPosition;
            Vector2 inside = new Vector2(Mathf.Clamp(at.x, minX, maxX),
                                         Mathf.Clamp(at.y, minY, maxY));
            if (inside != at) rect.anchoredPosition = inside;
        }

        /// <summary>
        /// Put the panel's bottom-right corner just left of the chat window's
        /// bottom-left, in whatever space its own canvas uses.
        ///
        /// Called when the panel opens and not every frame: the Window prefab
        /// carries a drag bar, and repositioning it continuously would drag it
        /// straight back out of the player's hand.
        /// </summary>
        private void Follow()
        {
            RectTransform rect = panel.Window;
            if (rect == null) return;
            RectTransform parentRect = rect.parent as RectTransform;
            if (parentRect == null) return;

            Vector3[] corners = new Vector3[4];
            container.GetWorldCorners(corners);      // 0 = bottom-left

            Canvas canvas = parentRect.GetComponentInParent<Canvas>();
            Camera camera = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                camera = canvas.worldCamera;
            }

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screen, camera, out local))
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = local + new Vector2(-OptionsPanel.RightOffset, 0f);
        }

        private void Toggle()
        {
            if (panel == null) return;
            if (panel.IsOpen())
            {
                panel.Close();
                return;
            }

            panel.Open();
            Follow();          // place it before the player sees it
        }

        /// <summary>
        /// The gear artwork, read from the mod's own folder.
        /// <c>Modding.ModIO</c> is the only file API available, and
        /// <c>data: false</c> resolves inside the mod's folder.
        /// </summary>
        private static Sprite LoadGear()
        {
            const string path = "Resources/icon_gear.png";
            try
            {
                if (!Modding.ModIO.ExistsFile(path, false))
                {
                    Log.Warn("gear icon missing at " + path
                             + "; the button will have no face.");
                    return null;
                }

                byte[] bytes = Modding.ModIO.ReadAllBytes(path, false);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!texture.LoadImage(bytes))
                {
                    Log.Warn("gear icon at " + path + " would not decode.");
                    return null;
                }
                texture.name = "MpTtsGear";
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;

                return Sprite.Create(texture,
                                     new Rect(0, 0, texture.width, texture.height),
                                     new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Log.Warn("could not read the gear icon: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Report what was measured, not just what was decided -- note 06's
        /// last technique, and the one that diagnoses a docking bug in one
        /// line instead of two rounds of reasoning.
        /// </summary>
        private static string Describe(RectTransform rect)
        {
            Rect r = rect.rect;
            return "(x:" + r.x.ToString("F1") + ", y:" + r.y.ToString("F1")
                 + ", width:" + r.width.ToString("F1")
                 + ", height:" + r.height.ToString("F1") + ")";
        }

        /// <summary>Whether the gear and panel are built and on screen.</summary>
        public bool IsDocked()
        {
            return container != null && gear != null;
        }

        /// <summary>Open or close the panel from elsewhere (the console).</summary>
        public bool TogglePanel()
        {
            if (panel == null) return false;
            Toggle();
            return true;
        }
    }
}
