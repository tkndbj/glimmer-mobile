namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How close a grove is to running out of tiles, as a reading the screen can colour by.
    ///
    /// Fractions of this level's own basket rather than fixed counts, for <c>FallPressure</c>'s
    /// reason: three tiles left is comfortable on a board dealt thirty and desperate on one dealt
    /// eight, so a threshold written as a count would mean a different thing on every level.
    /// </summary>
    public enum KeeperPressure
    {
        /// <summary>Tiles to spare.</summary>
        Easy = 0,

        /// <summary>Under a third of the basket: worth counting.</summary>
        Low = 1,

        /// <summary>Under a sixth, which on any grove that ships is a tile or two.</summary>
        Critical = 2,
    }

    /// <summary>
    /// A grove's basket: how many tiles this run may spend, how many are gone, and which of the
    /// two ways they went.
    ///
    /// <para>
    /// <b>It is the mode's budget, in the unit the mode is graded in</b> (invariant 22b). A
    /// Groovekeeper run is measured in tiles — a tile is this mode's turn — so the same par plus
    /// slack every other mode is dealt is dealt here as tiles, the basket counts them down, and
    /// the run ends when the last one is spent with a bed still waiting. Nothing about the
    /// grading is special: the same three factors over the same derived par, which is what stops
    /// a second mode quietly retuning the economy.
    /// </para>
    /// <para>
    /// <b>A tile is spent by being planted <em>or</em> composted, and that is one number on
    /// purpose.</b> Composting exists because a heartbed refuses every colour but its own, so a
    /// run can be holding exactly the wrong tile — and the honest answer to that is not a free
    /// re-deal but a priced one. Since both cost the same tile, what the player is being asked is
    /// simply "is moving the procession on worth a tile", which is a decision they can take with
    /// the basket in front of them. The two counts are kept apart only because the screen wants
    /// to say which happened, never because they are worth different amounts.
    /// </para>
    /// <para>
    /// Pure integers and no policy at all, for <c>RippleSatchel</c>'s reason: where the budget comes
    /// from is <c>KeeperMode.Tune</c>'s business, when a run is lost is
    /// <see cref="KeeperVerdict"/>'s, and what the player sees is the screen's.
    /// </para>
    /// </summary>
    public sealed class KeeperBasket
    {
        /// <summary>
        /// A grove with no basket, which therefore cannot run out.
        ///
        /// <see cref="int.MaxValue"/> rather than a flag, so <see cref="Left"/> compares without
        /// special-casing at every call site — the bargain <c>LevelTuning.MoveBudget</c> already
        /// makes for a glade authored with no budget.
        /// </summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>
        /// Tiles this run may spend.
        ///
        /// The pot rather than a constant, because it can be topped up: a continue that has been
        /// paid for raises this and nothing else. Note what it deliberately does not touch —
        /// <see cref="Spent"/>, which is the grade, so a grove finished with bought tiles scores
        /// exactly what it spent (invariant 23).
        /// </summary>
        public int Dealt { get; private set; }

        /// <summary>Tiles planted, which is the grove that is standing.</summary>
        public int Planted { get; private set; }

        /// <summary>Tiles composted to move the procession on.</summary>
        public int Composted { get; private set; }

        public KeeperBasket(int dealt)
            => Dealt = dealt < 0 ? Unlimited : dealt;

        /// <summary>Tiles spent however they went. This is the grade and the record.</summary>
        public int Spent => Planted + Composted;

        public bool Bounded => Dealt != Unlimited;

        /// <summary>Tiles still to come. <see cref="Unlimited"/> on a grove with no basket.</summary>
        public int Left
        {
            get
            {
                if (!Bounded) return Unlimited;
                int left = Dealt - Spent;
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>Whether there is a tile left to spend at all.</summary>
        public bool Any => Left > 0;

        /// <summary>Takes one for a tile that has landed. Called once per planting and nowhere else.</summary>
        public void Plant() => Planted++;

        /// <summary>Takes one for a tile turned back into the ground.</summary>
        public void Compost() => Composted++;

        /// <summary>
        /// Deals more, because a continue was paid for. Guarded against overflow rather than
        /// trusted: what is handed over is content and a content push is what changes it.
        /// </summary>
        public void Grant(int tiles)
        {
            if (tiles <= 0 || !Bounded) return;
            Dealt = tiles > Unlimited - Dealt ? Unlimited : Dealt + tiles;
        }

        /// <summary>
        /// How close this run is to running out. Unbounded baskets are always
        /// <see cref="KeeperPressure.Easy"/>, because there is nothing to be close to.
        /// </summary>
        public KeeperPressure Pressure
        {
            get
            {
                if (!Bounded || Dealt <= 0) return KeeperPressure.Easy;

                // Integer arithmetic throughout, for LevelTuning's reason: a threshold decided by
                // a float is a threshold three code generators round three ways.
                int left = Left;
                if (left * 6 <= Dealt) return KeeperPressure.Critical;
                if (left * 3 <= Dealt) return KeeperPressure.Low;
                return KeeperPressure.Easy;
            }
        }
    }
}
