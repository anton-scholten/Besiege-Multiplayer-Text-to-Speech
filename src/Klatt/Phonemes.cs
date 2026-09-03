using System;
using System.Collections.Generic;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// One phoneme's synthesis targets. The formant frequencies are the
    /// classic male-voice values from the Klatt 1980 tables, which is the
    /// right starting point here: DECtalk's "Perfect Paul" is a Klatt
    /// synthesiser driven from tables of exactly this shape.
    ///
    /// A diphthong carries a second target and is interpolated towards it
    /// across its own duration.
    /// </summary>
    public class Phone
    {
        public string Name;
        public int Class;

        public double F1, F2, F3;
        public double B1, B2, B3;

        public bool Diphthong;
        public double F1b, F2b, F3b;

        public double Duration;     // milliseconds at normal rate
        public double Amplitude;    // voicing, 0..1
        public double Frication;    // frication noise, 0..1
        public double Aspiration;   // aspiration noise, 0..1
        public double FricPole;     // centre of the frication band, Hz
        public double FricBw;
        public bool Voiced;

        public Phone(string name, int cls,
                     double f1, double f2, double f3,
                     double b1, double b2, double b3,
                     double duration, double amplitude,
                     double frication, double aspiration,
                     double fricPole, double fricBw, bool voiced)
        {
            Name = name;
            Class = cls;
            F1 = f1; F2 = f2; F3 = f3;
            B1 = b1; B2 = b2; B3 = b3;
            Duration = duration;
            Amplitude = amplitude;
            Frication = frication;
            Aspiration = aspiration;
            FricPole = fricPole;
            FricBw = fricBw;
            Voiced = voiced;
            Diphthong = false;
        }

        public Phone WithGlide(double f1b, double f2b, double f3b)
        {
            Diphthong = true;
            F1b = f1b; F2b = f2b; F3b = f3b;
            return this;
        }
    }

    /// <summary>
    /// The phoneme inventory, and the lookup from ARPAbet name to phone.
    ///
    /// Classes are plain int constants rather than an enum on purpose:
    /// Besiege's bundled C# compiler segfaults on any enum declaration (note
    /// 01 of the modding notes), and this assembly has to build under it.
    /// </summary>
    public static class Phonemes
    {
        public const int ClassSilence = 0;
        public const int ClassVowel = 1;
        public const int ClassNasal = 2;
        public const int ClassLiquid = 3;
        public const int ClassFricative = 4;
        public const int ClassStop = 5;
        public const int ClassAffricate = 6;

        private static readonly Dictionary<string, Phone> byName = new Dictionary<string, Phone>();
        private static readonly List<Phone> all = new List<Phone>();

        public static Phone Silence;

        static Phonemes()
        {
            // ---- silence and pause -------------------------------------
            Silence = Add(new Phone("_", ClassSilence, 500, 1500, 2500, 100, 100, 200,
                                    60, 0.0, 0.0, 0.0, 0, 0, false));

            // ---- vowels -------------------------------------------------
            // F1/F2/F3 after Klatt 1980 Table 1 (adult male).
            Add(new Phone("IY", ClassVowel, 310, 2020, 2960, 45, 200, 400, 130, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("IH", ClassVowel, 400, 1800, 2570, 50, 100, 140, 105, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("EH", ClassVowel, 530, 1680, 2500, 60, 90, 140, 120, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("AE", ClassVowel, 620, 1660, 2430, 70, 150, 320, 150, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("AA", ClassVowel, 700, 1220, 2600, 130, 70, 160, 150, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("AO", ClassVowel, 600, 990, 2570, 90, 100, 80, 145, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("UH", ClassVowel, 450, 1100, 2350, 80, 100, 80, 100, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("UW", ClassVowel, 350, 1250, 2200, 65, 110, 140, 130, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("AH", ClassVowel, 620, 1220, 2550, 80, 50, 140, 100, 1.0, 0, 0, 0, 0, true));
            Add(new Phone("AX", ClassVowel, 550, 1400, 2500, 80, 100, 140, 70, 0.85, 0, 0, 0, 0, true));
            Add(new Phone("ER", ClassVowel, 470, 1270, 1540, 100, 60, 110, 145, 1.0, 0, 0, 0, 0, true));

            // Diphthongs: a first target that glides to a second.
            Add(new Phone("EY", ClassVowel, 480, 1720, 2520, 70, 100, 200, 175, 1.0, 0, 0, 0, 0, true))
                .WithGlide(330, 2000, 2800);
            Add(new Phone("AY", ClassVowel, 700, 1220, 2600, 130, 70, 160, 190, 1.0, 0, 0, 0, 0, true))
                .WithGlide(350, 1900, 2700);
            Add(new Phone("OY", ClassVowel, 600, 990, 2570, 90, 100, 80, 200, 1.0, 0, 0, 0, 0, true))
                .WithGlide(350, 1900, 2700);
            Add(new Phone("AW", ClassVowel, 700, 1220, 2600, 130, 70, 160, 190, 1.0, 0, 0, 0, 0, true))
                .WithGlide(370, 950, 2300);
            Add(new Phone("OW", ClassVowel, 540, 1100, 2300, 80, 70, 70, 175, 1.0, 0, 0, 0, 0, true))
                .WithGlide(360, 900, 2300);

            // ---- nasals -------------------------------------------------
            // Low F1 and a heavily damped tract; the nasal zero is applied by
            // the synthesiser for this class.
            Add(new Phone("M", ClassNasal, 250, 1100, 2200, 90, 110, 180, 70, 0.7, 0, 0, 0, 0, true));
            Add(new Phone("N", ClassNasal, 250, 1600, 2700, 90, 110, 180, 65, 0.7, 0, 0, 0, 0, true));
            Add(new Phone("NG", ClassNasal, 250, 2000, 2900, 90, 110, 180, 70, 0.7, 0, 0, 0, 0, true));

            // ---- liquids and glides -------------------------------------
            Add(new Phone("L", ClassLiquid, 330, 1000, 2600, 60, 100, 200, 70, 0.9, 0, 0, 0, 0, true));
            Add(new Phone("R", ClassLiquid, 330, 1000, 1600, 60, 100, 160, 75, 0.9, 0, 0, 0, 0, true));
            Add(new Phone("W", ClassLiquid, 290, 610, 2150, 50, 80, 60, 65, 0.9, 0, 0, 0, 0, true));
            Add(new Phone("Y", ClassLiquid, 260, 2070, 3020, 40, 250, 500, 60, 0.9, 0, 0, 0, 0, true));

            // ---- fricatives ---------------------------------------------
            // Frication is noise through a resonator centred on FricPole. The
            // sibilants are loud and high; /f/ and /th/ are quiet and diffuse,
            // which is exactly why they are the pair humans confuse too.
            Add(new Phone("F", ClassFricative, 340, 1100, 2080, 200, 120, 150, 105, 0.0, 0.30, 0, 4000, 2500, false));
            Add(new Phone("TH", ClassFricative, 320, 1290, 2540, 200, 120, 150, 100, 0.0, 0.22, 0, 5500, 2500, false));
            Add(new Phone("S", ClassFricative, 320, 1390, 2530, 200, 120, 150, 115, 0.0, 0.70, 0, 5500, 900, false));
            Add(new Phone("SH", ClassFricative, 300, 1840, 2480, 200, 120, 150, 120, 0.0, 0.80, 0, 2600, 800, false));
            Add(new Phone("HH", ClassFricative, 500, 1500, 2500, 200, 200, 300, 80, 0.0, 0.0, 0.35, 1500, 3000, false));

            Add(new Phone("V", ClassFricative, 340, 1100, 2080, 90, 120, 150, 75, 0.45, 0.14, 0, 4000, 2500, true));
            Add(new Phone("DH", ClassFricative, 320, 1290, 2540, 90, 120, 150, 70, 0.45, 0.10, 0, 5500, 2500, true));
            Add(new Phone("Z", ClassFricative, 320, 1390, 2530, 90, 120, 150, 85, 0.45, 0.34, 0, 5500, 900, true));
            Add(new Phone("ZH", ClassFricative, 300, 1840, 2480, 90, 120, 150, 85, 0.45, 0.40, 0, 2600, 800, true));

            // ---- stops ---------------------------------------------------
            // Duration here is the burst; the closure silence before it is
            // added by the synthesiser, which also uses F2 as the locus the
            // neighbouring vowel's transition bends towards.
            Add(new Phone("P", ClassStop, 400, 800, 2000, 300, 250, 300, 18, 0.0, 0.35, 0.20, 900, 3000, false));
            Add(new Phone("T", ClassStop, 400, 1700, 2600, 300, 250, 300, 18, 0.0, 0.45, 0.22, 4000, 2200, false));
            Add(new Phone("K", ClassStop, 350, 1900, 2400, 300, 250, 300, 22, 0.0, 0.42, 0.25, 2200, 1400, false));
            Add(new Phone("B", ClassStop, 300, 800, 2000, 120, 200, 250, 14, 0.35, 0.16, 0.0, 900, 3000, true));
            Add(new Phone("D", ClassStop, 300, 1700, 2600, 120, 200, 250, 14, 0.35, 0.20, 0.0, 4000, 2200, true));
            Add(new Phone("G", ClassStop, 260, 1900, 2400, 120, 200, 250, 16, 0.35, 0.20, 0.0, 2200, 1400, true));

            // ---- affricates ---------------------------------------------
            // A stop burst followed by the matching sibilant, synthesised as
            // one segment with a long noisy tail.
            Add(new Phone("CH", ClassAffricate, 300, 1800, 2480, 200, 150, 200, 100, 0.0, 0.75, 0.15, 2600, 800, false));
            Add(new Phone("JH", ClassAffricate, 300, 1800, 2480, 120, 150, 200, 85, 0.40, 0.42, 0.0, 2600, 800, true));

            // DECtalk's own spellings for phones this table already has.
            //
            // These are not conveniences. The parser matches the longest
            // prefix it knows and skips any character it cannot place, so a
            // spelling that is missing here does not fall back to something
            // near it -- it silently loses a phone. "ur" came out as a bare
            // R, which is why "shivers down your spine" sang "your" as a
            // growl; "hx", and a plain "h", vanished outright.
            Alias("RR", "ER");     // DECtalk's spelling of the vowel in "bird"
            Alias("UR", "ER");     // what the copypastas write for the same
            Alias("AXR", "ER");    // its unstressed form
            Alias("HX", "HH");
            Alias("H", "HH");
            Alias("IX", "IH");     // the reduced "i" of "roses"
            Alias("YX", "Y");
        }

        /// <summary>
        /// Give an existing phone a second spelling. The alias is reachable
        /// through <see cref="Get"/> and <see cref="Has"/> but is deliberately
        /// kept out of <c>all</c>, which is the list of distinct phones.
        /// </summary>
        private static void Alias(string name, string existing)
        {
            byName[name] = byName[existing];
        }

        private static Phone Add(Phone p)
        {
            byName[p.Name] = p;
            all.Add(p);
            return p;
        }

        public static Phone Get(string name)
        {
            Phone p;
            if (byName.TryGetValue(name, out p)) return p;
            return Silence;
        }

        public static bool Has(string name)
        {
            return byName.ContainsKey(name);
        }
    }
}
