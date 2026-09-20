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

            // **Identical means the same colour <em>and</em> the same charm, and the second half
            // arrived with the prism.** This used to be the letters alone, which is a correct
            // shortcut for exactly as long as two gems of one colour are interchangeable: a prism
            // swapped with an ordinary gem of the letter it happens to be carrying underneath is a
            // real move — it puts a wild somewhere it can join something — and refusing it here
            // would have been a swap the drag rejected, the hint never offered and the shuffle
            // believed impossible, all with nothing in any file wrong.
            if (_cells[a] == _cells[b] && _charms[a] == _charms[b]) return false;

            // **A web and a sack are both refused here rather than by the caller**, because
            // `Lines` is the one question every door asks — the drag, `AnySwap`, `Settle`'s own
            // proof that the field is playable and the offline mirror. A rule enforced at the
            // drag alone would be a rule the shuffle could break.
            if (!Movable(a) || !Movable(b)) return false;

            Trade(a, b);
            bool any = SiegeLayout.Runs(_cells, Width, Height, _charms).Count > 0;
            Trade(a, b);

            return any;
        }

        /// <summary>
        /// Swaps two cells and everything standing on them.
        ///
        /// <b>One place, because a parallel array is only ever wrong in the move somebody
        /// forgot.</b> Three readers exchange two cells — the trial inside <see cref="Lines"/>,
        /// the real swap, and <see cref="Settle"/>'s shuffle — and a charm left behind by any one
        /// of them is a gem whose picture and whose rule disagree.
        /// </summary>
        void Trade(int a, int b)
        {
            char cell = _cells[a];
            _cells[a] = _cells[b];
            _cells[b] = cell;

            var charm = _charms[a];
            _charms[a] = _charms[b];
            _charms[b] = charm;

            // Recorded, never read by any rule - see `SiegeAttention`. It is here rather than at
            // the three call sites for the reason this method exists at all: a parallel fact is
            // only ever wrong in the move somebody forgot.
            Attention.CharmSwapped(a, b);
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

            Trade(a, b);

            var turn = new SiegeTurn { A = a, B = b };

            int depth = 0;
            while (true)
            {
                for (int i = 0; i < _paid.Length; i++) _paid[i] = '\0';

                var hit = SiegeLayout.Runs(_cells, Width, Height, _charms, _paid);
                if (hit.Count == 0) break;

                depth++;
                var beat = new SiegeBeat { Depth = depth, Fuel = new float[_wards.Length] };

                // **Everything a charm adds is folded into the beat that set it off, before a
                // single cell is taken away.** A lance that takes its row and column has not
                // started a cascade — a cascade is what falls in afterwards — so its cells clear
                // on the same beat, pay on the same beat and are drawn going in one picture. The
                // alternative, resolving it as a beat of its own, would have shown the row going
                // a fifth of a second after the match that caused it and read as two events.
                Spring(hit, beat);

                foreach (int cell in hit) beat.Cleared.Add(cell);
                beat.Cleared.Sort();

                // Sorted above rather than walked as a set: a `HashSet<int>` is not promised to
                // enumerate the same way on two runtimes, and this loop decides the order the
                // view lights cells in.
                for (int i = 0; i < beat.Cleared.Count; i++)
                {
                    int cell = beat.Cleared[i];

                    // **Paid as what the run made it, never as the letter underneath.** For every
                    // ordinary gem those are the same character; for a prism they are not, and
                    // that difference is the whole of what a wild is worth (`SiegeLayout.Runs`).
                    // A cell a lance took that was in no run of its own is paid as its own colour,
                    // which is what the fallback says.
                    char worth = _paid[cell] != '\0' ? _paid[cell] : _cells[cell];
                    beat.Paid.Add(SiegeLayout.Letters.IndexOf(worth));

                    int ward = Layout.WardOf(worth);
                    if (ward >= 0) beat.Fuel[ward] += SiegeTuning.FuelPerGem;

                    _cells[cell] = Hole;
                    _charms[cell] = SiegeCharm.None;
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

        /// <summary>
        /// Gravity, then a refill, both written into the beat for the view to animate.
        ///
        /// <para>
        /// <b>Two passes over the field rather than one pass per column, and the split is what
        /// makes a settled refill possible at all.</b> A dealt gem is chosen by asking the field
        /// whether it would land already matched (<see cref="Settled"/>), and that question only
        /// has a true answer once every gem a run could reach is where it is going to stay. While
        /// a column was collapsed and refilled before the next one fell, a fresh gem was asked
        /// about neighbours still floating above their holes — so it would settle against a board
        /// that no longer existed a moment later.
        /// </para>
        /// <para>
        /// <b>Within a column both passes keep the order they had</b>, which is what the view
        /// relies on: it moves a falling gem out of <c>_gems[from]</c> and into <c>_gems[to]</c>,
        /// and a source is always read before anything is written over it only because the drops
        /// of one column arrive lowest-first. Columns never touch each other's cells, so
        /// interleaving them costs nothing and re-ordering inside one would cost a gem.
        /// </para>
        /// </summary>
        void Collapse(SiegeBeat beat)
        {
            var empty = new int[Width];

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

                        // A charm falls with the gem it is riding. Cleared rather than left
                        // behind, because a stale entry above the write head would be picked up
                        // by the next thing to land on it.
                        _charms[to] = _charms[from];
                        _charms[from] = SiegeCharm.None;
                        Attention.CharmMoved(from, to);

                        beat.Drops.Add(new SiegeDrop(x, y, write, SiegeLayout.Letters.IndexOf(c),
                                                     _charms[to]));
                    }

                    write--;
                }

                empty[x] = write;
            }

            // Everything above each column's write head is new, and it falls in from above the
            // field.
            for (int x = 0; x < Width; x++)
            {
                int fresh = 0;

                for (int y = empty[x]; y >= 0; y--)
                {
                    int to = IndexOf(x, y);
                    char c = Deal(to, out var charm);

                    _cells[to] = c;
                    _charms[to] = charm;

                    if (charm != SiegeCharm.None) Attention.CharmDealt(charm, to);

                    beat.Drops.Add(new SiegeDrop(x, -1 - fresh, y, SiegeLayout.Letters.IndexOf(c),
                                                 charm));
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
                    Trade(free[i], free[j]);
                }

                // A shuffle that lands three alike together would go off with nobody having
                // touched it, so it is dealt again rather than resolved.
                if (SiegeLayout.Runs(_cells, Width, Height, _charms).Count > 0) continue;
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
        /// <summary>
        /// <b>Still exactly one draw, and that is what let charms ship without re-rolling every
        /// board in the mode.</b> A stream drawn from a different number of times deals a
        /// different field from the same seed, so a second <see cref="Next"/> here would have
        /// changed every refill on every shipped rung with nothing in any file wrong (invariant
        /// 41) — three of the first chapter's ten became unholdable the last time that happened.
        /// The charm is read out of the <em>same</em> word, and the letter is read out of it
        /// exactly as before, so every gem this mode has ever dealt is still the gem it was.
        ///
        /// <para>
        /// <b>Avalanched rather than multiplied, and the difference between those two is a bug this
        /// shipped with for a day.</b> The first version was one multiply by Knuth's constant and
        /// then the <em>low</em> sixteen bits of the product — which mixes nothing at all, because
        /// the low half of a product depends only on the low halves of its operands. So the roll
        /// was a relabelling of the same low bits the letter is picked from, and xorshift32's low
        /// bits are its weakest: two of the first chapter's rungs dealt <b>no charm in any run at
        /// any rhythm</b>, and the rest dealt between a third and twice what the rate asks for.
        /// Every gate was green — the rate is right when the roll is fed a clean stream, which is
        /// exactly what a fixture calling this in a tight loop does — and what found it was
        /// somebody playing the game and saying they had seen one.
        /// </para>
        /// <para>
        /// <b>The rule is that a multiply mixes <em>upward</em>, so a slice of a product is only
        /// safe at the top.</b> What is used instead is a full avalanche (the lowbias32 finaliser:
        /// shift, multiply, shift, multiply, shift), after which every output bit depends on every
        /// input bit and the two halves can be taken as two independent decisions. Thirty-two bit
        /// throughout, for <see cref="Next"/>'s reason: the offline mirror has to reach the same
        /// field.
        /// </para>
        /// <para>
        /// The rate is a fraction of 65,536 rather than a clean thousandth, which biases a charm
        /// by about one part in two thousand of itself. That is immaterial to a rarity roll and it
        /// is <em>exactly</em> reproducible in Python, which a rejection loop would not be without
        /// costing a draw.
        /// </para>
        /// </summary>
        internal char Deal(int at, out SiegeCharm charm)
        {
            uint drawn = Next();

            charm = SiegeCharm.None;

            var charms = Layout.Charms;
            if (charms == null || charms.Length == 0) return Settled(at, drawn);

            uint mixed = Avalanche(drawn);

            // **One charm a window, at a place inside it the roll picks** — see
            // `SiegeTuning.CharmWithin` for why this is a window rather than a chance. The counter
            // only advances on a field that deals charms, so a level authoring none leaves no
            // state behind and nothing to reason about.
            //
            // **What this guarantees and what it averages are two numbers, and only the first may
            // be gated on.** The gap between two charms is somewhere in 1..`CharmWithin`, so a run
            // clearing a whole window's worth of gems is dealt *at least* one — and, because the
            // gap averages half a window, it meets about *two*. Both gates refuse on the floor.
            if (_sinceCharm++ == _charmAt)
            {
                charm = charms[(int)((mixed >> 16) % (uint)charms.Length)];

                _sinceCharm = 0;
                _charmAt = (int)((mixed & 0xFFFFu) % SiegeTuning.CharmWithin);
            }

            return Settled(at, drawn);
        }

        /// <summary>
        /// The gem this draw deals into <paramref name="at"/>: the one it picked, unless that one
        /// would land already matched, in which case the next colour in the bag that would not.
        ///
        /// <para>
        /// <b>A dealt gem may never land in a run (invariant 37eo), and that is a rule about
        /// agency rather than about difficulty.</b> Nothing used to stop one: a refill was a free
        /// draw per cell, so on every collapse the board rolled itself a fresh chance of three
        /// alike, and the chains that followed were the board's work rather than the player's.
        /// Measured over ninety runs a chapter before this rule, <b>39% of every match cascaded</b>
        /// and the deepest reached <b>x15</b> — on the three-colour opening rungs a match cleared
        /// <b>13 gems</b> against par's assumed 5.5 and chained 2.7 deep on average. A cascade
        /// that arrives because the deal happened to agree with itself is the free payoff
        /// invariant 5d refuses: it rejects no play, so it says nothing about any.
        /// </para>
        /// <para>
        /// <b>What is left is the cascade a player earns</b> — gravity dropping gems that were
        /// already on the field into line with each other. That one is caused by the move, it is
        /// readable before it is taken, and it is the only kind this mode pays for now.
        /// </para>
        /// <para>
        /// <b>Still exactly one <see cref="Next"/>, which is the whole reason it is a rotation of
        /// the bag rather than a re-roll.</b> A rejection loop would draw again, and a stream
        /// drawn a different number of times deals a different field from the same seed
        /// (invariant 41) — so this walks the bag from where the draw landed and takes the first
        /// gem that settles, which keeps the draw's own choice whenever that choice is legal and
        /// keeps an author's weighting (a bag may write a letter twice) as nearly as a skip can.
        /// </para>
        /// <para>
        /// <b>Every colour matching is a real state and is dealt anyway</b>, rather than left to a
        /// fallback nobody chose: on a three-colour field a cell with two alike above it and two
        /// alike beside it has no settled answer at all. The drawn gem is what lands, which is
        /// exactly today's behaviour for that cell and nothing worse.
        /// </para>
        /// <para>
        /// <b>The charm riding in is deliberately not consulted, so the letter this deals stays a
        /// function of the draw and the field alone.</b> A prism is wild, so a cell that settles
        /// as an ordinary gem may still line up as a prism — rare, and the right way round: a
        /// charm is a payoff and one that arrives having already done something is not a fault.
        /// What it buys is that a charmed field and a plain one deal the same letters from the
        /// same seed, which is the one proof that a charm roll costs no draw
        /// (<c>SiegeCharmTests.DealingACharmCostsNoExtraDraw</c>).
        /// </para>
        /// <para>
        /// <b>It is asked of the field as it stands</b>, so it is only sound while every gem a
        /// run could reach is already in its final place — which is what splits
        /// <see cref="Collapse"/> into gravity for the whole field and then the refill, rather
        /// than both a column at a time.
        /// </para>
        /// </summary>
        char Settled(int at, uint drawn)
        {
            string bag = Layout.Deal;
            int from = (int)(drawn % (uint)bag.Length);

            char was = _cells[at];
            char dealt = bag[from];

            for (int i = 0; i < bag.Length; i++)
            {
                char gem = bag[(from + i) % bag.Length];

                _cells[at] = gem;
                if (SiegeLayout.Lined(_cells, Width, Height, _charms, at)) continue;

                dealt = gem;
                break;
            }

            _cells[at] = was;

            return dealt;
        }

        /// <summary>
        /// Scatters one word so that every bit of the answer depends on every bit of the question.
        ///
        /// <b>The lowbias32 finaliser, written out</b> — two multiplies with an xor-shift either
        /// side of each. It is here rather than inline because what it is for is a rule rather than
        /// an arithmetic convenience: <em>a slice of this is a fair coin, and a slice of an
        /// xorshift word is not</em>. Anything else in this mode that ever needs a second
        /// independent decision out of one draw goes through it.
        /// </summary>
        static uint Avalanche(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
            }

            return x;
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
