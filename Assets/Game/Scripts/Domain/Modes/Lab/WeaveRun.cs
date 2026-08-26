using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One Lightweave grove being played: a board, the light it is drawn with, and what has been
    /// drawn so far.
    ///
    /// <para>
    /// <b>Four things that each answer one question, and this is only the wiring between
    /// them.</b> <see cref="WeaveBoard"/> knows what is drawn and what may be; <see cref="Ink"/>
    /// knows what a channel costs and what is left; <see cref="WeaveStrokes"/> knows what
    /// happened and how much of it may be taken back; <see cref="WeaveVerdict"/> reads a board
    /// against a meter and says whether the run is over. All four were one class for exactly one
    /// change, which is how long it took to notice that a puzzle model had grown an economy, an
    /// undo stack and a fail state — and that none of the three could be tested without a grove.
    /// </para>
    /// <para>
    /// What is left here is the two verbs a player has — draw, and take it back — expressed as
    /// the one place the three pieces are allowed to move together. That is the point: a channel
    /// that lands must charge, and be written down, in one step, or a crash between two of them
    /// leaves a meter that disagrees with the board it is measuring.
    /// </para>
    /// </summary>
    public sealed class WeaveRun
    {
        public readonly WeaveBoard Board;
        public readonly WeaveInk Ink;

        readonly WeaveStrokes _strokes = new WeaveStrokes();

        /// <summary>A grove with no ink budget, which is therefore impossible to lose.</summary>
        public WeaveRun(WeaveLayout layout) : this(layout, WeaveInk.Unlimited) { }

        public WeaveRun(WeaveLayout layout, int inkBudget)
        {
            Board = new WeaveBoard(layout);
            Ink = new WeaveInk(inkBudget);
        }

        public WeaveLayout Grove => Board.Grove;

        // ------------------------------------------------------------------ the two verbs
        /// <summary>
        /// Lays a channel down and charges its light: one cell per cell it covers.
        ///
        /// <para>
        /// <b>A run with no redraw in it spends exactly <c>Board.Occupied</c></b>, which is what
        /// the stars are read off — so the meter on screen and the grade at the end are the same
        /// number rather than two that can quietly disagree. Where they part is the channel drawn,
        /// thought better of, and drawn again; the ink is the honest reading of that one, because
        /// the light really was spent.
        /// </para>
        /// <para>
        /// <b>It does not refuse for want of ink.</b> What stops a channel nobody can afford is
        /// one step earlier — the drag is walled at <see cref="Affords"/>, so the path never
        /// reaches this — and what ends the run is <see cref="Verdict"/>. Refusing here as well
        /// would put the same rule in two places and enforce it in one.
        /// </para>
        /// </summary>
        public bool Draw(int pair, IReadOnlyList<int> path)
        {
            if (!Board.Draw(pair, path, out var replaced)) return false;

            Ink.Spend(path.Count);
            _strokes.Note(pair, replaced, path.Count);
            return true;
        }

        /// <summary>
        /// Takes back the last channel that landed, puts back whatever it replaced, and refunds
        /// its light in full.
        ///
        /// <para>
        /// A true undo rather than an erase, and the difference is the pair that was being
        /// <em>redrawn</em>: it had a perfectly good channel a moment ago, and taking the new one
        /// away while leaving it bare would cost the player something they never asked to lose.
        /// </para>
        /// <para>
        /// Reports which pair moved, so a view can repaint that one channel rather than the whole
        /// board. -1 when there was nothing to undo.
        /// </para>
        /// </summary>
        public bool TryUndo(out int pair)
        {
            pair = -1;
            if (!_strokes.TryUndo(out var stroke)) return false;

            Board.PutBack(stroke.Pair, stroke.Replaced);
            Ink.Refund(stroke.Cost);

            pair = stroke.Pair;
            return true;
        }

        /// <summary>Whether a channel of this many cells could be laid with the light in hand.</summary>
        public bool Affords(int cells) => Ink.Affords(cells);

        /// <summary>Whether there is a channel to take back and an undo left to do it with.</summary>
        public bool CanUndo => _strokes.CanUndo;

        /// <summary>How many more channels this grove may hand back. See <c>WeaveStrokes.Allowance</c>.</summary>
        public int UndosLeft => _strokes.Left;

        /// <summary>How the run stands: still playing, finished, or over.</summary>
        public WeaveVerdict Verdict => WeaveVerdict.Read(Board, Ink);

        /// <summary>
        /// Back to the grove the player was dealt — the channels, the light and the undos.
        /// A restart, in other words, and the three have to go together or one of them is a
        /// memory of a run that no longer exists.
        /// </summary>
        public void Restart()
        {
            Board.Reset();
            Ink.Reset();
            _strokes.Reset();
        }

        // ------------------------------------------------------------------ the board, read-only
        // Forwarded rather than reached through, so nothing outside has to know whether a fact
        // belongs to the board or to the run — and so the board itself stays the one place these
        // are worked out. Only what a screen or a view actually asks for is here.
        public int Pairs => Board.Pairs;
        public int Joined => Board.Joined;
        public int BeadsLeft => Board.BeadsLeft;
        public bool IsSolved => Board.IsSolved;
        public int Occupied => Board.Occupied;

        public int OwnerOf(int cell) => Board.OwnerOf(cell);
        public IReadOnlyList<int> PathOf(int pair) => Board.PathOf(pair);
        public bool IsJoined(int pair) => Board.IsJoined(pair);
        public bool IsThreaded(int bead) => Board.IsThreaded(bead);
        public bool Free(int pair, int cell) => Board.Free(pair, cell);

        /// <summary>
        /// Draws the arrangement the generator carved, to prove the board can be finished at all.
        ///
        /// Forwarded to the board deliberately: it is a question about a grove rather than about
        /// a run, so it neither charges light nor writes down a stroke — the validator is not
        /// playing.
        /// </summary>
        public bool DrawSolution() => Board.DrawSolution();
    }
}
