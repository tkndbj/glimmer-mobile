using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>The Shuffle lane's field cards</b> (MODES.md 59m): what a build makes a match do on the
    /// gem board. <c>blast_gems</c> blows up the gems round every run; <c>power_gems</c> forges a
    /// rocket out of a run of four and a nova out of five or a corner.
    ///
    /// <para>
    /// <b>Shut on every other board, and that is one line rather than a promise.</b>
    /// <see cref="Empower"/> returns on <see cref="SiegeBoosts.IsIdentity"/> before it reads
    /// anything, and a live build holding neither card answers no on the next line - so a
    /// chapter, the Infinite lane and an empty Shuffle build resolve every swap exactly as they
    /// did before this file existed (<c>ShuffleTests.TheIdentityBoostChangesNothing</c>).
    /// </para>
    /// <para>
    /// <b>It draws nothing from any stream.</b> Which run forges, where, and what a blast takes
    /// are all read off the field as it stands, so the refill that follows deals exactly the gems
    /// it would have dealt (invariant 41) and two devices playing the same swaps hold the same
    /// field.
    /// </para>
    /// <para>
    /// <b>Everything is decided on the beat and folded into it</b>, for the lance's reason
    /// (<c>SiegeBoard.Charms</c>): a blast and a forge are the match that caused them, so the
    /// cells they take clear, pay and are drawn going in one picture. A power gem goes off the way
    /// every charm does - it rides its gem, falls with it, and springs when that gem is cleared
    /// (<c>Spring</c>) - so a rocket a cascade lines up is a rocket the cascade fires.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>One run on the field: its colour, which way it lies and its cells in order.</summary>
        readonly struct Stroke
        {
            public readonly int Colour;
            public readonly bool Across;
            public readonly int[] Cells;

            public Stroke(int colour, bool across, int[] cells)
            {
                Colour = colour;
                Across = across;
                Cells = cells;
            }
        }

        /// <summary>
        /// The cells a power gem is being forged in this beat. <b>Nothing may take them</b> - not a
        /// blast, not a lance, not a rocket - because the gem is the match's payoff and is still
        /// standing when the beat ends. Empty outside a beat, and always empty on every board that
        /// holds no <c>power_gems</c>.
        /// </summary>
        readonly HashSet<int> _forming = new HashSet<int>();

        /// <summary>The two cells the player swapped, for where a power gem forms on the swap's own beat.</summary>
        int _swapA = -1, _swapB = -1;

        /// <summary>What this beat forges, applied to the field once its cells have cleared.</summary>
        readonly List<SiegeForging> _forged = new List<SiegeForging>(2);

        /// <summary>
        /// Folds a build's field cards into the beat: the forges first (they decide which cells
        /// stay), then the blasts. Before <c>Spring</c>, so a charm a blast takes goes off on the
        /// same beat.
        /// </summary>
        void Empower(HashSet<int> hit, SiegeBeat beat)
        {
            if (_boosts.IsIdentity) return;
            if (!_boosts.PowerGems && _boosts.BlastGems <= 0) return;

            // A collapsed singularity has taken the whole field: nothing is left to forge or blow.
            if (beat.Swallowed) return;

            var strokes = Strokes();
            if (strokes.Count == 0) return;

            if (_boosts.PowerGems) Forging(strokes, hit, beat);
            if (_boosts.BlastGems > 0) Blasting(strokes, hit, beat);
        }

        /// <summary>
        /// Every run on the field, read as <see cref="SiegeLayout.Runs"/> defines one - a maximal
        /// block of a colour or a prism holding at least one real gem of that colour, three or
        /// more long - with the way it lies. Colours in <see cref="SiegeLayout.Letters"/> order,
        /// rows before columns, cells in reading order: a stated order, so two runtimes forge in
        /// the same place.
        /// </summary>
        List<Stroke> Strokes()
        {
            var found = new List<Stroke>(4);

            for (int c = 0; c < SiegeLayout.Letters.Length; c++)
            {
                char colour = SiegeLayout.Letters[c];

                for (int y = 0; y < Height; y++) Scan(c, colour, y * Width, 1, Width, true, found);
                for (int x = 0; x < Width; x++) Scan(c, colour, x, Width, Height, false, found);
            }

            return found;
        }

        void Scan(int c, char colour, int from, int step, int span, bool across, List<Stroke> found)
        {
            int run = 0, real = 0;

            for (int k = 0; k <= span; k++)
            {
                int i = from + k * step;

                bool wild = k < span && SiegeCharms.IsWild(_charms[i]);
                bool joins = k < span && SiegeLayout.IsGem(_cells[i]) && (wild || _cells[i] == colour);

                if (joins)
                {
                    run++;
                    if (!wild) real++;
                    continue;
                }

                if (run >= SiegeTuning.MinRun && real > 0)
                {
                    var cells = new int[run];
                    for (int back = 0; back < run; back++) cells[back] = from + (k - run + back) * step;
                    found.Add(new Stroke(c, across, cells));
                }

                run = 0;
                real = 0;
            }
        }

        // ------------------------------------------------------------------ the forge
        /// <summary>
        /// Forges this beat's power gems: a <see cref="SiegeCharm.Nova"/> where two runs of one
        /// colour cross (a corner, a T or a plus) or where one runs five or more, and a rocket
        /// where one runs exactly four - lying the way the run lay. One gem a run at most. The
        /// cell it stands in is taken out of the clear; the rest of the run clears and pays as
        /// any match does.
        /// </summary>
        void Forging(List<Stroke> strokes, HashSet<int> hit, SiegeBeat beat)
        {
            var used = new bool[strokes.Count];

            // Corners first, because a corner is two runs and one gem.
            for (int a = 0; a < strokes.Count; a++)
            {
                if (used[a] || !strokes[a].Across) continue;

                for (int b = 0; b < strokes.Count; b++)
                {
                    if (used[b] || strokes[b].Across || strokes[b].Colour != strokes[a].Colour) continue;

                    int cross = Shared(strokes[a].Cells, strokes[b].Cells);
                    if (cross < 0) continue;

                    var both = Union(strokes[a].Cells, strokes[b].Cells);
                    if (!StandPower(both, strokes[a].Colour, SiegeCharm.Nova, cross, hit, beat)) continue;

                    used[a] = used[b] = true;
                    break;
                }
            }

            for (int s = 0; s < strokes.Count; s++)
            {
                if (used[s]) continue;

                var stroke = strokes[s];
                int length = stroke.Cells.Length;

                if (length >= 5)
                    StandPower(stroke.Cells, stroke.Colour, SiegeCharm.Nova, -1, hit, beat);
                else if (length == 4)
                    StandPower(stroke.Cells, stroke.Colour,
                          stroke.Across ? SiegeCharm.RocketRow : SiegeCharm.RocketColumn, -1, hit, beat);
            }
        }

        /// <summary>
        /// Stands one power gem in <paramref name="run"/>, answering whether there was a cell to
        /// stand it in. The cell is the one the player moved when the swap made this run, else
        /// <paramref name="prefer"/>, else the run's middle, else the first cell that will take
        /// it - never a prism (its letter is not its colour) and never a cell already carrying a
        /// charm (that charm is about to go off).
        /// </summary>
        bool StandPower(int[] run, int colour, SiegeCharm charm, int prefer, HashSet<int> hit, SiegeBeat beat)
        {
            int at = -1;

            if (beat.Depth == 1)
            {
                if (Holds(run, _swapA) && Takes(_swapA, colour)) at = _swapA;
                else if (Holds(run, _swapB) && Takes(_swapB, colour)) at = _swapB;
            }

            if (at < 0 && prefer >= 0 && Takes(prefer, colour)) at = prefer;
            if (at < 0 && Takes(run[run.Length / 2], colour)) at = run[run.Length / 2];

            for (int i = 0; at < 0 && i < run.Length; i++)
                if (Takes(run[i], colour)) at = run[i];

            if (at < 0) return false;

            hit.Remove(at);
            _forming.Add(at);

            var rest = new int[run.Length - 1];
            for (int i = 0, n = 0; i < run.Length; i++) if (run[i] != at) rest[n++] = run[i];

            var forging = new SiegeForging(at, charm, colour, rest);
            beat.Forged.Add(forging);
            _forged.Add(forging);
            return true;
        }

        /// <summary>Whether a power gem of <paramref name="colour"/> may stand in <paramref name="cell"/>.</summary>
        bool Takes(int cell, int colour)
            => cell >= 0 && cell < _cells.Length && !_forming.Contains(cell)
               && _charms[cell] == SiegeCharm.None
               && SiegeLayout.Letters.IndexOf(_cells[cell]) == colour;

        /// <summary>
        /// Puts this beat's forged gems on the field, once the beat's cells have cleared and
        /// before it falls - so each one falls with the column it stands in, charm and all.
        /// </summary>
        void SetPowers()
        {
            for (int i = 0; i < _forged.Count; i++)
            {
                var forged = _forged[i];
                _cells[forged.Cell] = SiegeLayout.Letters[forged.Colour];
                _charms[forged.Cell] = forged.Charm;
            }

            _forged.Clear();
            _forming.Clear();
        }

        // ------------------------------------------------------------------ the blast
        /// <summary>
        /// Every run blows up the gems round it: those touching its sides and ends, and on a
        /// wider blast (<see cref="SiegeBoosts.BlastGems"/> two) its corners too. A blast takes
        /// coloured gems only - never a hole, an obsidian or a void stone, which are a curse's and
        /// a collapse's own to break - and never a cell a power gem is forming in. What it takes
        /// clears and pays as any cleared gem does, and a charm it takes goes off.
        /// </summary>
        void Blasting(List<Stroke> strokes, HashSet<int> hit, SiegeBeat beat)
        {
            bool corners = _boosts.BlastGems >= 2;

            for (int s = 0; s < strokes.Count; s++)
            {
                var stroke = strokes[s];
                beat.Blasts.Add(new SiegeBlastRun(stroke.Cells[0], stroke.Cells[stroke.Cells.Length - 1], stroke.Colour));

                for (int i = 0; i < stroke.Cells.Length; i++)
                {
                    int cx = stroke.Cells[i] % Width, cy = stroke.Cells[i] / Width;

                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (!corners && dx != 0 && dy != 0) continue;

                            int x = cx + dx, y = cy + dy;
                            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;

                            int at = IndexOf(x, y);
                            if (_forming.Contains(at)) continue;
                            if (SiegeLayout.Letters.IndexOf(_cells[at]) < 0) continue;
                            if (!hit.Add(at)) continue;

                            // A prism a blast takes joined no run, so it is paid as the blast's
                            // colour - the colour drawn on it - rather than the letter it hides.
                            if (SiegeCharms.IsWild(_charms[at])) _paid[at] = SiegeLayout.Letters[stroke.Colour];

                            beat.Blasted.Add(at);
                        }
                }
            }

            beat.Blasted.Sort();
        }

        // ------------------------------------------------------------------ going off
        /// <summary>
        /// A rocket: its whole row or its whole column, added to what this beat takes. Holes are
        /// skipped and a forming gem is spared; a charm it reaches goes off in turn.
        /// </summary>
        void Rocket(int cell, int colour, bool across, HashSet<int> hit, List<int> queue)
        {
            int cx = cell % Width, cy = cell / Width;

            if (across) for (int x = 0; x < Width; x++) Taken(IndexOf(x, cy), colour, hit, queue);
            else for (int y = 0; y < Height; y++) Taken(IndexOf(cx, y), colour, hit, queue);
        }

        /// <summary>A nova: every gem of its colour on the field, added to what this beat takes.</summary>
        void Nova(int colour, HashSet<int> hit, List<int> queue)
        {
            if (colour < 0 || colour >= SiegeLayout.Letters.Length) return;

            char letter = SiegeLayout.Letters[colour];

            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i] == letter && !SiegeCharms.IsWild(_charms[i])) Taken(i, colour, hit, queue);
        }

        /// <summary>One cell a power gem reaches: taken unless it is a hole or forming, and set off if it carries a charm.</summary>
        void Taken(int at, int colour, HashSet<int> hit, List<int> queue)
        {
            if (!SiegeLayout.IsGem(_cells[at]) || _forming.Contains(at)) return;
            if (!hit.Add(at)) return;

            if (colour >= 0 && SiegeCharms.IsWild(_charms[at])) _paid[at] = SiegeLayout.Letters[colour];
            if (_charms[at] != SiegeCharm.None) queue.Add(at);
        }

        // ------------------------------------------------------------------ the arithmetic
        static bool Holds(int[] cells, int cell)
        {
            if (cell < 0) return false;
            for (int i = 0; i < cells.Length; i++) if (cells[i] == cell) return true;
            return false;
        }

        static int Shared(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (Holds(b, a[i])) return a[i];

            return -1;
        }

        static int[] Union(int[] a, int[] b)
        {
            var all = new List<int>(a.Length + b.Length);
            all.AddRange(a);
            for (int i = 0; i < b.Length; i++) if (!all.Contains(b[i])) all.Add(b[i]);
            all.Sort();
            return all.ToArray();
        }
    }
}
