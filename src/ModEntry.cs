using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MultiplayerTTS
{
    /// <summary>
    /// The mod's entry point. Creates the one manager object and registers the
    /// console commands.
    ///
    /// The manager lives on a <c>DontDestroyOnLoad</c> object because chat and
    /// machines outlive any one scene, and because a mod is never unloaded
    /// once loaded -- there is nothing to tear down between levels.
    ///
    /// Note that there is deliberately no <c>&lt;LoadInTitleScreen /&gt;</c> in
    /// the manifest. With it, a fatal error here would lock the player out of
    /// the game rather than out of the mod, and this mod has nothing to do on
    /// the title screen.
    /// </summary>
    public class ModEntry : Modding.ModEntryPoint
    {
        private static GameObject host;

        public override void OnLoad()
        {
            if (host != null) return;

            host = new GameObject("MultiplayerTTS");
            UnityEngine.Object.DontDestroyOnLoad(host);
            TtsManager manager = host.AddComponent<TtsManager>();

            // The dock waits for a chat window to appear and hangs the gear
            // off it; in single player it simply never finds one.
            MultiplayerTTS.Ui.ChatDock dock = host.AddComponent<MultiplayerTTS.Ui.ChatDock>();
            dock.Manager = manager;
            manager.Dock = dock;

            RegisterCommands();

            Log.Info("loaded. Type 'tts' in the console for options.");
        }

        private void RegisterCommands()
        {
            try
            {
                Modding.ModConsole.RegisterCommand("tts", OnCommand,
                    "Multiplayer text to speech.\n"
                    + "  tts                       show current settings\n"
                    + "  tts on | off              enable or disable speech\n"
                    + "  tts volume <0-100>        overall speech volume\n"
                    + "  tts speed <50-200>        speaking rate, percent\n"
                    + "  tts player <0-100> <name> one player's volume; 0 mutes\n"
                    + "  tts mute <name>           silence one player\n"
                    + "  tts unmute <name>         and put them back\n"
                    + "  tts own <0-100>           volume of your own messages; 0 is off\n"
                    + "  tts team on | off         only speak your own team\n"
                    + "  tts spatial <0-100>       how positional the voice is\n"
                    + "  tts range <metres>        how far speech carries\n"
                    + "  tts say <text>            hear a line in your own voice\n"
                    + "  tts test <name> <text>    hear a line in someone's voice\n"
                    + "  tts panel                 open or close the options panel\n"
                    + "  tts stop                  cut everything off now\n"
                    + "  tts status                is the chat hook working?");
            }
            catch (Exception e)
            {
                // Losing the console commands is not worth losing the mod for.
                Log.Warn("could not register console command: " + e.Message);
            }
        }

        private static void OnCommand(string[] args)
        {
            TtsManager m = TtsManager.Instance;
            if (m == null || m.Settings == null)
            {
                Modding.ModConsole.Log("[MP-TTS] not ready yet.");
                return;
            }
            TtsSettings s = m.Settings;

            if (args == null || args.Length == 0)
            {
                ShowSettings(m, s);
                return;
            }

            string verb = args[0].ToLowerInvariant();

            switch (verb)
            {
                case "on":
                    m.SetEnabled(true);
                    Say("speech enabled.");
                    return;

                case "off":
                    m.SetEnabled(false);
                    Say("speech disabled.");
                    return;

                case "stop":
                    m.SilenceAll();
                    Say("stopped.");
                    return;

                case "status":
                    ShowStatus(m);
                    return;

                case "volume":
                {
                    float v;
                    if (!Percent(args, 1, out v)) { Say("usage: tts volume <0-100>"); return; }
                    m.SetVolume(v);
                    Say("volume " + Pct(v) + ".");
                    return;
                }

                case "speed":
                {
                    float v;
                    if (!Number(args, 1, out v) || v < 50f || v > 200f)
                    {
                        Say("usage: tts speed <50-200>");
                        return;
                    }
                    s.Speed = v / 100f;
                    s.Save();
                    Say("speed " + ((int)v) + "%.");
                    return;
                }

                case "spatial":
                {
                    float v;
                    if (!Percent(args, 1, out v)) { Say("usage: tts spatial <0-100>"); return; }
                    s.Spatialisation = v;
                    s.Save();
                    Say("spatialisation " + Pct(v)
                        + (v <= 0.001f ? " -- speech is now flat 2D." : "."));
                    return;
                }

                case "range":
                {
                    // One argument is the cutoff, with the full-volume
                    // distance kept proportional -- the same single control
                    // the options panel offers. Two arguments set both
                    // explicitly, for anyone who wants a different falloff.
                    float first, second;
                    if (!Number(args, 1, out first))
                    {
                        Say("usage: tts range <cutoff distance>\n"
                            + "     or tts range <full-volume distance> <cutoff distance>");
                        return;
                    }

                    if (Number(args, 2, out second))
                    {
                        if (first < 1f || second <= first)
                        {
                            Say("the cutoff distance must be larger than the "
                                + "full-volume distance.");
                            return;
                        }
                        s.ReferenceDistance = first;
                        s.MaxDistance = second;
                    }
                    else
                    {
                        if (first < TtsSettings.RangeMin
                            || first > TtsSettings.RangeMax)
                        {
                            Say("range must be between " + TtsSettings.RangeMin
                                + " and " + TtsSettings.RangeMax + " metres.");
                            return;
                        }
                        s.SetRange(first);
                    }

                    s.Save();
                    Say(s.DescribeRange() + ".");
                    return;
                }

                case "own":
                {
                    float v;
                    if (!Percent(args, 1, out v)) { Say("usage: tts own <0-100>"); return; }
                    s.OwnVolume = v;
                    s.Save();
                    Say(v <= 0.0001f
                        ? "your own messages will not be spoken."
                        : "your own messages: " + Pct(v) + ".");
                    return;
                }

                case "panel":
                {
                    if (m.Dock == null || !m.Dock.TogglePanel())
                    {
                        Say("the options panel is not docked yet -- it appears "
                            + "beside the chat window in a multiplayer game.");
                        return;
                    }
                    return;
                }

                case "team":
                {
                    bool on;
                    if (!OnOff(args, 1, out on)) { Say("usage: tts team on|off"); return; }
                    s.SpeakTeamOnly = on;
                    s.Save();
                    Say(on ? "only your own team will be spoken."
                           : "everyone will be spoken.");
                    return;
                }

                case "player":
                {
                    float v;
                    if (args.Length < 3 || !Percent(args, 1, out v))
                    {
                        Say("usage: tts player <0-100> <name>");
                        return;
                    }
                    string name = Rest(args, 2);
                    m.SetPlayerVolume(name, v);
                    Say(name + ": " + Pct(v) + (v <= 0.0001f ? " (muted)." : "."));
                    return;
                }

                case "mute":
                {
                    if (args.Length < 2) { Say("usage: tts mute <name>"); return; }
                    string name = Rest(args, 1);
                    m.SetPlayerVolume(name, 0f);
                    Say(name + " muted.");
                    return;
                }

                case "unmute":
                {
                    if (args.Length < 2) { Say("usage: tts unmute <name>"); return; }
                    string name = Rest(args, 1);
                    m.SetPlayerVolume(name, 1f);
                    Say(name + " unmuted.");
                    return;
                }

                case "say":
                {
                    if (args.Length < 2) { Say("usage: tts say <text>"); return; }
                    PlayerData local = PlayerData.localPlayer;
                    string name = local != null && !string.IsNullOrEmpty(local.name)
                        ? local.name : "you";
                    m.SpeakTest(name, Rest(args, 1));
                    return;
                }

                case "test":
                {
                    if (args.Length < 3) { Say("usage: tts test <name> <text>"); return; }
                    m.SpeakTest(args[1], Rest(args, 2));
                    return;
                }

                default:
                    Say("unknown option '" + verb + "'. Type 'help tts' for the list.");
                    return;
            }
        }

        private static void ShowSettings(TtsManager m, TtsSettings s)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("[MP-TTS] " + (s.Enabled ? "on" : "off")
                         + "  volume " + Pct(s.Volume)
                         + "  speed " + ((int)(s.Speed * 100f)) + "%"
                         + "  spatial " + Pct(s.Spatialisation));
            b.AppendLine("  " + s.DescribeRange());
            b.AppendLine("  own messages: "
                         + (s.OwnVolume <= 0.0001f ? "not spoken" : Pct(s.OwnVolume))
                         + "   scope: " + (s.SpeakTeamOnly ? "own team" : "everyone"));

            List<string> adjusted = s.AdjustedPlayers();
            if (adjusted.Count > 0)
            {
                b.AppendLine("  per player:");
                for (int i = 0; i < adjusted.Count; i++)
                {
                    float v = s.GetPlayerVolume(adjusted[i]);
                    b.AppendLine("    " + adjusted[i] + "  " + Pct(v)
                                 + (v <= 0.0001f ? " (muted)" : ""));
                }
            }

            List<string> heard = m.KnownSpeakers();
            if (heard.Count > 0)
            {
                b.Append("  heard this session: ");
                for (int i = 0; i < heard.Count; i++)
                {
                    if (i > 0) b.Append(", ");
                    b.Append(heard[i]);
                }
            }

            Modding.ModConsole.Log(b.ToString());
        }

        private static void ShowStatus(TtsManager m)
        {
            Say(MultiplayerTTS.Ui.UIF.Diagnose());
            if (m.Dock != null && !m.Dock.IsDocked())
            {
                Say("the options gear is not docked yet -- it appears beside "
                    + "the chat window in a multiplayer level.");
            }

            if (m.HasSeenChat())
            {
                Say("chat hook is working -- messages have been read.");
                return;
            }

            Say("no chat message has been seen yet. If speech is silent in a\n"
                + "  multiplayer game where people are talking, the log line this\n"
                + "  mod reads has probably changed in a Besiege update; see\n"
                + "  ChatWatcher.cs. 'tts say hello' tests the voice itself.");
        }

        // ---- small helpers ---------------------------------------------

        private static void Say(string message)
        {
            Modding.ModConsole.Log("[MP-TTS] " + message);
        }

        private static string Pct(float value)
        {
            return ((int)Mathf.Round(value * 100f)) + "%";
        }

        private static string Rest(string[] args, int from)
        {
            StringBuilder b = new StringBuilder();
            for (int i = from; i < args.Length; i++)
            {
                if (b.Length > 0) b.Append(' ');
                b.Append(args[i]);
            }
            return b.ToString();
        }

        private static bool Number(string[] args, int index, out float value)
        {
            value = 0f;
            if (args.Length <= index) return false;
            return float.TryParse(args[index],
                                  System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out value);
        }

        private static bool Percent(string[] args, int index, out float value)
        {
            float raw;
            value = 0f;
            if (!Number(args, index, out raw)) return false;
            if (raw < 0f || raw > 100f) return false;
            value = raw / 100f;
            return true;
        }

        private static bool OnOff(string[] args, int index, out bool value)
        {
            value = false;
            if (args.Length <= index) return false;
            string s = args[index].ToLowerInvariant();
            if (s == "on" || s == "true" || s == "1") { value = true; return true; }
            if (s == "off" || s == "false" || s == "0") { value = false; return true; }
            return false;
        }
    }
}
