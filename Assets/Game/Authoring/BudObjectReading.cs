using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// What the specials on a Budburst grove are worth: whether the board as dealt lets the
    /// player forge one, and whether the shortest plays fire one.
    ///
    /// <para>
    /// <b>This is invariant 26g's test, and it exists as a type of its own because it is asked
    /// twice.</b> <c>BudValidator</c> asks it to decide whether a grove may ship, and
    /// <c>BudLadderTests</c> asks it of every grove that already has — and a second copy of the
    /// arithmetic would be a second thing to keep in step with <c>Tools/verify/bud.py</c>, which
    /// is the third copy and the one that cannot call either (invariant 9a). The fixture pins
    /// these numbers for the shipped chapter, so this class <em>is</em> the C# side of that
    /// contract.
    /// </para>
    /// <para>
    /// <b>Three mechanics were withdrawn from this chapter for want of exactly this.</b> A
    /// runner, then a windmill, a firefly, a puffball and a hive all passed every other gate in
    /// this repository — solvable, correctly par'd, tight <c>ways</c>, every board green —
    /// while paying out as the same chain on every board they stood on. A special is different
    /// in kind: the player makes it. What has to be measured is whether the dealt board lets
    /// them, and whether doing so is ever the best play.
    /// </para>
    /// </summary>
    public readonly struct BudObjectReading
    {
        /// <summary>
        /// Opening moves that forge a special — a bunch of five or more on the first move.
        ///
        /// <b>The gate.</b> Nought means a player cannot make a special on the board as dealt,
        /// so the chapter's whole idea is a tap away at best and invisible at worst.
        /// </summary>
        public readonly int Forgeable;

        /// <summary>
        /// Of the shortest plays, how many forged a special and how many fired one — out of
        /// <see cref="Ways"/>. Whorlwater's <c>kindled</c>, for the special.
        /// </summary>
        public readonly int Forged, Fired;

        /// <summary>How many shortest plays there are, which the two above are out of.</summary>
        public readonly int Ways;

        BudObjectReading(int forgeable, int forged, int fired, int ways)
        {
            Forgeable = forgeable;
            Forged = forged;
            Fired = fired;
            Ways = ways;
        }

        public static readonly BudObjectReading Nothing = new BudObjectReading(0, 0, 0, 0);

        /// <summary>
        /// Reads the grove as dealt for the opening forges, and the survey for the shortest
        /// plays.
        ///
        /// The survey is taken rather than re-run because it is the expensive half and the
        /// validator already holds it; a caller with no survey passes <c>default</c> and gets
        /// the cheap reading alone.
        /// </summary>
        public static BudObjectReading Of(BudLayout layout, BudSurvey survey = default)
        {
            if (layout == null) return Nothing;

            var board = new BudBoard(layout);
            int hand = layout.Deal.At(0);
            var moves = new System.Collections.Generic.List<BudMove>(64);
            BudRun.Moves(board, hand, moves);

            int forgeable = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                var probe = new BudBoard(board);
                BudRun.Apply(probe, moves[i], hand, null, out var chain);
                if (chain.Forged > 0) forgeable++;
            }

            return new BudObjectReading(forgeable, survey.Forged, survey.Fired, survey.Ways);
        }
    }
}
