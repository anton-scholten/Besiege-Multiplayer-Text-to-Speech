using System;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerTTS.Ui
{
    /// <summary>
    /// The only file in this mod that mentions <c>Besiege.UI</c>.
    ///
    /// That is deliberate, and it is what makes UI Factory a *soft* dependency
    /// rather than a hard one. A type that cannot be resolved fails when the
    /// method mentioning it is compiled, so confining every mention to one
    /// class means a single guarded call decides whether the panel can be
    /// built at all — everywhere else can ask <see cref="Available"/> and get
    /// a plain bool back. Without UI Factory installed the mod still loads,
    /// still reads chat aloud, and simply has no panel; the console commands
    /// reach every setting either way.
    ///
    /// This also has to ship as a **pre-built assembly**. A ScriptAssembly is
    /// compiled during the load phase, before any other mod's assembly is in
    /// the AppDomain, so every reference here would fail to resolve. A
    /// pre-built DLL binds them lazily, on first use, by which point UI
    /// Factory is loaded (note 01).
    /// </summary>
    public static class UIF
    {
        public const string Package = "UIFactory3";

        // Prefab names, exactly as Besiege.UI.Mod registers them.
        public const string Empty = "Empty";
        public const string TextPrefab = "Text";
        public const string TextButton = "Text Button";
        public const string TextToggle = "Text Toggle";
        public const string IconButton = "Icon Button";
        public const string InputFieldPrefab = "Input Field";
        public const string SliderPrefab = "Slider";
        public const string WindowPrefab = "Window";

        private static bool available;
        private static bool settled;

        /// <summary>
        /// Whether UI Factory is installed and its types resolve.
        ///
        /// This deliberately does <b>not</b> test
        /// <c>Modding.ModResource.AllResourcesLoaded</c>, which is the obvious
        /// thing to add and is wrong here. That property resolves the
        /// <em>calling</em> assembly and checks that mod's own resource list —
        /// so asking it from this mod reports whether <em>our</em> icon and
        /// thumbnail have loaded, which has nothing to do with whether UI
        /// Factory is ready. It cost a session of the panel never appearing:
        /// a mistyped texture path in the manifest left our own resources
        /// permanently unloaded, and the panel was gated on it.
        ///
        /// What actually gates the prefabs is <see cref="WhenReady"/>, which is
        /// UI Factory's own callback for exactly this and is what its
        /// documentation points at.
        ///
        /// Only an affirmative answer is cached: UI Factory loads a moment
        /// after this mod does, so a single early ask answers "no" wrongly.
        /// While it is genuinely absent each ask costs a caught
        /// <c>TypeLoadException</c>, so callers should ask on a timer rather
        /// than every frame.
        /// </summary>
        public static bool Available
        {
            get
            {
                if (settled) return available;
                try
                {
                    available = Besiege.UI.Make.Instance != null;
                }
                catch (Exception)
                {
                    available = false;
                }
                settled = available;
                return available;
            }
        }

        /// <summary>
        /// A one-line answer to "why is there no panel?", for `tts status`.
        /// A missing panel is otherwise indistinguishable from a mod that did
        /// not load.
        /// </summary>
        public static string Diagnose()
        {
            try
            {
                if (Besiege.UI.Make.Instance == null)
                {
                    return "UI Factory's types resolve but it has not started yet.";
                }
                return "UI Factory is loaded and ready.";
            }
            catch (Exception)
            {
                return "UI Factory is not installed (Workshop item 2913469777). "
                     + "Everything works without it except the panel.";
            }
        }

        /// <summary>
        /// Run <paramref name="action"/> once UI Factory's own resources are
        /// loaded. <c>Make.Prefab</c> throws if they are not, so construction
        /// has to be gated on this rather than on any guess about when the
        /// game is ready.
        /// </summary>
        public static bool WhenReady(Action action)
        {
            try
            {
                Besiege.UI.Make.OnReady(Package, action);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("UI Factory is not available, so the options panel "
                         + "cannot be built: " + e.Message);
                return false;
            }
        }

        public static GameObject Spawn(string prefab, Transform parent)
        {
            try
            {
                return Besiege.UI.Make.Prefab(Package, prefab, parent);
            }
            catch (Exception e)
            {
                Log.Warn("could not spawn UI Factory prefab '" + prefab + "': "
                         + e.Message);
                return null;
            }
        }

        /// <summary>UI Factory's own font, which its prefabs expect.</summary>
        public static Font Font
        {
            get
            {
                try { return Besiege.UI.Make.Font; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>
        /// Stop a label putting its prefab's own wording back.
        ///
        /// Every UI Factory <c>Text</c> carries a <c>Translator</c>, which
        /// rewrites the text at the next language change. On a label the mod
        /// writes into — a player's name, a value — that means the caption
        /// silently reverts to "Option A" or whatever the prefab was authored
        /// with. Take it off any Text you own.
        /// </summary>
        public static void Untranslate(Text text)
        {
            if (text == null) return;
            try
            {
                Besiege.UI.Behaviours.Translator translator =
                    text.GetComponent<Besiege.UI.Behaviours.Translator>();
                if (translator != null) UnityEngine.Object.Destroy(translator);
            }
            catch (Exception)
            {
                // Nothing to do: if the type will not resolve, there is no
                // translator on the object either.
            }
        }

        /// <summary>
        /// Point a control's hover animation at a child instead of at the
        /// control itself.
        ///
        /// UI Factory's controls swell on hover, which is right for a button
        /// and wrong for a full-width row — a whole settings line growing
        /// under the pointer reads as a glitch. The scale factors are
        /// non-public and serialised into the prefab, and reflection is
        /// blacklisted, so they cannot be read or written; <c>Target</c> is
        /// public, so the animation can be aimed somewhere harmless instead.
        /// </summary>
        public static void RetargetHover(GameObject control, RectTransform target)
        {
            if (control == null) return;
            try
            {
                Besiege.UI.Bridge.ScaleAnimation animation =
                    control.GetComponent<Besiege.UI.Bridge.ScaleAnimation>();
                if (animation != null) animation.Target = target;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Give an Input Field's own Text and placeholder a font.
        ///
        /// They come out of the prefab with none, and a <c>Text</c> with no
        /// font draws nothing — so the field looks empty however much is typed
        /// into it, which reads as a box that swallows typing rather than one
        /// that failed to paint. Besiege logs "Font is null, replacing with
        /// default" around this; that is the game's own message, not a warning
        /// about the field.
        /// </summary>
        public static void FixFont(InputField field)
        {
            if (field == null) return;
            Font font = Font;
            if (font == null) return;

            if (field.textComponent != null) field.textComponent.font = font;

            Text placeholder = field.placeholder as Text;
            if (placeholder != null) placeholder.font = font;
        }

        // ---------------------------------------------------------------
        // Small helpers over the spawned objects
        // ---------------------------------------------------------------

        public static RectTransform Rect(GameObject go)
        {
            return go == null ? null : go.GetComponent<RectTransform>();
        }

        /// <summary>Set a spawned control's caption, translator removed.</summary>
        public static Text Caption(GameObject go, string caption)
        {
            if (go == null) return null;
            Text text = go.GetComponentInChildren<Text>(true);
            if (text == null) return null;

            Untranslate(text);
            text.text = caption;
            return text;
        }

        public static void OnClick(GameObject go, UnityEngine.Events.UnityAction action)
        {
            if (go == null) return;
            Button button = go.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(action);
        }
    }
}
