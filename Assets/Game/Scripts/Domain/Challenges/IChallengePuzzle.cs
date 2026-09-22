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

    public readonly struct ChallengeFeed
    {
        public readonly int Colour, Bolts;

        public ChallengeFeed(int colour, int bolts)
        {
            Colour = colour;
            Bolts = bolts;
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

        public void Feed(int colour, int bolts)
        {
            if (bolts <= 0) return;

            for (int i = 0; i < Feeds.Count; i++)
            {
                if (Feeds[i].Colour != colour) continue;
                Feeds[i] = new ChallengeFeed(colour, Feeds[i].Bolts + bolts);
                return;
            }

            Feeds.Add(new ChallengeFeed(colour, bolts));
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
