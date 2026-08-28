namespace GlimmerGrove.Modes
{
    /// <summary>How a Groovekeeper run stands, once and in one word.</summary>
    public enum KeeperEnding
    {
        /// <summary>Still being played.</summary>
        Live = 0,

        /// <summary>Every bed is open. Won.</summary>
        Grown = 1,

        /// <summary>The grove has nowhere left to grow, with a bed still waiting.</summary>
        Overgrown = 2,

        /// <summary>The basket ran out with a bed still waiting.</summary>
        Starved = 3,
    }

    /// <summary>
    /// The reading of a grove against its basket: whether the run is over, how, and what it would
    /// take to carry it on.
    ///
    /// <para>
    /// <b>One predicate rather than three booleans in an <c>if</c> on a screen.</b> That is the
    /// shape this project keeps paying for: every one of those booleans is an edge where the run
    /// is decided and the screen has not caught up, and a condition spread across them cannot be
    /// proved. <see cref="FallVerdict"/> and <c>WeaveVerdict</c> are the same class for the same
    /// reason, and every branch here is arithmetic over a board and two integers.
    /// </para>
    /// <para>
    /// <b>The order is the order a player would want.</b> A finished grove wins even if the tile
    /// that finished it was the last in the basket and left nowhere to grow — there is nothing
    /// left to want — and a grove that has just run out of room is not also reported as starved.
    /// </para>
    /// <para>
    /// <b>Two fail states, and only one of them may be sold a continue</b> (invariant 26b's rule
    /// for this mode). Running out of tiles is a shortage and more tiles fix it. Running out of
    /// <em>room</em> is not: no number of tiles gives a grove somewhere to grow that it does not
    /// have, so <see cref="Deficit"/> answers <see cref="RunContinueDeficit.None"/> and the offer
    /// is never made. That also means the mistake money cannot fix is the spatial one, which is
    /// the half this mode is actually about.
    /// </para>
    /// </summary>
    public readonly struct KeeperVerdict
    {
        public readonly KeeperEnding Ending;

        /// <summary>
        /// Tiles that would have to be restored before a bought one is a usable one, or
        /// <see cref="RunContinueDeficit.None"/> when nothing would help.
        ///
        /// <para>
        /// <b>Nought whenever an offer is honest at all</b>, unlike a weave's. A grove that has
        /// run dry always has somewhere to plant — running out of room is checked first and is a
        /// different ending — so any tile at all is a playable tile and the authored allowance is
        /// exactly what the player needs. What the shortfall would otherwise have covered is
        /// handled by refusing outright instead: a grove with a bed that can be <em>proved</em>
        /// unopenable is one no purchase rescues, and selling working room into it would take
        /// somebody's gems for a board that ends again a few tiles later.
        /// </para>
        /// </summary>
        public readonly int Deficit;

        KeeperVerdict(KeeperEnding ending, int deficit)
        {
            Ending = ending;
            Deficit = deficit;
        }

        public bool IsOver => Ending != KeeperEnding.Live;
        public bool IsWon => Ending == KeeperEnding.Grown;

        /// <summary>
        /// Whether this reading should end the run now.
        ///
        /// <para>
        /// Three clauses, and all three belong here rather than in an <c>if</c> on the screen. A
        /// run decided twice charges two hearts for one loss; one decided before the first tile
        /// has been laid charges a heart for a board nobody touched; and one decided after the
        /// grove has already been finished puts a defeat panel over a victory.
        /// </para>
        /// </summary>
        public bool EndsTheRun(bool live, bool committed)
            => live && committed
            && (Ending == KeeperEnding.Overgrown || Ending == KeeperEnding.Starved);

        /// <summary>
        /// Reads a grove and its basket. Pure — every input is passed in — so every branch is
        /// proved offline against a board and two integers.
        /// </summary>
        public static KeeperVerdict Read(KeeperBoard board, KeeperBasket basket)
        {
            if (board == null || basket == null) return new KeeperVerdict(KeeperEnding.Live, 0);

            if (board.IsFinished) return new KeeperVerdict(KeeperEnding.Grown, 0);

            if (!board.AnyRoom)
                return new KeeperVerdict(KeeperEnding.Overgrown, RunContinueDeficit.None);

            if (!basket.Any)
                return new KeeperVerdict(KeeperEnding.Starved,
                                         board.AnyBedLost() ? RunContinueDeficit.None : 0);

            return new KeeperVerdict(KeeperEnding.Live, 0);
        }
    }
}
