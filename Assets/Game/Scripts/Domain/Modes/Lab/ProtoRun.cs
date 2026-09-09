namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How close a run is to running out of moves, as a reading the screen can colour by.
    ///
    /// Fractions of this level's own allowance rather than fixed counts, for
    /// <c>FallPressure</c>'s reason: three moves left is comfortable on a board dealt thirty and
    /// desperate on one dealt eight, so a threshold written as a count would mean a different
    /// thing on every level.
    /// </summary>
    public enum ProtoPressure
    {
        /// <summary>Moves to spare.</summary>
        Easy = 0,

        /// <summary>Under a third of the allowance: worth counting.</summary>
        Low = 1,

        /// <summary>Under a sixth, which on any board that ships is a move or two.</summary>
        Critical = 2,
    }

    /// <summary>
    /// A run's allowance: how many moves it may spend and how many are gone.
    ///
    /// <para>
    /// <b>The budget, in the unit the mode is graded in</b> (invariant 22b). Every mode built on
    /// this shape counts one thing — an input — so they share one meter, and none of them gets to
    /// invent its own arithmetic for what running out means. The same par plus slack every other
    /// mode is dealt, counted in moves.
    /// </para>
    /// <para>
    /// Pure integers and no policy at all, for <c>BudSatchel</c>'s reason: where the budget
    /// comes from is the mode's <c>Tune</c>, when a run is lost is <see cref="ProtoVerdict"/>'s,
    /// and what the player sees is the screen's.
    /// </para>
    /// </summary>
    public sealed class ProtoBudget
    {
        /// <summary>
        /// A run with no allowance, which therefore cannot run out. <see cref="int.MaxValue"/>
        /// rather than a flag, so <see cref="Left"/> compares without special-casing at every call
        /// site.
        /// </summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>
        /// Moves this run may spend. The pot rather than a constant, because it can be topped up:
        /// a continue that has been paid for raises this and nothing else. Note what it
        /// deliberately does not touch — <see cref="Spent"/>, which is the grade, so a board
        /// finished with bought moves scores exactly what it spent (invariant 23).
        /// </summary>
        public int Dealt { get; private set; }

        /// <summary>Moves spent. This is the grade and the record.</summary>
        public int Spent { get; private set; }

        public ProtoBudget(int dealt) => Dealt = dealt < 0 ? Unlimited : dealt;

        public bool Bounded => Dealt != Unlimited;

        /// <summary>Moves still to come. <see cref="Unlimited"/> on a run with no allowance.</summary>
        public int Left
        {
            get
            {
                if (!Bounded) return Unlimited;
                int left = Dealt - Spent;
                return left < 0 ? 0 : left;
            }
        }

        public bool Any => Left > 0;

        /// <summary>
        /// Takes from the allowance for something that landed.
        ///
        /// <para>
        /// One by default, because one input is one move in every mode built on this shape. It
        /// takes a count for the one thing that is not an input: a utility, which is charged in
        /// this same unit at the rate its mode converts effect into moves, so that using one can
        /// never score better than doing the same work by playing (invariant 39). Nought is legal
        /// and is what a utility that delivers nothing worth charging for costs.
        /// </para>
        /// <para>
        /// <b>It stays the only door into <see cref="Spent"/>.</b> A second way to move the grade
        /// is a second place for "a run is charged once" to stop being true.
        /// </para>
        /// </summary>
        public void Take(int moves = 1)
        {
            if (moves <= 0) return;
            Spent += moves;
        }

        /// <summary>
        /// Deals more, because a continue was paid for. Guarded against overflow rather than
        /// trusted: what is handed over is content and a content push is what changes it.
        /// </summary>
        public void Grant(int moves)
        {
            if (moves <= 0 || !Bounded) return;
            Dealt = moves > Unlimited - Dealt ? Unlimited : Dealt + moves;
        }

        /// <summary>How close this run is to running out. Unbounded allowances are always easy.</summary>
        public ProtoPressure Pressure
        {
            get
            {
                if (!Bounded || Dealt <= 0) return ProtoPressure.Easy;

                // Integer arithmetic throughout, for LevelTuning's reason: a threshold decided by
                // a float is a threshold three code generators round three ways.
                int left = Left;
                if (left * 6 <= Dealt) return ProtoPressure.Critical;
                if (left * 3 <= Dealt) return ProtoPressure.Low;
                return ProtoPressure.Easy;
            }
        }
    }

    /// <summary>How a run stands, once and in one word.</summary>
    public enum ProtoEnding
    {
        /// <summary>Still being played.</summary>
        Live = 0,

        /// <summary>The goal is met. Won.</summary>
        Done = 1,

        /// <summary>
        /// The board has no legal move left, or has reached a state it can be proved never to
        /// finish from. The spatial ending.
        ///
        /// <para>
        /// <b>Whether money can fix it is the board's answer, not this member's.</b> It was the
        /// latter for as long as every mode on this shape ran out of <em>board</em> — no purchase
        /// gives a cairn another stone to pull — and Thornwatch is the first that does not: a
        /// siege has no legal move because its ward line has fallen, and a continue puts the line
        /// back up. So the deficit is asked of <see cref="IProtoBoard.Stranded"/> on this branch
        /// exactly as it is on <see cref="Spent"/>, and a board for which nothing helps says so
        /// itself.
        /// </para>
        /// </summary>
        Stuck = 2,

        /// <summary>The allowance ran out with the goal unmet.</summary>
        Spent = 3,
    }

    /// <summary>
    /// What a board of one of these modes has to be able to say about itself for the shared run
    /// to be able to end it.
    ///
    /// <para>
    /// Five members, and each answers a question the screen must never work out for itself. The
    /// two that matter most are the last: <see cref="AnyMove"/> and <see cref="Stranded"/> split
    /// "this run is over on the board" from "this run is over in a way no purchase rescues", and
    /// both are <b>certainties</b> rather than heuristics. They decide whether money changes
    /// hands, so they under-report and never over-report — the retired <c>KeeperBoard.AnyBedLost</c>'s
    /// rule, kept.
    /// </para>
    /// </summary>
    public interface IProtoBoard
    {
        /// <summary>Whether the goal is met.</summary>
        bool IsFinished { get; }

        /// <summary>How many things this board asked for altogether, for the readout.</summary>
        int Goals { get; }

        /// <summary>How many of them are still waiting.</summary>
        int GoalsLeft { get; }

        /// <summary>Whether there is any legal move at all from where the board now stands.</summary>
        bool AnyMove { get; }

        /// <summary>
        /// Whether this board can be <em>proved</em> never to finish, however many moves were
        /// bought.
        ///
        /// <para>
        /// <b>It never ends a run, and only decides whether it would be honest to sell one.</b>
        /// That is invariant 28f, learned by Lightfall shipping the other reading and taking it
        /// back: a run that ends while the player still has moves in hand reads as the game
        /// deciding on their behalf, and a player who wants to spend their last three pulls on a
        /// cairn that cannot be finished is entitled to. So this is asked at the moment a run is
        /// already over — the allowance gone, or no legal move left — and at no other.
        /// </para>
        /// <para>
        /// <b>It is a question about purchases and not about the board's shape</b>, which is why
        /// a mode may answer <c>false</c> with no move on the board at all. Thornwatch does: a
        /// fallen ward line has no legal move and a continue puts the line back up, so it is over
        /// and it is not stranded.
        /// </para>
        /// <para>
        /// Never a guess. It decides whether money changes hands, so it under-reports and never
        /// over-reports — a false answer costs a run that ends a few moves later, and a wrong true
        /// answer refuses a rescue to somebody who could still have won.
        /// </para>
        /// </summary>
        bool Stranded { get; }
    }

    /// <summary>
    /// The reading of a board against its allowance: whether the run is over, how, and what it
    /// would take to carry it on.
    ///
    /// <para>
    /// <b>One predicate rather than three booleans in an <c>if</c> on a screen</b>, which is
    /// <see cref="FallVerdict"/>'s argument and this project's most repeated one: every such
    /// boolean is an edge where the run is decided and the screen has not caught up.
    /// </para>
    /// <para>
    /// <b>Two fail states, and whether either may be sold a continue is the board's answer.</b>
    /// Running out of moves is a shortage and more moves fix it. Running out of <em>board</em>
    /// usually is not — no purchase gives a cairn another stone to pull or a grove another ribbon
    /// to draw — so <see cref="Deficit"/> answers <see cref="RunContinueDeficit.None"/> and the
    /// offer is never made. <b>Usually, not always</b>, and that is the one thing here that
    /// changed after five modes: Thornwatch has no legal move when its ward line has fallen, and
    /// a continue puts the line back up. So both branches ask
    /// <see cref="IProtoBoard.Stranded"/> — which was written as a certainty about
    /// <em>purchases</em> from the start — instead of one of them assuming the answer.
    /// </para>
    /// </summary>
    public readonly struct ProtoVerdict
    {
        public readonly ProtoEnding Ending;

        /// <summary>
        /// Moves that would have to be restored before a bought one is a usable one, or
        /// <see cref="RunContinueDeficit.None"/> when nothing would help.
        ///
        /// Nought whenever an offer is honest at all, and never a positive number on this shape:
        /// a board that has run dry always has a legal move, so any move at all is a playable
        /// move, and a board that has run out of moves to <em>make</em> is rescued by putting
        /// back whatever went missing rather than by topping up an allowance. The shortfall case
        /// the field exists for is Lightfall's, where the motes that come next may be the wrong
        /// colours entirely.
        /// </summary>
        public readonly int Deficit;

        ProtoVerdict(ProtoEnding ending, int deficit)
        {
            Ending = ending;
            Deficit = deficit;
        }

        public bool IsOver => Ending != ProtoEnding.Live;
        public bool IsWon => Ending == ProtoEnding.Done;

        /// <summary>
        /// Whether this reading should end the run now.
        ///
        /// Three clauses, all here rather than in an <c>if</c> on a screen. A run decided twice
        /// charges two hearts for one loss; one decided before the first move charges a heart for
        /// a board nobody touched; one decided after the board is finished puts a defeat panel
        /// over a victory.
        /// </summary>
        public bool EndsTheRun(bool live, bool committed)
            => live && committed
            && (Ending == ProtoEnding.Stuck || Ending == ProtoEnding.Spent);

        /// <summary>
        /// Reads a board and its allowance. Pure — every input is passed in — so every branch is
        /// proved offline against a board and two integers.
        ///
        /// The order is the order a player would want: a finished board wins even if the move
        /// that finished it was the last one and left nothing to do, and a board that has just run
        /// out of moves to make is not also reported as having run out of allowance.
        /// </summary>
        public static ProtoVerdict Read(IProtoBoard board, ProtoBudget budget)
        {
            if (board == null || budget == null) return new ProtoVerdict(ProtoEnding.Live, 0);

            if (board.IsFinished) return new ProtoVerdict(ProtoEnding.Done, 0);

            // The deficit is the *board's* answer on this branch as much as on the next one. A
            // cairn with no stone to pull is stranded and says so; a fallen ward line is not, and
            // a continue that raises it is an honest sale (invariant 28f, and 23's ordering).
            if (!board.AnyMove)
                return new ProtoVerdict(ProtoEnding.Stuck,
                                        board.Stranded ? RunContinueDeficit.None : 0);

            if (!budget.Any)
                return new ProtoVerdict(ProtoEnding.Spent,
                                        board.Stranded ? RunContinueDeficit.None : 0);

            return new ProtoVerdict(ProtoEnding.Live, 0);
        }
    }

    /// <summary>
    /// One run of a prototype board: the board, the allowance it is dealt, and the dozen lines
    /// that let the two move together.
    ///
    /// <para>
    /// <b>Shared rather than copied per mode.</b> Lightfall and Budburst each wrote their own
    /// because each counts a different thing in a different way; a prototype board counts one
    /// input, so a copy per mode of "a move is taken exactly once per landed move" would be a
    /// place per mode for it to stop being true. It carried five modes at once and then survived
    /// four of them being withdrawn, which is that argument settled by events.
    /// </summary>
    public sealed class ProtoRun
    {
        public readonly IProtoBoard Board;
        public readonly ProtoBudget Budget;

        /// <summary>What this board opened with, for the readout and the abandonment note.</summary>
        public readonly int Goals;

        public ProtoRun(IProtoBoard board, int budget)
        {
            Board = board;
            Budget = new ProtoBudget(budget);
            Goals = board != null ? board.Goals : 0;
        }

        /// <summary>Moves spent, which is the grade and the record.</summary>
        public int Spent => Budget.Spent;

        /// <summary>Goals still waiting.</summary>
        public int Left => Board != null ? Board.GoalsLeft : 0;

        /// <summary>The most this run has taken with a single move, for the flourish readout.</summary>
        public int Best { get; private set; }

        /// <summary>How the run stands. Recomputed rather than cached: it is one walk of a small board.</summary>
        public ProtoVerdict Verdict => ProtoVerdict.Read(Board, Budget);

        /// <summary>
        /// Takes one from the allowance for a move that landed, and notes what it was worth.
        ///
        /// <b>Called once per landed move and nowhere else.</b> A run charged for an input that
        /// could not be honoured is a run that quietly costs a player a move for touching stone.
        /// </summary>
        public void Took(int worth)
        {
            Budget.Take();
            if (worth > Best) Best = worth;
        }

        /// <summary>
        /// Charges the run for a utility that landed, in the unit the mode is graded in.
        ///
        /// <para>
        /// <b>Separate from <see cref="Took"/> because it is not a move.</b> It does not touch
        /// <see cref="Best"/>, which reads "the most one move was worth" and would be a lie if a
        /// firepot could win it; and the count it takes is the mode's own conversion rather than
        /// one, because what makes a utility safe is that it costs at least what the work it
        /// replaced would have cost. See <c>SiegeUtility</c>.
        /// </para>
        /// <para>
        /// A charge of nought is legal and still passes through here, so a mending that costs the
        /// grade nothing still counts as the player having acted — which is what
        /// <c>ProtoView.Took</c>'s commit flag is for and what stops a run being abandoned as
        /// untouched after one was spent on it.
        /// </para>
        /// </summary>
        public void Charged(int moves) => Budget.Take(moves);

        /// <summary>
        /// Deals more moves, because a continue was paid for. Nothing else moves: the board stands
        /// exactly as it stood, and <see cref="Spent"/> — the grade — is untouched.
        /// </summary>
        public void Grant(int moves) => Budget.Grant(moves);
    }
}
