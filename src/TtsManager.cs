using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using MultiplayerTTS.Klatt;

namespace MultiplayerTTS
{
    /// <summary>
    /// Owns the mod: watches for chat, renders it, and keeps one
    /// <see cref="SpeechVoice"/> per speaker anchored to that player's core
    /// block.
    ///
    /// Synthesis runs on a worker thread. It is fast -- a few hundred times
    /// realtime -- but a maximum-length chat message still costs tens of
    /// milliseconds, which on the game thread is a visible hitch at exactly
    /// the moment something interesting is happening. The worker only ever
    /// touches plain arrays and strings; every Unity call stays on the game
    /// thread.
    /// </summary>
    public class TtsManager : MonoBehaviour
    {
        public static TtsManager Instance;

        public TtsSettings Settings;

        /// <summary>The chat-window gear and panel, once it has found a chat window.</summary>
        public MultiplayerTTS.Ui.ChatDock Dock;

        private ChatWatcher watcher;
        private readonly List<ChatMessage> incoming = new List<ChatMessage>();
        private readonly Dictionary<string, SpeechVoice> voices =
            new Dictionary<string, SpeechVoice>();

        // ---- worker thread ---------------------------------------------
        private Thread worker;
        private volatile bool running;
        private readonly Queue<Job> jobs = new Queue<Job>();
        private readonly Queue<Job> finished = new Queue<Job>();
        private readonly object jobGate = new object();
        private readonly object doneGate = new object();
        private readonly AutoResetEvent hasWork = new AutoResetEvent(false);

        private int sampleRate = 44100;

        private class Job
        {
            public string PlayerName;
            public List<string> Phonemes;
            public KlattVoice Voice;
            public int Seed;
            public bool Question;
            public SpeechPlan Plan;      // null unless the message used markup
            public float[] Samples;
        }

        // ---------------------------------------------------------------

        private void Awake()
        {
            Instance = this;
            Settings = TtsSettings.Load();

            // AudioSettings is a Unity call, so it is read here on the game
            // thread and handed to the worker as a plain int.
            sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0) sampleRate = 44100;

            watcher = new ChatWatcher();
            watcher.Start();

            running = true;
            worker = new Thread(WorkerLoop);
            worker.IsBackground = true;   // never hold the game open on exit
            worker.Name = "MpTtsSynth";
            worker.Start();

            Log.Info("ready. Output rate " + sampleRate + " Hz.");
        }

        private void OnDestroy()
        {
            if (watcher != null) watcher.Stop();
            running = false;
            hasWork.Set();
            Instance = null;
        }

        // ---------------------------------------------------------------
        // Game thread
        // ---------------------------------------------------------------

        private void Update()
        {
            CollectMessages();
            CollectRenderedAudio();
            UpdateAnchors();
        }

        private void CollectMessages()
        {
            incoming.Clear();
            watcher.Drain(incoming);
            if (incoming.Count == 0) return;

            for (int i = 0; i < incoming.Count; i++)
            {
                ChatMessage m = incoming[i];
                if (!ShouldSpeak(m)) continue;

                // The message still has the sender's name on the front; the
                // voice must read what they said, not who they are.
                string spoken = TextNormaliser.WithoutSpeaker(m.Text, m.SourceName);
                if (spoken.Length == 0) continue;

                Job job = new Job();
                job.PlayerName = m.SourceName;
                job.Voice = VoiceBank.ForPlayer(m.SourceName, Settings);
                job.Seed = VoiceBank.SeedForPlayer(m.SourceName);
                job.Question = spoken.TrimEnd().EndsWith("?");

                // DECtalk markup -- the Moonbase Alpha syntax -- only if the
                // message actually contains any. Everything else takes exactly
                // the path it always did.
                if (DecTalk.LooksLikeMarkup(spoken))
                {
                    SpeechPlan plan = DecTalk.Parse(spoken);
                    if (plan.UsedMarkup && plan.Items.Count > 0)
                    {
                        ApplyPlanVoice(job.Voice, plan);
                        job.Plan = plan;
                    }
                }

                if (job.Plan == null)
                {
                    job.Phonemes = LetterToSound.Translate(spoken);
                    if (job.Phonemes.Count == 0) continue;
                }

                lock (jobGate)
                {
                    // A backlog means someone is spamming. Speaking a message
                    // from a minute ago is worse than dropping it.
                    if (jobs.Count > 8) jobs.Dequeue();
                    jobs.Enqueue(job);
                }
                hasWork.Set();
            }
        }

        /// <summary>
        /// Fold a parsed message's own [:pitch] and [:volume] into the
        /// speaker's voice.
        ///
        /// Voice and rate are deliberately *not* applied here: they are
        /// per-item, because a message can change speaker part way through,
        /// and the synthesiser reads them off each item as it goes. Applying
        /// them here as well would fold the last one in the message over the
        /// whole of it.
        /// </summary>
        private static void ApplyPlanVoice(KlattVoice voice, SpeechPlan plan)
        {
            if (voice == null || plan == null) return;

            voice.Gain *= Mathf.Clamp01((float)plan.Volume);

            if (plan.PitchOffset != 0.0)
            {
                voice.Pitch *= Math.Pow(2.0, plan.PitchOffset / 12.0);
            }
        }

        private bool ShouldSpeak(ChatMessage m)
        {
            if (Settings == null || !Settings.Enabled) return false;
            if (string.IsNullOrEmpty(m.SourceName)) return false;
            if (Settings.IsMuted(m.SourceName)) return false;

            if (IsLocalPlayer(m)) return Settings.OwnVolume > 0.0001f;

            if (Settings.SpeakTeamOnly && m.Source != null)
            {
                PlayerData local = PlayerData.localPlayer;
                if (local != null && local.team != m.Source.team) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a message came from us. The <c>PlayerData</c> is the
        /// authority; the name comparison is the fallback for a message whose
        /// sender could not be resolved from the player list.
        /// </summary>
        private static bool IsLocalPlayer(ChatMessage m)
        {
            if (m.Source != null) return m.Source.isLocalPlayer;

            PlayerData local = PlayerData.localPlayer;
            return local != null && local.name == m.SourceName;
        }

        /// <summary>
        /// The volume this speaker should be played at, taking the own-message
        /// setting into account for our own lines.
        /// </summary>
        public float VolumeForSpeaker(string playerName)
        {
            PlayerData local = PlayerData.localPlayer;
            if (local != null && local.name == playerName) return Settings.OwnVolume;
            return Settings.GetPlayerVolume(playerName);
        }

        private void CollectRenderedAudio()
        {
            while (true)
            {
                Job job = null;
                lock (doneGate)
                {
                    if (finished.Count == 0) break;
                    job = finished.Dequeue();
                }
                if (job == null || job.Samples == null || job.Samples.Length == 0) continue;

                SpeechVoice voice = GetVoice(job.PlayerName);
                if (voice == null) continue;

                voice.PlayerVolume = VolumeForSpeaker(job.PlayerName);

                // If this player is still speaking their last line, replace it.
                // Queueing sounds like a backlog; interrupting sounds like a
                // person who typed twice.
                if (!voice.Speak(job.Samples))
                {
                    voice.StopSpeaking();
                    voice.Speak(job.Samples);
                }
            }
        }

        /// <summary>
        /// Point every active voice at its speaker's core block, every frame.
        ///
        /// This is re-resolved rather than cached because a simulation runs on
        /// a <b>clone</b> of the machine, rebuilt from scratch on every run
        /// (note 08). A Transform captured while building is destroyed the
        /// moment the player hits go. <c>Machine.FirstBlock</c> already
        /// switches between the simulation and building block lists, so asking
        /// it each frame is both the simplest and the only correct option.
        /// </summary>
        private void UpdateAnchors()
        {
            foreach (KeyValuePair<string, SpeechVoice> entry in voices)
            {
                SpeechVoice voice = entry.Value;
                if (voice == null) continue;

                voice.Settings = Settings;
                if (!voice.IsSpeaking) continue;

                voice.PlayerVolume = VolumeForSpeaker(entry.Key);
                voice.Anchor = FindCoreBlock(entry.Key);
            }
        }

        /// <summary>BlockType.StartingBlock, checked against the game's own enum.</summary>
        private const int StartingBlockId = 0;

        /// <summary>
        /// The transform of a player's core block, or null if they have no
        /// machine in the level right now.
        ///
        /// The starting block is found by id rather than by taking
        /// <c>Machine.FirstBlock</c> at its word: that property is the first
        /// entry of the block list, which is the starting block for a machine
        /// built in the usual way but is not guaranteed to be. Scanning for
        /// the id is exact, and <c>FirstBlock</c> remains the fallback for the
        /// odd machine that has no starting block at all.
        /// </summary>
        private static Transform FindCoreBlock(string playerName)
        {
            List<PlayerData> players = Playerlist.Players;
            if (players == null) return null;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerData p = players[i];
                if (p == null || p.name != playerName) continue;

                ServerMachine machine = p.machine;
                if (machine == null) return null;

                // While simulating, the blocks that exist in the world are the
                // clone's; the building blocks still exist but are inactive
                // and parked wherever the machine was assembled (note 08).
                List<BlockBehaviour> blocks = machine.isSimulating
                    ? machine.SimulationBlocks
                    : machine.BuildingBlocks;

                if (blocks != null)
                {
                    for (int b = 0; b < blocks.Count; b++)
                    {
                        BlockBehaviour block = blocks[b];
                        if (block == null) continue;          // also catches destroyed
                        if (block.BlockID != StartingBlockId) continue;
                        return block.transform;
                    }
                }

                BlockBehaviour first = machine.FirstBlock;
                return first == null ? null : first.transform;
            }
            return null;
        }

        private SpeechVoice GetVoice(string playerName)
        {
            SpeechVoice voice;
            if (voices.TryGetValue(playerName, out voice) && voice != null) return voice;

            GameObject go = new GameObject("MpTtsVoice_" + playerName);
            go.transform.SetParent(transform, false);

            voice = go.AddComponent<SpeechVoice>();
            voice.PlayerName = playerName;
            voice.Settings = Settings;

            voices[playerName] = voice;
            return voice;
        }

        // ---------------------------------------------------------------
        // Worker thread -- no Unity API beyond Debug logging
        // ---------------------------------------------------------------

        private void WorkerLoop()
        {
            while (running)
            {
                hasWork.WaitOne(250);

                while (running)
                {
                    Job job = null;
                    lock (jobGate)
                    {
                        if (jobs.Count == 0) break;
                        job = jobs.Dequeue();
                    }
                    if (job == null) break;

                    try
                    {
                        KlattSynth synth = new KlattSynth(sampleRate, job.Seed);
                        job.Samples = job.Plan != null
                            ? synth.Synthesise(job.Plan, job.Voice, job.Question)
                            : synth.Synthesise(job.Phonemes, job.Voice, job.Question);
                    }
                    catch (Exception e)
                    {
                        // A synthesis failure must not take the thread down --
                        // it would take every later message with it, silently.
                        job.Samples = null;
                        Log.Warn("synthesis failed for " + job.PlayerName + ": " + e.Message);
                    }

                    if (job.Samples != null)
                    {
                        lock (doneGate) { finished.Enqueue(job); }
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Public control surface, for the console commands and the eventual
        // in-chat options panel.
        // ---------------------------------------------------------------

        public void SetVolume(float volume)
        {
            Settings.Volume = Mathf.Clamp01(volume);
            Settings.Save();
        }

        public void SetPlayerVolume(string playerName, float volume)
        {
            Settings.SetPlayerVolume(playerName, volume);
            Settings.Save();
        }

        public void SetEnabled(bool enabled)
        {
            Settings.Enabled = enabled;
            if (!enabled) SilenceAll();
            Settings.Save();
        }

        public void SilenceAll()
        {
            foreach (KeyValuePair<string, SpeechVoice> entry in voices)
            {
                if (entry.Value != null) entry.Value.StopSpeaking();
            }
            lock (jobGate) { jobs.Clear(); }
            lock (doneGate) { finished.Clear(); }
        }

        /// <summary>Speak a line locally, for testing the voice.</summary>
        public void SpeakTest(string playerName, string text)
        {
            Job job = new Job();
            job.PlayerName = playerName;
            job.Voice = VoiceBank.ForPlayer(playerName, Settings);
            job.Seed = VoiceBank.SeedForPlayer(playerName);
            job.Question = text.TrimEnd().EndsWith("?");

            if (DecTalk.LooksLikeMarkup(text))
            {
                SpeechPlan plan = DecTalk.Parse(text);
                if (plan.UsedMarkup && plan.Items.Count > 0)
                {
                    ApplyPlanVoice(job.Voice, plan);
                    job.Plan = plan;
                }
            }
            if (job.Plan == null) job.Phonemes = LetterToSound.Translate(text);

            lock (jobGate) { jobs.Enqueue(job); }
            hasWork.Set();
        }

        /// <summary>
        /// Names we have built a voice for this session -- the list the
        /// options panel will offer per-player sliders for.
        /// </summary>
        public List<string> KnownSpeakers()
        {
            return new List<string>(voices.Keys);
        }

        public bool HasSeenChat()
        {
            return watcher != null && watcher.SawAnyMessage;
        }
    }
}
