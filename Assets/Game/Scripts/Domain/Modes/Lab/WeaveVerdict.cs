using System;

namespace GlimmerGrove.Modes
{
    /// <summary>How a weave stands: still being played, finished, or over.</summary>
    public enum WeaveState
    {
        Playing = 0,
        Solved = 1,
        Lost = 2,
    }

    /// <summary>
    /// Whether a weave is still winnable with the light left, read off a board and a meter.
    ///
    /// <para>
    /// <b>It is a pure reading of two things, which is why it is not on either of them.</b> The
    /// board knows what has been drawn and the ink knows what is left; neither is the place for
    /// a rule about the pair of them, and the screen certainly is not — a <c>switch</c> inside a
    /// <c>MonoBehaviour</c> is the one place in this project nothing can be proved, which is why
    /// <c>HintPrompt</c>, <c>AccountGate</c> and <c>GroveUnveil</c> all live in Domain. This is
    /// that answer for a run: every branch that ends one is here, and every one of them is
    /// pinned by a test that needs no Unity.
    /// </para>
    /// <para>
    /// <b>Both loss conditions are lower bounds, and they have to be.</b> Ending a run the
    /// player could still have won is the worst thing this mode could do to somebody, so
    /// neither reading is allowed to be optimistic about what a continuation costs. Between
    /// them they cover the two ways a grove dies, and the second exists because the first
    /// cannot see it — see <see cref="Read"/>.
    /// </para>
    /// </summary>
    public readonly struct WeaveVerdict
    {
        public readonly WeaveState State;

        /// <summary>The fewest further cells any finish could take.</summary>
        public readonly int Floor;

        /// <summary>Cells of light still in hand.</summary>
        public readonly int Left;

        /// <summary>
        /// The least light that has to be <em>in hand</em> for this grove to be playable at
        /// all: the floor, or the cheapest single channel that could still be drawn, whichever
        /// is larger. -1 when no arrangement could be drawn at any price.
        ///
        /// <para>
        /// It is the same pair of readings <see cref="Read"/> already takes to decide whether
        /// the run is lost, kept rather than thrown away — which is what lets a continue be
        /// priced honestly. See <see cref="Deficit"/>.
        /// </para>
        /// </summary>
        public readonly int Need;

        WeaveVerdict(WeaveState state, int floor, int left, int need)
        {
            State = state;
            Floor = floor;
            Left = left;
            Need = need;
        }

        /// <summary>
        /// How much light a continue has to restore before a grant is usable room, in
        /// <c>RunContinue</c>'s terms.
        ///
        /// <para>
        /// <b>This is why a weave's continue is not simply the authored figure.</b> A grove is
        /// not lost when the meter reads zero — it is lost when what is left cannot cover the
        /// cheapest possible finish, so there is usually light in the pot and none of it
        /// spendable. Handing over twenty cells alone would put the player back on a board
        /// that is still provably unwinnable and end the run again in the same frame, having
        /// taken their gems. So the shortfall is cleared first and the authored figure is
        /// working room on top of it.
        /// </para>
        /// <para>
        /// <see cref="RunContinue.NoContinue"/> when no amount of light would help: every pair
        /// walled in, which no arrangement can undo because a channel can only be redrawn from
        /// its own crystal over ground it can reach. Charging for that would be charging for
        /// nothing, and the honest answer is to let the run end.
        /// </para>
        /// <para>
        /// Nought on a run that is not lost, so a caller never has to ask twice.
        /// </para>
        /// </summary>
        public int Deficit
        {
            get
            {
                if (!IsLost) return 0;
                if (Need < 0) return RunContinue.NoContinue;

                int owed = Need - Left;
                return owed < 0 ? 0 : owed;
            }
        }

        public bool IsLost => State == WeaveState.Lost;
        public bool IsSolved => State == WeaveState.Solved;
        public bool IsPlaying => State == WeaveState.Playing;

        /// <summary>
        /// Reads a board against its ink.
        ///
        /// <para>
        /// <b>The floor.</b> For every pair not yet settled, that pair's own floor on an
        /// <em>empty</em> board — which already prices the beads it owes, by Held-Karp. No
        /// arrangement can finish for less whatever is standing in the way, and none is ruled
        /// out, because the player may always take another pair's channel up and redraw it. A
        /// bound that assumed the ground stays as it is would be wrong in exactly the direction
        /// that costs somebody a run.
        /// </para>
        /// <para>
        /// <b>What the floor cannot see.</b> A grove can be left with enough light on paper and
        /// no way to spend it: a critter walled in by two channels, where freeing it means
        /// redrawing one of them and the redraw costs more than is left. Nothing would ever land
        /// again, the floor would keep saying the grove is affordable, and the player would sit
        /// in front of a board that could not be finished and would not end — which is precisely
        /// the "reads as a broken game" state invariant 20g is about. So the second question is
        /// asked outright: is there any pair at all whose cheapest completion is affordable.
        /// </para>
        /// <para>
        /// Every pair is asked, settled ones included, because taking a finished channel up and
        /// putting it somewhere else is a legitimate move and often the only one that opens a
        /// board. What each is asked for is the larger of its two lower bounds — the walk it
        /// could take now, and its floor on an empty board — which is still a lower bound, so a
        /// pair that looks affordable can turn out dearer once its beads are threaded. That is
        /// the safe direction: the run carries on and the floor ends it later.
        /// </para>
        /// <para>
        /// A grove with no ink budget is never lost, which is the shape <c>LevelTuning</c>
        /// already uses for the first glade in the game.
        /// </para>
        /// </summary>
        public static WeaveVerdict Read(WeaveBoard board, WeaveInk ink)
        {
            if (board == null || ink == null) return new WeaveVerdict(WeaveState.Playing, 0, 0, 0);

            int left = ink.Left;
            if (board.IsSolved) return new WeaveVerdict(WeaveState.Solved, 0, left, 0);

            int floor = board.Floor;
            if (!ink.Bounded) return new WeaveVerdict(WeaveState.Playing, floor, left, floor);

            // The two lower bounds, taken once and kept. They used to be a boolean apiece and
            // thrown away, which was enough to *end* a run and not enough to *price* one — see
            // Deficit for what a continue has to know that "is it lost" does not.
            int cheapest = Cheapest(board);
            int need = cheapest < 0 ? -1 : Math.Max(floor, cheapest);

            bool lost = need < 0 || need > left;
            return new WeaveVerdict(lost ? WeaveState.Lost : WeaveState.Playing, floor, left, need);
        }

        /// <summary>
        /// The cheapest single channel that could still be laid, whatever the light in hand —
        /// or -1 when there is no pair with any way through at all.
        ///
        /// <para>
        /// Every pair is asked, settled ones included, because taking a finished channel up and
        /// putting it somewhere else is a legitimate move and often the only one that opens a
        /// board. What each is asked for is the larger of its two lower bounds — the walk it
        /// could take now, and its floor on an empty board — which is still a lower bound, so a
        /// pair that looks affordable can turn out dearer once its beads are threaded. That is
        /// the safe direction: the run carries on and the floor ends it later.
        /// </para>
        /// </summary>
        static int Cheapest(WeaveBoard board)
        {
            int best = -1;

            for (int p = 0; p < board.Pairs; p++)
            {
                int walk = board.Reach(p);
                if (walk < 0) continue;

                int cost = Math.Max(walk, board.Grove.Straight(p));
                if (best < 0 || cost < best) best = cost;
            }

            return best;
        }

        /// <summary>
        /// Whether this reading should end the run now.
        ///
        /// <para>
        /// The guard that used to be three booleans in an <c>if</c> on the screen, which is
        /// exactly the shape this project keeps paying for: <c>RunGuard</c>, the pause latch and
        /// the closing cascade are all edges where a run is decided and the screen has not caught
        /// up yet, and a condition spread across them cannot be tested. Both clauses matter.
        /// </para>
        /// <para>
        /// <paramref name="live"/> is false while a panel, a cascade or an ending already owns
        /// the board — a run that has already been decided must never be decided twice, which
        /// would charge two hearts for one loss.
        /// </para>
        /// <para>
        /// <paramref name="committed"/> is false until the first channel lands. A grove that
        /// somehow arrived unwinnable before the player had drawn anything is a content fault —
        /// <c>WeaveMode.Validate</c> fails the build on one — and must not take a heart for it.
        /// </para>
        /// </summary>
        public bool EndsTheRun(bool live, bool committed) => IsLost && live && committed;
    }
}
