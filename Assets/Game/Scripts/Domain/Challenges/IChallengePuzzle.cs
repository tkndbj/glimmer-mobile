using System.Collections.Generic;

namespace GlimmerGrove.Challenges
{
    public enum ChallengeInputKind
    {
        /// <summary>A finger on a cell. <c>Cell</c> says which.</summary>
        Tap,

        /// <summary>A swipe across the board. <c>Dx</c>/<c>Dy</c> is the direction, y up.</summary>
        Swipe,
    }

    /// <summary>What a finger did, in the puzzle's own vocabulary and no widget's.</summary>
    public readonly struct ChallengeInput
    {
        public readonly ChallengeInputKind Kind;
        public readonly int Cell, Dx, Dy;

        ChallengeInput(ChallengeInputKind kind, int cell, int dx, int dy)
        {
            Kind = kind;
            Cell = cell;
            Dx = dx;
            Dy = dy;
        }

        public static ChallengeInput Tap(int cell) => new ChallengeInput(ChallengeInputKind.Tap, cell, 0, 0);
        public static ChallengeInput Swipe(int dx, int dy) => new ChallengeInput(ChallengeInputKind.Swipe, -1, dx, dy);
    }

    /// <summary>
    /// Bolts a move earned for one turret. <see cref="Banks"/> says what happens to a bolt with
    /// nothing of its colour on the hill: it waits on the ward (a burst — a merge, a pair, a
    /// seated gem — is paid in full whenever a target comes), or it is spent into the air
    /// (a <em>steady</em> fire, the glade's lit critter, which is paid again next turn anyway).
    /// </summary>
    public readonly struct ChallengeFeed
    {
        public readonly int Colour, Bolts;
        public readonly bool Banks;

        public ChallengeFeed(int colour, int bolts, bool banks = true)
        {
            Colour = colour;
            Bolts = bolts;
            Banks = banks;
        }
    }

    /// <summary>
    /// What one input did to a puzzle. Reused by the puzzle across calls, so a caller reads it
    /// before the next input.
    /// </summary>
    public sealed class ChallengeMove
    {
        /// <summary>Whether the hill walks for this input. False for a free adjustment or a refusal.</summary>
        public bool Turn;

        /// <summary>Whether the input was refused outright, so a view can say so.</summary>
        public bool Refused;

        public readonly List<ChallengeFeed> Feeds = new List<ChallengeFeed>(4);

        public void Clear()
        {
            Turn = false;
            Refused = false;
            Feeds.Clear();
        }

        /// <summary>Bolts that bank on the ward until something of their colour is on the hill.</summary>
        public void Feed(int colour, int bolts) => Add(colour, bolts, true);

        /// <summary>
        /// Bolts that fire this turn or not at all (<see cref="ChallengeHill.Volley"/>): the shape
        /// of a source that pays every turn it holds, which banked would stockpile a turret
        /// against every raider still to come and empty the hill.
        /// </summary>
        public void Stream(int colour, int bolts) => Add(colour, bolts, false);

        void Add(int colour, int bolts, bool banks)
        {
            if (bolts <= 0) return;

            for (int i = 0; i < Feeds.Count; i++)
            {
                if (Feeds[i].Colour != colour || Feeds[i].Banks != banks) continue;
                Feeds[i] = new ChallengeFeed(colour, Feeds[i].Bolts + bolts, banks);
                return;
            }

            Feeds.Add(new ChallengeFeed(colour, bolts, banks));
        }
    }

    /// <summary>
    /// A puzzle genre's rules: a board that takes inputs and says which of them cost a move and
    /// what each one fed.
    ///
    /// <para>
    /// <b>Domain only.</b> No puzzle here knows a widget, a clock or a sprite; the four views
    /// read the concrete class for what to draw and hand inputs back through
    /// <see cref="Apply"/>. That is what lets <c>ChallengeTests</c> play every shipped challenge
    /// end to end without an Editor (invariant 53d's reason, said of four boards).
    /// </para>
    /// <para>
    /// <b>Three answers every genre must give</b> (MODES.md 20j): whether it is solved, whether
    /// it can no longer be solved, and — through <see cref="Apply"/> — that every input either
    /// moves something one way or is refused, so nothing can stall.
    /// </para>
    /// </summary>
    public interface IChallengePuzzle
    {
        ChallengeGenre Genre { get; }
        int Width { get; }
        int Height { get; }

        /// <summary>The puzzle's goal is met. A run wins the move this turns true.</summary>
        bool Solved { get; }

        /// <summary>The puzzle can no longer be solved (a well overflowed, no slide left). A run loses.</summary>
        bool Failed { get; }

        /// <summary>Apply one input. The result is owned by the puzzle and valid until the next call.</summary>
        ChallengeMove Apply(ChallengeInput input);
    }
}
