using GlimmerGrove.Content;

namespace GlimmerGrove
{
    /// <summary>
    /// Everything that is true about a run the moment it ends, won or lost.
    ///
    /// <para>
    /// <b>Why this type exists.</b> Almost everything the game does in reaction to a
    /// player is a reaction to a run finishing: the star ledger, the reward, the daily
    /// chest counter, the celebration, the defeat copy, analytics — and, as the game
    /// grows, a streak, a variable bonus, an event track. Before this type each of those
    /// read what it needed straight off the board and the record, which works exactly
    /// until two of them disagree about what "the run" was. They already had begun to: a
    /// defeat counted a chest but produced no other record at all, so nothing downstream
    /// could tell a lost run from a run that never happened, and the move count quoted to
    /// the player came from the live board rather than from a decided result.
    /// </para>
    /// <para>
    /// So the screen decides once, here, and hands the same value to everyone. That has
    /// three consequences worth having. Consumers are <em>additive</em> — a new reaction
    /// is a new reader of an existing value, not another hand in the board's state, which
    /// is what keeps the cost of the next feature flat. Nothing downstream can reach back
    /// into a live <see cref="Puzzle"/> that has since been restarted, which is a real
    /// hazard here because the defeat panel offers a retry and the board it came from is
    /// still on screen. And the whole reaction set becomes testable without a board, a
    /// canvas or a frame.
    /// </para>
    /// <para>
    /// It is deliberately a value with no behaviour beyond deriving from its own fields.
    /// It does not know what a reward is worth, what a streak is, or what the screen
    /// should say. Those are policies, they change, and they belong to whoever owns them.
    /// </para>
    /// </summary>
    public readonly struct RunOutcome
    {
        /// <summary>Which glade was played. Permanent id — see <see cref="LevelId"/>.</summary>
        public readonly LevelId Level;

        /// <summary>True when every lamp was lit.</summary>
        public readonly bool Won;

        /// <summary>Stars this run scored. Always 0 on a loss — a defeat is not a worse clear.</summary>
        public readonly int Stars;

        /// <summary>Turns actually taken.</summary>
        public readonly int Moves;

        /// <summary>
        /// The move count worth aiming at: this glade's <b>three-star</b> threshold, which
        /// is <see cref="Puzzle.Gold"/> and not <see cref="Puzzle.Par"/>.
        ///
        /// Named for what it is to the player rather than for the tuning field it comes
        /// from, because the two have never been the same number and the win panel has
        /// always shown this one. A field called <c>Par</c> holding the gold threshold is
        /// the sort of thing that reads correctly for a year and then gets "fixed".
        /// </summary>
        public readonly int Target;

        /// <summary>
        /// The player's best move count before this run, or 0 when they had never cleared
        /// it. Zero means "no record", not "a perfect run" — the same convention the save
        /// file uses, so the two cannot disagree.
        /// </summary>
        public readonly int PreviousBest;

        /// <summary>True when this run cleared the glade for the first time ever.</summary>
        public readonly bool FirstClear;

        /// <summary>
        /// Which attempt this was, counting from 1. Includes the run being described, so
        /// a first-ever run reads 1 rather than 0.
        /// </summary>
        public readonly int Attempt;

        /// <summary>Why the run ended. Meaningless when <see cref="Won"/>.</summary>
        public readonly DefeatReason Reason;

        /// <summary>
        /// How many turns still separated the board from a finished glade, or -1 when that
        /// could not be known. Zero on a win.
        ///
        /// An upper bound — see <see cref="Puzzle.TurnsToSolution"/> — so a small number
        /// here is a promise that can be kept and never a flattering guess.
        /// </summary>
        public readonly int TurnsToSolution;

        public readonly int LampsLit;
        public readonly int LampCount;

        /// <summary>Hints spent on this run.</summary>
        public readonly int HintsUsed;

        /// <summary>Wall-clock seconds from the board appearing to the run resolving.</summary>
        public readonly float Seconds;

        RunOutcome(LevelId level, bool won, int stars, int moves, int target, int previousBest,
                   bool firstClear, int attempt, DefeatReason reason, int turnsToSolution,
                   int lampsLit, int lampCount, int hintsUsed, float seconds)
        {
            Level = level;
            Won = won;
            Stars = stars < 0 ? 0 : stars;
            Moves = moves < 0 ? 0 : moves;
            Target = target < 0 ? 0 : target;
            PreviousBest = previousBest < 0 ? 0 : previousBest;
            FirstClear = firstClear;
            Attempt = attempt < 1 ? 1 : attempt;
            Reason = reason;
            TurnsToSolution = turnsToSolution;
            LampsLit = lampsLit < 0 ? 0 : lampsLit;
            LampCount = lampCount < 0 ? 0 : lampCount;
            HintsUsed = hintsUsed < 0 ? 0 : hintsUsed;
            Seconds = seconds < 0f ? 0f : seconds;
        }

        /// <summary>A glade finished. <paramref name="attempt"/> counts this run.</summary>
        public static RunOutcome Win(Puzzle board, int stars, int previousBest, bool firstClear,
                                     int attempt, int hintsUsed, float seconds)
            => new RunOutcome(board.Id, true, stars, board.Moves, board.Gold, previousBest,
                              firstClear, attempt, DefeatReason.OutOfMoves, 0,
                              board.LampsLit, board.LampCount, hintsUsed, seconds);

        /// <summary>
        /// A run lost. No stars, no record and no reward — <c>PlayerProgress</c> never
        /// hears about this, and the fields that describe a clear are left at their
        /// "never" values rather than at anything a reader could mistake for a result.
        /// </summary>
        public static RunOutcome Loss(Puzzle board, DefeatReason reason, int previousBest,
                                      int attempt, int hintsUsed, float seconds)
            => new RunOutcome(board.Id, false, 0, board.Moves, board.Gold, previousBest,
                              false, attempt, reason, board.TurnsToSolution,
                              board.LampsLit, board.LampCount, hintsUsed, seconds);

        // ------------------------------------------------------------- derived
        /// <summary>
        /// How many turns from finishing the player was, when that is a number worth
        /// quoting to them, otherwise -1.
        ///
        /// <para>
        /// Only ever offered after the turns ran out. That is not squeamishness about the
        /// other ending — it is that the number is not sound there. A crumbled conduit
        /// takes its own owed turns out of the board with it, so the count over what
        /// survives reads <em>lower</em> than the truth, and a defeat screen that tells a
        /// player they were one turn away when they were four is the exact failure the
        /// upper bound was chosen to avoid. <see cref="Puzzle.TurnsToSolution"/> already
        /// refuses to answer in that case; this refuses to ask.
        /// </para>
        /// <para>
        /// It is also the only ending where the player has no other evidence. They watched
        /// a conduit give way and know why they lost; running out of turns looks identical
        /// whether they were one turn short or twenty.
        /// </para>
        /// </summary>
        public int TurnsShort
            => Won || Reason != DefeatReason.OutOfMoves ? -1 : TurnsToSolution;

        /// <summary>
        /// True when the player was close enough that saying so is worth a sentence.
        ///
        /// <para>
        /// Two turns, and the threshold is doing real work at that size. The effect this
        /// draws on is a genuine one — a loss that registers as nearly a win drives a
        /// retry far harder than a plain loss — but it survives only while the claim stays
        /// rare and true. Widen it to five and every defeat says "so close", which is how
        /// a player learns to stop reading the line; keep it at two, where
        /// <see cref="TurnsToSolution"/> is a bound they could verify by restarting and
        /// counting, and it keeps meaning something.
        /// </para>
        /// </summary>
        public bool NearMiss
        {
            get
            {
                int short_ = TurnsShort;
                return short_ >= 1 && short_ <= NearMissTurns;
            }
        }

        /// <summary>The widest gap still described as a near miss. See <see cref="NearMiss"/>.</summary>
        public const int NearMissTurns = 2;

        /// <summary>True when this run beat the player's own record, or set the first one.</summary>
        public bool NewBest => Won && (PreviousBest <= 0 || Moves < PreviousBest);

        /// <summary>Cleared at or under the three-star threshold, unaided.</summary>
        public bool Flawless => Won && Target > 0 && Moves <= Target && HintsUsed == 0;

        public override string ToString()
            => Won
                ? $"won {Level} in {Moves} for {Stars}*"
                : $"lost {Level} on {Reason} at {Moves} moves, {TurnsToSolution} from solved";
    }
}
