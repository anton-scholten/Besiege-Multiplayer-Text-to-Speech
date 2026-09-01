using System;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// A two-pole resonator, as in Klatt 1980 section 2. This is the single
    /// building block the whole vocal tract is made of: one of these per
    /// formant, chained in cascade for the voiced branch and run in parallel
    /// for frication.
    ///
    ///     y[n] = A*x[n] + B*y[n-1] + C*y[n-2]
    ///
    /// with A chosen so the response is unity at DC, which keeps the gain of a
    /// long cascade from drifting as the formants move.
    /// </summary>
    public class Resonator
    {
        private double a, b, c;
        private double y1, y2;
        private readonly double dt;

        public Resonator(int sampleRate)
        {
            dt = 1.0 / sampleRate;
            SetPole(1000.0, 100.0);
        }

        /// <summary>Retune to a centre frequency and bandwidth, both in Hz.</summary>
        public void SetPole(double frequency, double bandwidth)
        {
            double r = Math.Exp(-Math.PI * bandwidth * dt);
            c = -r * r;
            b = 2.0 * r * Math.Cos(2.0 * Math.PI * frequency * dt);
            a = 1.0 - b - c;
        }

        public double Step(double x)
        {
            double y = a * x + b * y1 + c * y2;
            y2 = y1;
            y1 = y;
            return y;
        }

        public void Reset()
        {
            y1 = 0.0;
            y2 = 0.0;
        }
    }

    /// <summary>
    /// A two-zero antiresonator. Klatt uses one of these paired with a pole to
    /// put the nasal zero in, which is what makes /m/ and /n/ sound nasal
    /// rather than just muffled.
    ///
    /// The coefficients are the resonator's, inverted -- a zero at the place a
    /// pole would have gone.
    /// </summary>
    public class AntiResonator
    {
        private double a, b, c;
        private double x1, x2;
        private readonly double dt;

        public AntiResonator(int sampleRate)
        {
            dt = 1.0 / sampleRate;
            SetZero(1000.0, 100.0);
        }

        public void SetZero(double frequency, double bandwidth)
        {
            double r = Math.Exp(-Math.PI * bandwidth * dt);
            double rc = -r * r;
            double rb = 2.0 * r * Math.Cos(2.0 * Math.PI * frequency * dt);
            double ra = 1.0 - rb - rc;

            // Guard the reciprocal: ra goes to zero as the zero approaches DC,
            // and an infinity here becomes a NaN that never leaves the filter.
            if (ra < 1e-9) ra = 1e-9;

            a = 1.0 / ra;
            b = -rb / ra;
            c = -rc / ra;
        }

        public double Step(double x)
        {
            double y = a * x + b * x1 + c * x2;
            x2 = x1;
            x1 = x;
            return y;
        }

        public void Reset()
        {
            x1 = 0.0;
            x2 = 0.0;
        }
    }

    /// <summary>
    /// One-pole low-pass, used for spectral tilt on the glottal source and for
    /// shaping the noise. Also serves, subtracted from its input, as the
    /// high-pass that keeps DC out of the output -- note 07 of the modding
    /// notes is explicit that Unity has no capacitor-coupled output stage, so
    /// the offset has to be removed here or it steps the speaker.
    /// </summary>
    public class OnePole
    {
        private double coefficient;
        private double state;

        public OnePole(int sampleRate, double cutoff)
        {
            SetCutoff(sampleRate, cutoff);
        }

        public void SetCutoff(int sampleRate, double cutoff)
        {
            double x = 2.0 * Math.PI * cutoff / sampleRate;
            coefficient = x / (x + 1.0);
            if (coefficient > 1.0) coefficient = 1.0;
            if (coefficient < 0.0) coefficient = 0.0;
        }

        public double LowPass(double x)
        {
            state += coefficient * (x - state);
            return state;
        }

        public double HighPass(double x)
        {
            return x - LowPass(x);
        }

        public void Reset()
        {
            state = 0.0;
        }
    }
}
