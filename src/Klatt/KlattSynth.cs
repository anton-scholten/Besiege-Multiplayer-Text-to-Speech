using System;
using System.Collections.Generic;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// The knobs that make one voice different from another. Defaults are a
    /// mid-range adult male at 122 Hz, which is where DECtalk's "Perfect Paul"
    /// sits and what the formant tables in <see cref="Phonemes"/> assume.
    /// </summary>
    public class KlattVoice
    {
        public double Pitch = 122.0;       // Hz, the baseline before declination
        public double PitchRange = 1.0;    // multiplier on the contour's excursions
        public double Speed = 1.0;         // >1 is faster
        public double HeadSize = 1.0;      // scales every formant; >1 is a longer tract
        public double Breathiness = 0.04;  // aspiration mixed into voicing
        public double Gain = 1.0;

        public KlattVoice Clone()
        {
            KlattVoice v = new KlattVoice();
            v.Pitch = Pitch;
            v.PitchRange = PitchRange;
            v.Speed = Speed;
            v.HeadSize = HeadSize;
            v.Breathiness = Breathiness;
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
        /// </summary>
        private const double ToneAmplitude = 0.26;

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

                if (item.Kind == SpeechItem.KindText)
                {
                    List<string> phonemes = LetterToSound.Translate(item.Text);
                    AppendPhonemes(segments, phonemes, voice);
                }
                else if (item.Kind == SpeechItem.KindPhoneme)
                {
                    Phone p = Phonemes.Get(item.Phoneme);
                    // A duration from the markup wins over the phoneme's own,
                    // and is *not* scaled by the speaking rate: a sung note is
                    // a length the author chose, not a rate the reader chose.
                    double duration = item.Duration > 0.0
                        ? item.Duration
                        : p.Duration / voice.Speed;

                    if (p.Class == Phonemes.ClassStop || p.Class == Phonemes.ClassAffricate)
                    {
                        segments.Add(NewSegment(p, 45.0 / voice.Speed, true));
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

            FillTracks(segments, voice, frameCount,
                       f0, av, ah, af, tf1, tf2, tf3, tb1, tb2, tb3,
                       fricF, fricB, nasality, pitchOverride, tone1, tone2, question);

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
            for (int i = 0; i < phonemes.Count; i++)
            {
                Phone p = Phonemes.Get(phonemes[i]);

                if (p.Class == Phonemes.ClassSilence)
                {
                    // A word break. Short -- a full pause only at punctuation,
                    // which the normaliser has already removed.
                    segments.Add(NewSegment(p, 55.0 / voice.Speed, false));
                    continue;
                }

                if (p.Class == Phonemes.ClassStop || p.Class == Phonemes.ClassAffricate)
                {
                    // Closure, then burst. The closure is what makes a stop a
                    // stop; without it /b/ and /w/ are the same sound.
                    bool initial = segments.Count == 0;
                    double closure = initial ? 40.0 : 55.0;
                    Segment hold = NewSegment(p, closure / voice.Speed, true);
                    segments.Add(hold);
                }

                Segment s = NewSegment(p, p.Duration / voice.Speed, false);

                // Lengthen the last vowel of the utterance: final lengthening
                // is most of what makes speech sound like it has ended.
                if (p.Class == Phonemes.ClassVowel && IsLastVowel(phonemes, i))
                {
                    s.Duration *= 1.35;
                    s.StressedVowel = true;
                }
                else if (p.Class == Phonemes.ClassVowel && IsFirstVowel(phonemes, i))
                {
                    s.StressedVowel = true;
                }

                segments.Add(s);
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
                                bool question)
        {
            double scale = 1.0 / voice.HeadSize;

            int frame = 0;
            for (int s = 0; s < segments.Count && frame < frameCount; s++)
            {
                Segment seg = segments[s];
                Phone p = seg.Phone;
                int length = (int)Math.Round(seg.Duration / FrameMs);
                if (length < 1) length = 1;

                for (int k = 0; k < length && frame < frameCount; k++, frame++)
                {
                    double t = length > 1 ? (double)k / (length - 1) : 0.0;

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
                        ah[frame] = p.Aspiration + (p.Amplitude > 0.0 ? voice.Breathiness : 0.0);
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
            for (; frame < frameCount; frame++)
            {
                tf1[frame] = 500.0 * scale;
                tf2[frame] = 1500.0 * scale;
                tf3[frame] = 2500.0 * scale;
                tb1[frame] = 100.0; tb2[frame] = 100.0; tb3[frame] = 200.0;
                fricF[frame] = 4000.0; fricB[frame] = 1000.0;
            }

            FillPitch(f0, av, voice, question);

            // An explicit note replaces the contour outright, rather than
            // being added to it: markup that says a pitch means that pitch.
            for (int i = 0; i < frameCount; i++)
            {
                if (pitchOverride[i] > 0.0) f0[i] = pitchOverride[i];
            }
        }

        /// <summary>
        /// The pitch contour. Perfect Paul is famously flat, and most of what
        /// keeps it from sounding like a buzzer is declination plus a terminal
        /// move -- so that is what this does, with a small rise on the first
        /// and last vowel and nothing else.
        /// </summary>
        private void FillPitch(double[] f0, double[] av, KlattVoice voice, bool question)
        {
            int n = f0.Length;
            double baseF0 = voice.Pitch;
            double range = voice.PitchRange;

            for (int i = 0; i < n; i++)
            {
                double t = n > 1 ? (double)i / (n - 1) : 0.0;

                // Declination: pitch drifts down across an utterance.
                double declination = 1.06 - 0.16 * t * range;

                // Terminal contour over the last fifth.
                double terminal = 1.0;
                if (t > 0.8)
                {
                    double u = (t - 0.8) / 0.2;
                    terminal = question
                        ? 1.0 + 0.28 * u * range     // rise
                        : 1.0 - 0.14 * u * range;    // fall
                }

                // A slow vibrato-ish drift keeps long vowels from sounding
                // like a held test tone. Small enough not to read as wobble.
                double drift = 1.0 + 0.006 * Math.Sin(i * 0.19) + 0.004 * Math.Sin(i * 0.041);

                f0[i] = baseF0 * declination * terminal * drift;
                if (f0[i] < 50.0) f0[i] = 50.0;
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
            double openPhase = 0.55;
            double nyquist = sampleRate * 0.45;
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

                        // Ramp the first and last few milliseconds of a tone,
                        // or its square-edged start is an audible click.
                        double edge = EdgeGain(frame, tone1);
                        output[write] = (float)(value * ToneAmplitude * edge * voice.Gain);
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
        /// Loudness is set by RMS, with a peak ceiling that wins when it has
        /// to: RMS alone lets a single transient clip, and peak alone makes a
        /// message with one loud burst inaudibly quiet.
        /// </summary>
        private static void Normalise(float[] buffer, double gain, bool[] isTone)
        {
            const double targetRms = 0.14;
            const double ceiling = 0.95;

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
            double scale = targetRms / rms;
            if (peak * scale > ceiling) scale = ceiling / peak;
            scale *= gain;

            for (int i = 0; i < buffer.Length; i++)
            {
                if (isTone != null && isTone[i]) continue;
                buffer[i] = (float)(buffer[i] * scale);
            }
        }

        /// <summary>
        /// Fade a tone in and out across a few frames at its ends.
        ///
        /// A tone that starts at full amplitude on a sample boundary clicks,
        /// and a run of them -- which is what [:dial] is -- clicks on every
        /// digit.
        /// </summary>
        private static double EdgeGain(int frame, double[] tone)
        {
            const int fade = 3;              // frames, so 15 ms

            int back = 0;
            while (back < fade && frame - back - 1 >= 0
                   && tone[frame - back - 1] > 0.0) back++;

            int forward = 0;
            while (forward < fade && frame + forward + 1 < tone.Length
                   && tone[frame + forward + 1] > 0.0) forward++;

            int edge = back < forward ? back : forward;
            return (edge + 1.0) / (fade + 1.0);
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
