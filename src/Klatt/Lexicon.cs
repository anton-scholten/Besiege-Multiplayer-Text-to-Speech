using System;
using System.Collections.Generic;

namespace MultiplayerTTS.Klatt
{
    /// <summary>
    /// Words the letter-to-sound rules get wrong, spelled out by hand.
    ///
    /// This is not a shortcoming of the rule set so much as a property of
    /// English: the highest-frequency words are the ones that have had the
    /// most time to erode away from their spelling, so a few hundred entries
    /// buys more intelligibility than any number of extra rules. Every real
    /// system from DECtalk onwards ships one of these.
    ///
    /// The Besiege-specific block of entries at the end is the part worth
    /// maintaining: "machine" comes out as M AE CH IH N from the rules, and it
    /// is probably the single most common noun in this game's chat.
    /// </summary>
    public static class Lexicon
    {
        private static readonly Dictionary<string, string[]> words =
            new Dictionary<string, string[]>();

        static Lexicon()
        {
            // ---- Besiege vocabulary -------------------------------------
            Add("besiege", "B IH S IY JH");
            Add("machine", "M AX SH IY N");
            Add("machines", "M AX SH IY N Z");
            Add("cannon", "K AE N AX N");
            Add("cannons", "K AE N AX N Z");
            Add("ballista", "B AX L IH S T AX");
            Add("catapult", "K AE T AX P AH L T");
            Add("trebuchet", "T R EH B Y UW SH EY");
            Add("siege", "S IY JH");
            Add("wheel", "W IY L");
            Add("wheels", "W IY L Z");
            Add("steering", "S T IH R IH NG");
            Add("suspension", "S AX S P EH N SH AX N");
            Add("piston", "P IH S T AX N");
            Add("pistons", "P IH S T AX N Z");
            Add("propeller", "P R AX P EH L ER");
            Add("flywheel", "F L AY W IY L");
            Add("armour", "AA R M ER");
            Add("armor", "AA R M ER");
            Add("boulder", "B OW L D ER");
            Add("multiplayer", "M AH L T IY P L EY ER");
            Add("singleplayer", "S IH NG G AX L P L EY ER");
            Add("workshop", "W ER K SH AA P");
            Add("sandbox", "S AE N D B AA K S");
            Add("logic", "L AA JH IH K");
            Add("level", "L EH V AX L");
            Add("levels", "L EH V AX L Z");
            Add("engine", "EH N JH IH N");
            Add("engines", "EH N JH IH N Z");
            Add("physics", "F IH Z IH K S");
            Add("skin", "S K IH N");
            Add("mod", "M AA D");
            Add("mods", "M AA D Z");
            Add("spawn", "S P AO N");
            Add("respawn", "R IY S P AO N");
            Add("lag", "L AE G");
            Add("laggy", "L AE G IY");
            Add("ping", "P IH NG");
            Add("host", "HH OW S T");
            Add("server", "S ER V ER");
            Add("team", "T IY M");
            Add("teams", "T IY M Z");

            // ---- high-frequency irregulars ------------------------------
            Add("a", "AX");
            Add("the", "DH AX");
            Add("of", "AH V");
            Add("to", "T UW");
            Add("do", "D UW");
            Add("does", "D AH Z");
            Add("done", "D AH N");
            Add("go", "G OW");
            Add("goes", "G OW Z");
            Add("gone", "G AO N");
            Add("one", "W AH N");
            Add("once", "W AH N S");
            Add("two", "T UW");
            Add("some", "S AH M");
            Add("someone", "S AH M W AH N");
            Add("something", "S AH M TH IH NG");
            Add("somebody", "S AH M B AA D IY");
            Add("sometimes", "S AH M T AY M Z");
            Add("anyone", "EH N IY W AH N");
            Add("anything", "EH N IY TH IH NG");
            Add("everyone", "EH V R IY W AH N");
            Add("everything", "EH V R IY TH IH NG");
            Add("every", "EH V R IY");
            Add("very", "V EH R IY");
            Add("many", "M EH N IY");
            Add("any", "EH N IY");
            Add("friend", "F R EH N D");
            Add("friends", "F R EH N D Z");
            Add("said", "S EH D");
            Add("says", "S EH Z");
            Add("again", "AX G EH N");
            Add("against", "AX G EH N S T");
            Add("answer", "AE N S ER");
            Add("people", "P IY P AX L");
            Add("been", "B IH N");
            Add("build", "B IH L D");
            Add("built", "B IH L T");
            Add("building", "B IH L D IH NG");
            Add("would", "W UH D");
            Add("could", "K UH D");
            Add("should", "SH UH D");
            Add("your", "Y AO R");
            Add("you're", "Y AO R");
            Add("yours", "Y AO R Z");
            Add("there", "DH EH R");
            Add("their", "DH EH R");
            Add("they're", "DH EH R");
            Add("they", "DH EY");
            Add("where", "W EH R");
            Add("were", "W ER");
            Add("we're", "W IH R");
            Add("here", "HH IH R");
            Add("hear", "HH IH R");
            Add("heard", "HH ER D");
            Add("what", "W AH T");
            Add("want", "W AA N T");
            Add("wants", "W AA N T S");
            Add("was", "W AA Z");
            Add("who", "HH UW");
            Add("whose", "HH UW Z");
            Add("why", "W AY");
            Add("work", "W ER K");
            Add("works", "W ER K S");
            Add("working", "W ER K IH NG");
            Add("word", "W ER D");
            Add("world", "W ER L D");
            Add("worth", "W ER TH");
            Add("word", "W ER D");
            Add("come", "K AH M");
            Add("comes", "K AH M Z");
            Add("coming", "K AH M IH NG");
            Add("give", "G IH V");
            Add("gives", "G IH V Z");
            Add("live", "L IH V");
            Add("love", "L AH V");
            Add("move", "M UW V");
            Add("moves", "M UW V Z");
            Add("prove", "P R UW V");
            Add("have", "HH AE V");
            Add("having", "HH AE V IH NG");
            Add("great", "G R EY T");
            Add("break", "B R EY K");
            Add("broke", "B R OW K");
            Add("broken", "B R OW K AX N");
            Add("both", "B OW TH");
            Add("only", "OW N L IY");
            Add("other", "AH DH ER");
            Add("others", "AH DH ER Z");
            Add("another", "AX N AH DH ER");
            Add("mother", "M AH DH ER");
            Add("brother", "B R AH DH ER");
            Add("nothing", "N AH TH IH NG");
            Add("through", "TH R UW");
            Add("though", "DH OW");
            Add("thought", "TH AO T");
            Add("enough", "IH N AH F");
            Add("rough", "R AH F");
            Add("tough", "T AH F");
            Add("laugh", "L AE F");
            Add("cough", "K AO F");
            Add("eye", "AY");
            Add("eyes", "AY Z");
            Add("busy", "B IH Z IY");
            Add("business", "B IH Z N AX S");
            Add("women", "W IH M IH N");
            Add("woman", "W UH M AX N");
            Add("man", "M AE N");
            Add("men", "M EH N");
            Add("child", "CH AY L D");
            Add("children", "CH IH L D R AX N");
            Add("money", "M AH N IY");
            Add("honest", "AA N AX S T");
            Add("hour", "AW ER");
            Add("hours", "AW ER Z");
            Add("island", "AY L AX N D");
            Add("iron", "AY ER N");
            Add("area", "EH R IY AX");
            Add("idea", "AY D IY AX");
            Add("real", "R IY L");
            Add("really", "R IH L IY");
            Add("sure", "SH UH R");
            Add("sugar", "SH UH G ER");
            Add("science", "S AY AX N S");
            Add("ocean", "OW SH AX N");
            Add("nature", "N EY CH ER");
            Add("picture", "P IH K CH ER");
            Add("future", "F Y UW CH ER");
            Add("minute", "M IH N IH T");
            Add("minutes", "M IH N IH T S");
            Add("second", "S EH K AX N D");
            Add("seconds", "S EH K AX N D Z");
            Add("colour", "K AH L ER");
            Add("color", "K AH L ER");
            Add("front", "F R AH N T");
            Add("front", "F R AH N T");
            Add("above", "AX B AH V");
            Add("about", "AX B AW T");
            Add("above", "AX B AH V");
            Add("said", "S EH D");
            Add("says", "S EH Z");
            Add("use", "Y UW Z");
            Add("used", "Y UW Z D");
            Add("using", "Y UW Z IH NG");
            Add("useful", "Y UW S F UH L");
            Add("put", "P UH T");
            Add("puts", "P UH T S");
            Add("push", "P UH SH");
            Add("pull", "P UH L");
            Add("full", "F UH L");
            Add("bull", "B UH L");
            Add("pretty", "P R IH T IY");
            Add("front", "F R AH N T");
            Add("none", "N AH N");
            Add("won", "W AH N");
            Add("won't", "W OW N T");
            Add("don't", "D OW N T");
            Add("can't", "K AE N T");
            Add("isn't", "IH Z AX N T");
            Add("wasn't", "W AA Z AX N T");
            Add("didn't", "D IH D AX N T");
            Add("doesn't", "D AH Z AX N T");
            Add("i'm", "AY M");
            Add("i've", "AY V");
            Add("i'll", "AY L");
            Add("it's", "IH T S");
            Add("that's", "DH AE T S");
            Add("let's", "L EH T S");
            Add("lets", "L EH T S");
            Add("also", "AO L S OW");
            Add("always", "AO L W EY Z");
            Add("almost", "AO L M OW S T");
            Add("already", "AO L R EH D IY");
            Add("alright", "AO L R AY T");
            Add("okay", "OW K EY");
            Add("yeah", "Y AE");
            Add("yes", "Y EH S");
            Add("no", "N OW");
            Add("nice", "N AY S");
            Add("cool", "K UW L");
            Add("good", "G UH D");
            Add("game", "G EY M");
            Add("games", "G EY M Z");
            Add("play", "P L EY");
            Add("playing", "P L EY IH NG");
            Add("player", "P L EY ER");
            Add("players", "P L EY ER Z");
            Add("thanks", "TH AE NG K S");
            Add("thank", "TH AE NG K");
            Add("please", "P L IY Z");
            Add("sorry", "S AA R IY");
            Add("hello", "HH AX L OW");
            Add("hey", "HH EY");
            Add("bye", "B AY");
            Add("wait", "W EY T");
            Add("ready", "R EH D IY");
            Add("again", "AX G EH N");
            Add("try", "T R AY");
            Add("trying", "T R AY IH NG");
            Add("tried", "T R AY D");
            Add("look", "L UH K");
            Add("looks", "L UH K S");
            Add("looking", "L UH K IH NG");
            Add("book", "B UH K");
            Add("took", "T UH K");
            Add("know", "N OW");
            Add("knows", "N OW Z");
            Add("known", "N OW N");
            Add("new", "N UW");
            Add("news", "N UW Z");
            Add("now", "N AW");
            Add("how", "HH AW");
            Add("down", "D AW N");
            Add("town", "T AW N");
            Add("out", "AW T");
            Add("our", "AW ER");
            Add("house", "HH AW S");
            Add("mouse", "M AW S");
            Add("south", "S AW TH");
            Add("north", "N AO R TH");
            Add("first", "F ER S T");
            Add("girl", "G ER L");
            Add("early", "ER L IY");
            Add("earth", "ER TH");
            Add("learn", "L ER N");
            Add("heart", "HH AA R T");
            Add("head", "HH EH D");
            Add("dead", "D EH D");
            Add("death", "D EH TH");
            Add("bread", "B R EH D");
            Add("ahead", "AX HH EH D");
            Add("instead", "IH N S T EH D");
            Add("weather", "W EH DH ER");
            Add("together", "T AX G EH DH ER");
            Add("whether", "W EH DH ER");
            Add("either", "IY DH ER");
            Add("neither", "N IY DH ER");
        }

        private static void Add(string word, string phonemes)
        {
            words[word] = phonemes.Split(new char[] { ' ' },
                                         StringSplitOptions.RemoveEmptyEntries);
        }

        public static bool TryGet(string word, out string[] phonemes)
        {
            return words.TryGetValue(word, out phonemes);
        }
    }
}
