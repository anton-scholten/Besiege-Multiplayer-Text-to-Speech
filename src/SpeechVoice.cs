using System;
using UnityEngine;

namespace MultiplayerTTS
{
    /// <summary>
    /// Plays one player's speech, positioned at their core block.
    ///
    /// The whole design of this component comes out of note 07 of the modding
    /// notes, which is worth restating because every part of it looks wrong
    /// until you know why:
    ///
    ///   * The <c>AudioSource</c> is <b>2D</b> (<c>spatialBlend = 0</c>) and
    ///     the panning and distance attenuation are done by hand. This is not
    ///     a shortcut. <c>OnAudioFilterRead</c> is inserted into the source's
    ///     chain *downstream* of Unity's 3D stage, so a filter that writes the
    ///     buffer -- which is what playing our own samples means -- discards
    ///     everything the panner did. The symptom is a voice heard at one
    ///     volume, dead centre, from anywhere on the map, with everything
    ///     apparently set correctly.
    ///
    ///   * The gains are computed on the game thread and handed over in
    ///     <c>volatile</c> floats, then slid onto across the buffer. A
    ///     transform must never be read from the audio thread, and a gain
    ///     applied in one step at the head of a buffer makes a turning camera
    ///     audible as a staircase.
    ///
    ///   * The source needs a clip and a <c>Play()</c> even though the clip is
    ///     never heard: Unity does not run the filter chain on a source that
    ///     is not playing.
    ///
    /// The samples themselves are rendered ahead of time on a worker thread,
    /// so this callback only ever copies and scales. That also avoids the
    /// streaming-clip latency trap note 07 warns about.
    /// </summary>
    public class SpeechVoice : MonoBehaviour
    {
        // ---- handed across to the audio thread -------------------------
        private volatile float gainLeft;
        private volatile float gainRight;
        private volatile bool speaking;

        // Single-producer / single-consumer handoff. The game thread writes
        // `queued` only when it is null; the audio thread takes it and clears
        // it. A reference assignment is atomic, so neither side needs a lock,
        // and note 07 is clear that a lock on the audio callback is the thing
        // to avoid.
        private volatile float[] queued;

        // Audio-thread state. Nothing else touches these.
        private float[] active;
        private int cursor;
        private float smoothedLeft;
        private float smoothedRight;

        private AudioSource source;
        private AudioListener listener;

        private static AudioClip silence;

        public string PlayerName;

        /// <summary>
        /// Set each frame by the manager: the speaker's core block. Null when
        /// they have no machine placed -- or when the core block has just been
        /// destroyed, which mid-sentence is the common case.
        /// </summary>
        public Transform Anchor;

        // Where the voice was last heard from. A core block that explodes
        // takes its transform with it, and snapping from positioned audio to
        // dead centre is far more noticeable than the voice simply carrying on
        // from where the machine used to be.
        private Vector3 lastKnownPosition;
        private bool hasLastKnownPosition;

        public TtsSettings Settings;

        /// <summary>Volume for this player specifically, 0..1.</summary>
        public float PlayerVolume = 1f;

        public bool IsSpeaking { get { return speaking || queued != null; } }

        // ---------------------------------------------------------------

        private void Awake()
        {
            if (silence == null)
            {
                // One sample of silence, looped. Enough to keep the source
                // "playing" so our filter runs; shared by every voice.
                silence = AudioClip.Create("MpTtsSilence", 1, 1,
                                           AudioSettings.outputSampleRate, false);
                silence.SetData(new float[] { 0f }, 0);
            }

            source = gameObject.AddComponent<AudioSource>();
            source.clip = silence;
            source.loop = true;
            source.spatialBlend = 0f;      // 2D: see the note above
            source.volume = 1f;
            source.playOnAwake = false;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.Play();
        }

        /// <summary>
        /// Hand over a finished utterance. Returns false if this voice is
        /// still busy with the last one.
        /// </summary>
        public bool Speak(float[] samples)
        {
            if (samples == null || samples.Length == 0) return true;
            if (queued != null) return false;

            // A new utterance starts from wherever the speaker is now, not
            // from where their last machine died.
            hasLastKnownPosition = false;

            speaking = true;
            queued = samples;
            return true;
        }

        public void StopSpeaking()
        {
            queued = null;
            active = null;
            speaking = false;
        }

        // ---------------------------------------------------------------
        // Game thread
        // ---------------------------------------------------------------

        private void Update()
        {
            // Besiege swaps cameras between building and running and the
            // listener goes with them, so a cached one goes stale rather than
            // null. Re-find it whenever it is not active.
            if (listener == null || !listener.isActiveAndEnabled)
            {
                listener = FindObjectOfType<AudioListener>();
            }

            float volume = PlayerVolume;
            if (Settings != null) volume *= Settings.Volume;
            volume *= MasterVolume();

            if (listener == null)
            {
                gainLeft = volume;
                gainRight = volume;
                return;
            }

            if (Anchor != null)
            {
                lastKnownPosition = Anchor.position;
                hasLastKnownPosition = true;
            }
            else if (!hasLastKnownPosition)
            {
                // Nothing to place it at and nowhere it has been: play it
                // centred rather than not at all. This is the lobby, and the
                // moment before a machine has been placed.
                gainLeft = volume;
                gainRight = volume;
                return;
            }

            Vector3 delta = lastKnownPosition - listener.transform.position;
            float distance = delta.magnitude;

            float reference = Settings != null ? Settings.ReferenceDistance : 8f;
            float max = Settings != null ? Settings.MaxDistance : 90f;
            float spatial = Settings != null ? Settings.Spatialisation : 1f;

            // Inverse rolloff out to `max`, then a linear fade to nothing so a
            // far-off player is actually silent instead of merely quiet.
            float attenuation = 1f;
            if (distance > reference)
            {
                attenuation = reference / distance;
                if (distance > max * 0.75f)
                {
                    float fade = 1f - (distance - max * 0.75f) / (max * 0.25f);
                    attenuation *= Mathf.Clamp01(fade);
                }
            }

            // Pan by where the source sits across the listener's own axes.
            float pan = 0f;
            if (distance > 0.001f)
            {
                pan = Vector3.Dot(delta / distance, listener.transform.right);
                pan = Mathf.Clamp(pan, -1f, 1f);
            }

            // Blend the positional result against flat 2D, so a player who
            // wants speech to be plainly audible can have it.
            attenuation = Mathf.Lerp(1f, attenuation, spatial);
            pan *= spatial;

            // min(1, 1 -/+ pan) keeps a source dead ahead exactly as loud as
            // it was before any panning, and never exceeds 1, so nothing
            // clips as it moves.
            float left = Mathf.Min(1f, 1f - pan);
            float right = Mathf.Min(1f, 1f + pan);

            gainLeft = volume * attenuation * left;
            gainRight = volume * attenuation * right;
        }

        /// <summary>
        /// Besiege's master slider sets <c>AudioListener.volume</c>, and Unity
        /// does not apply that to audio coming out of an <c>AudioMixer</c>. We
        /// have no mixer group, so the listener already scales us and this
        /// returns 1 -- but the check is what stops the slider being applied
        /// twice if a mixer group is ever set, which would make half volume a
        /// quarter. Straight from note 07.
        /// </summary>
        private float MasterVolume()
        {
            if (source == null || source.outputAudioMixerGroup == null) return 1f;
            BesiegeConfig config = OptionsMaster.BesiegeConfig;
            return config == null ? 1f : Mathf.Clamp01(config.MasterVolume / 100f);
        }

        // ---------------------------------------------------------------
        // Audio thread -- nothing here may touch a Unity object
        // ---------------------------------------------------------------

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float[] incoming = queued;
            if (incoming != null)
            {
                active = incoming;
                cursor = 0;
                queued = null;
            }

            float[] buffer = active;
            if (buffer == null)
            {
                // Not our audio to keep: zero it, since this source's clip is
                // silence anyway and anything already in the buffer is ours
                // from the last block.
                Array.Clear(data, 0, data.Length);
                speaking = false;
                return;
            }

            int frames = data.Length / channels;
            float targetLeft = gainLeft;
            float targetRight = gainRight;

            // Slide onto the new gain across the buffer rather than stepping
            // to it: the value is a frame old and moves in steps, and without
            // the slide a turning camera is audible as a staircase.
            float stepLeft = frames > 0 ? (targetLeft - smoothedLeft) / frames : 0f;
            float stepRight = frames > 0 ? (targetRight - smoothedRight) / frames : 0f;

            int position = cursor;
            int length = buffer.Length;

            for (int i = 0; i < frames; i++)
            {
                smoothedLeft += stepLeft;
                smoothedRight += stepRight;

                float sample = position < length ? buffer[position] : 0f;
                position++;

                int at = i * channels;
                if (channels >= 2)
                {
                    data[at] = sample * smoothedLeft;
                    data[at + 1] = sample * smoothedRight;
                    for (int c = 2; c < channels; c++)
                    {
                        data[at + c] = sample * 0.5f * (smoothedLeft + smoothedRight);
                    }
                }
                else
                {
                    data[at] = sample * 0.5f * (smoothedLeft + smoothedRight);
                }
            }

            smoothedLeft = targetLeft;
            smoothedRight = targetRight;
            cursor = position;

            if (position >= length)
            {
                active = null;
                speaking = false;
            }
        }
    }
}
