using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a weave has drawn, in order, and how much of it may be taken back.
    ///
    /// <para>
    /// <b>A stack of what happened, holding no opinion about boards.</b> It records that a pair
    /// was drawn, what route that displaced and what it cost, and it hands the most recent one
    /// back on request. Whether the route can be re-laid is <see cref="WeaveBoard"/>'s question
    /// and whether the light comes back is <see cref="WeaveInk"/>'s; <see cref="WeaveRun"/> is
    /// what puts the three together. Kept apart so the rule that actually matters here — that
    /// there are exactly two of these and they do not come back — is arithmetic over plain
    /// integers, provable without a grove.
    /// </para>
    /// <para>
    /// <b>Only the most recent stroke may be taken back, and that is what makes it safe rather
    /// than merely simple.</b> Undoing the last one returns the board to a state it was
    /// demonstrably in a moment ago, so the route being put back cannot collide with anything:
    /// nothing has been drawn since it was displaced. Reaching further down has no such
    /// guarantee — a later channel may be standing on ground an older one wants — so it would
    /// have to either refuse or half-restore, and both are worse than not offering it.
    /// </para>
    /// </summary>
    public sealed class WeaveStrokes
    {
        /// <summary>
        /// How many landed channels a player may take back on one grove.
        ///
        /// <para>
        /// <b>Two, and the number is the whole design of it.</b> Ink is only a fail state while
        /// spending it is permanent (<see cref="WeaveInk"/>), so an unlimited undo would hand
        /// every cell back and leave the meter decorative — the fault invariant 5d names. Nought
        /// would be the opposite mistake and a worse one: a finger is wrong constantly on a grid
        /// this size, and a puzzle that charges full price for the correction rather than for
        /// the mistake is one people put down. Two is enough to survive the ordinary
        /// misjudgement — draw a channel, see what it walled off, put it back — and few enough
        /// that the third mistake is one the player pays for, which is where the mode's tension
        /// lives.
        /// </para>
        /// <para>
        /// A constant rather than content because it is a <em>rule</em> and not a difficulty
        /// knob: the boards carry difficulty here (invariant 5d), and a grove that dealt one
        /// player three undos and another two would make the same seed two different puzzles.
        /// The lever a retune wants is the ink, which is derived from par and moves with
        /// <c>LevelTuning</c> for every mode at once.
        /// </para>
        /// </summary>
        public const int Allowance = 2;

        /// <summary>One landed channel, and what the board looked like before it landed.</summary>
        public readonly struct Stroke
        {
            /// <summary>Which pair was drawn.</summary>
            public readonly int Pair;

            /// <summary>The route it displaced, empty when the pair had none.</summary>
            public readonly int[] Replaced;

            /// <summary>What it cost in cells of light.</summary>
            public readonly int Cost;

            public Stroke(int pair, int[] replaced, int cost)
            {
                Pair = pair;
                Replaced = replaced ?? System.Array.Empty<int>();
                Cost = cost;
            }
        }

        readonly List<Stroke> _drawn = new List<Stroke>();
        int _left = Allowance;

        /// <summary>How many more channels may be handed back.</summary>
        public int Left => _left;

        /// <summary>How many channels have landed and not been taken back.</summary>
        public int Count => _drawn.Count;

        /// <summary>Whether there is a channel to take back and an undo left to do it with.</summary>
        public bool CanUndo => _left > 0 && _drawn.Count > 0;

        /// <summary>Writes down a channel that has landed.</summary>
        public void Note(int pair, int[] replaced, int cost)
            => _drawn.Add(new Stroke(pair, replaced, cost));

        /// <summary>
        /// Takes the most recent stroke off the stack and spends one of the allowance.
        /// False, and nothing moves, when there is nothing to take or nothing to take it with.
        /// </summary>
        public bool TryUndo(out Stroke stroke)
        {
            stroke = default;
            if (!CanUndo) return false;

            stroke = _drawn[_drawn.Count - 1];
            _drawn.RemoveAt(_drawn.Count - 1);
            _left--;

            return true;
        }

        /// <summary>Back to an untouched grove: nothing drawn, and the whole allowance in hand.</summary>
        public void Reset()
        {
            _drawn.Clear();
            _left = Allowance;
        }
    }
}
