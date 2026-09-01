using System;
using System.Collections.Generic;
using System.Text;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// Turns a line of multiplayer chat into something the letter-to-sound
    /// rules can work on: rich-text markup gone, numbers spelled out, the
    /// abbreviations people actually type expanded, and the runs of repeated
    /// letters that chat is made of collapsed to something with a finite
    /// duration.
    ///
    /// The last of those is not cosmetic. "noooooooooooooooo" is a legal chat
    /// message and, taken literally, sixteen /OW/ segments is four seconds of
    /// one vowel.
    /// </summary>
    public static class TextNormaliser
    {
        /// <summary>Longest message we will speak, in characters.</summary>
        public const int MaxLength = 200;

        private static readonly Dictionary<string, string> Abbreviations =
            new Dictionary<string, string>();

        private static readonly string[] Ones = new string[]
        {
            "zero", "one", "two", "three", "four",
            "five", "six", "seven", "eight", "nine"
        };

        private static readonly string[] Teens = new string[]
        {
            "ten", "eleven", "twelve", "thirteen", "fourteen",
            "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
        };

        private static readonly string[] Tens = new string[]
        {
            "", "", "twenty", "thirty", "forty",
            "fifty", "sixty", "seventy", "eighty", "ninety"
        };

        static TextNormaliser()
        {
            // Chat shorthand. Spoken as words rather than spelled out, because
            // a Klatt synthesiser saying "gee gee" is the point.
            Abbreviations["gg"] = "good game";
            Abbreviations["ggs"] = "good games";
            Abbreviations["glhf"] = "good luck have fun";
            Abbreviations["lol"] = "loll";
            Abbreviations["lmao"] = "lam ay oh";
            Abbreviations["rofl"] = "roffle";
            Abbreviations["brb"] = "be right back";
            Abbreviations["afk"] = "away from keyboard";
            Abbreviations["ty"] = "thanks";
            Abbreviations["thx"] = "thanks";
            Abbreviations["np"] = "no problem";
            Abbreviations["yw"] = "you are welcome";
            Abbreviations["wtf"] = "what the heck";
            Abbreviations["omg"] = "oh my god";
            Abbreviations["idk"] = "i dont know";
            Abbreviations["imo"] = "in my opinion";
            Abbreviations["btw"] = "by the way";
            Abbreviations["ez"] = "easy";
            Abbreviations["op"] = "over powered";
            Abbreviations["rip"] = "rip";
            Abbreviations["wp"] = "well played";
            Abbreviations["nvm"] = "never mind";
            Abbreviations["pls"] = "please";
            Abbreviations["plz"] = "please";
            Abbreviations["u"] = "you";
            Abbreviations["ur"] = "your";
            Abbreviations["r"] = "are";
            Abbreviations["k"] = "kay";
            Abbreviations["ok"] = "okay";
            Abbreviations["kk"] = "okay";
            Abbreviations["yh"] = "yeah";
            Abbreviations["ye"] = "yeah";
            Abbreviations["ffs"] = "for goodness sake";
            Abbreviations["afaik"] = "as far as i know";
            Abbreviations["irl"] = "in real life";
            Abbreviations["mp"] = "multiplayer";
            Abbreviations["sp"] = "singleplayer";
        }

        /// <summary>
        /// Remove the speaker's name from the front of a chat message.
        ///
        /// A message arriving at <c>ChatController.HandleSayMessage</c> is
        /// <b>already formatted for display</b> -- the sender builds it in
        /// <c>PerformPlayerChat</c> as
        ///
        ///     "&lt;color=#{0}&gt;{1}:  &lt;/color&gt;{2}"        // name, then message
        ///
        /// so stripping the rich-text tags leaves "Name:  " in front of every
        /// line and the voice reads out who is speaking before what they said.
        /// It is not obvious from the method's signature, which takes the
        /// sender as a separate <c>PlayerData</c> argument and looks for all
        /// the world as though the string were just the message.
        /// </summary>
        public static string WithoutSpeaker(string text, string speaker)
        {
            if (text == null) return "";

            // Strip only the colour tags Besiege wraps the *name* in, not
            // every angle bracket in the line. DECtalk's singing syntax is
            // "[dh<200,10>]", so a blanket rich-text strip would quietly
            // remove every note's duration and pitch and leave the phonemes
            // to be read at their default length.
            string s = text;
            int endTag = s.IndexOf("</color>", StringComparison.Ordinal);
            if (endTag >= 0) s = s.Substring(endTag + 8);
            s = StripLeadingTags(s).TrimStart();

            // Preferred: we know who sent it, so match the name exactly.
            if (!string.IsNullOrEmpty(speaker) && s.StartsWith(speaker, StringComparison.Ordinal))
            {
                int at = speaker.Length;
                if (at < s.Length && s[at] == ':') at++;
                if (at <= s.Length) return s.Substring(at).TrimStart();
            }

            // Fallback for a sender we could not resolve. The game's own format
            // puts a colon and *two* spaces after the name, which is rare in
            // typed text -- rare enough to be worth matching, and specific
            // enough not to eat the colon out of "note:  see below".
            int sep = s.IndexOf(":  ", StringComparison.Ordinal);
            if (sep > 0 && sep < 64) return s.Substring(sep + 3).TrimStart();

            return s;
        }

        /// <summary>
        /// Remove any tags at the very front of the string, and stop at the
        /// first character that is not one. Anything later in the line is the
        /// player's own text and is left alone.
        /// </summary>
        private static string StripLeadingTags(string s)
        {
            int i = 0;
            while (i < s.Length && s[i] == '<')
            {
                int close = s.IndexOf('>', i);
                if (close < 0) break;
                i = close + 1;
            }
            return i == 0 ? s : s.Substring(i);
        }

        public static string Normalise(string text)
        {
            if (text == null) return "";

            string s = StripRichText(text);
            s = StripUrls(s);

            if (s.Length > MaxLength) s = s.Substring(0, MaxLength);

            // Split into tokens on anything that is not a letter, digit or
            // apostrophe, expanding as we go.
            StringBuilder output = new StringBuilder();
            StringBuilder token = new StringBuilder();

            for (int i = 0; i <= s.Length; i++)
            {
                char c = i < s.Length ? s[i] : ' ';

                bool isWordChar = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                                  || (c >= '0' && c <= '9') || c == '\'';

                if (isWordChar)
                {
                    token.Append(c);
                }
                else
                {
                    if (token.Length > 0)
                    {
                        EmitToken(token.ToString(), output);
                        token.Length = 0;
                    }
                }
            }

            return output.ToString().Trim();
        }

        private static void EmitToken(string token, StringBuilder output)
        {
            // A token can be a mix of digits and letters ("2nd", "x3"), so
            // split on the digit/letter boundary and handle each run.
            int i = 0;
            while (i < token.Length)
            {
                bool digits = IsDigit(token[i]);
                int start = i;
                while (i < token.Length && IsDigit(token[i]) == digits) i++;

                string run = token.Substring(start, i - start);
                if (digits)
                {
                    Append(output, SpeakNumber(run));
                }
                else
                {
                    Append(output, SpeakWord(run));
                }
            }
        }

        private static string SpeakWord(string word)
        {
            string collapsed = CollapseRuns(word);
            string lower = collapsed.ToLowerInvariant();

            // An apostrophe is meaningful to the rules ("don't", "it's"), so
            // it survives; a leading or trailing one does not.
            lower = lower.Trim('\'');
            if (lower.Length == 0) return "";

            string expanded;
            if (Abbreviations.TryGetValue(lower, out expanded)) return expanded;

            // An all-consonant run of three or fewer letters is an initialism
            // more often than a word -- spell it out.
            if (collapsed.Length <= 3 && !HasVowel(lower))
            {
                StringBuilder letters = new StringBuilder();
                for (int i = 0; i < lower.Length; i++)
                {
                    if (letters.Length > 0) letters.Append(' ');
                    letters.Append(SpeakLetter(lower[i]));
                }
                return letters.ToString();
            }

            return lower;
        }

        private static string SpeakLetter(char c)
        {
            switch (c)
            {
                case 'a': return "ay";
                case 'b': return "bee";
                case 'c': return "see";
                case 'd': return "dee";
                case 'e': return "ee";
                case 'f': return "eff";
                case 'g': return "jee";
                case 'h': return "aitch";
                case 'i': return "eye";
                case 'j': return "jay";
                case 'k': return "kay";
                case 'l': return "ell";
                case 'm': return "em";
                case 'n': return "en";
                case 'o': return "oh";
                case 'p': return "pee";
                case 'q': return "cue";
                case 'r': return "arr";
                case 's': return "ess";
                case 't': return "tee";
                case 'u': return "you";
                case 'v': return "vee";
                case 'w': return "double you";
                case 'x': return "ex";
                case 'y': return "why";
                case 'z': return "zed";
                default: return "";
            }
        }

        /// <summary>
        /// "nooooo" to "noo", "!!!!!" to nothing. Two of a letter survive
        /// because English has real doubles; three or more never carry
        /// information worth four seconds of vowel.
        /// </summary>
        private static string CollapseRuns(string word)
        {
            StringBuilder b = new StringBuilder(word.Length);
            int run = 1;
            for (int i = 0; i < word.Length; i++)
            {
                if (i > 0 && char.ToLowerInvariant(word[i]) == char.ToLowerInvariant(word[i - 1]))
                {
                    run++;
                }
                else
                {
                    run = 1;
                }
                if (run <= 2) b.Append(word[i]);
            }
            return b.ToString();
        }

        private static string SpeakNumber(string digits)
        {
            // Trim a leading zero run but keep one digit.
            int start = 0;
            while (start < digits.Length - 1 && digits[start] == '0') start++;
            string s = digits.Substring(start);

            // Long runs are read digit by digit, which is both what a person
            // does with an ID and what avoids needing "quintillion".
            if (s.Length > 4)
            {
                StringBuilder b = new StringBuilder();
                for (int i = 0; i < s.Length; i++)
                {
                    if (b.Length > 0) b.Append(' ');
                    b.Append(Ones[s[i] - '0']);
                }
                return b.ToString();
            }

            long value;
            if (!long.TryParse(s, out value)) return "";
            return SpeakInteger(value);
        }

        private static string SpeakInteger(long n)
        {
            if (n < 0) return "minus " + SpeakInteger(-n);
            if (n < 10) return Ones[n];
            if (n < 20) return Teens[n - 10];
            if (n < 100)
            {
                string t = Tens[n / 10];
                long rest = n % 10;
                return rest == 0 ? t : t + " " + Ones[rest];
            }
            if (n < 1000)
            {
                string h = Ones[n / 100] + " hundred";
                long rest = n % 100;
                return rest == 0 ? h : h + " and " + SpeakInteger(rest);
            }
            if (n < 1000000)
            {
                string th = SpeakInteger(n / 1000) + " thousand";
                long rest = n % 1000;
                return rest == 0 ? th : th + " " + SpeakInteger(rest);
            }

            string m = SpeakInteger(n / 1000000) + " million";
            long r2 = n % 1000000;
            return r2 == 0 ? m : m + " " + SpeakInteger(r2);
        }

        /// <summary>
        /// Besiege formats chat through GlobalChatFormat / TeamChatFormat,
        /// which wrap the text in uGUI rich-text tags. Spoken literally those
        /// become "less than color equals".
        /// </summary>
        private static string StripRichText(string text)
        {
            StringBuilder b = new StringBuilder(text.Length);
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<') { depth++; continue; }
                if (c == '>') { if (depth > 0) depth--; continue; }
                if (depth == 0) b.Append(c);
            }
            return b.ToString();
        }

        private static string StripUrls(string text)
        {
            string[] words = text.Split(new char[] { ' ', '\t', '\n', '\r' },
                                        StringSplitOptions.RemoveEmptyEntries);
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                string lower = w.ToLowerInvariant();
                bool isUrl = lower.StartsWith("http://") || lower.StartsWith("https://")
                             || lower.StartsWith("www.") || lower.Contains(".com")
                             || lower.Contains(".net") || lower.Contains(".org");
                Append(b, isUrl ? "link" : w);
            }
            return b.ToString();
        }

        private static void Append(StringBuilder b, string s)
        {
            if (s == null || s.Length == 0) return;
            if (b.Length > 0) b.Append(' ');
            b.Append(s);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool HasVowel(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if ("aeiouy".IndexOf(s[i]) >= 0) return true;
            }
            return false;
        }
    }
}
