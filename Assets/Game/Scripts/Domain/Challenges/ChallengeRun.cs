using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    public enum ChallengeState { Playing, Won, Lost }

    /// <summary>
    /// What one input did to the whole run: the puzzle's answer and the hill's replay.
    /// </summary>
    public sealed class ChallengeTurnReport
    {
        public ChallengeMove Move;
        public readonly List<ChallengeEvent> Events = new List<ChallengeEvent>(24);
        public ChallengeState State;

        /// <summary>Whether the hill moved for this input at all.</summary>
        public bool Walked;
    }

    /// <summary>
    /// A challenge being played: one puzzle and one hill, joined by a single rule.
    ///
    /// <para>
    /// <b>The rule: a move that costs a turn feeds the line and walks the hill; the move that
    /// solves the puzzle wins before the hill walks; a line with no ward standing loses.</b>
    /// Nothing else. There is no clock, no move allowance and no score - how many turns a
    /// solution took is the only reading, and it is printed by the fixture rather than graded
    /// (nothing here reaches a save, a ledger, XP or credits, by the owner's instruction that
    /// a challenge never touches the core game).
    /// </para>
    /// <para>
    /// <b>Decided once.</b> <see cref="State"/> moves off <c>Playing</c> exactly once and every
    /// later input is refused, so a screen can never be told a run ended twice.
    /// </para>
    /// </summary>
    public sealed class ChallengeRun
    {
        public readonly ChallengeDefinition Definition;
        public readonly IChallengePuzzle Puzzle;
        public readonly ChallengeHill Hill;

        readonly ChallengeTurnReport _report = new ChallengeTurnReport();

        public ChallengeState State { get; private set; } = ChallengeState.Playing;

        /// <summary>Moves that cost a turn so far.</summary>
        public int Turns => Hill.Turn;

        /// <param name="deal">
        /// Which deal of the row this play is (<c>ChallengePlay.Deal</c>), for a genre that
        /// shuffles what the row authors - Pairs. Nought keeps the row exactly as written.
        /// </param>
        public ChallengeRun(ChallengeDefinition definition, ChallengeLine line, uint deal = 0u)
        {
            Definition = definition;
            Puzzle = ChallengePuzzles.Build(definition, deal);
            Hill = new ChallengeHill(line, definition.Hill, definition.Waves);
        }

        /// <summary>
        /// Apply one input. The report is owned by the run and valid until the next call.
        /// </summary>
        public ChallengeTurnReport Play(ChallengeInput input)
        {
            _report.Events.Clear();
            _report.Walked = false;
            _report.State = State;

            if (State != ChallengeState.Playing)
            {
                _report.Move = null;
                return _report;
            }

            var move = Puzzle.Apply(input);
            _report.Move = move;

            if (!move.Turn)
            {
                // A free adjustment can still finish a puzzle (a rotate that happens to line a
                // pipe up is a turn, but a tray pick never is); only a turn is judged.
                return _report;
            }

            // **The solving move wins before the hill walks.** Otherwise a perfect last move
            // could lose to a raider stepping onto the line in the same instant, which reads
            // as the game cheating.
            if (Puzzle.Solved)
            {
                State = ChallengeState.Won;
                _report.State = State;
                return _report;
            }

            if (Puzzle.Failed)
            {
                State = ChallengeState.Lost;
                _report.State = State;
                return _report;
            }

            for (int i = 0; i < move.Feeds.Count; i++)
            {
                var feed = move.Feeds[i];
                if (feed.Banks) Hill.Feed(feed.Colour, feed.Bolts);
                else Hill.Volley(feed.Colour, feed.Bolts);
            }

            Hill.Resolve(_report.Events);
            _report.Walked = true;

            // A stumble is the hill walking again with nothing fed (a Pairs cursed stone): the
            // same resolve, appended to the same report, so the view replays it as one turn
            // that went badly rather than as two.
            for (int s = 0; s < move.Stumbles && Hill.LineStanding; s++)
                Hill.Resolve(_report.Events);

            if (!Hill.LineStanding) State = ChallengeState.Lost;

            _report.State = State;
            return _report;
        }
    }
}
