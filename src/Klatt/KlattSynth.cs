using System;
using System.Collections.Generic;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// The knobs that make one voice different from another. Defaults are a
    /// mid-range adult male at 122 Hz, which is where DECtalk's "Perfect Paul"
    /// sits and what the formant tables in <see cref="Phonemes"/> assume.
    /// </summary>
    /// <summary>
    /// A DECtalk speaker definition.
    ///
    /// The fields are DECtalk's own <c>[:dv]</c> options, kept under their own
    /// names and in their own units -- percent, Hz, dB -- because that is the
    /// form every published table is in. The first version of this file
    /// converted them on the way in, to a set of tidy 0..1 multipliers, and
    /// the values then drifted: several voices ended up guessed rather than
    /// transcribed, and two of them were wrong by a fifth.
    ///
    /// <see cref="Klatt.DecTalkVoices"/> holds the nine built-in voices.
    /// </summary>
    public class KlattVoice
    {
        public int Sex = 1;                    // sx: 1 male, 0 female
        public double HeadSize = 1.00;         // hs, as a fraction (hs% / 100)

        /// <summary>ap: average pitch, Hz. Not the mean of the rendered
        /// contour -- see <c>FillPitch</c> for how the two relate.</summary>
        public double Pitch = 122.0;

        public double PitchRange = 100.0;      // pr, %
        public double BaselineFall = 18.0;     // bf, Hz
        public double HatRise = 18.0;          // hr, Hz
        public double StressRise = 32.0;       // sr, Hz
        public double Assertiveness = 100.0;   // as, %
        public double Quickness = 40.0;        // qu, %
        public double Breathiness = 0.0;       // br, dB, 0..70
        public double Laryngealization = 0.0;  // la, %
        public double Smoothness = 3.0;        // sm, %
        public double Richness = 70.0;         // ri, %
        public double F4 = 3300.0;             // f4, Hz
        public double B4 = 260.0;              // b4, Hz
        public double F5 = 3650.0;             // f5, Hz
        public double B5 = 330.0;              // b5, Hz

        public double Speed = 1.0;             // >1 is faster; not a DECtalk option
        public double Gain = 1.0;

        public KlattVoice Clone()
        {
            KlattVoice v = new KlattVoice();
            v.Sex = Sex;
            v.HeadSize = HeadSize;
            v.Pitch = Pitch;
            v.PitchRange = PitchRange;
            v.BaselineFall = BaselineFall;
            v.HatRise = HatRise;
            v.StressRise = StressRise;
            v.Assertiveness = Assertiveness;
            v.Quickness = Quickness;
            v.Breathiness = Breathiness;
            v.Laryngealization = Laryngealization;
            v.Smoothness = Smoothness;
            v.Richness = Richness;
            v.F4 = F4; v.B4 = B4; v.F5 = F5; v.B5 = B5;
            v.Speed = Speed;
            v.Gain = Gain;
            return v;
        }
    }

    /// <summary>
    /// A Klatt cascade/parallel formant synthesiser.
    ///
    /// The voiced branch runs the glottal source through a cascade of five
    /// resonators plus a nasal pole/zero pair; frication runs noise through a
    /// single parallel resonator tuned to the phoneme's noise band. That is
    /// the 1980 design with the parallel bank reduced to the one branch this
    /// inventory actually needs, which costs nothing audible for English and
    /// removes a dozen amplitude parameters that would otherwise have to be
    /// tabulated per phoneme.
    ///
    /// Everything here runs on the game thread and produces a finished buffer.
    /// Nothing in this file is touched from the audio callback -- note 07 of
    /// the modding notes is emphatic about that, and pre-rendering also side-
    /// steps the streaming-clip latency trap it warns about.
    /// </summary>
    public class KlattSynth
    {
        private const double FrameMs = 5.0;

        // How often the formant resonators are retuned, in samples. See the
        // comment at the retune site in Render.
        private const int RetuneInterval = 16;

        /// <summary>
        /// Peak amplitude of a generated tone, in the finished buffer.
        ///
        /// Tones are written at their final level and then held out of the
        /// normalisation pass, rather than being levelled along with the
        /// speech. A telephone tone has no "correct" loudness relative to a
        /// sentence, and normalising the two together makes the tone's level
        /// depend on how sibilant the words next to it happened to be -- which
        /// in the first version put it 20 dB under the speech.
        ///
        /// The value is measured off real DECtalk output, where a dialled
        /// digit reaches full scale and is markedly louder than the speech
        /// around it. That is a property of the original, not an accident, so
        /// it is reproduced. A [:t] tone measures 0.996 of full scale -- a
        /// sine fitted to one reads an amplitude of 32642 out of 32767, with
        /// a couple of hundred samples touching the ceiling -- so this is set
        /// just under that. A DTMF pair is the mean of its two frequencies
        /// and so shares the same ceiling rather than doubling.
        /// </summary>
        private const double ToneAmplitude = 0.99;

        /// <summary>
        /// How long a tone's attack and release take, in ms.
        ///
        /// DECtalk does not ramp its tones at all: it gates the sine hard and
        /// lets its output filter do the softening. That measures as roughly
        /// two milliseconds of smearing at each end, and it is a filter's
        /// group delay rather than an envelope -- the samples there lag the
        /// ideal sine instead of shrinking, which is why they can exceed it.
        /// Two milliseconds of raised cosine is indistinguishable by ear and
        /// far simpler than modelling the filter.
        /// </summary>
        private const double ToneEdgeMs = 2.0;

        /// <summary>
        /// Depth of the vibrato on a sung note, as a fraction of its pitch.
        ///
        /// Measured off a sung DECtalk recording, where a note written as
        /// index 19 swings between 184.0 and 190.4 Hz about a centre of
        /// 187 -- so plus or minus 1.7 percent, three times the drift a
        /// spoken vowel gets. It is not subtle and it is not meant to be:
        /// it is most of what separates DECtalk singing from DECtalk
        /// holding a vowel.
        /// </summary>
        private const double SungVibratoDepth = 0.017;

        /// <summary>
        /// Vibrato rate on a sung note, in radians per frame. Frames are 5 ms,
        /// so this is about 6 Hz -- the rate measured off the same recording,
        /// and the same rate the spoken drift already runs at.
        /// </summary>
        private const double SungVibratoRate = 0.19;

        /// <summary>
        /// How long a consonant runs inside a phoneme block, relative to the
        /// length it gets in ordinary speech.
        ///
        /// A block like <c>[spuh&lt;300,19&gt;kiy&lt;300,19&gt;...]</c> gives
        /// a length for the notes only; every consonant between them falls
        /// back on its own. DECtalk's are markedly shorter than this
        /// synthesiser's, because it is fitting them between notes rather
        /// than speaking them.
        ///
        /// Measured over a sung recording of the Spooky Scary Skeletons
        /// verse: its 25 notes account for 7.28 s of a 12.09 s recording, so
        /// the consonants between them take 3.78 s -- where an unscaled
        /// render of the same markup takes 5.09 s.
        ///
        /// This matters more than it sounds like it should, because the error
        /// accumulates. Every consonant is a little long, nothing is audibly
        /// wrong at the start, and by the end of the verse the tune is more
        /// than a second behind where it should be.
        ///
        /// It applies only where a phoneme has no length of its own, so it
        /// reaches nothing outside a phoneme block: ordinary speech is timed
        /// against a different recording and is left alone.
        /// </summary>
        private const double SungConsonantScale = 0.76;

        /// <summary>
        /// How long words sung on a note run, relative to the same words
        /// spoken.
        ///
        /// A <c>[_&lt;1,29&gt;]</c> marker puts a word or a phrase on a note,
        /// and DECtalk fits it to that note rather than reading it. Measured
        /// off a recording of the Hoes-in-Areas copypasta: "I throw my hoe"
        /// takes 840 ms there, where an unclipped render of the same four
        /// words takes about twice that.
        ///
        /// This cannot tell "singing is faster" apart from "this
        /// synthesiser's speech is slow" -- the recording has no spoken line
        /// to compare against. It is confined to sung text for that reason:
        /// the spoken rate is tuned against a different recording, and is
        /// left where it is.
        /// </summary>
        private const double SungTextScale = 0.65;

        /// <summary>
        /// The pitch a DECtalk sentence is built around, in Hz, before the
        /// speaker's own <c>ap</c> and <c>pr</c> are applied. Documented: with
        /// <c>bf</c> at 0 "the reference baseline fundamental frequency of a
        /// sentence begins and ends at 115 Hz".
        /// </summary>
        private const double BaselineHz = 115.0;

        /// <summary>
        /// The pivot in <c>f0' = ap + (f0 - 120) * pr / 100</c>. It is 120 and
        /// not 115: <c>pr</c> expands the swings about 120 Hz while <c>bf</c>
        /// hangs the baseline off 115, and the five hertz between them is in
        /// the published formula rather than a mistake in it.
        /// </summary>
        private const double BaselinePivot = 120.0;

        /// <summary>
        /// How fast the baseline falls, in Hz per second. Fixed in DECtalk
        /// "regardless of the extent of the fall".
        /// </summary>
        private const double BaselineFallRate = 16.0;

        /// <summary>Span of one stressed-syllable rise and fall, in ms.</summary>
        private const double StressPulseMs = 150.0;

        /// <summary>
        /// How much each successive stress rise is reduced within a clause.
        /// DECtalk's rules "reduce the actual height of successive stress rise
        /// and falls"; the amount is not published, and this is the value that
        /// keeps a long sentence from pulsing evenly to the end.
        /// </summary>
        private const double StressDecay = 0.82;

        /// <summary>
        /// How much aspiration a fully breathy voice (<c>br</c> 70 dB) mixes
        /// into its voicing. DECtalk states the range as 0 dB for none to
        /// 70 dB for strong; what a given dB figure means in this
        /// synthesiser's source is a fitting question, and this is the depth
        /// at which Frank and Wendy read as breathy without going to a
        /// whisper.
        /// </summary>
        private const double BreathinessDepth = 0.55;

        /// <summary>
        /// The level speech is normalised to, as RMS over the sounding parts.
        ///
        /// Chosen to sit alongside the Music mod's instrument blocks, which
        /// run near full scale under a shared limiter. Raising this without
        /// the knee below would simply clip.
        /// </summary>
        private const double TargetRms = 0.26;

        /// <summary>Where the soft knee starts, and how sharply it bends.</summary>
        private const double KneeStart = 0.7;
        private const double KneeCurve = 3.0;

        private readonly int sampleRate;
        private readonly Random noise;

        // Cascade branch.
        private readonly Resonator r1, r2, r3, r4, r5;
        private readonly Resonator nasalPole;
        private readonly AntiResonator nasalZero;

        // Parallel frication branch.
        private readonly Resonator fricRes;

        private readonly OnePole tilt;       // spectral tilt on the source
        private readonly OnePole dcBlock;    // note 07: Unity has no output coupling
        private readonly OnePole noiseShape;

        public KlattSynth(int sampleRate, int seed)
        {
            this.sampleRate = sampleRate;
            noise = new Random(seed);

            r1 = new Resonator(sampleRate);
            r2 = new Resonator(sampleRate);
            r3 = new Resonator(sampleRate);
            r4 = new Resonator(sampleRate);
            r5 = new Resonator(sampleRate);
            nasalPole = new Resonator(sampleRate);
            nasalZero = new AntiResonator(sampleRate);
            fricRes = new Resonator(sampleRate);

            tilt = new OnePole(sampleRate, 5000.0);
            dcBlock = new OnePole(sampleRate, 60.0);
            noiseShape = new OnePole(sampleRate, 3000.0);

            // The upper two formants never move in this design: they are
            // above the region that distinguishes English phonemes and exist
            // to give the spectrum a realistic roll-off rather than to carry
            // information.
            r4.SetPole(3500.0, 250.0);
            r5.SetPole(4500.0, 300.0);

            // The nasal pair is fixed too: the pole/zero placement that makes
            // a nasal sound nasal is a property of the nasal cavity, which
            // does not change shape between /m/, /n/ and /ng/. What
            // distinguishes those three is the oral formants, which do move.
            nasalPole.SetPole(270.0, 100.0);
            nasalZero.SetZero(450.0, 100.0);
        }

        // ---------------------------------------------------------------
        // Parameter track construction
        // ---------------------------------------------------------------

        private class Segment
        {
            public Phone Phone;
            public double Duration;    // ms
            public bool IsClosure;     // the silent hold before a stop burst
            public bool StressedVowel;

            /// <summary>Hz, or 0 to follow the voice's own contour.</summary>
            public double Pitch;

            /// <summary>A pure tone instead of speech; 0 for speech.</summary>
            public double ToneHz;

            /// <summary>Second frequency, for a DTMF pair.</summary>
            public double ToneHz2;

            /// <summary>Base F0 for this segment's voice, 0 for the default.</summary>
            public double VoicePitch;
            public double VoiceRange;

            /// <summary>Formant scale for this segment's voice, 0 for default.</summary>
            public double VoiceScale;

            /// <summary>Speaking-rate multiplier this segment was built at.</summary>
            public double VoiceBreath;
        }

        /// <summary>
        /// Render a phoneme sequence to mono float samples in [-1, 1].
        /// </summary>
        public float[] Synthesise(List<string> phonemes, KlattVoice voice, bool question)
        {
            return Render(BuildSegments(phonemes, voice), voice, question);
        }

        /// <summary>
        /// Render a parsed DECtalk plan: text runs through the letter-to-sound
        /// rules as usual, while explicit phonemes keep the duration and pitch
        /// the markup gave them, and tones are generated outright.
        /// </summary>
        public float[] Synthesise(SpeechPlan plan, KlattVoice voice, bool question)
        {
            if (plan == null || plan.Items.Count == 0) return new float[0];

            List<Segment> segments = new List<Segment>();
            segments.Add(NewSegment(Phonemes.Silence, 40.0 / voice.Speed, false));

            for (int i = 0; i < plan.Items.Count; i++)
            {
                SpeechItem item = plan.Items[i];

                // Each item carries the voice and rate that were in force when
                // it was parsed, so a message can change speaker part way
                // through -- which is most of what the DECtalk copypastas do.
                KlattVoice active = voice;
                if (item.Voice != null || item.RateWpm > 0.0 || item.Design != null)
                {
                    active = voice.Clone();
                    DecTalkVoices.Apply(active, item.Voice);
                    ApplyDesign(active, item.Design);
                    if (item.RateWpm > 0.0)
                    {
                        active.Speed = voice.Speed
                                     * (item.RateWpm / DecTalk.RateReferenceWpm);
                        if (active.Speed < 0.3) active.Speed = 0.3;
                        if (active.Speed > 3.0) active.Speed = 3.0;
                    }
                }

                int from = segments.Count;
                bool sungText = item.Kind == SpeechItem.KindText && item.Pitch > 0.0;

                if (item.Kind == SpeechItem.KindText)
                {
                    List<string> phonemes = LetterToSound.Translate(item.Text);
                    if (sungText)
                    {
                        // Words on a note are clipped, and none of them is the
                        // end of anything: the final lengthening that makes a
                        // sentence sound finished would fire on every fragment
                        // between two markers.
                        AppendPhonemes(segments, phonemes, active,
                                       SungTextScale, false);
                    }
                    else
                    {
                        AppendPhonemes(segments, phonemes, active);
                    }
                }
                else if (item.Kind == SpeechItem.KindPhoneme)
                {
                    Phone p = Phonemes.Get(item.Phoneme);
                    // A duration from the markup wins over the phoneme's own,
                    // and is *not* scaled by the speaking rate: a sung note is
                    // a length the author chose, not a rate the reader chose.
                    // A phoneme with no length of its own is a consonant
                    // between two notes, and takes the clipped length those
                    // get in a phoneme block.
                    double duration = item.Duration > 0.0
                        ? item.Duration
                        : p.Duration * SungConsonantScale / active.Speed;

                    if (p.Class == Phonemes.ClassStop || p.Class == Phonemes.ClassAffricate)
                    {
                        segments.Add(NewSegment(
                            p, 45.0 * SungConsonantScale / active.Speed, true));
                    }

                    Segment segment = NewSegment(p, duration, false);
                    segment.Pitch = item.Pitch;
                    segments.Add(segment);
                }
                else if (item.Kind == SpeechItem.KindTone)
                {
                    Segment segment = NewSegment(Phonemes.Silence, item.Duration, false);
                    segment.ToneHz = item.Frequency;
                    segment.ToneHz2 = item.Pitch;      // the DTMF partner, if any
                    segments.Add(segment);
                }
                else
                {
                    segments.Add(NewSegment(Phonemes.Silence, item.Duration, false));
                }

                // Stamp the voice onto everything this item produced, so the
                // renderer can change pitch and tract length between speakers.
                //
                // A note goes on here too when the item is sung text. A
                // [_<1,29>] marker puts one word or phrase on one note, so
                // unlike a phoneme's note it belongs to every segment the
                // letter-to-sound rules produced rather than to one of them.
                for (int k = from; k < segments.Count; k++)
                {
                    segments[k].VoicePitch = active.Pitch;
                    segments[k].VoiceRange = active.PitchRange;
                    segments[k].VoiceScale = active.HeadSize;
                    segments[k].VoiceBreath = active.Breathiness;
                    if (sungText) segments[k].Pitch = item.Pitch;
                }
            }

            segments.Add(NewSegment(Phonemes.Silence, 70.0 / voice.Speed, false));
            return Render(segments, voice, question);
        }

        private float[] Render(List<Segment> segments, KlattVoice voice, bool question)
        {
            if (segments.Count == 0) return new float[0];

            // Total frames.
            double totalMs = 0.0;
            for (int i = 0; i < segments.Count; i++) totalMs += segments[i].Duration;
            int frameCount = (int)Math.Ceiling(totalMs / FrameMs) + 1;
            if (frameCount < 2) return new float[0];

            double[] f0 = new double[frameCount];
            double[] av = new double[frameCount];
            double[] ah = new double[frameCount];
            double[] af = new double[frameCount];
            double[] tf1 = new double[frameCount];
            double[] tf2 = new double[frameCount];
            double[] tf3 = new double[frameCount];
            double[] tb1 = new double[frameCount];
            double[] tb2 = new double[frameCount];
            double[] tb3 = new double[frameCount];
            double[] fricF = new double[frameCount];
            double[] fricB = new double[frameCount];
            double[] nasality = new double[frameCount];
            double[] pitchOverride = new double[frameCount];
            double[] tone1 = new double[frameCount];
            double[] tone2 = new double[frameCount];
            double[] basePitch = new double[frameCount];
            double[] pitchRange = new double[frameCount];
            bool[] stressed = new bool[frameCount];

            FillTracks(segments, voice, frameCount,
                       f0, av, ah, af, tf1, tf2, tf3, tb1, tb2, tb3,
                       fricF, fricB, nasality, pitchOverride, tone1, tone2,
                       basePitch, pitchRange, stressed, question);

            SmoothFormants(tf1, tf2, tf3);

            return Render(voice, frameCount,
                          f0, av, ah, af, tf1, tf2, tf3, tb1, tb2, tb3,
                          fricF, fricB, nasality, tone1, tone2);
        }

        private List<Segment> BuildSegments(List<string> phonemes, KlattVoice voice)
        {
            List<Segment> segments = new List<Segment>();

            // A short lead-in and tail of silence: the first formant transition
            // needs somewhere to come from, and cutting the release of a final
            // stop sounds like a dropped packet.
            segments.Add(NewSegment(Phonemes.Silence, 40.0 / voice.Speed, false));
            AppendPhonemes(segments, phonemes, voice);
            segments.Add(NewSegment(Phonemes.Silence, 70.0 / voice.Speed, false));
            return segments;
        }

        private void AppendPhonemes(List<Segment> segments, List<string> phonemes,
                                    KlattVoice voice)
        {
            AppendPhonemes(segments, phonemes, voice, 1.0, true);
        }

        /// <summary>
        /// Turn a run of phonemes into segments.
        ///
        /// <paramref name="scale"/> multiplies every length, and
        /// <paramref name="finalLengthening"/> switches off the stretch on the
        /// last vowel. Both are for words sung on a note, where the run is a
        /// fragment between two markers rather than an utterance.
        /// </summary>
        private void AppendPhonemes(List<Segment> segments, List<string> phonemes,
                                    KlattVoice voice, double scale,
                                    bool finalLengthening)
        {
            for (int i = 0; i < phonemes.Count; i++)
            {
                Phone p = Phonemes.Get(phonemes[i]);

                if (p.Class == Phonemes.ClassSilence)
                {
                    // A word break. Short -- a full pause only at punctuation,
                    // which the normaliser has already removed.
                    segments.Add(NewSegment(p, 55.0 * scale / voice.Speed, false));
                    continue;
                }

                if (p.Class == Phonemes.ClassStop || p.Class == Phonemes.ClassAffricate)
                {
                    // Closure, then burst. The closure is what makes a stop a
                    // stop; without it /b/ and /w/ are the same sound.
                    bool initial = segments.Count == 0;
                    double closure = initial ? 40.0 : 55.0;
                    Segment hold = NewSegment(p, closure * scale / voice.Speed, true);
                    segments.Add(hold);
                }

                Segment s = NewSegment(p, p.Duration * scale / voice.Speed, false);

                // Lengthen the last vowel of the utterance: final lengthening
                // is most of what makes speech sound like it has ended.
                if (p.Class == Phonemes.ClassVowel && IsLastVowel(phonemes, i))
                {
                    if (finalLengthening) s.Duration *= 1.35;
                    s.StressedVowel = true;
                }
                else if (p.Class == Phonemes.ClassVowel && IsFirstVowel(phonemes, i))
                {
                    s.StressedVowel = true;
                }

                segments.Add(s);
            }
        }

        /// <summary>
        /// Apply the <c>[:dv]</c> edits an item was parsed with.
        ///
        /// The parser has already dropped unknown options and clamped the
        /// values, so this is a plain switch. It runs after the named speaker
        /// so that "[:np][:dv ap 100]" reads left to right: pick Paul, then
        /// change his pitch.
        /// </summary>
        private static void ApplyDesign(KlattVoice v, Dictionary<string, double> design)
        {
            if (v == null || design == null) return;

            foreach (KeyValuePair<string, double> edit in design)
            {
                double value = edit.Value;
                switch (edit.Key)
                {
                    case "sx": v.Sex = (int)value; break;
                    case "hs": v.HeadSize = value / 100.0; break;
                    case "ap": v.Pitch = value; break;
                    case "pr": v.PitchRange = value; break;
                    case "as": v.Assertiveness = value; break;
                    case "qu": v.Quickness = value; break;
                    case "bf": v.BaselineFall = value; break;
                    case "hr": v.HatRise = value; break;
                    case "sr": v.StressRise = value; break;
                    case "br": v.Breathiness = value; break;
                    case "sm": v.Smoothness = value; break;
                    case "ri": v.Richness = value; break;
                    case "la": v.Laryngealization = value; break;
                    case "f4": v.F4 = value; break;
                    case "b4": v.B4 = value; break;
                    case "f5": v.F5 = value; break;
                    case "b5": v.B5 = value; break;
                }
            }
        }

        private static Segment NewSegment(Phone p, double duration, bool closure)
        {
            Segment s = new Segment();
            s.Phone = p;
            s.Duration = duration;
            s.IsClosure = closure;
            return s;
        }

        private static bool IsLastVowel(List<string> phonemes, int index)
        {
            for (int i = index + 1; i < phonemes.Count; i++)
            {
                if (Phonemes.Get(phonemes[i]).Class == Phonemes.ClassVowel) return false;
            }
            return true;
        }

        private static bool IsFirstVowel(List<string> phonemes, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (Phonemes.Get(phonemes[i]).Class == Phonemes.ClassVowel) return false;
            }
            return true;
        }

        private void FillTracks(List<Segment> segments, KlattVoice voice, int frameCount,
                                double[] f0, double[] av, double[] ah, double[] af,
                                double[] tf1, double[] tf2, double[] tf3,
                                double[] tb1, double[] tb2, double[] tb3,
                                double[] fricF, double[] fricB, double[] nasality,
                                double[] pitchOverride, double[] tone1, double[] tone2,
                                double[] basePitch, double[] range, bool[] stressed,
                                bool question)
        {
            int frame = 0;
            for (int s = 0; s < segments.Count && frame < frameCount; s++)
            {
                Segment seg = segments[s];
                Phone p = seg.Phone;
                int length = (int)Math.Round(seg.Duration / FrameMs);
                if (length < 1) length = 1;

                // A segment carries its own voice when the markup changed
                // speaker part way through the message.
                bool stamped = seg.VoiceScale > 0.0;
                double segPitch = stamped ? seg.VoicePitch : voice.Pitch;
                double segRange = stamped ? seg.VoiceRange : voice.PitchRange;
                double segHead = stamped ? seg.VoiceScale : voice.HeadSize;

                // br is in dB over a 0..70 range. Mapped to the fraction of
                // aspiration mixed into voicing, which is what this
                // synthesiser's source takes -- Frank at 50 dB and Wendy at 55
                // are meant to be obviously breathy, not subtly so.
                double segBreath = (stamped ? seg.VoiceBreath : voice.Breathiness)
                                 / 70.0 * BreathinessDepth;
                double scale = 1.0 / segHead;

                for (int k = 0; k < length && frame < frameCount; k++, frame++)
                {
                    double t = length > 1 ? (double)k / (length - 1) : 0.0;
                    basePitch[frame] = segPitch;
                    range[frame] = segRange;
                    stressed[frame] = seg.StressedVowel && !seg.IsClosure;

                    double f1 = p.F1, f2 = p.F2, f3 = p.F3;
                    if (p.Diphthong && !seg.IsClosure)
                    {
                        // Hold the first target for the first third, then glide.
                        double g = t < 0.33 ? 0.0 : (t - 0.33) / 0.67;
                        f1 = p.F1 + (p.F1b - p.F1) * g;
                        f2 = p.F2 + (p.F2b - p.F2) * g;
                        f3 = p.F3 + (p.F3b - p.F3) * g;
                    }

                    tf1[frame] = f1 * scale;
                    tf2[frame] = f2 * scale;
                    tf3[frame] = f3 * scale;
                    tb1[frame] = p.B1;
                    tb2[frame] = p.B2;
                    tb3[frame] = p.B3;

                    if (seg.IsClosure)
                    {
                        // Silence, except that a voiced stop keeps a weak
                        // low-frequency voice bar running through the hold.
                        av[frame] = p.Voiced ? 0.10 : 0.0;
                        ah[frame] = 0.0;
                        af[frame] = 0.0;
                        tb1[frame] = 300.0;
                        tb2[frame] = 300.0;
                    }
                    else
                    {
                        av[frame] = p.Amplitude;
                        ah[frame] = p.Aspiration + (p.Amplitude > 0.0 ? segBreath : 0.0);
                        af[frame] = p.Frication;
                    }

                    fricF[frame] = p.FricPole > 0.0 ? p.FricPole * scale : 4000.0;
                    fricB[frame] = p.FricBw > 0.0 ? p.FricBw : 1000.0;
                    nasality[frame] = p.Class == Phonemes.ClassNasal ? 1.0 : 0.0;

                    // A note the markup asked for, and a tone instead of
                    // speech. Both are held flat across the segment: a sung
                    // note that drifted would be out of tune, and a telephone
                    // tone that drifted would not be a telephone tone.
                    pitchOverride[frame] = seg.Pitch;
                    tone1[frame] = seg.ToneHz;
                    tone2[frame] = seg.ToneHz2;
                }
            }

            // Anything past the last segment stays silent.
            double tailScale = 1.0 / voice.HeadSize;
            for (; frame < frameCount; frame++)
            {
                // These frames are past the last segment and so have never
                // been written. Set them outright rather than testing for a
                // zero: "[:dv pr 0]" is a monotone voice, not an unset one.
                basePitch[frame] = voice.Pitch;
                range[frame] = voice.PitchRange;
                tf1[frame] = 500.0 * tailScale;
                tf2[frame] = 1500.0 * tailScale;
                tf3[frame] = 2500.0 * tailScale;
                tb1[frame] = 100.0; tb2[frame] = 100.0; tb3[frame] = 200.0;
                fricF[frame] = 4000.0; fricB[frame] = 1000.0;
            }

            FillPitch(f0, av, voice, basePitch, range, stressed, question);

            // An explicit note replaces the contour outright, rather than
            // being added to it: markup that says a pitch means that pitch.
            // No declination and no terminal fall, which is what a recording
            // of a sung DECtalk line shows -- the last note of the verse sits
            // at the same frequency as the same index in the first bar.
            //
            // The vibrato is not part of that. A sung note is held with a
            // clear one, and without it the line is a run of test tones.
            for (int i = 0; i < frameCount; i++)
            {
                if (pitchOverride[i] > 0.0)
                {
                    f0[i] = pitchOverride[i]
                          * (1.0 + SungVibratoDepth * Math.Sin(i * SungVibratoRate));
                }
            }
        }

        /// <summary>
        /// DECtalk's fundamental-frequency contour.
        ///
        /// This is the published model, not an approximation of how it sounds.
        /// Everything is computed in DECtalk's own reference frame, where a
        /// sentence sits around 115 Hz whoever is speaking, and only the last
        /// step moves it to the speaker:
        ///
        /// <list type="number">
        /// <item>a <b>baseline</b> that starts at <c>115 + bf/2</c> and falls
        ///   at a fixed 16 Hz per second until it reaches <c>115 - bf/2</c>,
        ///   then holds;</item>
        /// <item>a <b>hat</b>: the contour steps up by <c>hr</c> on the first
        ///   stressed syllable, stays on that plateau, and falls again on the
        ///   last stressed syllable -- the shape linguists name after jumping
        ///   from the brim of a hat to its top and back;</item>
        /// <item>a <b>stress rise</b> of <c>sr</c> Hz on each stressed
        ///   syllable, a rise-and-fall over about 150 ms, added on top of the
        ///   hat and reduced on each successive stress;</item>
        /// <item>a <b>terminal move</b> whose depth is <c>as</c>: an assertive
        ///   voice ends a statement with a conclusive fall, an unassertive one
        ///   trails slightly upward;</item>
        /// <item>a first-order lag from <c>qu</c>, because a larynx reaches a
        ///   new pitch gradually -- 100 ms to get 70% of the way there at
        ///   <c>qu</c> 10, 50 ms at <c>qu</c> 90;</item>
        /// <item>and finally <c>f0' = ap + (f0 - 120) * pr / 100</c>, which is
        ///   the only step that knows who is talking.</item>
        /// </list>
        ///
        /// That last line is worth dwelling on, because it explains a
        /// contradiction that cost a session. Harry's published <c>ap</c> is
        /// 89 Hz, but pitch-tracking real DECtalk output puts Harry's voice
        /// around 105 -- so the table looked wrong and was "corrected" here to
        /// 105. Both are right. <c>ap</c> is where the *baseline* lands, not
        /// where the contour spends its time: a peak of the internal contour
        /// at 140 Hz maps, with Harry's <c>pr</c> of 80, to
        /// 89 + (140-120) * 0.8 = 105 Hz. Reading a measured peak as if it
        /// were <c>ap</c> is what put six of the nine voices out.
        ///
        /// <paramref name="basePitch"/> and <paramref name="range"/> carry
        /// <c>ap</c> and <c>pr</c> per frame rather than per message, so a
        /// message that changes speaker part way through changes voice at the
        /// right frame instead of retuning the whole contour.
        /// </summary>
        private void FillPitch(double[] f0, double[] av, KlattVoice voice,
                               double[] basePitch, double[] range,
                               bool[] stressed, bool question)
        {
            int n = f0.Length;

            // Where the hat sits: from the first stressed syllable to the
            // last. With no stress marked at all there is no hat, which is
            // what a single unstressed grunt should sound like.
            int hatFrom = -1, hatTo = -1;
            for (int i = 0; i < n; i++)
            {
                if (!stressed[i]) continue;
                if (hatFrom < 0) hatFrom = i;
                hatTo = i;
            }

            // The stress pulses, as (centre frame, height) pairs. DECtalk
            // reduces each successive rise within a clause, so the first
            // stress of a sentence is its strongest.
            List<int> pulses = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!stressed[i]) continue;
                if (pulses.Count > 0 && i - pulses[pulses.Count - 1] < 8) continue;
                pulses.Add(i);
            }

            double top = BaselineHz + voice.BaselineFall * 0.5;
            double bottom = BaselineHz - voice.BaselineFall * 0.5;
            int pulseWidth = (int)Math.Round(StressPulseMs / FrameMs);
            if (pulseWidth < 2) pulseWidth = 2;

            double[] target = new double[n];
            for (int i = 0; i < n; i++)
            {
                double seconds = i * FrameMs / 1000.0;
                double baseline = top - BaselineFallRate * seconds;
                if (baseline < bottom) baseline = bottom;

                double value = baseline;

                // The hat plateau.
                if (hatFrom >= 0 && i >= hatFrom && i < hatTo) value += voice.HatRise;

                // The local rise and fall on each stressed syllable.
                for (int k = 0; k < pulses.Count; k++)
                {
                    int offset = i - pulses[k];
                    if (offset < 0 || offset >= pulseWidth) continue;
                    double shape = Math.Sin(Math.PI * offset / pulseWidth);
                    double fade = Math.Pow(StressDecay, k);
                    value += voice.StressRise * shape * fade;
                }

                // The terminal move, over the tail of the utterance.
                double t = n > 1 ? (double)i / (n - 1) : 0.0;
                if (t > 0.8)
                {
                    double u = (t - 0.8) / 0.2;
                    double depth = voice.HatRise * voice.Assertiveness / 100.0;
                    value += question ? depth * u * 1.4 : -depth * u;
                }

                target[i] = value;
            }

            // The larynx lags the command. qu 10 is a 100 ms time constant and
            // qu 90 a 50 ms one, so the constant falls by 0.625 ms per percent.
            double tau = 106.25 - 0.625 * Clamp(voice.Quickness, 0.0, 100.0);
            if (tau < 10.0) tau = 10.0;
            double follow = 1.0 - Math.Exp(-FrameMs / tau);

            double smoothed = target.Length > 0 ? target[0] : BaselineHz;
            for (int i = 0; i < n; i++)
            {
                smoothed += (target[i] - smoothed) * follow;

                // Both tracks are written for every frame by FillTracks, so
                // there is no "unset" case to fall back from -- and there must
                // not be one, because zero is a real pitch range. DECtalk's own
                // example of a monotone is "[:nh][:dv ap 90 pr 0]", which a
                // fallback on zero turns back into ordinary intonation.
                double value = basePitch[i]
                             + (smoothed - BaselinePivot) * range[i] / 100.0;

                // Laryngealization: the voice goes irregular at the edges of a
                // sentence, which is the creak DECtalk's la option controls.
                // It is deliberately confined to the first and last fifth --
                // "many speakers turn voicing on and off irregularly at the
                // beginnings and ends of sentences" is the whole description.
                double la = voice.Laryngealization / 100.0;
                if (la > 0.0)
                {
                    double t = n > 1 ? (double)i / (n - 1) : 0.0;
                    double edge = t < 0.2 ? 1.0 - t / 0.2
                                : (t > 0.8 ? (t - 0.8) / 0.2 : 0.0);
                    if (edge > 0.0)
                    {
                        value *= 1.0 - la * edge * 0.30 * (0.5 + 0.5 * Math.Sin(i * 1.7));
                    }
                }

                // A slow drift keeps a long vowel from sounding like a held
                // test tone. Small enough not to read as wobble.
                value *= 1.0 + 0.006 * Math.Sin(i * 0.19) + 0.004 * Math.Sin(i * 0.041);

                f0[i] = value < 50.0 ? 50.0 : value;
            }
        }

        /// <summary>
        /// Low-pass the formant tracks so they move like a tongue rather than
        /// jumping at every segment boundary. This single step is most of the
        /// coarticulation in the system: it is what bends a vowel towards the
        /// locus of the consonant beside it.
        /// </summary>
        private static void SmoothFormants(double[] f1, double[] f2, double[] f3)
        {
            Smooth(f1, 0.30);
            Smooth(f2, 0.26);
            Smooth(f3, 0.34);
        }

        private static void Smooth(double[] track, double alpha)
        {
            // Forward then backward, so the transition is centred on the
            // boundary instead of lagging it.
            for (int i = 1; i < track.Length; i++)
            {
                track[i] = track[i - 1] + alpha * (track[i] - track[i - 1]);
            }
            for (int i = track.Length - 2; i >= 0; i--)
            {
                track[i] = track[i + 1] + alpha * (track[i] - track[i + 1]);
            }
        }

        // ---------------------------------------------------------------
        // Sample generation
        // ---------------------------------------------------------------

        private float[] Render(KlattVoice voice, int frameCount,
                               double[] f0, double[] av, double[] ah, double[] af,
                               double[] tf1, double[] tf2, double[] tf3,
                               double[] tb1, double[] tb2, double[] tb3,
                               double[] fricF, double[] fricB, double[] nasality,
                               double[] tone1, double[] tone2)
        {
            int samplesPerFrame = (int)Math.Round(sampleRate * FrameMs / 1000.0);
            if (samplesPerFrame < 1) samplesPerFrame = 1;

            int total = frameCount * samplesPerFrame;
            float[] output = new float[total];
            bool[] isTone = new bool[total];

            double phase = 0.0;        // samples since the last glottal opening
            double period = sampleRate / f0[0];
            // ri sets the fraction of the glottal period the folds are open.
            // A rich, brilliant voice closes abruptly -- a short open phase --
            // and a mellow one closes gently. DECtalk's own illustration is
            // "[:dv ri 0 sm 70]" for mellow against "[:dv ri 90 sm 0]" for
            // forceful, so the mapping runs the same way round.
            double openPhase = 0.70 - 0.30 * Clamp(voice.Richness, 0.0, 100.0) / 100.0;
            double nyquist = sampleRate * 0.45;

            // sm tilts the voicing source, attenuating the top of its spectrum
            // by as much as 30 dB at 100% and not at all at 0. A cutoff that
            // falls by a decade over the range gives that, and leaves Paul's
            // sm of 3 essentially where the fixed value used to be.
            tilt.SetCutoff(sampleRate,
                           5000.0 * Math.Pow(0.1, Clamp(voice.Smoothness, 0.0, 100.0) / 100.0));

            // The higher formants are fixed per speaker rather than per
            // phoneme. DECtalk switches one off by putting it above 5500 Hz
            // with a 5500 Hz bandwidth, which is why Kit's b4 and b5 are huge
            // -- a resonance that wide is no longer a resonance.
            r4.SetPole(Clamp(voice.F4 / voice.HeadSize, 500.0, nyquist), voice.B4);
            r5.SetPole(Clamp(voice.F5 / voice.HeadSize, 500.0, nyquist), voice.B5);
            int retune = 0;

            // Phase accumulators for the generated tones. Kept across the
            // whole render rather than reset per segment, so a tone that
            // continues over a frame boundary has no discontinuity in it.
            double tonePhase1 = 0.0;
            double tonePhase2 = 0.0;

            int write = 0;
            for (int frame = 0; frame < frameCount; frame++)
            {
                // Interpolate every parameter across the frame so nothing
                // steps. A stepped formant is audible as a click.
                int next = frame + 1 < frameCount ? frame + 1 : frame;

                for (int k = 0; k < samplesPerFrame; k++, write++)
                {
                    double t = (double)k / samplesPerFrame;

                    double cf1 = Lerp(tf1[frame], tf1[next], t);
                    double cf2 = Lerp(tf2[frame], tf2[next], t);
                    double cf3 = Lerp(tf3[frame], tf3[next], t);
                    double cb1 = Lerp(tb1[frame], tb1[next], t);
                    double cb2 = Lerp(tb2[frame], tb2[next], t);
                    double cb3 = Lerp(tb3[frame], tb3[next], t);
                    double cav = Lerp(av[frame], av[next], t);
                    double cah = Lerp(ah[frame], ah[next], t);
                    double caf = Lerp(af[frame], af[next], t);
                    double cf0 = Lerp(f0[frame], f0[next], t);
                    double cfr = Lerp(fricF[frame], fricF[next], t);
                    double cfb = Lerp(fricB[frame], fricB[next], t);
                    double cnas = Lerp(nasality[frame], nasality[next], t);

                    // ---- generated tones ---------------------------------
                    // A tone replaces speech for its segment rather than
                    // mixing with it: [:t] and [:dial] are telephone noises,
                    // not something said over the top of a voice. Frequencies
                    // are stepped, not interpolated -- sliding between two
                    // DTMF digits would not be DTMF.
                    double toneA = tone1[frame];
                    if (toneA > 0.0)
                    {
                        double toneB = tone2[frame];

                        tonePhase1 += 2.0 * Math.PI * toneA / sampleRate;
                        if (tonePhase1 > 2.0 * Math.PI) tonePhase1 -= 2.0 * Math.PI;

                        double value = Math.Sin(tonePhase1);
                        if (toneB > 0.0)
                        {
                            tonePhase2 += 2.0 * Math.PI * toneB / sampleRate;
                            if (tonePhase2 > 2.0 * Math.PI) tonePhase2 -= 2.0 * Math.PI;
                            // A DTMF digit is the two frequencies at equal
                            // level; halved so the pair does not sit twice as
                            // loud as a single tone.
                            value = (value + Math.Sin(tonePhase2)) * 0.5;
                        }

                        // The edges are ramped afterwards, by RampTones: the
                        // ramp has to be measured in samples, and in here the
                        // only thing to hand is which 5 ms frame this is.
                        output[write] = (float)(value * ToneAmplitude * voice.Gain);
                        isTone[write] = true;
                        continue;
                    }
                    tonePhase1 = 0.0;
                    tonePhase2 = 0.0;

                    // Retuning a resonator costs an exp and a cos, and doing
                    // that four times per sample is most of the synthesiser's
                    // run time. Formants move at speaking-articulator rates --
                    // tens of Hz -- so updating every RetuneInterval samples
                    // is inaudible and roughly an order of magnitude cheaper.
                    if (retune == 0)
                    {
                        r1.SetPole(Clamp(cf1, 150.0, nyquist), cb1);
                        r2.SetPole(Clamp(cf2, 400.0, nyquist), cb2);
                        r3.SetPole(Clamp(cf3, 900.0, nyquist), cb3);
                        fricRes.SetPole(Clamp(cfr, 200.0, nyquist), cfb);
                    }
                    if (++retune >= RetuneInterval) retune = 0;

                    // ---- glottal source ----------------------------------
                    period = sampleRate / cf0;
                    phase += 1.0;
                    if (phase >= period) phase -= period;

                    double open = period * openPhase;
                    double excitation = 0.0;
                    if (phase < open && open > 1.0)
                    {
                        // Derivative of Klatt's polynomial glottal pulse:
                        // rises gently, then a sharp negative spike at closure.
                        double x = phase;
                        excitation = (2.0 * x * open - 3.0 * x * x) / (open * open);
                    }

                    double aspiration = (Next() * 2.0 - 1.0);
                    aspiration = noiseShape.LowPass(aspiration);

                    double source = tilt.LowPass(excitation * cav) + aspiration * cah * 0.30;

                    // ---- cascade -----------------------------------------
                    double cascade = source;
                    if (cnas > 0.001)
                    {
                        double nasal = nasalPole.Step(nasalZero.Step(cascade));
                        cascade = cascade + (nasal - cascade) * cnas;
                    }

                    cascade = r5.Step(cascade);
                    cascade = r4.Step(cascade);
                    cascade = r3.Step(cascade);
                    cascade = r2.Step(cascade);
                    cascade = r1.Step(cascade);

                    // ---- parallel frication ------------------------------
                    double fric = 0.0;
                    if (caf > 0.001)
                    {
                        double n = (Next() * 2.0 - 1.0);
                        fric = fricRes.Step(n) * caf * 2.2;
                    }

                    double sample = cascade * 2.0 + fric;
                    sample = dcBlock.HighPass(sample);

                    output[write] = (float)sample;
                }
            }

            RampTones(output, isTone);
            Normalise(output, voice.Gain, isTone);
            FadeEdges(output);
            return output;
        }

        /// <summary>
        /// Level the finished utterance.
        ///
        /// A Klatt cascade's output level swings wildly with the vowel -- the
        /// gain of five stacked resonators depends on how close together the
        /// formants happen to be -- so an open /AA/ can come out several times
        /// louder than an /IY/ from identical source amplitude. Normalising
        /// per utterance is what stops one chat line arriving twice as loud as
        /// the last.
        ///
        /// <b>Loudness is set by RMS and the peaks are handled by a knee</b>,
        /// rather than by scaling the whole utterance down until its loudest
        /// transient fits under a ceiling. Speech has a high crest factor -- a
        /// plosive or a sibilant runs far above the average -- so a peak
        /// ceiling decides the level from one or two samples and leaves
        /// everything else quiet. That is why this used to sit well below the
        /// Music mod's instrument blocks: those reach near full scale and
        /// round their peaks off with a soft knee, so the same knee is used
        /// here, with the same 0.7 threshold and curve.
        /// </summary>
        private static void Normalise(float[] buffer, double gain, bool[] isTone)
        {
            double peak = 0.0, sum = 0.0;
            int counted = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                double s = buffer[i];
                if (double.IsNaN(s) || double.IsInfinity(s)) { buffer[i] = 0f; continue; }
                if (isTone != null && isTone[i]) continue;   // already at its level

                double a = s < 0.0 ? -s : s;
                if (a > peak) peak = a;
                // Only count samples that are actually sounding, so the lead-in
                // silence and the closures do not drag the average down and
                // make a short word louder than a long one.
                if (a > 0.005) { sum += s * s; counted++; }
            }

            // A message that is nothing but tones -- "[:dial 911]" on its own
            // -- has no speech to level, and its tones are already right.
            if (peak < 1e-6 || counted == 0) return;

            double rms = Math.Sqrt(sum / counted);
            double scale = (TargetRms / rms) * gain;

            for (int i = 0; i < buffer.Length; i++)
            {
                if (isTone != null && isTone[i]) continue;
                buffer[i] = (float)Knee(buffer[i] * scale);
            }
        }

        /// <summary>
        /// The Music mod's soft knee, so speech and its instrument blocks
        /// round their peaks off the same way: linear to 0.7, then a curve
        /// that approaches 1 and never reaches it.
        /// </summary>
        private static double Knee(double s)
        {
            if (s > KneeStart)
            {
                return KneeStart + (1.0 - KneeStart)
                     * (1.0 - 1.0 / (1.0 + (s - KneeStart) * KneeCurve));
            }
            if (s < -KneeStart)
            {
                return -KneeStart - (1.0 - KneeStart)
                     * (1.0 - 1.0 / (1.0 - (s + KneeStart) * KneeCurve));
            }
            return s;
        }

        /// <summary>
        /// Give every run of tone samples a short raised-cosine attack and
        /// release.
        ///
        /// A hard-gated sine clicks, so the edges have to be softened; see
        /// <see cref="ToneEdgeMs"/> for how long the original takes over it.
        ///
        /// This is a pass over the finished samples rather than a term inside
        /// the render loop because the loop knows only which 5 ms frame it is
        /// in. A ramp quantised to frames is four stair steps over 15 ms,
        /// which is not a de-click but an audibly soft attack -- and with a
        /// gap now between tones it would apply to every note of a tune
        /// rather than only to the ends of a run.
        /// </summary>
        private void RampTones(float[] buffer, bool[] isTone)
        {
            int fade = (int)Math.Round(sampleRate * ToneEdgeMs / 1000.0);
            if (fade < 1) return;

            int i = 0;
            while (i < buffer.Length)
            {
                if (!isTone[i]) { i++; continue; }

                int start = i;
                while (i < buffer.Length && isTone[i]) i++;
                int length = i - start;

                // A tone shorter than two ramps gets two half-length ones, so
                // a very short note still fades rather than clicking.
                int edge = fade * 2 <= length ? fade : length / 2;
                for (int e = 0; e < edge; e++)
                {
                    double g = 0.5 - 0.5 * Math.Cos(Math.PI * (e + 1) / (edge + 1));
                    buffer[start + e] *= (float)g;
                    buffer[start + length - 1 - e] *= (float)g;
                }
            }
        }

        /// <summary>
        /// A buffer that starts or ends on a non-zero sample is a click. The
        /// synthesiser starts and ends in silence, but the DC blocker has a
        /// transient of its own, so both ends get a short ramp regardless.
        /// </summary>
        private void FadeEdges(float[] buffer)
        {
            int fade = sampleRate / 200;   // 5 ms
            if (fade * 2 >= buffer.Length) fade = buffer.Length / 4;
            if (fade <= 0) return;

            for (int i = 0; i < fade; i++)
            {
                float g = (float)i / fade;
                buffer[i] *= g;
                buffer[buffer.Length - 1 - i] *= g;
            }
        }

        private double Next()
        {
            return noise.NextDouble();
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
