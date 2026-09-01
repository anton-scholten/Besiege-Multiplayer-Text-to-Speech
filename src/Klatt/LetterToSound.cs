using System;
using System.Collections.Generic;
using System.Text;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// English text to phonemes, by the NRL letter-to-sound rules (Elovitz et
    /// al., NRL Report 7948, 1976). This is the same family of rule set the
    /// small DECtalk-era systems used, and it is what gives the voice its
    /// characteristic confident mispronunciation of anything unusual.
    ///
    /// A rule is written LEFT[MATCH]RIGHT=PHONEMES. The rules for a letter are
    /// tried in order and the first whose contexts all match wins, so the
    /// specific cases are listed before the general ones and the last rule for
    /// each letter is its bare fallback.
    ///
    /// Context characters:
    ///     #  one or more vowels          :  zero or more consonants
    ///     ^  one consonant               .  one voiced consonant
    ///     +  one front vowel (E, I, Y)   &amp;  one sibilant
    ///     %  a suffix (E, ER, ES, ED, ING, ELY)
    ///     @  a consonant that palatalises a following U
    ///     (space) a word boundary
    /// </summary>
    public static class LetterToSound
    {
        private class Rule
        {
            public string Left;
            public string Match;
            public string Right;
            public string[] Phones;
        }

        // Indexed by letter (0..25), plus one bucket for everything else.
        private static readonly List<Rule>[] rules = new List<Rule>[27];

        private const string Vowels = "AEIOUY";
        private const string Consonants = "BCDFGHJKLMNPQRSTVWXZ";
        private const string VoicedConsonants = "BDVGJLMNRWZ";
        private const string FrontVowels = "EIY";

        static LetterToSound()
        {
            for (int i = 0; i < rules.Length; i++) rules[i] = new List<Rule>();
            foreach (string text in RuleText) AddRule(text);
        }

        // ---------------------------------------------------------------
        // Rule compilation
        // ---------------------------------------------------------------

        private static void AddRule(string text)
        {
            int open = text.IndexOf('[');
            int close = text.IndexOf(']');
            int equals = text.IndexOf('=', close);
            if (open < 0 || close < 0 || equals < 0) return;

            Rule r = new Rule();
            r.Left = text.Substring(0, open);
            r.Match = text.Substring(open + 1, close - open - 1);
            r.Right = text.Substring(close + 1, equals - close - 1);

            string output = text.Substring(equals + 1).Trim();
            if (output.Length == 0)
            {
                r.Phones = new string[0];
            }
            else
            {
                r.Phones = output.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            int bucket = 26;
            if (r.Match.Length > 0)
            {
                char c = r.Match[0];
                if (c >= 'A' && c <= 'Z') bucket = c - 'A';
            }
            rules[bucket].Add(r);
        }

        // ---------------------------------------------------------------
        // Translation
        // ---------------------------------------------------------------

        /// <summary>
        /// Translate a run of text into ARPAbet phoneme names. Word breaks
        /// come back as "_" so the prosody stage can find them.
        /// </summary>
        public static List<string> Translate(string text)
        {
            List<string> output = new List<string>();
            string normalised = TextNormaliser.Normalise(text);

            string[] words = normalised.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int w = 0; w < words.Length; w++)
            {
                if (w > 0) output.Add("_");
                TranslateWord(words[w], output);
            }

            Degeminate(output);
            return output;
        }

        /// <summary>
        /// Collapse a doubled consonant to one. The rules translate letter by
        /// letter, so "cannon" comes out as K AE N N AX N -- and a doubled
        /// nasal is not a long nasal, it is a stutter.
        ///
        /// Vowels are left alone: a genuine sequence like "cooperate" needs
        /// both, and a doubled vowel across a word break is a real pause.
        /// </summary>
        private static void Degeminate(List<string> phones)
        {
            for (int i = phones.Count - 1; i > 0; i--)
            {
                if (phones[i] != phones[i - 1]) continue;
                if (Phonemes.Get(phones[i]).Class == Phonemes.ClassVowel) continue;
                phones.RemoveAt(i);
            }
        }

        private static void TranslateWord(string word, List<string> output)
        {
            // The exception lexicon wins over the rules: the words it holds
            // are the ones the rules are known to get wrong.
            string[] known;
            if (Lexicon.TryGet(word.ToLowerInvariant(), out known))
            {
                for (int k = 0; k < known.Length; k++)
                {
                    if (Phonemes.Has(known[k])) output.Add(known[k]);
                }
                return;
            }

            // Pad with spaces so the word-boundary context has something to
            // match against at both ends.
            string padded = " " + word.ToUpperInvariant() + " ";
            int i = 1;
            int guard = 0;

            while (i < padded.Length - 1)
            {
                if (++guard > 4096) break;   // no rule matched and consumed; bail out

                char c = padded[i];
                int bucket = (c >= 'A' && c <= 'Z') ? c - 'A' : 26;

                Rule hit = null;
                List<Rule> candidates = rules[bucket];
                for (int r = 0; r < candidates.Count; r++)
                {
                    if (Matches(candidates[r], padded, i))
                    {
                        hit = candidates[r];
                        break;
                    }
                }

                if (hit == null)
                {
                    // Not a letter we have rules for -- skip it rather than
                    // stalling. Punctuation reaches here after normalisation.
                    i++;
                    continue;
                }

                for (int p = 0; p < hit.Phones.Length; p++)
                {
                    string name = hit.Phones[p];
                    if (name == "WH") name = "W";        // no separate /hw/ in the inventory
                    if (Phonemes.Has(name)) output.Add(name);
                }

                i += hit.Match.Length;
            }
        }

        private static bool Matches(Rule r, string s, int at)
        {
            // The literal body.
            if (at + r.Match.Length > s.Length) return false;
            for (int i = 0; i < r.Match.Length; i++)
            {
                if (s[at + i] != r.Match[i]) return false;
            }

            if (!MatchRight(r.Right, s, at + r.Match.Length)) return false;
            if (!MatchLeft(r.Left, s, at - 1)) return false;
            return true;
        }

        // Right context reads forwards from `pos`.
        private static bool MatchRight(string ctx, string s, int pos)
        {
            for (int i = 0; i < ctx.Length; i++)
            {
                char rule = ctx[i];
                char c = pos < s.Length ? s[pos] : ' ';

                if (rule == '#')
                {
                    if (!IsVowel(c)) return false;
                    while (pos < s.Length && IsVowel(s[pos])) pos++;
                }
                else if (rule == ':')
                {
                    while (pos < s.Length && IsConsonant(s[pos])) pos++;
                }
                else if (rule == '^')
                {
                    if (!IsConsonant(c)) return false;
                    pos++;
                }
                else if (rule == '.')
                {
                    if (VoicedConsonants.IndexOf(c) < 0) return false;
                    pos++;
                }
                else if (rule == '+')
                {
                    if (FrontVowels.IndexOf(c) < 0) return false;
                    pos++;
                }
                else if (rule == '&')
                {
                    if (!IsSibilant(s, pos)) return false;
                    pos++;
                    if (pos < s.Length && (s[pos] == 'H')) pos++;
                }
                else if (rule == '%')
                {
                    int consumed = SuffixLength(s, pos);
                    if (consumed < 0) return false;
                    pos += consumed;
                }
                else if (rule == '@')
                {
                    if (!IsPalatalising(s, pos)) return false;
                    pos++;
                }
                else
                {
                    if (c != rule) return false;
                    pos++;
                }
            }
            return true;
        }

        // Left context reads backwards from `pos`, so the rule text is walked
        // right-to-left too.
        private static bool MatchLeft(string ctx, string s, int pos)
        {
            for (int i = ctx.Length - 1; i >= 0; i--)
            {
                char rule = ctx[i];
                char c = pos >= 0 ? s[pos] : ' ';

                if (rule == '#')
                {
                    if (!IsVowel(c)) return false;
                    while (pos >= 0 && IsVowel(s[pos])) pos--;
                }
                else if (rule == ':')
                {
                    while (pos >= 0 && IsConsonant(s[pos])) pos--;
                }
                else if (rule == '^')
                {
                    if (!IsConsonant(c)) return false;
                    pos--;
                }
                else if (rule == '.')
                {
                    if (VoicedConsonants.IndexOf(c) < 0) return false;
                    pos--;
                }
                else if (rule == '+')
                {
                    if (FrontVowels.IndexOf(c) < 0) return false;
                    pos--;
                }
                else if (rule == '&')
                {
                    if (!IsSibilantBack(s, pos)) return false;
                    pos--;
                }
                else if (rule == '@')
                {
                    if (!IsPalatalising(s, pos)) return false;
                    pos--;
                }
                else
                {
                    if (c != rule) return false;
                    pos--;
                }
            }
            return true;
        }

        private static bool IsVowel(char c)
        {
            return Vowels.IndexOf(c) >= 0;
        }

        private static bool IsConsonant(char c)
        {
            return Consonants.IndexOf(c) >= 0;
        }

        private static bool IsSibilant(string s, int pos)
        {
            if (pos >= s.Length) return false;
            char c = s[pos];
            if ("SCGZXJ".IndexOf(c) >= 0) return true;
            if ((c == 'C' || c == 'S') && pos + 1 < s.Length && s[pos + 1] == 'H') return true;
            return false;
        }

        private static bool IsSibilantBack(string s, int pos)
        {
            if (pos < 0) return false;
            char c = s[pos];
            if ("SCGZXJ".IndexOf(c) >= 0) return true;
            if (c == 'H' && pos > 0 && (s[pos - 1] == 'C' || s[pos - 1] == 'S')) return true;
            return false;
        }

        private static bool IsPalatalising(string s, int pos)
        {
            if (pos < 0 || pos >= s.Length) return false;
            return "TSRDLZNJ".IndexOf(s[pos]) >= 0;
        }

        // Returns how many characters the suffix occupies, or -1 for no match.
        private static int SuffixLength(string s, int pos)
        {
            if (pos >= s.Length) return -1;
            if (Ahead(s, pos, "ING")) return 3;
            if (Ahead(s, pos, "ELY")) return 3;
            if (Ahead(s, pos, "ER")) return 2;
            if (Ahead(s, pos, "ES")) return 2;
            if (Ahead(s, pos, "ED")) return 2;
            if (s[pos] == 'E') return 1;
            return -1;
        }

        private static bool Ahead(string s, int pos, string what)
        {
            if (pos + what.Length > s.Length) return false;
            for (int i = 0; i < what.Length; i++)
            {
                if (s[pos + i] != what[i]) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------
        // The rule set
        // ---------------------------------------------------------------

        private static readonly string[] RuleText = new string[]
        {
            // ---- A ----
            " [A] =AX",
            " [ARE] =AA R",
            " [AR]O=AX R",
            "[AR]#=EH R",
            "^[AS]#=EY S",
            "[A]WA=AX",
            "[AW]=AO",
            " :[ANY]=EH N IY",
            "[A]^+#=EY",
            "#:[ALLY]=AX L IY",
            " [AL]#=AX L",
            "[AGAIN]=AX G EH N",
            "#:[AG]E=IH JH",
            "[A]^%=EY",
            "[A]^+:#=AE",
            " :[A]^+ =EY",
            " [ARR]=AX R",
            "[ARR]=AE R",
            " ^[AR] =AA R",
            "[AR]=AA R",
            "[AIR]=EH R",
            "[AI]=EY",
            "[AY]=EY",
            "[AU]=AO",
            "#:[AL] =AX L",
            "#:[ALS] =AX L Z",
            "[ALK]=AO K",
            "[AL]^=AO L",
            " :[ABLE]=EY B AX L",
            "[ABLE]=AX B AX L",
            "[ANG]+=EY N JH",
            "[A]=AE",

            // ---- B ----
            " [BE]^#=B IH",
            "[BEING]=B IY IH NG",
            " [BOTH] =B OW TH",
            " [BUS]#=B IH Z",
            "[BUIL]=B IH L",
            "[B]=B",

            // ---- C ----
            " [CH]^=K",
            "^E[CH]=K",
            "[CH]=CH",
            " S[CI]#=S AY",
            "[CI]A=SH",
            "[CI]O=SH",
            "[CI]EN=SH",
            "[C]+=S",
            "[CK]=K",
            "[COM]%=K AH M",
            "[C]=K",

            // ---- D ----
            "#:[DED] =D IH D",
            ".E[D] =D",
            "#:^E[D] =T",
            " [DE]^#=D IH",
            " [DO] =D UW",
            " [DOES]=D AH Z",
            " [DOING]=D UW IH NG",
            " [DOW]=D AW",
            "[DU]A=JH UW",
            "[D]=D",

            // ---- E ----
            "#:[E] =",
            "'^:[E] =",
            " :[E] =IY",
            "#[ED] =D",
            "#:[E]D =",
            "[EV]ER=EH V",
            "[E]^%=IY",
            "[ERI]#=IY R IY",
            "[ERI]=EH R IH",
            "#:[ER]#=ER",
            "[ER]#=EH R",
            "[ER]=ER",
            " [EVEN]=IY V EH N",
            "#:[E]W=",
            "@[EW]=UW",
            "[EW]=Y UW",
            "[E]O=IY",
            "#:&[ES] =IH Z",
            "#:[E]S =",
            "#:[ELY] =L IY",
            "#:[EMENT]=M EH N T",
            "[EFUL]=F UH L",
            "[EE]=IY",
            "[EARN]=ER N",
            " [EAR]^=ER",
            "[EAD]=EH D",
            "#:[EA] =IY AX",
            "[EA]SU=EH",
            "[EA]=IY",
            "[EIGH]=EY",
            "[EI]=IY",
            " [EYE]=AY",
            "[EY]=IY",
            "[EU]=Y UW",
            "[E]=EH",

            // ---- F ----
            "[FUL]=F UH L",
            "[F]=F",

            // ---- G ----
            "[GIV]=G IH V",
            " [G]I^=G",
            "[GE]T=G EH",
            "SU[GGES]=G JH EH S",
            "[GG]=G",
            " B#[G]=G",
            "[G]+=JH",
            "[GREAT]=G R EY T",
            "#[GH]=",
            "[G]=G",

            // ---- H ----
            " [HAV]=HH AE V",
            " [HERE]=HH IY R",
            " [HOUR]=AW ER",
            "[HOW]=HH AW",
            "[H]#=HH",
            "[H]=",

            // ---- I ----
            " [IN]=IH N",
            " [I] =AY",
            "[IN]D=AY N",
            "[IER]=IY ER",
            "#:R[IED] =IY D",
            "[IED] =AY D",
            "[IEN]=IY EH N",
            "[IE]T=AY EH",
            " :[I]%=AY",
            "[I]%=IY",
            "[IE]=IY",
            "[I]^+:#=IH",
            "[IR]#=AY R",
            "[IZ]%=AY Z",
            "[IS]%=AY Z",
            "[I]D%=AY",
            "+^[I]^+=IH",
            "[I]T%=AY",
            "#:^[I]^+=IH",
            "[I]^+=AY",
            "[IR]=ER",
            "[IGH]=AY",
            "[ILD]=AY L D",
            "[IGN] =AY N",
            "[IGN]^=AY N",
            "[IGN]%=AY N",
            "[IQUE]=IY K",
            "[I]=IH",

            // ---- J ----
            "[J]=JH",

            // ---- K ----
            " [K]N=",
            "[K]=K",

            // ---- L ----
            "[LO]C#=L OW",
            "L[L]=",
            "#:^[L]%=AX L",
            "[LEAD]=L IY D",
            "[L]=L",

            // ---- M ----
            "[MOV]=M UW V",
            "[M]=M",

            // ---- N ----
            "E[NG]+=N JH",
            "[NG]R=NG G",
            "[NG]#=NG G",
            "[NGL]%=NG G AX L",
            "[NG]=NG",
            "[NK]=NG K",
            " [NOW] =N AW",
            "[N]=N",

            // ---- O ----
            "[OF] =AX V",
            "[OROUGH]=ER OW",
            "#:[OR] =ER",
            "#:[ORS] =ER Z",
            "[OR]=AO R",
            " [ONE]=W AH N",
            "[OW]=OW",
            " [OVER]=OW V ER",
            "[OV]=AH V",
            "[O]^%=OW",
            "[O]^EN=OW",
            "[O]^I#=OW",
            "[OL]D=OW L",
            "[OUGHT]=AO T",
            "[OUGH]=AH F",
            " [OU]=AW",
            "H[OU]S#=AW",
            "[OUS]=AX S",
            "[OUR]=AO R",
            "[OULD]=UH D",
            "^[OU]^L=AH",
            "[OUP]=UW P",
            "[OU]=AW",
            "[OY]=OY",
            "[OING]=OW IH NG",
            "[OI]=OY",
            "[OOR]=AO R",
            "[OOK]=UH K",
            "[OOD]=UH D",
            "[OO]=UW",
            "[O]E=OW",
            "[O] =OW",
            "[OA]=OW",
            " [ONLY]=OW N L IY",
            " [ONCE]=W AH N S",
            "[ON'T]=OW N T",
            "C[O]N=AA",
            "[O]NG=AO",
            " :^[O]N=AH",
            "I[ON]=AX N",
            "#:[ON] =AX N",
            "#^[ON]=AX N",
            "[O]ST =OW",
            "[OF]^=AO F",
            "[OTHER]=AH DH ER",
            "[OSS] =AO S",
            "#^:[OM]=AH M",
            "[O]=AA",

            // ---- P ----
            "[PH]=F",
            "[PEOP]=P IY P",
            "[POW]=P AW",
            "[PUT] =P UH T",
            "[P]=P",

            // ---- Q ----
            "[QUAR]=K W AO R",
            "[QU]=K W",
            "[Q]=K",

            // ---- R ----
            " [RE]^#=R IY",
            "[R]=R",

            // ---- S ----
            "[SH]=SH",
            "#[SION]=ZH AX N",
            "[SOME]=S AH M",
            "#[SUR]#=ZH ER",
            "[SUR]#=SH ER",
            "#[SU]#=ZH UW",
            "#[SSU]#=SH UW",
            "#[SED] =Z D",
            "#[S]#=Z",
            "[SAID]=S EH D",
            "^[SION]=SH AX N",
            "[S]S=",
            ".[S] =Z",
            "#:.E[S] =Z",
            "#:^##[S] =Z",
            "#:^#[S] =S",
            "U[S] =S",
            " :#[S] =Z",
            " [SCH]=S K",
            "[S]C+=",
            "#[SM]=Z M",
            "#[SN]'=Z AX N",
            "[S]=S",

            // ---- T ----
            " [THE] =DH AX",
            "[TO] =T UW",
            "[THAT] =DH AE T",
            " [THIS] =DH IH S",
            " [THEY]=DH EY",
            " [THERE]=DH EH R",
            "[THER]=DH ER",
            "[THEIR]=DH EH R",
            " [THAN] =DH AE N",
            " [THEM] =DH EH M",
            "[THESE] =DH IY Z",
            " [THEN]=DH EH N",
            "[THROUGH]=TH R UW",
            "[THOSE]=DH OW Z",
            "[THOUGH] =DH OW",
            " [THUS]=DH AH S",
            "[TH]=TH",
            "#:[TED] =T IH D",
            "S[TI]#N=CH",
            "[TI]O=SH",
            "[TI]A=SH",
            "[TIEN]=SH AX N",
            "[TUR]#=CH ER",
            "[TU]A=CH UW",
            " [TWO]=T UW",
            "[T]=T",

            // ---- U ----
            " [UN]I=Y UW N",
            " [UN]=AH N",
            " [UPON]=AX P AO N",
            "@[UR]#=UH R",
            "[UR]#=Y UH R",
            "[UR]=ER",
            "[U]^ =AH",
            "[U]^^=AH",
            "[UY]=AY",
            " G[U]#=",
            "G[U]%=W",
            "G[U]#=W",
            "#N[U]=Y UW",
            "@[U]=UW",
            "[U]=Y UW",

            // ---- V ----
            "[VIEW]=V Y UW",
            "[V]=V",

            // ---- W ----
            " [WERE]=W ER",
            "[WA]S=W AA",
            "[WA]T=W AA",
            "[WHERE]=WH EH R",
            "[WHAT]=WH AA T",
            "[WHOL]=HH OW L",
            "[WHO]=HH UW",
            "[WH]=WH",
            "[WAR]=W AO R",
            "[WOR]^=W ER",
            "[WR]=R",
            "[W]=W",

            // ---- X ----
            " [X]=Z",
            "[X]=K S",

            // ---- Y ----
            "[YOUNG]=Y AH NG",
            " [YOU]=Y UW",
            " [YES]=Y EH S",
            " [Y]=Y",
            "#^:[Y] =IY",
            "#^:[Y]I=IY",
            " :[Y] =AY",
            " :[Y]#=AY",
            " :[Y]^+:#=IH",
            " :[Y]^#=AY",
            "[Y]=IH",

            // ---- Z ----
            "[Z]=Z",
        };
    }
}
