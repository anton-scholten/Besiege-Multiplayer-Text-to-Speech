using System;
using System.Collections.Generic;
using System.Text;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// One thing to render. A message becomes a list of these.
    /// </summary>
    public class SpeechItem
    {
        public const int KindText = 0;       // ordinary words, via the rules
        public const int KindPhoneme = 1;    // one phoneme, spelled out
        public const int KindTone = 2;       // a pure tone
        public const int KindSilence = 3;

        public int Kind;

        public string Text;                  // KindText
        public string Phoneme;               // KindPhoneme

        /// <summary>Milliseconds. Zero on a phoneme means "use its own".</summary>
        public double Duration;

        /// <summary>Hz. Zero on a phoneme means "use the voice's contour".</summary>
        public double Pitch;

        public double Frequency;             // KindTone
    }

    /// <summary>
    /// The state a run of DECtalk markup can change: which voice, how fast,
    /// how high, how loud.
    /// </summary>
    public class SpeechPlan
    {
        public readonly List<SpeechItem> Items = new List<SpeechItem>();

        public double RateWpm = 200.0;       // DECtalk's default
        public double PitchOffset = 0.0;     // semitones, from [:pitch]
        public double Volume = 1.0;
        public string Voice;                 // [:name paul] and friends
        public bool UsedMarkup;
    }

    /// <summary>
    /// Parses DECtalk inline markup — the syntax Moonbase Alpha made famous,
    /// because Moonbase Alpha's text-to-speech <em>was</em> DECtalk.
    ///
    /// <code>
    /// [:dial 6387657]Hello there[:t 350,500][:t 1,500][:t 350,500]
    /// [:phoneme arpabet speak on][dh&lt;200,10&gt;ah&lt;300,14&gt;]
    /// </code>
    ///
    /// Two families of thing appear in square brackets:
    ///
    ///   * <b>commands</b>, which begin with a colon — <c>[:rate 300]</c>,
    ///     <c>[:t 350,500]</c>, <c>[:dial 12345]</c>, <c>[:phoneme on]</c>;
    ///   * <b>phoneme blocks</b>, which do not — <c>[dh&lt;200,10&gt;]</c>, a
    ///     run of ARPAbet phonemes each optionally carrying
    ///     <c>&lt;duration in ms, pitch index&gt;</c>. That second form is what
    ///     makes DECtalk sing, and it maps straight onto this synthesiser,
    ///     which already drives duration and pitch as explicit per-segment
    ///     tracks.
    ///
    /// Everything outside brackets is ordinary text and goes through the
    /// letter-to-sound rules as before, so a message with no markup in it is
    /// unaffected.
    ///
    /// <b>What is deliberately not exact.</b> DECtalk's pitch numbers index a
    /// table this code does not have, so they are read as semitones above a
    /// reference note; the result is in tune with itself and transposed from
    /// whatever the original was. Unknown commands are skipped rather than
    /// spoken, which is the behaviour that keeps a message readable when it
    /// uses something from the reference guide that is not implemented here.
    /// </summary>
    public static class DecTalk
    {
        /// <summary>Longest a single tone may be asked to hold, in ms.</summary>
        public const double MaxToneMs = 4000.0;

        /// <summary>Longest a single phoneme may be held, in ms.</summary>
        public const double MaxPhonemeMs = 4000.0;

        /// <summary>Longest an entire message may render to, in ms.</summary>
        public const double MaxTotalMs = 20000.0;

        /// <summary>
        /// True if the text contains anything this parser would act on. Used
        /// to keep the plain path exactly as it was for messages with no
        /// markup, which is nearly all of them.
        /// </summary>
        public static bool LooksLikeMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int open = text.IndexOf('[');
            return open >= 0 && text.IndexOf(']', open) > open;
        }

        public static SpeechPlan Parse(string text)
        {
            SpeechPlan plan = new SpeechPlan();
            if (string.IsNullOrEmpty(text)) return plan;

            bool phonemeMode = false;
            double budget = MaxTotalMs;
            StringBuilder plain = new StringBuilder();

            int i = 0;
            while (i < text.Length)
            {
                if (text[i] != '[')
                {
                    plain.Append(text[i]);
                    i++;
                    continue;
                }

                int close = text.IndexOf(']', i);
                if (close < 0)
                {
                    // An unclosed bracket is just text. Speaking the rest is
                    // friendlier than swallowing it.
                    plain.Append(text.Substring(i));
                    break;
                }

                string body = text.Substring(i + 1, close - i - 1);
                i = close + 1;

                FlushText(plan, plain);
                plan.UsedMarkup = true;

                if (body.Length > 0 && body[0] == ':')
                {
                    Command(plan, body.Substring(1), ref phonemeMode, ref budget);
                }
                else
                {
                    PhonemeBlock(plan, body, ref budget);
                }
            }

            FlushText(plan, plain);
            return plan;
        }

        private static void FlushText(SpeechPlan plan, StringBuilder plain)
        {
            if (plain.Length == 0) return;

            string text = plain.ToString();
            plain.Length = 0;

            if (text.Trim().Length == 0) return;

            SpeechItem item = new SpeechItem();
            item.Kind = SpeechItem.KindText;
            item.Text = text;
            plan.Items.Add(item);
        }

        // ---------------------------------------------------------------
        // Commands
        // ---------------------------------------------------------------

        private static void Command(SpeechPlan plan, string body,
                                    ref bool phonemeMode, ref double budget)
        {
            body = body.Trim();
            if (body.Length == 0) return;

            string verb, rest;
            Split(body, out verb, out rest);
            verb = verb.ToLowerInvariant();

            switch (verb)
            {
                // ---- tones ---------------------------------------------
                // [:t 350,500] and [:tone 350 500] are the same thing: a pure
                // tone at a frequency for a duration. This is what the joke
                // messages use to imitate a telephone.
                case "t":
                case "tone":
                    Tone(plan, rest, ref budget);
                    return;

                // ---- DTMF ----------------------------------------------
                case "dial":
                    Dial(plan, rest, ref budget);
                    return;

                // ---- phoneme mode --------------------------------------
                // "[:phoneme arpabet speak on]" and "[:phoneme on]" both turn
                // it on; "off" turns it off. The mode itself only affects
                // whether a bare [..] block is read as phonemes, which this
                // parser does anyway -- it is tracked so that turning it off
                // is honoured.
                case "phoneme":
                    phonemeMode = !rest.ToLowerInvariant().Contains("off");
                    return;

                // ---- prosody -------------------------------------------
                case "rate":
                {
                    double wpm;
                    if (Number(rest, out wpm)) plan.RateWpm = Clamp(wpm, 75.0, 600.0);
                    return;
                }

                case "pitch":
                {
                    double p;
                    if (Number(rest, out p))
                    {
                        // DECtalk's pitch is an absolute setting on a scale of
                        // its own; here it is read as an offset in semitones
                        // from the middle of that scale.
                        plan.PitchOffset = Clamp(p - 50.0, -24.0, 24.0) * 0.25;
                    }
                    return;
                }

                case "volume":
                {
                    // "[:volume set 60]" -- the verb in between is ignored.
                    double v;
                    string tail = rest;
                    string word, remainder;
                    Split(tail, out word, out remainder);
                    if (!Number(word, out v)) tail = remainder;
                    if (Number(tail, out v)) plan.Volume = Clamp(v / 100.0, 0.0, 1.0);
                    return;
                }

                // ---- voices --------------------------------------------
                // The named voices, plus DECtalk's two-letter shorthands.
                case "name":
                    plan.Voice = rest.Trim().ToLowerInvariant();
                    return;

                case "np": plan.Voice = "paul"; return;
                case "nb": plan.Voice = "betty"; return;
                case "nh": plan.Voice = "harry"; return;
                case "nf": plan.Voice = "frank"; return;
                case "nd": plan.Voice = "dennis"; return;
                case "nu": plan.Voice = "ursula"; return;
                case "nr": plan.Voice = "rita"; return;
                case "nk": plan.Voice = "kit"; return;
                case "nw": plan.Voice = "wendy"; return;
                case "nv": plan.Voice = "val"; return;

                // ---- pauses --------------------------------------------
                case "period":
                case "comma":
                {
                    double ms;
                    if (Number(rest, out ms)) Silence(plan, ms, ref budget);
                    return;
                }

                default:
                    // Every other DECtalk command -- [:say], [:mode], [:punct],
                    // [:index], [:sync], [:error], [:pronounce] and the rest --
                    // is skipped rather than spoken. Reading an unimplemented
                    // command aloud is far worse than ignoring it.
                    return;
            }
        }

        private static void Tone(SpeechPlan plan, string rest, ref double budget)
        {
            // Accepts "350,500" and "350 500".
            string[] parts = rest.Split(new char[] { ',', ' ', '\t' },
                                        StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return;

            double frequency, ms;
            if (!Number(parts[0], out frequency)) return;
            if (!Number(parts[1], out ms)) return;

            ms = Clamp(ms, 0.0, MaxToneMs);
            if (ms <= 0.0 || budget <= 0.0) return;
            if (ms > budget) ms = budget;
            budget -= ms;

            // A "frequency" below hearing is how these messages write silence:
            // [:t 1,500] is a rest, not a 1 Hz tone.
            if (frequency < 20.0)
            {
                AddSilence(plan, ms);
                return;
            }

            SpeechItem item = new SpeechItem();
            item.Kind = SpeechItem.KindTone;
            item.Frequency = Clamp(frequency, 20.0, 8000.0);
            item.Duration = ms;
            plan.Items.Add(item);
        }

        /// <summary>
        /// <c>[:dial 6387657]</c> — the touch tones for a phone number.
        ///
        /// Each digit is the pair of frequencies its key sends, which is what
        /// a DTMF tone is; rendering both at once is two tone items of the
        /// same length, summed by the synthesiser.
        /// </summary>
        private static void Dial(SpeechPlan plan, string digits, ref double budget)
        {
            const double toneMs = 120.0;
            const double gapMs = 60.0;

            for (int i = 0; i < digits.Length; i++)
            {
                double low, high;
                if (!Dtmf(digits[i], out low, out high)) continue;
                if (budget < toneMs + gapMs) return;

                SpeechItem a = new SpeechItem();
                a.Kind = SpeechItem.KindTone;
                a.Frequency = low;
                a.Duration = toneMs;
                a.Pitch = high;          // the second frequency of the pair
                plan.Items.Add(a);

                AddSilence(plan, gapMs);
                budget -= toneMs + gapMs;
            }
        }

        /// <summary>The standard DTMF row and column frequencies.</summary>
        private static bool Dtmf(char key, out double low, out double high)
        {
            low = 0.0;
            high = 0.0;

            int row, column;
            switch (key)
            {
                case '1': row = 0; column = 0; break;
                case '2': row = 0; column = 1; break;
                case '3': row = 0; column = 2; break;
                case '4': row = 1; column = 0; break;
                case '5': row = 1; column = 1; break;
                case '6': row = 1; column = 2; break;
                case '7': row = 2; column = 0; break;
                case '8': row = 2; column = 1; break;
                case '9': row = 2; column = 2; break;
                case '*': row = 3; column = 0; break;
                case '0': row = 3; column = 1; break;
                case '#': row = 3; column = 2; break;
                default: return false;
            }

            double[] rows = new double[] { 697.0, 770.0, 852.0, 941.0 };
            double[] columns = new double[] { 1209.0, 1336.0, 1477.0 };
            low = rows[row];
            high = columns[column];
            return true;
        }

        private static void Silence(SpeechPlan plan, double ms, ref double budget)
        {
            ms = Clamp(ms, 0.0, MaxToneMs);
            if (ms <= 0.0 || budget <= 0.0) return;
            if (ms > budget) ms = budget;
            budget -= ms;
            AddSilence(plan, ms);
        }

        private static void AddSilence(SpeechPlan plan, double ms)
        {
            SpeechItem item = new SpeechItem();
            item.Kind = SpeechItem.KindSilence;
            item.Duration = ms;
            plan.Items.Add(item);
        }

        // ---------------------------------------------------------------
        // Phoneme blocks
        // ---------------------------------------------------------------

        /// <summary>
        /// A run of ARPAbet phonemes, each optionally followed by
        /// <c>&lt;duration, pitch&gt;</c>. This is the singing syntax.
        ///
        /// Phoneme names are matched longest-first, because the inventory has
        /// one- and two-letter names that prefix each other: a bare "a" must
        /// not eat the front of "ae", and "n" must not eat "ng".
        /// </summary>
        private static void PhonemeBlock(SpeechPlan plan, string body, ref double budget)
        {
            int i = 0;
            int guard = 0;

            while (i < body.Length)
            {
                if (++guard > 512) return;

                if (body[i] == ' ' || body[i] == '\t')
                {
                    i++;
                    continue;
                }

                string name = MatchPhoneme(body, ref i);
                if (name == null)
                {
                    i++;                    // not a phoneme; skip the character
                    continue;
                }

                double duration = 0.0;
                double pitch = 0.0;

                if (i < body.Length && body[i] == '<')
                {
                    int close = body.IndexOf('>', i);
                    if (close > i)
                    {
                        string[] parts = body.Substring(i + 1, close - i - 1)
                            .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) Number(parts[0], out duration);
                        if (parts.Length > 1)
                        {
                            double index;
                            if (Number(parts[1], out index)) pitch = PitchFromIndex(index);
                        }
                        i = close + 1;
                    }
                }

                duration = Clamp(duration, 0.0, MaxPhonemeMs);
                if (duration > 0.0)
                {
                    if (budget <= 0.0) return;
                    if (duration > budget) duration = budget;
                    budget -= duration;
                }

                SpeechItem item = new SpeechItem();
                item.Kind = SpeechItem.KindPhoneme;
                item.Phoneme = name;
                item.Duration = duration;
                item.Pitch = pitch;
                plan.Items.Add(item);
            }
        }

        private static string MatchPhoneme(string body, ref int i)
        {
            // Try three characters, then two, then one.
            for (int length = 3; length >= 1; length--)
            {
                if (i + length > body.Length) continue;

                string candidate = body.Substring(i, length).ToUpperInvariant();
                if (!Phonemes.Has(candidate)) continue;
                if (candidate == "_") continue;

                i += length;
                return candidate;
            }
            return null;
        }

        /// <summary>
        /// DECtalk's pitch numbers index a table of notes. The table itself is
        /// not reproduced here, so the number is read as a semitone step: 1 is
        /// the bottom of the range and each step up is a semitone. A tune
        /// written for DECtalk therefore comes out in tune with itself, and
        /// transposed as a whole.
        /// </summary>
        private static double PitchFromIndex(double index)
        {
            double semitones = Clamp(index, 1.0, 60.0) - 1.0;
            const double bottom = 98.0;          // G2, low in a male range
            return bottom * Math.Pow(2.0, semitones / 12.0);
        }

        // ---------------------------------------------------------------

        private static void Split(string s, out string head, out string tail)
        {
            s = s.Trim();
            int space = s.IndexOf(' ');
            if (space < 0)
            {
                head = s;
                tail = "";
                return;
            }
            head = s.Substring(0, space);
            tail = s.Substring(space + 1).Trim();
        }

        private static bool Number(string s, out double value)
        {
            return double.TryParse(s == null ? "" : s.Trim(),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture,
                                   out value);
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
