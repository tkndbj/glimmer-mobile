using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The curse: what happens when three obsidians line up (<see cref="SiegeLayout.Obsidian"/>).
    ///
    /// <para>
    /// <b>Two halves on two boards, and the file is split the way the stormglass's is.</b> On the
    /// field, a break draws every obsidian standing anywhere into itself and they all shatter on
    /// the one beat (<see cref="Unbound"/>). On the hill, a beat later - booked with the break's
    /// own fuel, invariant 37s - every raider standing there is hexed (<see cref="Unbind"/>) and
    /// takes <see cref="SiegeTuning.HexPercent"/> of everything thrown at it until the hex runs
    /// out. Nothing in between is decided by the view.
    /// </para>
    /// <para>
    /// <b>What the curse takes is the raiders' <em>toughness</em>, which is the one axis seven
    /// payoffs had not touched</b> (invariant 37z's test, asked of a payoff an eighth time): a
    /// prism decides a colour, a lance a piece of the board, a stormglass a moment on the hill, a
    /// furnace a charge on the line, an hourglass the hill's time and an anvil its ground. A hex
    /// changes what every bolt, burn, charge and bomb is <em>worth</em> for a while, and it
    /// scales with the line without a number (invariant 37cg): it multiplies what the standing
    /// turrets land, so it is worth nothing to a fallen line and most to a bought one.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>A curse that has broken on the field and not yet fallen on the hill.</summary>
        struct Curse
        {
            public int Stones;
            public float In;
        }

        readonly List<Curse> _curses = new List<Curse>(2);

        /// <summary>
        /// Stands a gem, or an obsidian, on a cell. <b>Internal, and the fixture is the only
        /// caller</b> - <see cref="Stand"/>'s rule: nothing in the game places a stone anywhere,
        /// they are dealt, so a fixture proving what a break does would otherwise have to deal a
        /// few hundred gems and hope.
        /// </summary>
        internal void Lay(int cell, char gem)
        {
            if (cell < 0 || cell >= _cells.Length) return;
            _cells[cell] = gem;
            _charms[cell] = SiegeCharm.None;
        }

        /// <summary>How many obsidians are standing on the field right now.</summary>
        public int Obsidians
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++)
                    if (_cells[i] == SiegeLayout.Obsidian) n++;

                return n;
            }
        }

        /// <summary>
        /// Folds a broken curse into this beat: if any run on it is a run of obsidians, every
        /// obsidian on the field goes with it, and the curse is booked to fall on the hill.
        ///
        /// <para>
        /// <b>Every stone, not only the run, and that is the design rather than a flourish.</b>
        /// An obsidian is dead weight on the field - it lines up with nothing a ward burns - so the
        /// board has to give the player a way to be rid of it that is worth reaching for. The
        /// break sweeping the whole field is that way, and it is also what makes gathering them
        /// worth something: each stone past the three that broke it is a longer hex
        /// (<see cref="SiegeTuning.HexFor"/>).
        /// </para>
        /// <para>
        /// <b>Gems only, and nothing a charm rides</b> - an obsidian never carries one - so the
        /// stones added here reach no second rule: they pay nothing (no ward burns them) and spring
        /// nothing. They are added to <paramref name="hit"/> exactly as a lance's cross is, so the
        /// fuel loop, the collapse and the drawing need no clause of their own.
        /// </para>
        /// </summary>
        void Unbound(HashSet<int> hit, SiegeBeat beat)
        {
            // The ordinary case, asked before anything is allocated: a field that deals no stones
            // has none, and this runs on every beat of every cascade in the mode.
            if (!Layout.Cursed) return;

            bool broke = false;

            foreach (int cell in hit)
                if (_cells[cell] == SiegeLayout.Obsidian && _paid[cell] == SiegeLayout.Obsidian)
                {
                    broke = true;
                    break;
                }

            if (!broke) return;

            // Walked in cell order rather than off the set, for `beat.Cleared`'s reason: a
            // `HashSet<int>` is not promised to enumerate the same way on two runtimes, and the
            // order decides the order the view draws the stones being pulled in.
            for (int cell = 0; cell < _cells.Length; cell++)
            {
                if (_cells[cell] != SiegeLayout.Obsidian) continue;

                bool inRun = hit.Contains(cell) && _paid[cell] == SiegeLayout.Obsidian;
                if (inRun) beat.Breakers.Add(cell);

                // A stone taken by the break rather than by a run of its own is paid as what it
                // is: no ward burns it, so this writes nothing anybody is paid for. It is written
                // at all so the fuel loop reads one answer for every cell of the clear.
                if (_paid[cell] == '\0') _paid[cell] = SiegeLayout.Obsidian;

                hit.Add(cell);
                beat.Broken.Add(cell);
            }

            _curses.Add(new Curse
            {
                Stones = beat.Broken.Count,
                In = SiegeTuning.FuelLands(beat.Depth - 1),
            });

            Attention.CurseBroken(beat.Broken.Count);
        }

        /// <summary>
        /// Lands whatever curse has finished crossing the field: every raider standing on the hill
        /// is hexed for <see cref="SiegeTuning.HexFor"/> the stones that broke it.
        ///
        /// <para>
        /// <b>Standing, and only standing</b> - a body still in the wings was not there when the
        /// curse broke (<see cref="SiegeRaider.Hex"/>). So a curse broken over an empty hill
        /// marks nothing, and the report still carries its seconds so the view can draw the
        /// refusal: a payoff that silently did nothing would be a broken gem rather than a wrong
        /// choice (the furnace's rule).
        /// </para>
        /// <para>
        /// <b>A boss is hexed too.</b> Nothing about a fight is skipped by it - the floor still
        /// holds a stand (<c>Wound</c> applies the hex before the clamp) - so what it buys against
        /// a boss is the line chewing each stand down sooner, which is a curse doing what a curse
        /// is for.
        /// </para>
        /// </summary>
        void Unbind(float dt)
        {
            for (int i = _curses.Count - 1; i >= 0; i--)
            {
                var curse = _curses[i];
                curse.In -= dt;

                if (curse.In > 0f)
                {
                    _curses[i] = curse;
                    continue;
                }

                _curses.RemoveAt(i);

                float seconds = SiegeTuning.HexFor(curse.Stones);
                if (seconds > _report.Hex) _report.Hex = seconds;

                for (int r = 0; r < _raiders.Count; r++)
                    if (_raiders[r].Hex(seconds)) _report.Hexed.Add(_raiders[r].Id);
            }
        }
    }
}
