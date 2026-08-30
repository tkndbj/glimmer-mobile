namespace GlimmerGrove.Modes
{
    /// <summary>How big one bunch going off is. See <see cref="BudChain.Blast"/>.</summary>
    public enum BudBlast
    {
        /// <summary>Three or four: the rule being met.</summary>
        Small = 0,

        /// <summary>Five or more: worth a bigger ring and a lower note.</summary>
        Big = 1,

        /// <summary>Eight or more: the thing a player tells somebody about.</summary>
        Huge = 2,
    }

    /// <summary>
    /// How loudly a chain is celebrated, as a function of how far it ran.
    ///
    /// <para>
    /// <b>A celebration should say how good, not that something was good.</b> Confetti reads
    /// identically for a two-wave chain and a nine, so the grove counts them out loud instead —
    /// one number per wave while the chain is still running, because nobody watching the fourth
    /// wave knows yet whether there is a fifth, and a word at the end that climbs.
    /// </para>
    /// <para>
    /// In Domain rather than beside the paint, for <c>FallChain</c>'s reason: a switch on a wave
    /// count inside a <c>MonoBehaviour</c> is the one place in this game nothing can be proved,
    /// and how loud to shout is exactly the decision that gets retuned. <b>Measured before it was
    /// set</b>: the shipped grove runs chains of one to nine waves, and its biggest single tap
    /// is nine, so the ladder is pitched across that and its top word is reachable rather than
    /// theoretical.
    /// </para>
    /// </summary>
    public static class BudChain
    {
        /// <summary>Below this a chain is just a tap, and gets no number.</summary>
        public const int CountFrom = 2;

        /// <summary>
        /// Below this it gets a number but no word.
        ///
        /// <b>Two, not three.</b> It was three, which meant the word — the loudest thing this
        /// mode says — arrived only on a chain most taps never reach, and a player doing well
        /// was told so in a number. Two waves is already the grove going off on its own, and it
        /// is the moment worth naming.
        /// </summary>
        public const int NameFrom = 2;

        /// <summary>The longest chain this ladder distinguishes. Beyond it, the top rung.</summary>
        public const int Most = 9;

        public const int TopTier = Most - CountFrom;

        public static bool Counts(int waves) => waves >= CountFrom;

        public static int Tier(int waves)
        {
            int tier = waves - CountFrom;
            if (tier < 0) return 0;
            return tier > TopTier ? TopTier : tier;
        }

        /// <summary>
        /// The word at the end, or null for a chain too short to earn one.
        ///
        /// <para>
        /// <b>Four rungs, and they climb in a language the player already speaks.</b> It used to
        /// be LOVELY / WILD / GLORIOUS / WILDFIRE, which is the grove's own voice and is a
        /// perfectly nice thing to read — and it does not tell anybody how well they just did,
        /// because nobody knows whether GLORIOUS beats WILD. GREAT, AMAZING, EPIC, LEGENDARY is
        /// a ladder every player on earth can already order without being taught it, which is
        /// the entire job of the word: it is the score, said out loud.
        /// </para>
        /// <para>
        /// The four old keys are <b>retired</b> rather than re-pointed. A loc key is not
        /// permanent the way a level id is, but re-using one means a translator's memory and any
        /// shipped locale keep answering the old string for a rung that now means something
        /// else — so the ladder gets its own four and the old ones are named in
        /// <c>b01_thicket.py</c>'s retired list.
        /// </para>
        /// </summary>
        public static string WordKey(int waves)
        {
            if (waves < NameFrom) return null;

            if (waves <= 2) return "mode.bud.chain_great";
            if (waves <= 3) return "mode.bud.chain_amazing";
            if (waves <= 5) return "mode.bud.chain_epic";
            return "mode.bud.chain_legendary";
        }

        /// <summary>
        /// Which of the four rungs a chain lands on, 0 to 3.
        ///
        /// The view needs it as a number rather than as a string: the word is drawn in the
        /// rung's own colour and at the rung's own size, and reading either back off a loc key
        /// would be a second answer to a question this method already answers.
        /// </summary>
        public static int Rung(int waves)
        {
            if (waves < NameFrom) return -1;

            if (waves <= 2) return 0;
            if (waves <= 3) return 1;
            if (waves <= 5) return 2;
            return 3;
        }

        // ------------------------------------------------------------------ one bunch
        /// <summary>
        /// How loud one bunch going off is drawn, as a function of how many flowers were in it.
        ///
        /// <para>
        /// <b>Three alike is the rule and nine alike is the reward, and the mode drew them
        /// identically.</b> Every burst used the same six petals, the same ring and the same
        /// note, so the difference between scraping a bunch together and setting off a third of
        /// the grove at once was a number that appeared afterwards. That is <c>FallChain</c>'s
        /// complaint about confetti — <em>a celebration should say how good, not that something
        /// was good</em> — one level further down, at the single burst rather than at the chain.
        /// </para>
        /// <para>
        /// Two thresholds and no more. A bunch is at least <see cref="BudLayout.Bunch"/>, the
        /// shipped groves run bunches of three to about a dozen, and three rungs across that is
        /// a step somebody can see; a rung per flower would be a ramp nobody could read and
        /// eight numbers to retune.
        /// </para>
        /// </summary>
        public static BudBlast Blast(int bunch)
        {
            if (bunch >= HugeFrom) return BudBlast.Huge;
            return bunch >= BigFrom ? BudBlast.Big : BudBlast.Small;
        }

        /// <summary>Where a bunch stops being the rule being met and starts being an event.</summary>
        public const int BigFrom = 5, HugeFrom = 8;

        /// <summary>
        /// How much bigger than an ordinary burst this one is drawn — its rings, its petals and
        /// its reach, all off one number so a rung cannot be half-applied.
        /// </summary>
        public static float Force(BudBlast blast)
        {
            switch (blast)
            {
                case BudBlast.Huge: return 2f;
                case BudBlast.Big: return 1.45f;
                default: return 1f;
            }
        }

        /// <summary>How big the running count is drawn, in points.</summary>
        public static int PointsFor(int waves)
        {
            int points = 84 + Tier(waves) * 12;
            return points > 168 ? 168 : points;
        }

        /// <summary>How big the word at the end is drawn, in points.</summary>
        public static int WordPointsFor(int waves)
        {
            int rung = Rung(waves);
            if (rung < 0) rung = 0;

            // A rung apart is a step you can see across the room. The top one is deliberately
            // near the width of the panel it is drawn over — LEGENDARY should not fit
            // comfortably anywhere.
            int points = 122 + rung * 24;
            return points > 194 ? 194 : points;
        }
    }
}
