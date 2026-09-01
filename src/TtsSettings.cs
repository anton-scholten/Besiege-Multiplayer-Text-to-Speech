using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MultiplayerTTS
{
    /// <summary>
    /// The mod's settings, persisted to the mod's own data folder.
    ///
    /// Storage goes through <c>Modding.ModIO</c> because it is the only file
    /// API a mod has -- <c>System.IO.File</c> is blacklisted by the loader --
    /// and it is written at the data root rather than in a subfolder, since
    /// <c>ModIO.CreateDirectory</c> will happily make a folder out of anything
    /// and there is no reason to give it the chance.
    ///
    /// Per-player volumes are keyed by name rather than network id: a network
    /// id is only meaningful for one session, and "I muted this person" should
    /// outlive the lobby.
    /// </summary>
    public class TtsSettings
    {
        private const string FileName = "settings.txt";

        public bool Enabled = true;
        public bool SpeakTeamOnly = false;

        /// <summary>
        /// Volume for your own messages read back to you, 0..1. Zero -- the
        /// default -- means they are not spoken at all. This is a volume
        /// rather than a toggle because hearing yourself at the same level as
        /// everyone else is the thing nobody wants; quieter than everyone else
        /// is the setting people actually reach for.
        /// </summary>
        public float OwnVolume = 0f;

        /// <summary>Master volume for all speech, 0..1.</summary>
        public float Volume = 0.8f;

        /// <summary>Words per minute scaling; 1.0 is the natural rate.</summary>
        public float Speed = 1.0f;

        /// <summary>Distance at which a voice is still at full volume.</summary>
        public float ReferenceDistance = 8f;

        /// <summary>Distance past which a voice is inaudible.</summary>
        public float MaxDistance = 90f;

        /// <summary>How much of the sound is positional. 0 is flat 2D.</summary>
        public float Spatialisation = 1.0f;

        private readonly Dictionary<string, float> perPlayer =
            new Dictionary<string, float>();

        // ---------------------------------------------------------------
        // Per-player volume
        // ---------------------------------------------------------------

        public float GetPlayerVolume(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return 1f;
            float v;
            if (perPlayer.TryGetValue(playerName, out v)) return v;
            return 1f;
        }

        public void SetPlayerVolume(string playerName, float volume)
        {
            if (string.IsNullOrEmpty(playerName)) return;
            if (volume < 0f) volume = 0f;
            if (volume > 1f) volume = 1f;

            if (volume >= 0.999f) perPlayer.Remove(playerName);
            else perPlayer[playerName] = volume;
        }

        public bool IsMuted(string playerName)
        {
            return GetPlayerVolume(playerName) <= 0.0001f;
        }

        public List<string> AdjustedPlayers()
        {
            return new List<string>(perPlayer.Keys);
        }

        // ---------------------------------------------------------------
        // Persistence
        // ---------------------------------------------------------------

        public static TtsSettings Load()
        {
            TtsSettings s = new TtsSettings();
            try
            {
                if (!Modding.ModIO.ExistsFile(FileName, true)) return s;
                string text = Modding.ModIO.ReadAllText(FileName, true);
                s.Parse(text);
            }
            catch (Exception e)
            {
                Log.Warn("could not read settings, using defaults: " + e.Message);
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Modding.ModIO.WriteAllText(FileName, Serialise(), true);
            }
            catch (Exception e)
            {
                Log.Warn("could not write settings: " + e.Message);
            }
        }

        private string Serialise()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("# Besiege Multiplayer Text to Speech");
            b.AppendLine("# Rewritten whenever a setting changes; edits are kept.");
            b.AppendLine("enabled=" + (Enabled ? "1" : "0"));
            b.AppendLine("ownVolume=" + OwnVolume.ToString("R"));
            b.AppendLine("speakTeamOnly=" + (SpeakTeamOnly ? "1" : "0"));
            b.AppendLine("volume=" + Volume.ToString("R"));
            b.AppendLine("speed=" + Speed.ToString("R"));
            b.AppendLine("referenceDistance=" + ReferenceDistance.ToString("R"));
            b.AppendLine("maxDistance=" + MaxDistance.ToString("R"));
            b.AppendLine("spatialisation=" + Spatialisation.ToString("R"));

            foreach (KeyValuePair<string, float> kv in perPlayer)
            {
                // A name can contain anything a Steam name can, including '='
                // and newlines, so it goes last on the line and the value goes
                // first. Newlines are stripped rather than escaped: a name
                // containing one cannot round-trip and is not worth a format.
                string name = kv.Key.Replace("\r", "").Replace("\n", "");
                if (name.Length == 0) continue;
                b.AppendLine("player=" + kv.Value.ToString("R") + "=" + name);
            }
            return b.ToString();
        }

        private void Parse(string text)
        {
            if (text == null) return;
            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq);
                string value = line.Substring(eq + 1);

                if (key == "player")
                {
                    int second = value.IndexOf('=');
                    if (second <= 0) continue;
                    float pv;
                    if (!TryFloat(value.Substring(0, second), out pv)) continue;
                    string name = value.Substring(second + 1);
                    if (name.Length > 0) perPlayer[name] = Mathf.Clamp01(pv);
                    continue;
                }

                switch (key)
                {
                    case "enabled": Enabled = value == "1"; break;
                    case "ownVolume": OwnVolume = ReadFloat(value, OwnVolume, 0f, 1f); break;
                    case "speakTeamOnly": SpeakTeamOnly = value == "1"; break;
                    case "volume": Volume = ReadFloat(value, Volume, 0f, 1f); break;
                    case "speed": Speed = ReadFloat(value, Speed, 0.5f, 2f); break;
                    case "referenceDistance":
                        ReferenceDistance = ReadFloat(value, ReferenceDistance, 1f, 200f); break;
                    case "maxDistance":
                        MaxDistance = ReadFloat(value, MaxDistance, 5f, 1000f); break;
                    case "spatialisation":
                        Spatialisation = ReadFloat(value, Spatialisation, 0f, 1f); break;
                }
            }

            if (MaxDistance <= ReferenceDistance) MaxDistance = ReferenceDistance + 1f;
        }

        private static float ReadFloat(string s, float fallback, float lo, float hi)
        {
            float v;
            if (!TryFloat(s, out v)) return fallback;
            return Mathf.Clamp(v, lo, hi);
        }

        private static bool TryFloat(string s, out float value)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out value);
        }
    }
}
