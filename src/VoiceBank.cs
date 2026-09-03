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
            // pr and br are DECtalk's own units now: a percentage and
            // decibels, not the 0..1 multipliers this used to mix. The spreads
            // are taken from the range the nine built-in voices actually
            // cover -- pr from Harry's 80 to Betty's 240, br from Paul's 0 to
            // Wendy's 55 -- so a generated voice sounds like it could have
            // been one of them.
            v.PitchRange = 80.0 + Slice(h, 22, 6) * 160.0;
            v.Breathiness = Slice(h, 28, 4) * 55.0;

            if (settings != null) v.Speed *= settings.Speed;

            return v;
        }

        /// <summary>
        /// Apply one of DECtalk's named voices, as selected by
        /// <c>[:name paul]</c> or its shorthand <c>[:np]</c>.
        ///
        /// The table itself lives in <see cref="MultiplayerTTS.Klatt.DecTalkVoices"/>,
        /// with the rest of the DECtalk handling and, more to the point, on the
        /// Unity-free side of the mod so the offline tests can reach it.
        /// </summary>
        public static void ApplyNamedVoice(KlattVoice v, string name)
        {
            DecTalkVoices.Apply(v, name);
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
