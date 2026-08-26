namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A weave's ink: how many cells of light this grove may be drawn with, and how many of
    /// them are gone.
    ///
    /// <para>
    /// <b>It is the mode's fail state, in the unit the mode is already graded in.</b> A weave
    /// has no turns, so when the clock went (invariant 22) it was left with no way to be lost
    /// at all — only forfeited — and <c>LevelTuning</c>'s answer for every other mode, a
    /// budget over par, had nothing to count. This is that count: a channel costs one cell of
    /// ink for every cell it covers, so the whole meter is <c>WeaveBoard.Occupied</c> seen from
    /// the other side, and a grove drawn tautly finishes with ink to spare exactly as a glade
    /// solved tidily finishes with turns to spare. That is the rule invariant 22a asks for —
    /// graded on the count it keeps, never on how fast it was.
    /// </para>
    /// <para>
    /// <b>Ink is spent, and spending is permanent.</b> A channel taken back frees the
    /// <em>ground</em> so a route can be redrawn; it does not give the light back. That is the
    /// whole of what makes a weave loseable, and it is deliberately the one thing here a
    /// player cannot get round: sprawl, redraw, sprawl again, and the grove runs dry. What
    /// keeps it fair rather than merely tight is what a glade's budget does with the same
    /// problem — a wrong move must be cheap to <em>discover</em> and only expensive to
    /// <em>keep</em>. A drag is free until it lands (nothing is charged for a finger that
    /// wanders and comes back), and <c>WeaveStrokes</c> hands back a bounded number of landed
    /// channels through undo, which refunds in full. Everything past that is paid for.
    /// </para>
    /// <para>
    /// <b>Pure integers, and no policy at all.</b> Where the budget comes from is
    /// <c>WeaveMode.Tune</c>'s business (par times the same factor every mode uses), when a run
    /// is lost is <c>WeaveVerdict</c>'s, what is written down is <c>WeaveStrokes</c>' and what
    /// the player sees is the screen's. This is only the arithmetic, for <c>GroveStock</c>'s
    /// reason: every rule over it can then be proved offline against plain numbers instead of
    /// against a board and a screen.
    /// </para>
    /// </summary>
    /// <summary>
    /// How close a weave is to running dry, as a reading a screen can colour by.
    ///
    /// <para>
    /// Fractions of the grove's own budget rather than fixed counts, because a channel on a 5x6
    /// opening grove is five cells and one on the 7x9 finale is nine — a threshold of "twelve
    /// left" would mean two channels of room on one board and barely one on the other. A tenth
    /// is under a channel however big the grove is, which is the reading that matters: there is
    /// not enough light left to join anybody.
    /// </para>
    /// <para>
    /// Here rather than beside the paint, so the thresholds are pinned by a test instead of
    /// living in a <c>MonoBehaviour</c> where nothing can prove them.
    /// </para>
    /// </summary>
    public enum InkPressure
    {
        /// <summary>Room to work in.</summary>
        Easy = 0,

        /// <summary>Under a quarter of the pot: worth watching.</summary>
        Low = 1,

        /// <summary>Under a tenth, which is less than one channel on any grove that ships.</summary>
        Critical = 2,
    }

    public sealed class WeaveInk
    {
        /// <summary>
        /// A grove with no ink budget, which therefore cannot be lost.
        ///
        /// <see cref="int.MaxValue"/> rather than a negative or a separate flag, so
        /// <see cref="Left"/> compares without special-casing at every call site — the same
        /// bargain <c>LevelTuning.MoveBudget</c> already makes for a glade with no budget.
        /// </summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>
        /// Cells of light this grove may be drawn with.
        ///
        /// <para>
        /// It is the pot rather than a constant, because it can be topped up: a continue that
        /// has been paid for raises this and nothing else (see <see cref="Grant"/>). Note what
        /// that deliberately does <em>not</em> touch — <see cref="Spent"/>, which is the grade
        /// (invariant 22a). A grove finished with bought light spent every cell it spent, so
        /// it scores exactly what it earned, and since a run only reaches the offer after
        /// spending past the two-star line it can score one star at most.
        /// </para>
        /// </summary>
        public int Budget { get; private set; }

        /// <summary>Cells committed so far, over the whole run and never reduced by an erase.</summary>
        public int Spent { get; private set; }

        /// <summary>The pot this grove was dealt, so a restart can put it back. See <see cref="Reset"/>.</summary>
        readonly int _dealt;

        public WeaveInk(int budget)
        {
            _dealt = budget < 1 ? Unlimited : budget;
            Budget = _dealt;
        }

        /// <summary>Whether this grove has an ink budget at all.</summary>
        public bool Bounded => Budget != Unlimited;

        /// <summary>
        /// Cells still to spend, floored at zero.
        ///
        /// Floored rather than allowed to go negative because nothing downstream has a use for
        /// how far past the end a run went, and a negative reaching a readout is a number no
        /// player can act on. In practice it never arrives: the drag is walled at the ink in
        /// hand, so a channel that cannot be afforded is one that cannot be drawn.
        /// </summary>
        public int Left => !Bounded ? Unlimited : Spent >= Budget ? 0 : Budget - Spent;

        /// <summary>Whether a channel of this many cells could be laid with what is left.</summary>
        public bool Affords(int cells) => !Bounded || cells <= Left;

        /// <summary>Charges a channel that has landed.</summary>
        public void Spend(int cells)
        {
            if (cells <= 0) return;

            // Saturating rather than wrapping. An unbounded meter still counts, because the
            // grade is read off it, and a run long enough to overflow would otherwise score
            // three stars for having gone on for ever.
            Spent = cells > int.MaxValue - Spent ? int.MaxValue : Spent + cells;
        }

        /// <summary>
        /// Deals more light into the pot, for a continue that has been paid for.
        ///
        /// <para>
        /// The pot rather than a refund, and that distinction is the whole of why this is safe.
        /// <see cref="Refund"/> lowers what was spent and is only ever reached by undo, which
        /// is bounded; this raises what may be spent and leaves the record of what already was
        /// exactly where it stood. So a bought continue cannot walk a grade backwards, and the
        /// meter, the stars and the record stay one number (invariant 22b).
        /// </para>
        /// <para>
        /// Refused on an unbounded grove rather than clamped, for <c>Puzzle.Grant</c>'s reason:
        /// nothing there can run dry, so a continue could never have been offered, and quietly
        /// accepting one would leave a player's gem balance as the only witness to the bug.
        /// </para>
        /// </summary>
        public void Grant(int cells)
        {
            if (cells <= 0 || !Bounded) return;

            // Saturating, and one below Unlimited at the top so a topped-up grove can never
            // become an *unbounded* one — which would silently retire the fail state the whole
            // mode rests on.
            long raised = (long)Budget + cells;
            Budget = raised >= Unlimited ? Unlimited - 1 : (int)raised;
        }

        /// <summary>Hands a channel's light back. Only undo does this.</summary>
        public void Refund(int cells)
        {
            if (cells <= 0) return;
            Spent = cells >= Spent ? 0 : Spent - cells;
        }

        /// <summary>How close this grove is to running dry. See <see cref="InkPressure"/>.</summary>
        public InkPressure Pressure
        {
            get
            {
                if (!Bounded) return InkPressure.Easy;

                int left = Left;

                // Multiplied rather than divided, so a small budget cannot round its own
                // warning away — on a pot of nine, left/9f < .1f and left * 10 <= 9 disagree
                // about the last cell, and the integer is the one that means what it says.
                if (left * 10 <= Budget) return InkPressure.Critical;
                return left * 4 <= Budget ? InkPressure.Low : InkPressure.Easy;
            }
        }

        /// <summary>
        /// Back to the grove as it was dealt, for a restart — an empty spend <em>and</em> the
        /// pot the level authored.
        ///
        /// <para>
        /// Bought light goes with it, and that is deliberate rather than mean: a restart
        /// abandons the run and begins another (<c>RunScreen.RestartLevel</c>), so it costs a
        /// heart and is asked about first. A continue buys <em>this</em> run, not this grove.
        /// The alternative — carrying a purchase across restarts — would make a bought pot
        /// cheaper to keep than to use, which is the one shape of offer a player is right to
        /// resent.
        /// </para>
        /// </summary>
        public void Reset()
        {
            Spent = 0;
            Budget = _dealt;
        }

        public override string ToString()
            => Bounded ? Spent + "/" + Budget + " cells" : Spent + " cells";
    }
}
