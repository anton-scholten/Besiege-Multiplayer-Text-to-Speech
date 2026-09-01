using System;
using MultiplayerTTS.Klatt;

namespace MultiplayerTTS
{
    /// <summary>
    /// Gives each player a voice of their own, derived from their name.
    ///
    /// Deriving it rather than assigning it in order means the same person
    /// sounds the same in every lobby, on every client, without anything
    /// having to be synchronised or stored -- everyone computes the same
    /// answer from a string they all already have.
    ///
    /// The spread is deliberately narrow. These are all recognisably the same
    /// synthesiser, the way DECtalk's voices were all recognisably DECtalk;
    /// the point is to tell two speakers apart, not to sound like different
    /// people.
    /// </summary>
    public static class VoiceBank
    {
        public static KlattVoice ForPlayer(string name, TtsSettings settings)
        {
            uint h = Hash(name);

            KlattVoice v = new KlattVoice();

            // Pitch: 96..168 Hz. Low enough for a bass Paul, high enough for
            // something Betty-adjacent, and never so high that the formant
            // tables (which assume an adult male tract) stop making sense.
            v.Pitch = 96.0 + Slice(h, 0, 8) * 72.0;

            // Head size scales every formant. A small tract with a high pitch
            // reads as a smaller speaker; keeping the two correlated avoids
            // the uncanny combinations.
            double sizeBias = (v.Pitch - 132.0) / 72.0;      // -0.5 .. +0.5
            v.HeadSize = 1.0 + 0.13 * Slice(h, 8, 8) - 0.10 * sizeBias;

            v.Speed = 0.92 + Slice(h, 16, 6) * 0.20;
            v.PitchRange = 0.75 + Slice(h, 22, 6) * 0.65;
            v.Breathiness = 0.02 + Slice(h, 28, 4) * 0.06;

            if (settings != null) v.Speed *= settings.Speed;

            return v;
        }

        /// <summary>
        /// Apply one of DECtalk's named voices, as selected by <c>[:name paul]</c>
        /// or its shorthand <c>[:np]</c>.
        ///
        /// These are approximations, not the originals: the real voices are
        /// full parameter sets, and what is reproduced here is the part a
        /// listener actually identifies them by -- roughly where the pitch
        /// sits, how big the speaker sounds, and how breathy they are.
        /// </summary>
        public static void ApplyNamedVoice(KlattVoice v, string name)
        {
            if (v == null || string.IsNullOrEmpty(name)) return;

            switch (name.Trim().ToLowerInvariant())
            {
                case "paul":   v.Pitch = 122; v.HeadSize = 1.00; v.Breathiness = 0.03; break;
                case "harry":  v.Pitch = 89;  v.HeadSize = 1.12; v.Breathiness = 0.03; break;
                case "frank":  v.Pitch = 105; v.HeadSize = 1.06; v.Breathiness = 0.10; break;
                case "dennis": v.Pitch = 110; v.HeadSize = 1.04; v.Breathiness = 0.05; break;
                case "betty":  v.Pitch = 208; v.HeadSize = 0.86; v.Breathiness = 0.05; break;
                case "ursula": v.Pitch = 240; v.HeadSize = 0.82; v.Breathiness = 0.04; break;
                case "rita":   v.Pitch = 196; v.HeadSize = 0.88; v.Breathiness = 0.08; break;
                case "wendy":  v.Pitch = 200; v.HeadSize = 0.85; v.Breathiness = 0.12; break;
                case "kit":    v.Pitch = 306; v.HeadSize = 0.72; v.Breathiness = 0.04; break;
                case "val":    v.Pitch = 190; v.HeadSize = 0.90; v.Breathiness = 0.06; break;
                default: return;
            }
        }

        /// <summary>
        /// A stable seed for the synthesiser's noise generator, so a player's
        /// fricatives have the same character every time rather than being
        /// reseeded per message.
        /// </summary>
        public static int SeedForPlayer(string name)
        {
            return (int)(Hash(name) & 0x7fffffff);
        }

        // Extract `bits` bits starting at `offset` and scale to 0..1.
        private static double Slice(uint h, int offset, int bits)
        {
            uint mask = (1u << bits) - 1u;
            uint value = (h >> offset) & mask;
            return value / (double)mask;
        }

        /// <summary>
        /// FNV-1a. Chosen because it is four lines and its exact behaviour is
        /// fixed forever -- <c>string.GetHashCode</c> is explicitly not stable
        /// across runtimes, and a voice that changes when Unity is upgraded
        /// would be a genuinely baffling bug.
        /// </summary>
        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            // Avalanche, so adjacent names ("Bob1", "Bob2") do not land on
            // adjacent pitches.
            h ^= h >> 16;
            h *= 2246822507u;
            h ^= h >> 13;
            h *= 3266489909u;
            h ^= h >> 16;
            return h;
        }
    }
}
