using UnityEngine;

namespace MultiplayerTTS
{
    /// <summary>
    /// Tagged logging. Everything goes to Player.log, and to the in-game
    /// console with <c>show_logs true</c>.
    ///
    /// The tag is fixed and searchable on purpose: note 06 of the modding
    /// notes points out that the loader's own messages name the file and never
    /// the mod, so grepping for a mod's name finds nothing. This mod's own
    /// messages are findable with <c>grep -a '\[MP-TTS\]' Player.log</c>.
    /// </summary>
    public static class Log
    {
        private const string Tag = "[MP-TTS] ";

        public static void Info(string message)
        {
            Debug.Log(Tag + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Tag + message);
        }
    }
}
