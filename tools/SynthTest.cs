// Offline harness: renders a phrase with the same synthesiser the mod uses
// and writes a WAV, so the voice can be tuned without launching Besiege.
//
// This file is NOT part of the mod assembly -- it uses System.IO, which the
// game's loader blacklists. It is compiled separately, by tools/say.sh.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MultiplayerTTS.Klatt;

public static class SynthTest
{
    public static int Main(string[] args)
    {
        string text = args.Length > 0 ? args[0] : "hello world";
        string path = args.Length > 1 ? args[1] : "out.wav";

        int rate = 22050;
        if (args.Length > 2) rate = int.Parse(args[2]);

        KlattVoice voice = new KlattVoice();
        if (args.Length > 3) voice.Pitch = double.Parse(args[3]);
        if (args.Length > 4) voice.Speed = double.Parse(args[4]);

        // DECtalk markup takes the plan path; everything else the plain one.
        SpeechPlan plan = null;
        if (DecTalk.LooksLikeMarkup(text))
        {
            plan = DecTalk.Parse(text);
            if (!plan.UsedMarkup || plan.Items.Count == 0) plan = null;
        }

        string normalised = TextNormaliser.Normalise(text);
        List<string> phones = LetterToSound.Translate(text);

        StringBuilder line = new StringBuilder();
        for (int i = 0; i < phones.Count; i++)
        {
            if (i > 0) line.Append(' ');
            line.Append(phones[i]);
        }

        Console.WriteLine("text       : " + text);
        if (plan != null)
        {
            Console.WriteLine("markup     : " + Describe(plan));
            if (plan.Voice != null) Console.WriteLine("voice      : " + plan.Voice);
            Console.WriteLine("rate       : " + plan.RateWpm + " wpm");
        }
        else
        {
            Console.WriteLine("normalised : " + normalised);
            Console.WriteLine("phonemes   : " + line);
        }

        bool question = text.TrimEnd().EndsWith("?");
        KlattSynth synth = new KlattSynth(rate, 12345);

        // Time it: this runs on Besiege's game thread, so anything near a
        // frame budget (16 ms) is a visible hitch when a message arrives.
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float[] samples = plan != null
            ? synth.Synthesise(plan, voice, question)
            : synth.Synthesise(phones, voice, question);
        clock.Stop();

        // Report what came out, so a silent or broken render is obvious here
        // rather than in the game.
        double peak = 0.0, sum = 0.0;
        int nan = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            if (float.IsNaN(s) || float.IsInfinity(s)) { nan++; continue; }
            double a = Math.Abs(s);
            if (a > peak) peak = a;
            sum += (double)s * s;
        }
        double rms = samples.Length > 0 ? Math.Sqrt(sum / samples.Length) : 0.0;

        Console.WriteLine("samples    : " + samples.Length
                          + "  (" + (samples.Length / (double)rate).ToString("F2") + " s)");
        Console.WriteLine("peak       : " + peak.ToString("F4"));
        Console.WriteLine("rms        : " + rms.ToString("F4"));
        Console.WriteLine("non-finite : " + nan);
        Console.WriteLine("synth time : " + clock.Elapsed.TotalMilliseconds.ToString("F1") + " ms"
                          + "  (" + (samples.Length / (double)rate * 1000.0
                                     / Math.Max(clock.Elapsed.TotalMilliseconds, 0.001)).ToString("F0")
                          + "x realtime)");

        WriteWav(path, samples, rate);
        Console.WriteLine("wrote      : " + path);

        if (nan > 0) { Console.WriteLine("FAIL: non-finite samples"); return 1; }
        if (samples.Length == 0) { Console.WriteLine("FAIL: no samples"); return 1; }
        if (peak < 0.01) { Console.WriteLine("FAIL: silent"); return 1; }
        return 0;
    }

    private static string Describe(SpeechPlan plan)
    {
        StringBuilder b = new StringBuilder();
        for (int i = 0; i < plan.Items.Count; i++)
        {
            SpeechItem it = plan.Items[i];
            if (b.Length > 0) b.Append("  ");
            if (it.Kind == SpeechItem.KindText) b.Append("text(\"" + it.Text.Trim() + "\")");
            else if (it.Kind == SpeechItem.KindPhoneme)
            {
                b.Append(it.Phoneme);
                if (it.Duration > 0.0 || it.Pitch > 0.0)
                {
                    b.Append("<" + (int)it.Duration + "ms," + (int)it.Pitch + "Hz>");
                }
            }
            else if (it.Kind == SpeechItem.KindTone)
            {
                b.Append("tone(" + (int)it.Frequency
                         + (it.Pitch > 0.0 ? "+" + (int)it.Pitch : "")
                         + "," + (int)it.Duration + "ms)");
            }
            else b.Append("rest(" + (int)it.Duration + "ms)");
        }
        return b.ToString();
    }

    private static void WriteWav(string path, float[] samples, int rate)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            int dataBytes = samples.Length * 2;
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);          // PCM
            w.Write((short)1);          // mono
            w.Write(rate);
            w.Write(rate * 2);          // byte rate
            w.Write((short)2);          // block align
            w.Write((short)16);         // bits
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                float s = samples[i];
                if (s > 1f) s = 1f;
                if (s < -1f) s = -1f;
                w.Write((short)(s * 32767f));
            }
        }
    }
}
