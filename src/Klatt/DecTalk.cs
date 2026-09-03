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

        /// <summary>
        /// The voice in force when this item was parsed, or null for the
        /// speaker's own. DECtalk switches voice mid-stream -- a two-hander
        /// written as "[:nh]why? [:nv]because" is the whole point of the
        /// format -- so the voice belongs to the item, not to the message.
        /// </summary>
        public string Voice;

        /// <summary>Words per minute in force when this item was parsed.</summary>
        public double RateWpm;

        /// <summary>
        /// Speaker-definition overrides in force when this item was parsed,
        /// from <c>[:dv]</c>, or null for the speaker as published. Shared
        /// between items rather than copied: the parser replaces the whole
        /// dictionary on every edit, so an item that holds one can never see
        /// a later change.
        /// </summary>
        public Dictionary<string, double> Design;
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

        /// <summary>
        /// The [:dv] edits in force. Replaced wholesale on each edit rather
        /// than mutated, so items already stamped keep the values they were
        /// parsed with.
        /// </summary>
        public Dictionary<string, double> Design;

        /// <summary>
        /// The note a <c>[_&lt;1,29&gt;]</c> marker put in force, in Hz, or
        /// zero for the voice's own contour.
        ///
        /// This is how a copypasta sings ordinary words instead of spelling
        /// them out as phonemes: a silent phoneme carrying a pitch and no
        /// length, then the words. It stays in force until the next marker,
        /// so <c>[_&lt;1,29&gt;]I throw my hoe[_&lt;1,27&gt;]zez</c> is two
        /// notes and not one -- and a message with no marker in it never
        /// leaves zero here, so ordinary speech is untouched.
        /// </summary>
        public double SungPitch;
    }

    /// <summary>
    /// DECtalk's built-in speakers.
    ///
    /// These are the published speaker definitions, transcribed rather than
    /// approximated: every number is a <c>[:dv]</c> option in its own units,
    /// straight out of DECtalk's own "Definitions of DECtalk Software Voices"
    /// table. What made the earlier hand-tuned set wrong was not carelessness
    /// but a wrong reading of one option -- see <c>KlattSynth.FillPitch</c>,
    /// which explains why a measured pitch and a published <c>ap</c> disagree
    /// by design.
    ///
    /// Three things in the table are worth knowing before changing anything:
    ///
    ///   * <b>The women do not have small heads.</b> Every female voice except
    ///     Kit sits at <c>hs</c> 95-100, the same as Paul. What makes them
    ///     female is <c>sx</c>, the pitch, and the higher formants -- Betty,
    ///     Ursula and Wendy run <c>f4</c> above 4400 with <c>b5</c> at 2048,
    ///     which switches the fifth formant off rather than moving it.
    ///   * <b>Breathiness is a main character trait, not a garnish.</b> Frank
    ///     50 dB, Wendy 55, Kit 47, Rita 46, Dennis 38. An earlier version of
    ///     this table had all of them under 0.12 on a 0..1 scale, which is
    ///     most of why they did not sound like different people.
    ///   * <b>Rita is a low voice.</b> <c>ap</c> 106, below Dennis and barely
    ///     above Paul. Assuming a woman's voice is a high one put her at 196.
    ///
    /// Kit's <c>b4</c> and <c>b5</c> of 2048 are not a typo: "if a higher
    /// formant does not exist, the frequency and bandwidth are set to special
    /// values that cause the resonance to disappear ... this was done to the
    /// fourth and fifth formants of Kit's voice".
    /// </summary>
    public static class DecTalkVoices
    {
        /// <summary>
        /// Apply one of DECtalk's speakers, as selected by <c>[:name paul]</c>
        /// or its shorthand <c>[:np]</c>.
        ///
        /// <c>[:nv]</c> is the odd one out. It is not a tenth built-in voice:
        /// DECtalk keeps a user-defined slot called Val, which <c>[:dv save]</c>
        /// writes into -- "saves the changes as the voice of Val". Untouched,
        /// it holds a copy of Paul, which is why a copypasta that switches to
        /// <c>[:nv]</c> without having designed a voice first still speaks.
        /// The tenth *built-in* speaker is Chris, which has no shorthand.
        /// </summary>
        public static void Apply(KlattVoice v, string name)
        {
            if (v == null || string.IsNullOrEmpty(name)) return;

            switch (name.Trim().ToLowerInvariant())
            {
                //          sx  hs   ap   pr   as  qu  bf  hr  sr  br  lx  sm   ri  nf la
                case "paul":
                    Set(v, 1, 100, 122, 100, 100, 40, 18, 18, 32,  0,  3,  70, 3300, 260, 3650,  330); break;
                case "harry":
                    Set(v, 1, 115,  89,  80, 100, 10,  9, 20, 30,  0, 12,  86, 3300, 200, 3850,  240); break;
                case "frank":
                    Set(v, 1,  90, 155,  90,  65,  0,  9, 20, 22, 50, 46,  40, 3650, 280, 4200,  300); break;
                case "dennis":
                    Set(v, 1, 105, 110, 135, 100, 50,  9, 20, 22, 38, 100,  0, 3200, 240, 3600,  280); break;
                case "betty":
                    Set(v, 0, 100, 208, 240,  35, 55,  0, 14, 20,  0,  4,  40, 4450, 260, 2500, 2048); break;
                case "ursula":
                    Set(v, 0,  95, 240, 135, 100, 30,  8, 20, 32,  0, 60, 100, 4450, 260, 2500, 2048); break;
                case "rita":
                    Set(v, 0,  95, 106,  80,  65, 30,  0, 20, 32, 46, 24,  20, 4000, 250, 2500, 2048); break;
                case "wendy":
                    Set(v, 0, 100, 200, 175,  50, 10,  0, 20, 22, 55, 100,  0, 4500, 400, 2500, 2048); break;
                case "kit":
                    Set(v, 0,  80, 306, 210,  65, 50,  0, 20, 22, 47,  5,  40, 2500, 2048, 2500, 2048); break;

                // Val is the user-defined slot and starts as a copy of Paul.
                case "val":
                    Set(v, 1, 100, 122, 100, 100, 40, 18, 18, 32,  0,  3,  70, 3300, 260, 3650,  330); break;

                default: return;
            }
        }

        private static void Set(KlattVoice v, int sx, double hs, double ap, double pr,
                                double asrt, double qu, double bf, double hr, double sr,
                                double br, double sm, double ri,
                                double f4, double b4, double f5, double b5)
        {
            v.Sex = sx;
            v.HeadSize = hs / 100.0;
            v.Pitch = ap;
            v.PitchRange = pr;
            v.Assertiveness = asrt;
            v.Quickness = qu;
            v.BaselineFall = bf;
            v.HatRise = hr;
            v.StressRise = sr;
            v.Breathiness = br;
            v.Smoothness = sm;
            v.Richness = ri;
            v.F4 = f4; v.B4 = b4; v.F5 = f5; v.B5 = b5;
        }
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

        /// <summary>
        /// The silence DECtalk leaves after every <c>[:t]</c> tone, in ms.
        ///
        /// Measured, not guessed. In a recording of
        /// <c>[:t 430,500][:t 320,250]...</c> — the Tetris theme, which is
        /// what these messages are usually for — every tone sounds for
        /// exactly the milliseconds it asked for and is then followed by 712
        /// samples of true silence at DECtalk's 11025 Hz, on every one of the
        /// ten notes and regardless of whether the note was 250 ms or 500 ms.
        ///
        /// Without it a tune is one continuous glissando rather than a run of
        /// separate notes, which is the single largest difference between
        /// this synthesiser's tones and the original's. It is a property of
        /// the tone generator, so it is charged to the message's time budget
        /// like the tone itself.
        /// </summary>
        public const double ToneGapMs = 64.58;

        /// <summary>Longest a single phoneme may be held, in ms.</summary>
        public const double MaxPhonemeMs = 4000.0;

        /// <summary>Longest an entire message may render to, in ms.</summary>
        public const double MaxTotalMs = 20000.0;

        /// <summary>
        /// The words-per-minute figure that corresponds to this synthesiser's
        /// own speed of 1.0.
        ///
        /// DECtalk's default is 200 wpm, but 200 wpm of DECtalk is not the
        /// same length as this synthesiser's default -- its segment durations
        /// come from Klatt's tables rather than from DECtalk's. Rendering a
        /// known recording and comparing total speech time put the two 36%
        /// apart, so a message that asks for DECtalk's default lands here at
        /// 200/270, and one that asks for [:rate 270] runs at this
        /// synthesiser's natural pace.
        /// </summary>
        public const double RateReferenceWpm = 270.0;

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
            plan.RateWpm = 200.0;

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

        /// <summary>
        /// Make an item carrying the voice and rate in force at this point in
        /// the message.
        ///
        /// Stamping happens here, at creation, and not in a pass afterwards.
        /// A pass afterwards is the obvious way to do it and is wrong: the run
        /// of text *before* a "[:nv]" is flushed when that bracket is reached,
        /// so a later pass gives it the voice the bracket switched to -- and
        /// with a "[:np]" at the end of the message, as these copypastas
        /// usually have, every line ends up in Paul's voice.
        /// </summary>
        private static SpeechItem NewItem(SpeechPlan plan, int kind)
        {
            SpeechItem item = new SpeechItem();
            item.Kind = kind;
            item.Voice = plan.Voice;
            item.RateWpm = plan.RateWpm;
            item.Design = plan.Design;
            plan.Items.Add(item);
            return item;
        }

        private static void FlushText(SpeechPlan plan, StringBuilder plain)
        {
            if (plain.Length == 0) return;

            string text = plain.ToString();
            plain.Length = 0;

            if (text.Trim().Length == 0) return;

            SpeechItem item = NewItem(plan, SpeechItem.KindText);
            item.Text = text;
            item.Pitch = plan.SungPitch;
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

            // DECtalk does not require the space: "[:dial67589340]" is what
            // the Moonbase Alpha copypastas are actually written with, and it
            // is accepted. Split a trailing run of argument characters off the
            // verb when the whole token is not a command name on its own.
            if (rest.Length == 0 && !IsKnownVerb(verb))
            {
                int cut = verb.Length;
                while (cut > 0 && IsArgumentChar(verb[cut - 1])) cut--;
                if (cut > 0 && cut < verb.Length)
                {
                    string head = verb.Substring(0, cut);
                    if (IsKnownVerb(head))
                    {
                        rest = verb.Substring(cut);
                        verb = head;
                    }
                }
            }

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

                // ---- the voice designer --------------------------------
                case "dv":
                    Design(plan, rest);
                    return;

                // ---- voices --------------------------------------------
                // The named voices, plus DECtalk's two-letter shorthands.
                // Selecting a speaker drops any [:dv] edits with it. That is
                // DECtalk's own rule -- design-voice changes "are active only
                // while the current speaker remains current" -- and it is what
                // makes a copypasta that switches back and forth between two
                // voices sound the same on the second visit as the first.
                case "name":
                    Select(plan, rest.Trim().ToLowerInvariant());
                    return;

                case "np": Select(plan, "paul"); return;
                case "nb": Select(plan, "betty"); return;
                case "nh": Select(plan, "harry"); return;
                case "nf": Select(plan, "frank"); return;
                case "nd": Select(plan, "dennis"); return;
                case "nu": Select(plan, "ursula"); return;
                case "nr": Select(plan, "rita"); return;
                case "nk": Select(plan, "kit"); return;
                case "nw": Select(plan, "wendy"); return;
                case "nv": Select(plan, "val"); return;

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

        private static bool IsArgumentChar(char c)
        {
            return (c >= '0' && c <= '9') || c == ',' || c == '.'
                || c == '-' || c == '+' || c == '*' || c == '#';
        }

        private static bool IsKnownVerb(string verb)
        {
            switch (verb)
            {
                case "t": case "tone": case "dial": case "phoneme":
                case "rate": case "pitch": case "volume": case "name":
                case "dv":
                case "period": case "comma":
                case "np": case "nb": case "nh": case "nf": case "nd":
                case "nu": case "nr": case "nk": case "nw": case "nv":
                    return true;
                default:
                    return false;
            }
        }

        private static void Select(SpeechPlan plan, string voice)
        {
            plan.Voice = voice;
            plan.Design = null;
        }

        /// <summary>
        /// The known <c>[:dv]</c> options, and the range each one is allowed.
        ///
        /// A name that is not here is skipped rather than guessed at: an
        /// unknown option in DECtalk markup is far more likely to be a typo
        /// than a feature, and silently applying it to the wrong parameter is
        /// worse than ignoring it. "save", which writes the current definition
        /// into Val's slot, is deliberately absent -- it persists across
        /// messages, and a chat mod that let one player permanently redefine a
        /// voice for everyone would be a nuisance at best.
        /// </summary>
        private static bool DesignRange(string option, out double low, out double high)
        {
            switch (option)
            {
                case "sx": low = 0;    high = 1;    return true;
                case "hs": low = 50;   high = 200;  return true;
                case "ap": low = 50;   high = 400;  return true;
                case "pr": low = 0;    high = 400;  return true;
                case "as": low = 0;    high = 100;  return true;
                case "qu": low = 0;    high = 100;  return true;
                case "bf": low = 0;    high = 40;   return true;
                case "hr": low = 0;    high = 100;  return true;
                case "sr": low = 0;    high = 100;  return true;
                case "br": low = 0;    high = 70;   return true;
                case "sm": low = 0;    high = 100;  return true;
                case "ri": low = 0;    high = 100;  return true;
                case "la": low = 0;    high = 100;  return true;
                case "lx": low = 0;    high = 100;  return true;
                case "nf": low = 0;    high = 100;  return true;
                case "f4": low = 500;  high = 6000; return true;
                case "f5": low = 500;  high = 6000; return true;
                case "b4": low = 50;   high = 5500; return true;
                case "b5": low = 50;   high = 5500; return true;
                default:   low = 0;    high = 0;    return false;
            }
        }

        /// <summary>
        /// <c>[:dv ap 100 pr 0]</c> -- the voice designer, as pairs of option
        /// and value. Anything unparseable is skipped and the rest of the
        /// command still applies, which is the same forgiveness the other
        /// commands here give.
        /// </summary>
        private static void Design(SpeechPlan plan, string rest)
        {
            string[] parts = rest.Split(new char[] { ' ', '\t', ',' },
                                        StringSplitOptions.RemoveEmptyEntries);

            Dictionary<string, double> edits = plan.Design == null
                ? new Dictionary<string, double>()
                : new Dictionary<string, double>(plan.Design);

            bool changed = false;
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string option = parts[i].ToLowerInvariant();

                double low, high;
                if (!DesignRange(option, out low, out high)) continue;

                double value;
                if (!Number(parts[i + 1], out value)) continue;

                edits[option] = Clamp(value, low, high);
                changed = true;
            }

            if (changed) plan.Design = edits;
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
            }
            else
            {
                SpeechItem item = NewItem(plan, SpeechItem.KindTone);
                item.Frequency = Clamp(frequency, 20.0, 8000.0);
                item.Duration = ms;
            }

            // Every tone is followed by DECtalk's own gap, so a tune comes out
            // as notes rather than one sliding note. A message that has run
            // out of budget gets whatever is left, which is usually nothing --
            // the gap is never allowed to push the total over.
            double gap = ToneGapMs < budget ? ToneGapMs : budget;
            if (gap > 0.0)
            {
                AddSilence(plan, gap);
                budget -= gap;
            }
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
            // Measured off DECtalk's own output: 100 ms on, 100 ms off, at
            // full scale. The tones are markedly louder than the speech around
            // them, which is a property of the original and not an accident.
            const double toneMs = 100.0;
            const double gapMs = 100.0;

            for (int i = 0; i < digits.Length; i++)
            {
                double low, high;
                if (!Dtmf(digits[i], out low, out high)) continue;
                if (budget < toneMs + gapMs) return;

                SpeechItem a = NewItem(plan, SpeechItem.KindTone);
                a.Frequency = low;
                a.Duration = toneMs;
                a.Pitch = high;          // the second frequency of the pair

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
            NewItem(plan, SpeechItem.KindSilence).Duration = ms;
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

                // "_" is DECtalk's silence, and in these songs it does two
                // jobs. With a real length -- [_<500,22>] -- it is a rest.
                // With a token one -- [_<1,29>] -- it is not heard at all and
                // exists only to put a note under the words that follow it.
                if (name == "_")
                {
                    if (pitch > 0.0) plan.SungPitch = pitch;
                    NewItem(plan, SpeechItem.KindSilence).Duration = duration;
                    continue;
                }

                SpeechItem item = NewItem(plan, SpeechItem.KindPhoneme);
                item.Phoneme = name;
                item.Duration = duration;
                item.Pitch = pitch;
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

                i += length;
                return candidate;
            }
            return null;
        }

        /// <summary>
        /// DECtalk's pitch numbers index a table of notes: one semitone per
        /// step, with <b>1 = C2</b>, so index <c>n</c> is MIDI note
        /// <c>n + 35</c>.
        ///
        /// The anchor is measured, not guessed. Pitch-tracking a recording of
        /// the Spooky Scary Skeletons copypasta, which uses indices 11, 13,
        /// 14, 16, 18 and 19, gives centres of 117, 132, 139, 157, 176 and
        /// 187 Hz. Equal temperament from C2 predicts 116.5 (A#2), 130.8
        /// (C3), 138.6 (C#3), 155.6 (D#3), 174.6 (F3) and 185.0 (F#3) -- all
        /// within a fifth of a semitone, and the residual is a uniform sharp
        /// bias that looks like the tracker rather than the table.
        ///
        /// The first version of this guessed G2 for the bottom, a tritone
        /// too high, so every tune came out in tune with itself but sung by
        /// somebody else.
        /// </summary>
        private static double PitchFromIndex(double index)
        {
            double semitones = Clamp(index, 1.0, 60.0) - 1.0;
            const double bottom = 65.40639;      // C2, MIDI 36
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
