using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The gem field: what a swap does, how the board falls, refills and re-deals, and the one
    /// random stream all of it runs on.
    ///
    /// <para>
    /// <b>Its own file rather than a region</b>, because the board is three machines that share
    /// a state and nothing else — a match-three field, a clock the hill walks on, and the
    /// utilities the bar spends into it. Splitting them is what stops the next mechanic being
    /// added to whichever half the reader happened to be looking at.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ swapping
        public bool Adjacent(int a, int b)
        {
            if (a < 0 || b < 0 || a >= Count || b >= Count || a == b) return false;

            int ax = a % Width, ay = a / Width, bx = b % Width, by = b / Width;
            return Math.Abs(ax - bx) + Math.Abs(ay - by) == 1;
        }

        /// <summary>Whether swapping these two would line anything up.</summary>
        public bool Lines(int a, int b)
        {
            if (!Adjacent(a, b)) return false;
            if (_cells[a] == _cells[b]) return false;

            // **A web and a sack are both refused here rather than by the caller**, because
            // `Lines` is the one question every door asks — the drag, `AnySwap`, `Settle`'s own
            // proof that the field is playable and the offline mirror. A rule enforced at the
            // drag alone would be a rule the shuffle could break.
            if (!Movable(a) || !Movable(b)) return false;

            char keepA = _cells[a], keepB = _cells[b];
            _cells[a] = keepB;
            _cells[b] = keepA;

            bool any = SiegeLayout.Runs(_cells, Width, Height, null).Count > 0;

            _cells[a] = keepA;
            _cells[b] = keepB;
            return any;
        }

        /// <summary>Whether any swap on this field would line anything up.</summary>
        public bool AnySwap() => FindSwap(0).Found;

        /// <summary>
        /// A swap that would line something up, found by walking the field from
        /// <paramref name="from"/> and wrapping.
        ///
        /// <para>
        /// <b>Here rather than in the view, because "would this swap work" is already a rule.</b>
        /// <see cref="Lines"/> is the one door every reader goes through — the drag,
        /// <see cref="AnySwap"/>, <c>Settle</c>'s own proof that the field is playable, and the
        /// offline mirror — and a hint that found its own answer would be a second opinion about
        /// the only question this field asks. <see cref="AnySwap"/> is this, which is what keeps
        /// them from being able to disagree: a field the shuffle believes is playable is a field a
        /// hint can always point at.
        /// </para>
        /// <para>
        /// <b><paramref name="from"/> is what stops a repeated nudge pointing at the same pair.</b>
        /// The scan order is a fact rather than a preference — a <c>HashSet</c> walk is not
        /// promised to enumerate the same way on two runtimes and neither is a shuffle, so the
        /// pairs are numbered and walked in order, and a caller that wants the *next* answer hands
        /// back <see cref="SiegeSwap.At"/> plus one. Nothing about a hint reaches the rules, so
        /// this decides nothing about a run; it is deterministic because a reading that is not
        /// cannot be pinned.
        /// </para>
        /// </summary>
        public SiegeSwap FindSwap(int from)
        {
            int pairs = Count * 2;
            if (pairs <= 0) return default;

            int start = ((from % pairs) + pairs) % pairs;

            for (int i = 0; i < pairs; i++)
            {
                int p = (start + i) % pairs;

                // Two pairs per cell — the one to its right and the one below it — which between
                // them name every adjacency on the field exactly once.
                int here = p >> 1;
                int x = here % Width, y = here / Width;

                int other = (p & 1) == 0
                    ? (x + 1 < Width ? here + 1 : -1)
                    : (y + 1 < Height ? here + Width : -1);

                if (other < 0 || !Lines(here, other)) continue;

                return new SiegeSwap(here, other, p);
            }

            return default;
        }

        /// <summary>
        /// Swaps two gems and resolves everything that follows, fuelling the wards as it goes.
        ///
        /// Answers null for a swap that lines nothing up: the field is left exactly as it was and
        /// the run is charged nothing, which is what makes trying a swap free and keeping one
        /// expensive.
        /// </summary>
        public SiegeTurn Swap(int a, int b)
        {
            if (!Lines(a, b)) return null;

            char keep = _cells[a];
            _cells[a] = _cells[b];
            _cells[b] = keep;

            var turn = new SiegeTurn { A = a, B = b };

            int depth = 0;
            while (true)
            {
                var hit = SiegeLayout.Runs(_cells, Width, Height, null);
                if (hit.Count == 0) break;

                depth++;
                var beat = new SiegeBeat { Depth = depth, Fuel = new float[_wards.Length] };

                foreach (int cell in hit) beat.Cleared.Add(cell);
                beat.Cleared.Sort();

                // Sorted above rather than walked as a set: a `HashSet<int>` is not promised to
                // enumerate the same way on two runtimes, and this loop decides the order the
                // view lights cells in.
                for (int i = 0; i < beat.Cleared.Count; i++)
                {
                    int cell = beat.Cleared[i];

                    int ward = Layout.WardOf(_cells[cell]);
                    if (ward >= 0) beat.Fuel[ward] += SiegeTuning.FuelPerGem;

                    _cells[cell] = Hole;
                }

                turn.Worth += beat.Cleared.Count;

                for (int w = 0; w < _wards.Length; w++)
                {
                    if (beat.Fuel[w] <= 0f) continue;

                    // **Booked, not paid.** It lands when the motes do - see
                    // `SiegeTuning.FuelLands`. A fallen ward is not filtered here either: it may
                    // still be standing now and down by the time this arrives, and the ward that
                    // is asked is the one that exists when the fuel gets there.
                    _flying.Add(new SiegeCharge
                    {
                        Ward = w,
                        Fuel = beat.Fuel[w],
                        In = SiegeTuning.FuelLands(depth - 1),
                    });
                }

                Collapse(beat);
                turn.Beats.Add(beat);
            }

            Settle();
            return turn;
        }

        /// <summary>
        /// Whether this level's line can be upgraded at all.
        ///
        /// <b>A fact about the level rather than about the field.</b> Cogs are dropped by felled
        /// raiders now, so nothing about the board can be asked whether one is standing — what
        /// decides it is the authored drop rate, and it is read here so that everything asking
        /// "does this rung have the cog mechanic" keeps asking one question.
        /// </summary>
        public bool Upgrades => Layout.Cogs > 0;

        public bool Movable(int index)
            => index >= 0 && index < _cells.Length;


        // **`Shielded` went with the lesson it was written for.** It answered "does this hill
        // ever send a bulwark", read off the muster rather than off the raiders standing now, and
        // its only caller was `SiegeScreen.Lessons` deciding whether to raise `siege_shield`. With
        // that lesson retired it had no reader at all, and a public reading on a Domain board with
        // no caller is the shape somebody later reaches for to mean something it was never
        // measured against. The rule itself is untouched: what a bulwark's shield does is
        // `SiegeTuning.DamageTo`, and every rung that sent one still sends one.

        /// <summary>Gravity, then a refill, both written into the beat for the view to animate.</summary>
        void Collapse(SiegeBeat beat)
        {
            for (int x = 0; x < Width; x++)
            {
                int write = Height - 1;

                for (int y = Height - 1; y >= 0; y--)
                {
                    char c = _cells[IndexOf(x, y)];
                    if (c == Hole) continue;

                    if (write != y)
                    {
                        int from = IndexOf(x, y), to = IndexOf(x, write);

                        _cells[to] = c;
                        _cells[from] = Hole;

                        beat.Drops.Add(new SiegeDrop(x, y, write, SiegeLayout.Letters.IndexOf(c)));
                    }

                    write--;
                }

                // Everything above the write head is new, and it falls in from above the field.
                int fresh = 0;
                for (int y = write; y >= 0; y--)
                {
                    char c = Deal();
                    _cells[IndexOf(x, y)] = c;
                    beat.Drops.Add(new SiegeDrop(x, -1 - fresh, y, SiegeLayout.Letters.IndexOf(c)));
                    fresh++;
                }
            }
        }

        /// <summary>
        /// Deals the field again if nothing on it lines up.
        ///
        /// <b>A field that cannot be played is the one thing this mode may never show</b>, because
        /// its clock does not stop: a locked board with raiders still walking is a run the player
        /// watches themselves lose. It is deterministic, so two devices reshuffle the same way.
        /// </summary>
        void Settle()
        {
            // **Only what may move is shuffled**, so a web and a sack keep the cells they were
            // put on. The alternative reads as the raider's own work being undone by the game
            // tidying up, and it would let a shuffle carry a lock into a corner the player had
            // just cleared.
            var free = new List<int>(_cells.Length);
            for (int i = 0; i < _cells.Length; i++) if (Movable(i)) free.Add(i);

            for (int attempt = 0; attempt < 40 && !AnySwap(); attempt++)
            {
                if (free.Count < 2) break;

                for (int i = free.Count - 1; i > 0; i--)
                {
                    int j = (int)(Next() % (uint)(i + 1));
                    char keep = _cells[free[i]];
                    _cells[free[i]] = _cells[free[j]];
                    _cells[free[j]] = keep;
                }

                // A shuffle that lands three alike together would go off with nobody having
                // touched it, so it is dealt again rather than resolved.
                if (SiegeLayout.Runs(_cells, Width, Height, null).Count > 0) continue;
            }
        }

        /// <summary>
        /// One fresh cell falling into the top of a column: usually a gem, occasionally a cog.
        ///
        /// <para>
        /// <b>The cog roll is only taken when there is room for one</b>, and that keeps the stream
        /// deterministic rather than breaking it: how many cogs are standing is a fact about the
        /// board, and two devices playing the same swaps hold the same board — so both take the
        /// roll or neither does, and the gems that follow line up exactly.
        /// </para>
        /// </summary>
        /// <summary>
        /// One fresh gem for a column that has emptied.
        ///
        /// <b>Exactly one draw, which is what keeps every shipped seed honest.</b> It used to take
        /// two on the draws that rolled a cog, so removing the cog from the field removes a draw —
        /// and a stream drawn from a different number of times deals a different board from the
        /// same seed, which re-rolls every field in the game with nothing in any file wrong
        /// (invariant 41). The cog roll moved to the hill and takes the same one draw per felled
        /// raider that a lane already took, so what changed is where a draw happens and never how
        /// many there are.
        /// </summary>
        char Deal() => Layout.Deal[(int)(Next() % (uint)Layout.Deal.Length)];

        /// <summary>xorshift32. Thirty-two bit throughout so the Python mirror reaches the same field.</summary>
        uint Next()
        {
            uint x = _rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rng = x == 0u ? 2463534242u : x;
            return _rng;
        }
    }
}
