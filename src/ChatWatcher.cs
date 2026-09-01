using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerTTS
{
    /// <summary>One received chat message, resolved to a player.</summary>
    public class ChatMessage
    {
        public PlayerData Source;
        public string SourceName;
        public string Text;
    }

    /// <summary>
    /// Finds incoming multiplayer chat messages.
    ///
    /// <b>Why this is done by reading the log.</b> Every received message
    /// funnels through <c>ChatController.HandleSayMessage(PlayerData, string)</c>
    /// -- both the global and the team path converge on it -- and that method
    /// unconditionally logs
    ///
    ///     [ChatController] HandleSayMessage source=&lt;name&gt; &lt;message&gt;
    ///
    /// before doing anything else. That log line is the only hook available:
    /// the method is private, <c>Modding.Events</c> exposes no chat event, and
    /// <c>System.Reflection</c> is on the loader's blacklist, so Harmony and
    /// every other patching approach is out. Subscribing to
    /// <c>Application.logMessageReceived</c> costs nothing and needs no
    /// patching at all.
    ///
    /// The cost is that this is a private diagnostic string, not an API, and a
    /// Besiege update could change or remove it. <see cref="SawAnyMessage"/>
    /// exists so the mod can say so plainly rather than just going quiet.
    /// </summary>
    public class ChatWatcher
    {
        private const string Prefix = "[ChatController] HandleSayMessage source=";

        /// <summary>
        /// True once a chat line has actually been parsed. If this is still
        /// false after a multiplayer session with chat in it, the log format
        /// has changed and this file is where to look.
        /// </summary>
        public bool SawAnyMessage;

        private readonly List<ChatMessage> pending = new List<ChatMessage>();
        private readonly object gate = new object();

        public void Start()
        {
            Application.logMessageReceived += OnLog;
        }

        public void Stop()
        {
            Application.logMessageReceived -= OnLog;
        }

        /// <summary>
        /// Take everything seen since the last call. Called from Update.
        /// </summary>
        public void Drain(List<ChatMessage> into)
        {
            lock (gate)
            {
                if (pending.Count == 0) return;
                into.AddRange(pending);
                pending.Clear();
            }
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            // Unity may deliver this from a background thread, so it does no
            // Unity work and only touches the queue under a lock. The parsing
            // is pure string handling and is safe anywhere.
            if (message == null || message.Length <= Prefix.Length) return;
            if (!message.StartsWith(Prefix, StringComparison.Ordinal)) return;

            string rest = message.Substring(Prefix.Length);

            PlayerData source;
            string name;
            string text;
            if (!Split(rest, out source, out name, out text)) return;
            if (text.Length == 0) return;

            ChatMessage m = new ChatMessage();
            m.Source = source;
            m.SourceName = name;
            m.Text = text;

            lock (gate)
            {
                SawAnyMessage = true;
                // Bound the queue. If something is producing these faster than
                // they can be spoken, dropping the oldest is better than
                // growing without limit.
                if (pending.Count > 32) pending.RemoveAt(0);
                pending.Add(m);
            }
        }

        /// <summary>
        /// Split "&lt;name&gt; &lt;message&gt;" into its two halves.
        ///
        /// The separator is a single space and a player name may contain
        /// spaces, so splitting on the first one is wrong for a great many
        /// Steam names. Instead the known player names are tried against the
        /// front of the string, longest first, which resolves it exactly. The
        /// first-space split is only the fallback for a name we have not seen
        /// -- and it also gives us the <c>PlayerData</c>, which is what the
        /// speech has to be positioned from.
        /// </summary>
        private static bool Split(string rest, out PlayerData source,
                                  out string name, out string text)
        {
            source = null;
            name = null;
            text = null;

            List<PlayerData> players = Playerlist.Players;
            if (players != null)
            {
                int bestLength = -1;
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerData p = players[i];
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;

                    // The name must be followed by the separating space, or
                    // "Bob" would match the front of "Bobby said hello".
                    if (p.name.Length >= rest.Length) continue;
                    if (!rest.StartsWith(p.name, StringComparison.Ordinal)) continue;
                    if (rest[p.name.Length] != ' ') continue;

                    if (p.name.Length > bestLength)
                    {
                        bestLength = p.name.Length;
                        source = p;
                    }
                }

                if (source != null)
                {
                    name = source.name;
                    text = rest.Substring(bestLength + 1);
                    return true;
                }
            }

            int space = rest.IndexOf(' ');
            if (space <= 0 || space >= rest.Length - 1) return false;

            name = rest.Substring(0, space);
            text = rest.Substring(space + 1);
            return true;
        }
    }
}
