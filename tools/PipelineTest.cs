// Tests the text-to-speech pipeline: normalisation, letter-to-sound, and the
// synthesiser's output being finite, audible and bounded.
//
// Everything under src/Klatt is deliberately free of Unity and of Besiege, so
// it can be tested here, offline, in a second. Run with tools/test.sh.

using System;
using System.Collections.Generic;
using System.Text;
using MultiplayerTTS.Klatt;

public static class PipelineTest
{
    private static int failures;
    private static int checks;

    public static int Main()
    {
        TestNormaliser();
        TestSpeakerPrefix();
        TestDecTalk();
        TestLetterToSound();
        TestRobustness();
        TestSynthesis();
        TestPlanSynthesis();
        TestDeterminism();

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("all " + checks + " checks passed");
            return 0;
        }
        Console.WriteLine(failures + " of " + checks + " checks FAILED");
        return 1;
    }

    // -----------------------------------------------------------------

    private static void TestNormaliser()
    {
        Section("normaliser");

        Same("numbers", Norm("i have 3 blocks"), "i have three blocks");
        Same("teens", Norm("15 wheels"), "fifteen wheels");
        Same("tens", Norm("42"), "forty two");
        Same("hundreds", Norm("300"), "three hundred");
        Same("mixed", Norm("135"), "one hundred and thirty five");
        Same("thousands", Norm("2500"), "two thousand five hundred");
        Same("long digits read out", Norm("123456"), "one two three four five six");

        Same("abbreviation", Norm("gg"), "good game");
        Same("abbreviation in context", Norm("gg wp"), "good game well played");

        Same("rich text stripped",
             Norm("<color=#ff0000>hello</color>"), "hello");
        Same("url replaced", Norm("look at https://example.com/x"), "look at link");

        Same("repeats collapsed", Norm("nooooooo"), "noo");
        Same("punctuation dropped", Norm("what?! really..."), "what really");
        Same("known initialism expanded", Norm("brb"), "be right back");

        // A short run with no vowel in it is an initialism far more often than
        // a word, so it gets spelled out.
        Same("unknown initialism spelled", Norm("tbh"), "tee bee aitch");
        Same("and another", Norm("ngl"), "en jee ell");

        // 'y' counts as a vowel for that test, which is the whole reason
        // "my" and "why" survive as words instead of being spelled out. The
        // cost is that a genuinely vowel-less "xyz" is said rather than
        // spelled, which is much the better way round to be wrong.
        Same("y protects short words", Norm("why fly"), "why fly");

        Same("empty", Norm(""), "");
        Same("punctuation only", Norm("!!!"), "");

        // The cap exists so one message cannot hold the voice for a minute.
        string huge = new string('a', 500) + " end";
        Check("length capped", Norm(huge).Length < 400,
              "normalised length " + Norm(huge).Length);
    }

    private static void TestSpeakerPrefix()
    {
        Section("speaker prefix");

        // What actually arrives at HandleSayMessage: already display-formatted.
        Same("name stripped",
             TextNormaliser.WithoutSpeaker("<color=#ff8800>Bob:  </color>hello there", "Bob"),
             "hello there");
        Same("name with spaces",
             TextNormaliser.WithoutSpeaker("<color=#ff8800>Big Bob:  </color>hi", "Big Bob"),
             "hi");
        Same("unknown sender falls back to the colon-two-spaces form",
             TextNormaliser.WithoutSpeaker("<color=#ff8800>Bob:  </color>hello", null),
             "hello");
        Same("a colon in the message survives",
             TextNormaliser.WithoutSpeaker("<color=#ff8800>Bob:  </color>note: this stays", "Bob"),
             "note: this stays");
        Same("no prefix at all is left alone",
             TextNormaliser.WithoutSpeaker("just a message", "Bob"),
             "just a message");
        Same("empty", TextNormaliser.WithoutSpeaker("", "Bob"), "");

        // The end-to-end shape: the name must not reach the phonemes.
        string raw = "<color=#ff8800>Wilhelm:  </color>go left";
        Check("name is not spoken",
              !Join(LetterToSound.Translate(
                        TextNormaliser.WithoutSpeaker(raw, "Wilhelm"))).Contains("W IH L"),
              Join(LetterToSound.Translate(TextNormaliser.WithoutSpeaker(raw, "Wilhelm"))));
    }

    private static void TestDecTalk()
    {
        Section("dectalk markup");

        Check("plain text is not markup", !DecTalk.LooksLikeMarkup("hello world"), "");
        Check("bracketed text is markup", DecTalk.LooksLikeMarkup("[:t 350,500]"), "");

        // The message from the request, in full.
        string moonbase = "[:dial 6387657]The birth parents you are trying to call "
                        + "do not love you, please hang up[:t 350,500][:t 1,500][:t 350,500]";
        SpeechPlan plan = DecTalk.Parse(moonbase);

        Check("markup was recognised", plan.UsedMarkup, "");

        int tones = 0, rests = 0, texts = 0, phonemes = 0;
        for (int i = 0; i < plan.Items.Count; i++)
        {
            int k = plan.Items[i].Kind;
            if (k == SpeechItem.KindTone) tones++;
            else if (k == SpeechItem.KindSilence) rests++;
            else if (k == SpeechItem.KindText) texts++;
            else if (k == SpeechItem.KindPhoneme) phonemes++;
        }

        // Seven dialled digits (each a tone plus a gap), then the sentence,
        // then tone / rest / tone.
        Check("seven digits dialled plus two tones", tones == 9, "tones=" + tones);
        Check("one run of text", texts == 1, "texts=" + texts);

        // [:t 1,500] is how these messages write a rest: 1 Hz is not a tone.
        // Eleven silences, not four: the seven dialled digits each carry one,
        // and so does every [:t] -- the tone, the rest and the tone.
        Check("a sub-audible tone becomes a rest", rests == 11, "rests=" + rests);

        // DTMF: '6' is row 770 Hz, column 1477 Hz.
        SpeechPlan dial = DecTalk.Parse("[:dial 6]");
        Check("DTMF frequencies are the standard pair",
              dial.Items.Count > 0
              && Math.Abs(dial.Items[0].Frequency - 770.0) < 0.5
              && Math.Abs(dial.Items[0].Pitch - 1477.0) < 0.5,
              dial.Items.Count > 0
                  ? dial.Items[0].Frequency + "+" + dial.Items[0].Pitch : "none");

        // The singing syntax: phoneme, duration, pitch index.
        SpeechPlan song = DecTalk.Parse("[:phoneme arpabet speak on][dh<300,10>ax<200,12>]");
        Check("two phonemes parsed", song.Items.Count == 2, "n=" + song.Items.Count);
        if (song.Items.Count == 2)
        {
            Same("first phoneme", song.Items[0].Phoneme, "DH");
            Same("second phoneme", song.Items[1].Phoneme, "AX");
            Check("duration honoured", Math.Abs(song.Items[0].Duration - 300.0) < 0.5,
                  song.Items[0].Duration.ToString());
            // Two steps up the index is two semitones: a ratio of 2^(2/12).
            double ratio = song.Items[1].Pitch / song.Items[0].Pitch;
            Check("pitch index steps in semitones",
                  Math.Abs(ratio - Math.Pow(2.0, 2.0 / 12.0)) < 0.01,
                  "ratio " + ratio.ToString("F4"));
        }

        // The table is anchored, not just relative: index n is MIDI note
        // n + 35, so 1 is C2. These six are the indices used by the Spooky
        // Scary Skeletons verse, and the frequencies are the ones a recording
        // of it measures.
        int[] indices = new int[] { 11, 13, 14, 16, 18, 19 };
        double[] hertz = new double[] { 116.54, 130.81, 138.59, 155.56, 174.61, 185.00 };
        for (int note = 0; note < indices.Length; note++)
        {
            SpeechPlan one = DecTalk.Parse("[ah<300," + indices[note] + ">]");
            double got = one.Items.Count > 0 ? one.Items[0].Pitch : 0.0;
            Check("pitch index " + indices[note] + " is " + hertz[note] + " Hz",
                  Math.Abs(got - hertz[note]) < 0.5, got.ToString("F2"));
        }

        // ---- speaker definitions -------------------------------------
        // The published DECtalk table, spot-checked where an earlier
        // hand-tuned version of it was wrong.
        KlattVoice paul = new KlattVoice();
        DecTalkVoices.Apply(paul, "paul");
        Check("Paul is ap 122, pr 100",
              paul.Pitch == 122.0 && paul.PitchRange == 100.0,
              paul.Pitch + "/" + paul.PitchRange);

        // ap is the baseline, not the mean of the rendered contour. Harry
        // measures around 105 Hz and his ap is 89; reading the measurement as
        // ap is what put this table out.
        KlattVoice harry = new KlattVoice();
        DecTalkVoices.Apply(harry, "harry");
        Check("Harry is ap 89, pr 80, hs 115",
              harry.Pitch == 89.0 && harry.PitchRange == 80.0
              && Math.Abs(harry.HeadSize - 1.15) < 0.001,
              harry.Pitch + "/" + harry.PitchRange + "/" + harry.HeadSize);

        // Rita is a low voice -- below Dennis. Assuming otherwise put her at 196.
        KlattVoice rita = new KlattVoice();
        DecTalkVoices.Apply(rita, "rita");
        Check("Rita is a low voice at ap 106", rita.Pitch == 106.0, rita.Pitch.ToString());

        // The female voices are not small-headed; they are formant-shifted.
        KlattVoice bettyVoice = new KlattVoice();
        DecTalkVoices.Apply(bettyVoice, "betty");
        Check("Betty has an average head and a switched-off fifth formant",
              Math.Abs(bettyVoice.HeadSize - 1.0) < 0.001 && bettyVoice.F4 == 4450.0
              && bettyVoice.B5 == 2048.0 && bettyVoice.Sex == 0,
              bettyVoice.HeadSize + "/" + bettyVoice.F4 + "/" + bettyVoice.B5);

        // Breathiness is in dB over 0..70 and is a main character trait.
        KlattVoice wendy = new KlattVoice();
        DecTalkVoices.Apply(wendy, "wendy");
        Check("Wendy is breathy at 55 dB", wendy.Breathiness == 55.0,
              wendy.Breathiness.ToString());

        // [:nv] is the user-defined slot, which starts as a copy of Paul.
        KlattVoice val = new KlattVoice();
        DecTalkVoices.Apply(val, "val");
        Check("Val starts as a copy of Paul",
              val.Pitch == paul.Pitch && val.PitchRange == paul.PitchRange,
              val.Pitch.ToString());

        // ---- the voice designer --------------------------------------
        SpeechPlan dv = DecTalk.Parse("[:nh][:dv ap 90 pr 0] I am a robot.");
        Check("[:dv] edits reach the item",
              dv.Items.Count > 0 && dv.Items[0].Design != null
              && dv.Items[0].Design["ap"] == 90.0
              && dv.Items[0].Design["pr"] == 0.0,
              dv.Items.Count > 0 && dv.Items[0].Design != null
                  ? dv.Items[0].Design.Count.ToString() : "none");

        // Zero is a real pitch range -- it is DECtalk's own monotone example --
        // so it must survive as a value and not be read as "unset".
        SpeechPlan mono = DecTalk.Parse("[:dv pr 0]hello");
        Check("a pitch range of zero is kept",
              mono.Items.Count > 0 && mono.Items[0].Design != null
              && mono.Items[0].Design.ContainsKey("pr")
              && mono.Items[0].Design["pr"] == 0.0,
              "none");

        // Selecting a speaker drops the edits: DECtalk keeps them only "while
        // the current speaker remains current".
        SpeechPlan reset = DecTalk.Parse("[:dv ap 90]one[:np]two");
        SpeechItem second = null;
        for (int m = reset.Items.Count - 1; m >= 0; m--)
        {
            if (reset.Items[m].Kind == SpeechItem.KindText) { second = reset.Items[m]; break; }
        }
        Check("selecting a speaker clears [:dv] edits",
              second != null && second.Design == null,
              second == null ? "no item" : "still set");

        // An option that is not DECtalk's is skipped, not guessed at.
        SpeechPlan junk = DecTalk.Parse("[:dv zz 5 ap 100]hi");
        Check("[:dv] ignores unknown options",
              junk.Items.Count > 0 && junk.Items[0].Design != null
              && junk.Items[0].Design.Count == 1
              && junk.Items[0].Design["ap"] == 100.0,
              junk.Items.Count > 0 && junk.Items[0].Design != null
                  ? junk.Items[0].Design.Count.ToString() : "none");

        // "_" carries a note into the words after it: this is how a copypasta
        // sings ordinary text instead of spelling it out as phonemes.
        SpeechPlan marked = DecTalk.Parse("[_<1,29>]I throw my hoe[_<1,27>]zez");
        int sung = 0;
        double firstNote = 0.0, secondNote = 0.0;
        for (int m = 0; m < marked.Items.Count; m++)
        {
            if (marked.Items[m].Kind != SpeechItem.KindText) continue;
            sung++;
            if (sung == 1) firstNote = marked.Items[m].Pitch;
            if (sung == 2) secondNote = marked.Items[m].Pitch;
        }
        // Index 29 is MIDI 64 (E4) and 27 is MIDI 62 (D4).
        Check("a marker sings the words after it",
              sung == 2
              && Math.Abs(firstNote - 329.63) < 0.5
              && Math.Abs(secondNote - 293.66) < 0.5,
              firstNote.ToString("F1") + "/" + secondNote.ToString("F1"));

        // The same phoneme with a real length is a rest, not a marker.
        SpeechPlan rest = DecTalk.Parse("[_<500,22>]");
        Check("a held underscore is a rest",
              rest.Items.Count == 1
              && rest.Items[0].Kind == SpeechItem.KindSilence
              && Math.Abs(rest.Items[0].Duration - 500.0) < 0.5,
              rest.Items.Count > 0 ? rest.Items[0].Duration.ToString() : "none");

        // A message with no marker in it must be left entirely alone.
        SpeechPlan spoken = DecTalk.Parse("hello there");
        Check("plain text carries no note",
              spoken.Items.Count == 1 && spoken.Items[0].Pitch == 0.0,
              spoken.Items.Count > 0 ? spoken.Items[0].Pitch.ToString() : "none");

        // DECtalk's own spellings have to resolve, because an unknown one is
        // not approximated -- it is dropped. "ur" used to leave a bare R, so
        // "your" was sung as a growl.
        SpeechPlan spelt = DecTalk.Parse("[nyur<300,18>]");
        Check("DECtalk spellings resolve to a phone",
              spelt.Items.Count == 3
              && spelt.Items[0].Phoneme == "N"
              && spelt.Items[1].Phoneme == "Y"
              && spelt.Items[2].Phoneme == "UR"
              && Phonemes.Get("UR").Name == "ER"
              && Math.Abs(spelt.Items[2].Duration - 300.0) < 0.5,
              spelt.Items.Count > 2 ? spelt.Items[2].Phoneme : "none");

        // Longest-first matching: "ng" must not be read as "n" then "g".
        SpeechPlan ng = DecTalk.Parse("[ng]");
        Check("two-letter phonemes win over one-letter",
              ng.Items.Count == 1 && ng.Items[0].Phoneme == "NG",
              ng.Items.Count > 0 ? ng.Items[0].Phoneme : "none");

        // Commands that change the voice rather than emitting anything.
        SpeechPlan betty = DecTalk.Parse("[:nb][:rate 300]hello");
        Same("shorthand selects a voice", betty.Voice, "betty");
        Check("rate is read", Math.Abs(betty.RateWpm - 300.0) < 0.5,
              betty.RateWpm.ToString());

        SpeechPlan named = DecTalk.Parse("[:name harry]hi");
        Same("long form selects a voice", named.Voice, "harry");

        // An unknown command must be skipped, never spoken.
        SpeechPlan unknown = DecTalk.Parse("[:mode math on]two plus two");
        bool spokeCommand = false;
        for (int i = 0; i < unknown.Items.Count; i++)
        {
            if (unknown.Items[i].Kind != SpeechItem.KindText) continue;
            if (unknown.Items[i].Text.Contains("mode")) spokeCommand = true;
        }
        Check("an unknown command is skipped, not spoken", !spokeCommand, "");

        // Caps, so one message cannot hold the voice open indefinitely.
        SpeechPlan huge = DecTalk.Parse("[:t 440,999999]");
        Check("a tone is capped",
              huge.Items.Count > 0 && huge.Items[0].Duration <= DecTalk.MaxToneMs,
              huge.Items.Count > 0 ? huge.Items[0].Duration.ToString() : "none");

        StringBuilder many = new StringBuilder();
        for (int i = 0; i < 200; i++) many.Append("[:t 440,4000]");
        SpeechPlan capped = DecTalk.Parse(many.ToString());
        double total = 0.0;
        for (int i = 0; i < capped.Items.Count; i++) total += capped.Items[i].Duration;
        Check("total length is capped", total <= DecTalk.MaxTotalMs + 1.0,
              total + " ms");

        // Voice changes are per item, not per message. The trailing [:np] in
        // the copypastas must not repaint every earlier line in Paul's voice.
        SpeechPlan twoHander = DecTalk.Parse("[:nh]why? [:nv]because[:np]");
        int hh = 0, vv = 0, pp = 0;
        for (int i = 0; i < twoHander.Items.Count; i++)
        {
            string v = twoHander.Items[i].Voice;
            if (v == "harry") hh++;
            else if (v == "val") vv++;
            else if (v == "paul") pp++;
        }
        Check("each line keeps the voice it was written in",
              hh == 1 && vv == 1 && pp == 0,
              "harry=" + hh + " val=" + vv + " paul=" + pp);

        // DECtalk does not require a space before the argument, and the
        // copypastas are written without one.
        SpeechPlan tight = DecTalk.Parse("[:dial67589340]");
        int tightTones = 0;
        for (int i = 0; i < tight.Items.Count; i++)
        {
            if (tight.Items[i].Kind == SpeechItem.KindTone) tightTones++;
        }
        Check("[:dial67589340] with no space still dials", tightTones == 8,
              "tones=" + tightTones);

        SpeechPlan tightTone = DecTalk.Parse("[:t350,500]");
        Check("[:t350,500] with no space still sounds",
              tightTone.Items.Count == 2
              && tightTone.Items[0].Kind == SpeechItem.KindTone
              && Math.Abs(tightTone.Items[0].Frequency - 350.0) < 0.5,
              tightTone.Items.Count.ToString());

        // A tune has to come out as separate notes. DECtalk leaves a fixed
        // gap after every tone whatever the tone's own length, and without it
        // the Tetris theme is one continuous glissando.
        SpeechPlan tune = DecTalk.Parse("[:t 430,500][:t 320,250]");
        Check("every tone is followed by DECtalk's gap",
              tune.Items.Count == 4
              && tune.Items[0].Kind == SpeechItem.KindTone
              && tune.Items[1].Kind == SpeechItem.KindSilence
              && tune.Items[2].Kind == SpeechItem.KindTone
              && tune.Items[3].Kind == SpeechItem.KindSilence
              && Math.Abs(tune.Items[1].Duration - DecTalk.ToneGapMs) < 0.01
              && Math.Abs(tune.Items[3].Duration - DecTalk.ToneGapMs) < 0.01,
              tune.Items.Count.ToString());

        // ... but a command whose name really does end in digits must not be
        // chopped up to make one.
        SpeechPlan notACommand = DecTalk.Parse("[:nonsense123]hello");
        int emitted = 0;
        for (int i = 0; i < notACommand.Items.Count; i++)
        {
            if (notACommand.Items[i].Kind != SpeechItem.KindText) emitted++;
        }
        Check("an unknown command with digits emits nothing", emitted == 0,
              "emitted=" + emitted);

        // DTMF timing, measured off real DECtalk: 100 ms on, 100 ms off.
        SpeechPlan timing = DecTalk.Parse("[:dial12]");
        Check("dialled digits are 100 ms on, 100 ms off",
              timing.Items.Count == 4
              && Math.Abs(timing.Items[0].Duration - 100.0) < 0.5
              && Math.Abs(timing.Items[1].Duration - 100.0) < 0.5,
              timing.Items.Count + " items");

        // An unclosed bracket is text, not a swallowed rest of message.
        SpeechPlan broken = DecTalk.Parse("hello [:t 350");
        bool keptText = false;
        for (int i = 0; i < broken.Items.Count; i++)
        {
            if (broken.Items[i].Kind == SpeechItem.KindText) keptText = true;
        }
        Check("an unclosed bracket does not eat the message", keptText, "");
    }

    private static void TestLetterToSound()
    {
        Section("letter to sound");

        Phones("hello world", "HH AX L OW _ W ER L D");
        Phones("machine", "M AX SH IY N");
        Phones("besiege", "B IH S IY JH");
        Phones("cannon", "K AE N AX N");
        Phones("block", "B L AA K");
        Phones("the", "DH AX");
        Phones("someone", "S AH M W AH N");
        Phones("friend", "F R EH N D");

        // Degemination: the rules translate letter by letter, so a doubled
        // consonant has to be collapsed or it stutters.
        Check("no doubled consonants",
              !HasAdjacentDuplicate(LetterToSound.Translate("cannon summer batting")),
              Join(LetterToSound.Translate("cannon summer batting")));

        // Word boundaries survive, because prosody needs them.
        List<string> two = LetterToSound.Translate("go now");
        Check("word break emitted", two.Contains("_"), Join(two));

        // Every phoneme produced must exist in the inventory, or the
        // synthesiser silently substitutes silence.
        string[] corpus = new string[]
        {
            "the quick brown fox jumps over the lazy dog",
            "your machine exploded again",
            "gg wp that was insane",
            "i think the cannon is too heavy",
            "check out my new flying machine",
            "why does this keep breaking",
        };
        bool allKnown = true;
        for (int i = 0; i < corpus.Length; i++)
        {
            List<string> p = LetterToSound.Translate(corpus[i]);
            for (int j = 0; j < p.Count; j++)
            {
                if (!Phonemes.Has(p[j])) { allKnown = false; break; }
            }
        }
        Check("every phoneme is in the inventory", allKnown, "");
    }

    private static void TestRobustness()
    {
        Section("robustness");

        string[] nasty = new string[]
        {
            "", " ", "   ", "!!!", "?", "'", "''''",
            "\n\t\r", "<>", "<<<>>>", "<color=", "aaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "éèê", "你好", "😀",
            "0", "000000", "-5", "3.14", "1e10",
            "a'b'c", "'''hello'''", "don't", "y'all'll've",
            new string('x', 1000),
            "!@#$%^&*()_+-=[]{}|;:,.<>?/~`",
        };

        bool ok = true;
        string broke = "";
        for (int i = 0; i < nasty.Length; i++)
        {
            try
            {
                List<string> p = LetterToSound.Translate(nasty[i]);
                KlattSynth s = new KlattSynth(22050, 1);
                float[] samples = s.Synthesise(p, new KlattVoice(), false);
                for (int j = 0; j < samples.Length; j++)
                {
                    if (float.IsNaN(samples[j]) || float.IsInfinity(samples[j]))
                    {
                        ok = false;
                        broke = "non-finite sample from input " + i;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                ok = false;
                broke = "input " + i + " threw: " + e.Message;
            }
            if (!ok) break;
        }
        Check("hostile input does not throw or produce NaN", ok, broke);
    }

    private static void TestSynthesis()
    {
        Section("synthesis");

        string[] lines = new string[]
        {
            "hello world",
            "good game everyone that was close",
            "my machine is on fire",
            "s",
            "a",
        };

        for (int i = 0; i < lines.Length; i++)
        {
            List<string> p = LetterToSound.Translate(lines[i]);
            KlattSynth synth = new KlattSynth(22050, 7);
            float[] samples = synth.Synthesise(p, new KlattVoice(), false);

            double peak = 0.0, sum = 0.0;
            bool finite = true;
            for (int j = 0; j < samples.Length; j++)
            {
                float s = samples[j];
                if (float.IsNaN(s) || float.IsInfinity(s)) { finite = false; break; }
                double a = Math.Abs(s);
                if (a > peak) peak = a;
                sum += (double)s * s;
            }
            double rms = samples.Length > 0 ? Math.Sqrt(sum / samples.Length) : 0.0;

            string label = "\"" + lines[i] + "\"";
            Check(label + " is finite", finite, "");
            Check(label + " is audible", peak > 0.02, "peak " + peak.ToString("F4"));
            Check(label + " does not clip", peak <= 1.0, "peak " + peak.ToString("F4"));
            // Levelled to sit alongside the Music mod's instrument blocks,
            // which run near full scale. The knee is what allows an RMS this
            // high without the peak going over.
            Check(label + " is levelled",
                  rms > 0.12 && rms < 0.35, "rms " + rms.ToString("F4"));
            Check(label + " peaks near full scale",
                  peak > 0.5, "peak " + peak.ToString("F4"));
        }

        // Level consistency is the point of the normalisation pass: two
        // different messages should not arrive at wildly different volumes.
        double lowest = 1e9, highest = 0.0;
        string[] varied = new string[]
        {
            "aaa", "sss", "hello", "the machine exploded", "eee", "ooo", "mmm",
        };
        for (int i = 0; i < varied.Length; i++)
        {
            float[] s = new KlattSynth(22050, 3)
                .Synthesise(LetterToSound.Translate(varied[i]), new KlattVoice(), false);
            double sum = 0.0;
            int n = 0;
            for (int j = 0; j < s.Length; j++)
            {
                if (Math.Abs(s[j]) > 0.005) { sum += (double)s[j] * s[j]; n++; }
            }
            if (n == 0) continue;
            double rms = Math.Sqrt(sum / n);
            if (rms < lowest) lowest = rms;
            if (rms > highest) highest = rms;
        }
        double spread = highest / Math.Max(lowest, 1e-9);
        Check("levels are consistent across utterances", spread < 2.0,
              "loudest/quietest = " + spread.ToString("F2"));
    }

    private static void TestPlanSynthesis()
    {
        Section("dectalk synthesis");

        string[] cases = new string[]
        {
            "[:dial 6387657]hello[:t 350,500][:t 1,500][:t 350,500]",
            "[:phoneme arpabet speak on][dh<300,10>ax<200,12>k<100,14>ae<400,17>]",
            "[:nb][:rate 300]hello i am betty",
            "[:dial 911]",
            "[:t 1,500]",
            "[]",
            "[:]",
            "[<>]",
            "[:t]",
            "[:dial]",
        };

        for (int i = 0; i < cases.Length; i++)
        {
            bool ok = true;
            string detail = "";
            try
            {
                SpeechPlan plan = DecTalk.Parse(cases[i]);
                KlattSynth synth = new KlattSynth(22050, 5);
                float[] samples = synth.Synthesise(plan, new KlattVoice(), false);

                double peak = 0.0;
                for (int j = 0; j < samples.Length; j++)
                {
                    float v = samples[j];
                    if (float.IsNaN(v) || float.IsInfinity(v))
                    {
                        ok = false; detail = "non-finite"; break;
                    }
                    double a = Math.Abs(v);
                    if (a > peak) peak = a;
                }
                if (ok && peak > 1.0) { ok = false; detail = "clips at " + peak; }

                double seconds = samples.Length / 22050.0;
                if (ok && seconds > DecTalk.MaxTotalMs / 1000.0 + 5.0)
                {
                    ok = false; detail = seconds + "s is past the cap";
                }
            }
            catch (Exception e)
            {
                ok = false;
                detail = "threw: " + e.Message;
            }
            Check("\"" + cases[i] + "\"", ok, detail);
        }
    }

    private static void TestDeterminism()
    {
        Section("determinism");

        List<string> p = LetterToSound.Translate("determinism matters here");

        float[] a = new KlattSynth(22050, 99).Synthesise(p, new KlattVoice(), false);
        float[] b = new KlattSynth(22050, 99).Synthesise(p, new KlattVoice(), false);

        bool same = a.Length == b.Length;
        if (same)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) { same = false; break; }
            }
        }
        Check("same seed gives the same audio", same, "");

        float[] c = new KlattSynth(22050, 100).Synthesise(p, new KlattVoice(), false);
        bool differs = false;
        for (int i = 0; i < Math.Min(a.Length, c.Length); i++)
        {
            if (a[i] != c[i]) { differs = true; break; }
        }
        Check("a different seed gives different noise", differs, "");
    }

    // -----------------------------------------------------------------

    private static string Norm(string s)
    {
        return TextNormaliser.Normalise(s);
    }

    private static string Join(List<string> phones)
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < phones.Count; i++)
        {
            if (i > 0) b.Append(' ');
            b.Append(phones[i]);
        }
        return b.ToString();
    }

    private static bool HasAdjacentDuplicate(List<string> phones)
    {
        for (int i = 1; i < phones.Count; i++)
        {
            if (phones[i] != phones[i - 1]) continue;
            if (Phonemes.Get(phones[i]).Class == Phonemes.ClassVowel) continue;
            return true;
        }
        return false;
    }

    private static void Phones(string text, string expected)
    {
        Same("\"" + text + "\"", Join(LetterToSound.Translate(text)), expected);
    }

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + name);
    }

    private static void Same(string label, string actual, string expected)
    {
        Check(label, actual == expected, "got \"" + actual + "\", want \"" + expected + "\"");
    }

    private static void Check(string label, bool ok, string detail)
    {
        checks++;
        if (ok)
        {
            Console.WriteLine("   ok    " + label);
            return;
        }
        failures++;
        Console.WriteLine("   FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : ""));
    }
}
