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

            bool any = SiegeLayout.Runs(_cells, Width, Height, _webbed).Count > 0;

            _cells[a] = keepA;
            _cells[b] = keepB;
            return any;
        }

        /// <summary>Whether any swap on this field would line anything up.</summary>
        public bool AnySwap()
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int here = IndexOf(x, y);
                    if (x + 1 < Width && Lines(here, here + 1)) return true;
                    if (y + 1 < Height && Lines(here, here + Width)) return true;
                }

            return false;
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
                var hit = SiegeLayout.Runs(_cells, Width, Height, _webbed);
                if (hit.Count == 0) break;

                depth++;
                var beat = new SiegeBeat { Depth = depth, Fuel = new float[_wards.Length] };

                foreach (int cell in hit) beat.Cleared.Add(cell);
                beat.Cleared.Sort();

                // **Cogs are claimed before anything comes off the field, and in cell order.**
                // Both halves matter: the colour that takes a cog has to be read off the board as
                // it stands, and the order has to be an ordering rather than whichever way a hash
                // set happened to enumerate — a `HashSet<int>` walk is not promised to be the same
                // on two runtimes, and this decides which turret a player's upgrade went to.
                Claim(beat);

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
        /// Takes every cog standing beside something this beat cleared, and spends it on the ward
        /// of the colour that took it.
        ///
        /// <para>
        /// <b>The colour of the run decides the ward, which is the whole mechanic.</b> A cog is
        /// not a colour and can never be lined up; what it is worth is a rank, and which turret
        /// gets it is settled by what the player chose to match beside it. So the question a cog
        /// asks is the mode's own question — <em>which colour is wanted</em> — asked about the
        /// line rather than about the hill, and a player who answers it carelessly upgrades the
        /// wrong turret and cannot take it back.
        /// </para>
        /// <para>
        /// <b>First neighbour in cell order wins a cog with two colours beside it</b>, and that is
        /// a rule rather than a tie-break: it is deterministic, it is the same on both runtimes,
        /// and it is *stated* rather than emergent. A player who wants a particular colour to take
        /// a cog can always arrange for that colour to be the only one touching it.
        /// </para>
        /// <para>
        /// <b>The rank lands here rather than with the fuel</b>, unlike everything else this turn
        /// books (invariant 37s). A rank is a property and not a hit: nothing about it is visible
        /// until a bolt leaves, and a bolt costs fuel, which is still crossing the field — so
        /// there is no moment where the player sees an effect arrive before its cause. Keeping it
        /// here is what keeps <see cref="SiegeWard.Rank"/> a single source of truth for the badge,
        /// the damage and the fuel cost at once.
        /// </para>
        /// </summary>
        void Claim(SiegeBeat beat)
        {
            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int cell = beat.Cleared[i];
                char colour = _cells[cell];

                int x = cell % Width, y = cell / Width;

                Take(x - 1, y, colour, beat);
                Take(x + 1, y, colour, beat);
                Take(x, y - 1, colour, beat);
                Take(x, y + 1, colour, beat);
            }
        }

        /// <summary>Takes the cog at this cell, if there is one, for this colour.</summary>
        void Take(int x, int y, char colour, SiegeBeat beat)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;

            int cell = IndexOf(x, y);
            if (_cells[cell] != SiegeLayout.Cog) return;

            // Emptied here rather than left for the sweep below, so a cog with three cleared gems
            // around it is taken once and by the first of them.
            _cells[cell] = Hole;

            int ward = Layout.WardOf(colour);

            // A cog taken by a colour whose ward has fallen — or is already at the top of the
            // ladder — is spent for nothing, and that is reported rather than hidden. It is the
            // cost of the decision, and a mechanic whose wrong answer costs nothing is a mechanic
            // with no decision in it (invariant 5d).
            if (ward >= 0 && _wards[ward].Upgradable)
            {
                _wards[ward].Rank++;
                beat.Rises.Add(new SiegeRise(cell, ward, _wards[ward].Rank,
                                             SiegeLayout.Letters.IndexOf(colour)));
                return;
            }

            beat.Rises.Add(new SiegeRise(cell, ward, 0, SiegeLayout.Letters.IndexOf(colour)));
        }

        /// <summary>How many cogs are standing on the field. Bounded by <c>MostCogs</c>.</summary>
        public int CogsStanding()
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] == SiegeLayout.Cog) n++;
            return n;
        }

        /// <summary>
        /// Whether a finger, or a shuffle, may move what is standing in this cell.
        ///
        /// <b>A cog may</b>, and that is deliberate rather than an oversight: a cog is a prize the
        /// player is trying to reach, so dragging one into place is exactly the play the mechanic
        /// wants. A web and a sack are what the hill has done <em>to</em> the field, so neither
        /// moves until the thing that put it there is dead.
        /// </summary>
        public bool Movable(int index)
            => index >= 0 && index < _cells.Length
            && !_webbed[index] && _cells[index] != SiegeLayout.Sack;

        /// <summary>Whether a weaver has locked this cell.</summary>
        public bool IsWebbed(int index)
            => index >= 0 && index < _webbed.Length && _webbed[index];

        /// <summary>Whether a thief has taken the gem that was standing here.</summary>
        public bool IsSack(int index)
            => index >= 0 && index < _cells.Length && _cells[index] == SiegeLayout.Sack;

        /// <summary>How many of the field's cells a weaver has locked.</summary>
        public int WebsStanding()
        {
            int n = 0;
            for (int i = 0; i < _webbed.Length; i++) if (_webbed[i]) n++;
            return n;
        }

        /// <summary>How many sacks are standing on the field.</summary>
        public int SacksStanding()
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] == SiegeLayout.Sack) n++;
            return n;
        }

        /// <summary>Whether this cell is holding a cog rather than a gem.</summary>
        public bool IsCog(int index)
            => index >= 0 && index < _cells.Length && _cells[index] == SiegeLayout.Cog;

        /// <summary>Whether this level ever deals a cog, authored or refilled.</summary>
        public bool Upgrades => Layout.Cogs > 0 || _seeded;

        /// <summary>
        /// Whether anything walking down this hill carries a shield.
        ///
        /// <b>Read off the muster rather than off the raiders standing now</b>, because a lesson
        /// has to go up when the board opens and the first bulwark may be two waves away — and a
        /// lesson is shown once in a player's life, so raising it on a rung that sends none would
        /// spend it on something never on the screen.
        /// </summary>
        public bool Shielded
        {
            get
            {
                for (int w = 0; w < Layout.Coming.Length; w++)
                    for (int i = 0; i < Layout.Coming[w].Length; i++)
                        if (Layout.KindAt(w, i) == SiegeKind.Bulwark) return true;

                return false;
            }
        }

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

                        // The web travels with the gem it is on. Anything parallel to the field
                        // has to move here or it ends up describing a cell that is not the one it
                        // was put on — which is invisible until a player wonders why a clean gem
                        // will not move.
                        _webbed[to] = _webbed[from];
                        _webbed[from] = false;

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
                    _webbed[IndexOf(x, y)] = false;
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
                if (SiegeLayout.Runs(_cells, Width, Height, _webbed).Count > 0) continue;
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
        char Deal()
        {
            if (Layout.Cogs > 0 && CogsStanding() < SiegeTuning.MostCogs
                && Next() % 100u < (uint)Layout.Cogs)
                return SiegeLayout.Cog;

            return Layout.Deal[(int)(Next() % (uint)Layout.Deal.Length)];
        }

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
