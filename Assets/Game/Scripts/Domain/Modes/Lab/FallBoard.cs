using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>Lightfall.</b> A well of coloured motes. You never match them — you <em>cook</em> them,
    /// and a mote that reaches white bursts and washes the colour that finished it into
    /// everything beside it.
    ///
    /// <para>
    /// <b>One verb with two branches, and the branch is what makes it a game.</b> A mote dropped
    /// onto a stack either <em>enriches</em> the top of it or <em>heightens</em> it. Red onto
    /// green makes yellow and the stack does not grow. Red onto yellow adds nothing — yellow
    /// already holds red — so the mote sits on top and the well is one row nearer its brim.
    /// Every drop therefore costs one of a finite supply and, if it was the wrong one, a row of
    /// headroom as well: one mistake, two meters, and both of them visible.
    /// </para>
    /// <para>
    /// <b>The wash is what makes a chain possible, and it is the rule this class was rewritten
    /// for.</b> The mode shipped with a detonation that took its white mote and the four motes
    /// touching it, and boasted in this very file about the cascades that set off. There were
    /// none, and there could not be: nothing here changes a mote's colour except a drop, so
    /// every white on the board was taken by the first wave and the second could never find one.
    /// The wave counter, the rising pitch and the chain multiplier were all dead code against a
    /// rule that rejects them. What replaced it is <em>one</em> destruction and a spread: a
    /// white mote bursts alone, and the motes beside it gain the channel that finished it. Any
    /// of them that is thereby completed bursts in turn — so a single well-chosen drop runs
    /// through a whole connected blob of motes that were all missing the same channel, which is
    /// the chain the mode was written as if it had.
    /// </para>
    /// <para>
    /// It also decides what the mode <em>is</em>. Dropping blue clears the yellows; the reds and
    /// greens it passes are left one step better rather than untouched; and a mote buried at the
    /// bottom of a column — which no drop can ever land on — is reached by the wash from its
    /// neighbours. That is what makes a full well solvable at all, and what makes which colour
    /// goes where the whole of the thinking.
    /// </para>
    /// <para>
    /// <b>A lens is the second chapter's answer to the one thing a wash cannot do, which is
    /// travel.</b> The wash reaches what a burst <em>touches</em> and stops, so a chain dies at
    /// the first gap and at the first colour that already holds the channel - which bounds every
    /// cascade to one connected blob. Glass carries light out of that blob. It cannot be
    /// enriched and nothing can be dropped into it; what it does is <em>fill up</em>, exactly as
    /// a mote does, and when it holds all three it <b>fires</b>, each beam crossing bare ground
    /// until the first cell in its line takes it.
    /// </para>
    /// <para>
    /// <b>It relayed on first touch once, and that was wrong in both directions at the same
    /// time.</b> Any burst beside a lens set it off, so the reach was free and the boards got
    /// easier - and because it happened on most drops that touched glass it could never be worth
    /// stopping the board for. It was reported as both at once: too easy, and an effect with no
    /// effort in it. Those are one fault. A payoff handed out for nothing cannot be a payoff, and
    /// the fix for the animation and the fix for the difficulty are the same fix.
    /// </para>
    /// <para>
    /// <b>So glass is cooked like everything else here, and the cost is what makes the shot worth
    /// watching.</b> Every wave of one drop carries that drop's colour, so a lens can gain at
    /// most one channel per drop: filling an empty one takes three separate drops of three
    /// separate colours, each engineered to burst beside it. That is a plan built across a run
    /// rather than a freebie - measured, an empty lens leaves 7 boards in 90 solvable where
    /// two-thirds-full glass leaves 50 - and it is why the chapter can ramp on nothing but how
    /// full each lens starts (<see cref="FallCell.TryParse"/>).
    /// </para>
    /// <para>
    /// <b>A lens fires white, and that is the one place in this mode where the threshold is
    /// bought rather than kept.</b> Glass holds all three channels by the time it goes off, so
    /// the light it throws is all three - which means every mote a beam lands on is completed
    /// and <em>pops</em>, whatever colour it was. Nothing else here can do that: a burst washes
    /// the drop's one colour and only sets off what was exactly that channel short. What stops
    /// it being invariant 20j's solvent is the <em>price</em> rather than a threshold on the
    /// consequence — a lens gains at most one channel per drop
    /// (<c>FallGlassTests.OneDropCanOnlyEverAddOneChannelToGlass</c>), so three separate drops
    /// of three separate colours pay for one shot, and a beam still stops at the first thing it
    /// meets. Reach is bought, and it is bought dearly.
    /// </para>
    /// <para>
    /// <b>And how far round it fires says where its light came from.</b> A lens charged the
    /// ordinary way - by bursts beside it, by beams, by drops taken in - fires <em>sideways</em>,
    /// which is the only pair of directions worth anything on a board with gravity: a lens rests
    /// on something, so its downward beam travels one cell and its upward one flies into the air
    /// above the stack. A lens set off by <em>another lens's shot</em> fires along all four axes,
    /// up and down together, because it was struck rather than filled. That is the chain: one
    /// well-aimed shot down a row of glass opens every lens in it and each of those opens its own
    /// column.
    /// </para>
    /// <para>
    /// <b>No Unity types and no randomness.</b> The whole thing is provable offline, which
    /// matters because a falling-piece game is wrong in ways a screenshot cannot show — a
    /// gravity pass that settles in the wrong order, a wash applied after the fall rather than
    /// before it, a cascade that resolves one column at a time.
    /// </para>
    /// </summary>
    public sealed class FallBoard
    {
        public readonly int Width, Height;

        readonly int[] _cells;          // Energy mask per cell, 0 = empty
        int _motes;

        /// <summary>
        /// Scratch for one wave: what channels each cell was handed, accumulated rather than
        /// latched — and allocated only when a wave actually happens.
        ///
        /// <para>
        /// <b>It is a mask rather than a flag because a wave no longer carries one colour.</b> A
        /// burst washes the drop's colour and a lens fires white, so one cell can be reached by
        /// both in the same wave and must take both. <c>|=</c> is commutative, so the result
        /// still cannot depend on which burst or which lens was scanned first, which is the
        /// property this whole method is arranged around. Non-nought is also what says a cell is
        /// already on the washed or charged list, so it is the flag as well as the amount.
        /// </para>
        /// <para>
        /// The search forks a board per position it tries, hundreds of thousands of them, and
        /// most of those forks resolve nothing at all. Allocating this alongside the cells made
        /// every one of them pay for a wave that never came.
        /// </para>
        /// </summary>
        int[] _gain;

        /// <summary>
        /// Which lenses were struck by another lens's beam, and therefore fire along all four
        /// axes rather than sideways when they go off.
        ///
        /// <para>
        /// <b>It has to outlive the wave that sets it, which is why it is not scratch.</b> A lens
        /// a beam lands on is filled in one wave and fires in the next, and the well settles in
        /// between — so the flag is carried by <see cref="Settle"/> alongside the cell it belongs
        /// to and cleared when that cell leaves. At rest it is all false: a struck lens is always
        /// full, and a full lens always fires on the very next wave.
        /// </para>
        /// <para>
        /// Copied by <see cref="Fork"/> for the same reason the cells are. The search never
        /// observes a set flag — it forks between drops, and every wave has resolved by then —
        /// but a fork that dropped state the rule reads is the kind of divergence nothing can
        /// see, because a board that settles differently still settles.
        /// </para>
        /// </summary>
        bool[] _struck;

        /// <summary>
        /// Scratch for one wave: which cells are leaving it, whether they burst or fired.
        ///
        /// Separate from <see cref="_gain"/>, which grows as the wave hands light out. This one
        /// is fixed the moment the wave is read, and it is what a beam consults to decide that a
        /// cell in its path will not be there when the light lands.
        /// </summary>
        bool[] _going;

        public FallBoard(FallLayout layout)
        {
            Width = layout.Width;
            Height = layout.Height;
            _cells = layout.Fill();
            _motes = Count();
        }

        FallBoard(FallBoard other)
        {
            Width = other.Width;
            Height = other.Height;
            _cells = new int[other._cells.Length];
            System.Array.Copy(other._cells, _cells, _cells.Length);
            _motes = other._motes;
            Flooded = other.Flooded;

            if (other._struck != null)
            {
                _struck = new bool[other._struck.Length];
                System.Array.Copy(other._struck, _struck, _struck.Length);
            }
        }

        /// <summary>A private copy, for a search that wants to try a drop without taking it.</summary>
        public FallBoard Fork() => new FallBoard(this);

        /// <summary>The row a mote may not come to rest in. See <see cref="FallLayout"/>.</summary>
        public const int Brim = FallLayout.Brim;

        public int Index(int x, int y) => y * Width + x;
        public int At(int x, int y) => _cells[Index(x, y)];
        public int At(int index) => _cells[index];
        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int X(int index) => index % Width;
        public int Y(int index) => index / Width;

        /// <summary>Motes still standing. The goal is nought of them.</summary>
        public int Motes => _motes;

        /// <summary>The well is empty and the run is won.</summary>
        public bool IsEmpty => _motes == 0;

        /// <summary>
        /// A mote came to rest above the brim line.
        ///
        /// <para>
        /// Decided after a drop has fully resolved rather than at the instant of landing, and
        /// that generosity is deliberate: a mote that lands on the brim and immediately bursts
        /// has not flooded anything, and it is the most exciting thing that can happen on this
        /// board. Reading it at the landing would end the run in the frame before the save.
        /// </para>
        /// </summary>
        public bool Flooded { get; private set; }

        /// <summary>
        /// Safe rows the tallest column can still take before the well floods. Nought means the
        /// next careless drop anywhere on the tallest column ends the run.
        /// </summary>
        public int Headroom
        {
            get
            {
                int highest = Height;
                for (int x = 0; x < Width; x++)
                {
                    int top = TopOf(x);
                    if (top >= 0 && top < highest) highest = top;
                }

                int safe = highest - 1;
                return safe < 0 ? 0 : safe;
            }
        }

        /// <summary>Every channel some mote is still missing, as a mask. Nought on an empty well.</summary>
        public int Wanted
        {
            get
            {
                int mask = Energy.None;
                for (int i = 0; i < _cells.Length; i++) mask |= FallCell.Wants(_cells[i]);
                return mask;
            }
        }

        /// <summary>
        /// Whether anything here could ever burst.
        ///
        /// <para>
        /// <b>Glass cannot, and that is the one way a well can hold motes and still be
        /// finished with.</b> A lens is only ever removed by light reaching it, and light only
        /// ever comes from a burst — so a well down to its last lenses with no mote left to cook
        /// is over whatever the supply says. It is the honest input to the one question here
        /// that charges money: <c>FallVerdict.Deficit</c> must answer "no offer" for it, because
        /// selling motes into a well that can never burst again is selling a run that is
        /// already finished (invariant 23).
        /// </para>
        /// </summary>
        public bool Cookable
        {
            get
            {
                for (int i = 0; i < _cells.Length; i++)
                    if (FallCell.IsMote(_cells[i])) return true;
                return false;
            }
        }

        /// <summary>Lenses still standing, for the readout and for an author's sweep.</summary>
        public int Lenses
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cells.Length; i++) if (FallCell.IsLens(_cells[i])) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ reading a column
        /// <summary>The row of the highest mote in a column, or -1 for an empty column.</summary>
        public int TopOf(int x)
        {
            for (int y = 0; y < Height; y++)
                if (_cells[Index(x, y)] != Energy.None) return y;
            return -1;
        }

        /// <summary>The lowest empty row in a column, or -1 when the column is full to the top.</summary>
        public int FirstFree(int x)
        {
            for (int y = Height - 1; y >= 0; y--)
                if (_cells[Index(x, y)] == Energy.None) return y;
            return -1;
        }

        /// <summary>
        /// Where a mote of this colour would come to rest: the top of the stack if it can enrich
        /// it, otherwise the first free cell above. -1 when the column cannot take one.
        ///
        /// <para>
        /// Worth being able to ask before committing — the screen draws a ghost of it under the
        /// player's thumb, and that preview is the whole reason this verb works where tapping a
        /// cell did not.
        /// </para>
        /// </summary>
        public int Landing(int colour, int x)
        {
            if (x < 0 || x >= Width || colour == Energy.None) return -1;

            int top = TopOf(x);
            if (top >= 0)
            {
                // Whatever is on top takes the drop if it lacks the colour: a mote is enriched,
                // a lens is charged. Neither raises the stack, and the one line covers both
                // because `|=` means the same thing to either.
                int cell = _cells[Index(x, top)];
                if ((cell | colour) != cell) return top;
            }

            return FirstFree(x);                             // sits on top instead
        }

        /// <summary>Whether a drop here would enrich the mote on top rather than heighten it.</summary>
        public bool Enriches(int colour, int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int mote = _cells[Index(x, top)];
            return FallCell.IsMote(mote) && (mote | colour) != mote;
        }

        /// <summary>
        /// Whether a drop here would be taken in by glass rather than by a mote.
        ///
        /// <para>
        /// <b>The valve that stops a well ever becoming unwinnable, and it was added after
        /// play.</b> Glass is only ever charged by light, and light only ever comes from a
        /// burst — so a player who cleared every mote before feeding the lens had destroyed the
        /// only thing that could ever fill it, and was left tapping at a board that could not be
        /// finished and would not end. Reported exactly that way: <em>"I have destroyed all the
        /// motes, only this prism ball left, but I cannot finish the level."</em> Measured
        /// afterwards, that state was three drops away on the fifth board and five on the
        /// sixth — not a corner, the obvious line of play.
        /// </para>
        /// <para>
        /// <b>It is a valve rather than a shortcut, and the arithmetic is what makes it one.</b>
        /// Feeding glass by hand costs one drop a channel and gives nothing back; feeding it
        /// with a burst is usually free, because the burst was clearing a blob anyway. So the
        /// search still prefers the burst route and par is unmoved on eight of the ten shipped
        /// boards — what the drop buys is that being wrong can always be paid for, out of the
        /// same five drops of slack every well is dealt.
        /// </para>
        /// </summary>
        public bool Charges(int colour, int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int cell = _cells[Index(x, top)];
            return FallCell.IsLens(cell) && (cell | colour) != cell;
        }

        /// <summary>
        /// Whether this drop would light a mote all the way to white, which is what the ghost
        /// promises and what the ripe halo on the board already says.
        ///
        /// It says nothing about how far the chain would run. That is the player's to read, and
        /// showing it would be showing them the answer.
        /// </summary>
        public bool Bursts(int colour, int x)
        {
            int top = TopOf(x);
            if (top < 0) return false;

            int mote = _cells[Index(x, top)];
            return FallCell.IsMote(mote) && (mote | colour) == Energy.All;
        }

        /// <summary>
        /// Whether this drop would come to rest above the brim. A warning rather than a verdict
        /// — the mote may burst on arrival and save the well — but an honest one, because most
        /// of the time it will not.
        /// </summary>
        public bool AtBrim(int colour, int x)
        {
            int at = Landing(colour, x);
            return at == Brim;
        }

        /// <summary>A column with somewhere for a mote of this colour to go.</summary>
        public bool CanDrop(int colour, int x)
            => !Flooded && !IsEmpty && x >= 0 && x < Width && Landing(colour, x) >= 0;

        // ------------------------------------------------------------------ dropping
        /// <summary>
        /// Drops a mote into a column and resolves everything that follows.
        ///
        /// <para>
        /// <paramref name="steps"/> may be null, and that is what lets the search run the very
        /// code the game runs rather than a copy of it (invariant 9a, for a board rule). A
        /// screen hands a list and plays the waves a beat apart, because a board handed over
        /// settled is the same information with none of the feeling; a solver hands nothing and
        /// pays for no allocation.
        /// </para>
        /// </summary>
        public FallResolution Drop(int colour, int x, List<FallStep> steps = null)
        {
            if (!CanDrop(colour, x)) return null;

            int at = Landing(colour, x);
            int index = Index(x, at);
            bool enriched = _cells[index] != Energy.None;

            if (!enriched) _motes++;
            _cells[index] |= colour;

            Resolve(colour, steps, out int waves, out int burst);

            // Read after the whole cascade rather than at the landing. It cannot differ today —
            // a mote only ever comes to rest on the brim by *heightening*, and a heightened mote
            // is a pure colour that cannot burst — but a rule that reads the board after it has
            // finished moving is the one that stays right if the wash ever reaches further.
            Flooded = BrimBreached();

            return new FallResolution(x, at, colour, enriched, waves, burst, steps);
        }

        /// <summary>
        /// Walks the board to rest: every white mote bursts, the motes beside it gain the colour
        /// that finished it, everything falls, and whatever that completed bursts in turn.
        ///
        /// <para>
        /// <b>A whole wave is decided before any of it is applied.</b> The wash is read off the
        /// positions the bursting motes are standing in, so it cannot depend on which of them
        /// was scanned first, and it is applied before gravity, so a mote is washed where it was
        /// rather than where it ends up. Resolving one burst at a time would let a mote fall
        /// into a gap the next burst in the same wave was about to make, and the well would
        /// settle differently depending on which column happened to be walked first.
        /// </para>
        /// <para>
        /// <b>It terminates because every wave destroys at least one mote.</b> Whites are always
        /// taken, never washed, so the loop cannot find the same one twice.
        /// </para>
        /// </summary>
        void Resolve(int wash, List<FallStep> steps, out int waves, out int burstCount)
        {
            int wave = 0;
            burstCount = 0;

            while (true)
            {
                // ---- what has reached white, decided over the whole board before anything moves.
                //      Two kinds, one condition: a mote at Energy.All bursts, and glass at
                //      FallCell.Full fires. That is the same sentence twice on purpose — light
                //      fills a thing up and then it goes off — which is why the lens needed no
                //      new rule taught, only a new consequence.
                List<int> burst = null, fired = null;

                for (int i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] == Energy.All)
                    {
                        if (burst == null) burst = new List<int>();
                        burst.Add(i);
                    }
                    else if (_cells[i] == FallCell.Full)
                    {
                        if (fired == null) fired = new List<int>();
                        fired.Add(i);
                    }
                }

                if (burst == null && fired == null) break;

                if (_gain == null) _gain = new int[_cells.Length];
                else for (int i = 0; i < _gain.Length; i++) _gain[i] = Energy.None;

                if (_going == null) _going = new bool[_cells.Length];
                else for (int i = 0; i < _going.Length; i++) _going[i] = false;

                // Everything leaving this wave, read once: a cell that is already going takes
                // nothing from anybody, and Reached asks this rather than a second copy of it.
                if (burst != null)
                    for (int b = 0; b < burst.Count; b++) _going[burst[b]] = true;
                if (fired != null)
                    for (int f = 0; f < fired.Count; f++) _going[fired[f]] = true;

                List<int> washed = null, charged = null;
                List<FallBeam> beams = null;

                // ---- what each burst touches: the four cells beside it, and nothing further.
                //      A mote takes the colour and a lens takes it as charge; either way a cell
                //      that already holds the channel is left off the list rather than changed to
                //      no effect, so the animation cannot promise what the rules did not do.
                if (burst != null)
                    for (int b = 0; b < burst.Count; b++)
                    {
                        int at = burst[b];
                        int x = at % Width, y = at / Width;

                        for (int n = 0; n < Neighbours.Length; n++)
                        {
                            int nx = x + Neighbours[n].dx, ny = y + Neighbours[n].dy;
                            if (!Inside(nx, ny)) continue;

                            int ni = Index(nx, ny);
                            if (_cells[ni] == FallCell.Empty) continue;

                            Reached(ni, wash, ref washed, ref charged);
                        }
                    }

                // ---- and the beams out of every lens that filled up. This is the whole payoff
                //      for charging one, and there are two things to read off it.
                //
                //      The light is *white*: glass holds all three channels by the time it goes
                //      off, so what it throws is all three, and every mote a beam lands on is
                //      completed and pops whatever colour it was. That is bought rather than
                //      given - three drops of three colours - which is what keeps it from being
                //      invariant 20j's solvent.
                //
                //      And how far round it fires says where its own light came from. Charged the
                //      ordinary way it fires sideways, which on a board with gravity is the only
                //      pair worth anything: it rests on something, so down travels one cell and up
                //      flies into the air. Struck by another lens's beam it fires along all four
                //      axes, up and down together, so a shot down a row of glass opens every lens
                //      in it and each of those then opens its own column.
                if (fired != null)
                    for (int f = 0; f < fired.Count; f++)
                    {
                        int at = fired[f];
                        var ways = Struck(at) ? Neighbours : Sideways;

                        for (int n = 0; n < ways.Length; n++)
                            Shoot(at, ways[n].dx, ways[n].dy,
                                  ref washed, ref charged, ref beams);
                    }

                // ---- apply, in the one order that has no reading order in it
                if (burst != null)
                    for (int b = 0; b < burst.Count; b++)
                    {
                        _cells[burst[b]] = FallCell.Empty;
                        _motes--;
                    }

                // Glass goes when it fires, and is counted as destroyed for the same reason it is
                // counted as standing: a well is emptied, and a lens left in it is a well that is
                // not. It is the only way glass ever leaves, which is what makes a board the
                // search proves emptiable a board where every lens on it is charged and fired.
                if (fired != null)
                    for (int f = 0; f < fired.Count; f++)
                    {
                        _cells[fired[f]] = FallCell.Empty;
                        if (_struck != null) _struck[fired[f]] = false;
                        _motes--;
                    }

                burstCount += (burst == null ? 0 : burst.Count) + (fired == null ? 0 : fired.Count);

                // Each cell takes what it was actually handed rather than one colour for the
                // whole wave: a burst washes the drop's, a beam hands over white, and a cell
                // reached by both takes both. The view is told the same figures, for the same
                // reason it is told everything else here - it draws what happened rather than
                // what the wave was about.
                List<int> washedWith = null, chargedWith = null;

                if (washed != null)
                {
                    washedWith = new List<int>(washed.Count);
                    for (int w = 0; w < washed.Count; w++)
                    {
                        washedWith.Add(_gain[washed[w]]);
                        _cells[washed[w]] |= _gain[washed[w]];
                    }
                }

                if (charged != null)
                {
                    chargedWith = new List<int>(charged.Count);
                    for (int c = 0; c < charged.Count; c++)
                    {
                        chargedWith.Add(_gain[charged[c]]);
                        _cells[charged[c]] |= _gain[charged[c]];
                    }
                }

                var moved = Settle();
                wave++;

                steps?.Add(new FallStep((IReadOnlyList<int>)burst ?? Empty,
                                        (IReadOnlyList<int>)fired ?? Empty,
                                        (IReadOnlyList<int>)washed ?? Empty,
                                        (IReadOnlyList<int>)charged ?? Empty,
                                        (IReadOnlyList<int>)washedWith ?? Empty,
                                        (IReadOnlyList<int>)chargedWith ?? Empty,
                                        (IReadOnlyList<FallBeam>)beams ?? NoBeams,
                                        wave, moved));
            }

            waves = wave;
        }

        /// <summary>
        /// One cell the light got to. Charges glass, washes a mote, or does neither.
        ///
        /// <para>
        /// The one place both kinds are handled together, because "does this take the channel"
        /// is the same question for both and answering it twice is how the two come to disagree.
        /// </para>
        /// <para>
        /// <b>It accumulates rather than latching, and that is what a white beam cost.</b> A wave
        /// used to carry one colour, so the first thing to reach a cell was the only thing that
        /// could give it anything and a bool was enough. A burst now washes the drop's colour
        /// while a lens throws all three, so one cell can be reached by both — and it has to take
        /// both, or the answer would depend on which of them was scanned first, which is the one
        /// property this whole wave is arranged to avoid. <c>|=</c> has no reading order in it.
        /// The list is still written once, on the first light to arrive, because that is a list
        /// of cells rather than of arrivals.
        /// </para>
        /// </summary>
        void Reached(int ni, int light, ref List<int> washed, ref List<int> charged)
        {
            if (_going[ni]) return;                                // gone by the time it arrives

            int cell = _cells[ni];
            if (cell == FallCell.Empty) return;

            int gain = light & ~cell;
            if (gain == Energy.None) return;                       // holds it already: takes nothing

            bool first = _gain[ni] == Energy.None;
            _gain[ni] |= gain;
            if (!first) return;                                    // already on a list

            if (FallCell.IsLens(cell))
            {
                if (charged == null) charged = new List<int>();
                charged.Add(ni);
                return;
            }

            if (washed == null) washed = new List<int>();
            washed.Add(ni);
        }

        /// <summary>
        /// Whether the glass at this cell was set off by another lens's shot rather than filled
        /// the ordinary way, which is what decides how far round it fires.
        ///
        /// Answers false on a board that has never seen a beam, because the array is allocated
        /// with the first one — a well with no glass in it, which is every well of the first
        /// chapter, never pays for it at all.
        /// </summary>
        bool Struck(int at) => _struck != null && _struck[at];

        /// <summary>
        /// Follows one beam of a firing lens until something stops it. The light it carries is
        /// <see cref="Energy.All"/>: glass holds all three by the time it goes off, so what it
        /// throws is all three.
        ///
        /// <para>
        /// <b>Two things stop it and both take something.</b> A mote is completed — whatever
        /// colour it was, white leaves it nothing to want — so it <em>pops</em> on the next wave.
        /// Glass is filled outright and fires on the next wave itself, which is the chain: a shot
        /// down a row of lenses opens every one of them. Bare ground, and anything going off in
        /// this same wave, it passes straight through.
        /// </para>
        /// <para>
        /// <b>The absorbing wall is gone from a beam and is still there for a wash.</b> That is
        /// the trade this rule makes and it is worth being clear about: a mote that already held
        /// the colour used to stop a beam dead and take nothing, which made a shot a question
        /// about what stood in the line. White has no such mote, so a beam always pays out. What
        /// keeps it from being invariant 20j's solvent is the price rather than the threshold — a
        /// lens gains at most one channel per drop, so a shot costs three drops of three colours,
        /// and it still only reaches the <em>first</em> thing in each line.
        /// </para>
        /// <para>
        /// <b>A cell going off in this wave is transparent, and that is deliberate.</b> It will
        /// not be there when the light arrives: the wave is read from where its cells stand, but
        /// they are taken before anything settles. A lens shielded by its own cause would be a
        /// rule with nothing on the board to show it.
        /// </para>
        /// </summary>
        void Shoot(int from, int dx, int dy,
                   ref List<int> washed, ref List<int> charged, ref List<FallBeam> beams)
        {
            int x = from % Width, y = from / Width;
            int steps = 0;

            while (true)
            {
                x += dx;
                y += dy;
                steps++;

                if (!Inside(x, y))
                {
                    // Out of the well, and still drawn: a shot that reaches nothing is a decision
                    // that went wrong, and the player is entitled to watch it happen rather than
                    // to see three drops of charge spent on nothing visible. The endpoint is one
                    // cell outside the wall, which is exactly where it should leave.
                    Beam(ref beams, new FallBeam(from, dx, dy, steps, -1));
                    return;
                }

                int ni = Index(x, y);
                if (_cells[ni] == FallCell.Empty) continue;        // what a lens exists to cross
                if (_going[ni]) continue;                          // gone by the time it arrives

                Beam(ref beams, new FallBeam(from, dx, dy, steps, ni));

                // Glass struck by a shot fires every way when its turn comes, and the flag has to
                // be set before the fill rather than after it: the fill is what makes it full, and
                // full is what makes the next wave read it as a lens that fires.
                if (FallCell.IsLens(_cells[ni]))
                {
                    if (_struck == null) _struck = new bool[_cells.Length];
                    _struck[ni] = true;
                }

                Reached(ni, Energy.All, ref washed, ref charged);
                return;
            }
        }

        static void Beam(ref List<FallBeam> beams, FallBeam beam)
        {
            if (beams == null) beams = new List<FallBeam>();
            beams.Add(beam);
        }

        static readonly FallBeam[] NoBeams = new FallBeam[0];

        static readonly int[] Empty = new int[0];

        /// <summary>
        /// Lets everything fall into the gaps, and reports what moved so the screen can animate
        /// the collapse rather than teleporting the board.
        /// </summary>
        IReadOnlyList<FallMove> Settle()
        {
            List<FallMove> moved = null;

            for (int x = 0; x < Width; x++)
            {
                int write = Height - 1;
                for (int y = Height - 1; y >= 0; y--)
                {
                    int at = Index(x, y);
                    if (_cells[at] == Energy.None) continue;

                    if (y != write)
                    {
                        int to = Index(x, write);
                        _cells[to] = _cells[at];
                        _cells[at] = Energy.None;

                        // The struck flag travels with the glass it belongs to. A lens filled by a
                        // beam fires on the next wave and the well settles in between, so a flag
                        // left behind at the old index would arm whatever fell into that cell and
                        // disarm the lens that earned it.
                        if (_struck != null)
                        {
                            _struck[to] = _struck[at];
                            _struck[at] = false;
                        }

                        if (moved == null) moved = new List<FallMove>();
                        moved.Add(new FallMove(at, to));
                    }

                    write--;
                }
            }

            return moved == null ? NoMoves : (IReadOnlyList<FallMove>)moved;
        }

        static readonly FallMove[] NoMoves = new FallMove[0];

        bool BrimBreached()
        {
            for (int x = 0; x < Width; x++)
                if (_cells[Index(x, Brim)] != Energy.None) return true;
            return false;
        }

        int Count()
        {
            int n = 0;
            for (int i = 0; i < _cells.Length; i++) if (_cells[i] != Energy.None) n++;
            return n;
        }

        static readonly (int dx, int dy)[] Neighbours = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        /// <summary>
        /// The two ways a lens fires when it was filled rather than struck.
        ///
        /// <para>
        /// <b>Sideways is not a reduction of four, it is the two that were ever worth anything.</b>
        /// A well has gravity, so a lens rests on something: its downward beam travels exactly one
        /// cell and its upward one flies into the air above the stack and leaves. Only across is
        /// there anything to cross, which is why every lens in the chapter is placed looking along
        /// a valley in the terrain and why <c>FallSolver</c> counts a lens's aim out of two.
        /// </para>
        /// </summary>
        static readonly (int dx, int dy)[] Sideways = { (1, 0), (-1, 0) };

        // ------------------------------------------------------------------ for the search
        /// <summary>
        /// A 64-bit fingerprint of what is standing in the well, so a search can recognise a
        /// position it has already been in.
        ///
        /// <para>
        /// FNV-1a, and a hash rather than the cells themselves on purpose: a search holds
        /// hundreds of thousands of these and a phone runs it at level load, so the difference
        /// between eight bytes and a hundred is the difference between a search that fits and
        /// one that does not. Collisions are the price and they are negligible — a quarter of a
        /// million entries in a 64-bit space collide with probability around two in a billion,
        /// and the consequence of one would be a par a single drop out, which the build gate
        /// would have to have passed first.
        /// </para>
        /// </summary>
        public ulong Signature()
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < _cells.Length; i++)
                {
                    hash ^= (ulong)(uint)(_cells[i] + 1);
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }

    /// <summary>Where a mote landed and what it set off.</summary>
    public sealed class FallResolution
    {
        public readonly int Column, Row, Colour;

        /// <summary>Whether it enriched the mote it landed on rather than sitting above it.</summary>
        public readonly bool Enriched;

        /// <summary>
        /// How far the chain ran. One is a burst; more than one is worth shouting about.
        ///
        /// <para>
        /// <b>Counted rather than read off <see cref="Steps"/></b>, and that is not tidiness. A
        /// caller that wants the number and not the choreography passes no step list — the
        /// search does exactly that, hundreds of thousands of times — and deriving this from the
        /// list would answer nought for every one of them. It did, and the first thing that
        /// noticed was the test that asked whether a burst had happened at all.
        /// </para>
        /// </summary>
        public readonly int Waves;

        /// <summary>
        /// Cells this drop destroyed, over every wave - motes that burst and glass that fired
        /// alike, because both are things the well had to be rid of. Counted for
        /// <see cref="Waves"/>' reason.
        /// </summary>
        public readonly int Burst;

        /// <summary>
        /// The waves in order, for a screen that has to play them a beat apart. Empty when the
        /// caller asked for none — see <see cref="Waves"/>.
        /// </summary>
        public readonly IReadOnlyList<FallStep> Steps;

        static readonly FallStep[] None = new FallStep[0];

        public FallResolution(int column, int row, int colour, bool enriched,
                              int waves, int burst, IReadOnlyList<FallStep> steps)
        {
            Column = column;
            Row = row;
            Colour = colour;
            Enriched = enriched;
            Waves = waves;
            Burst = burst;
            Steps = steps ?? None;
        }
    }

    /// <summary>
    /// One wave: what burst, what glass fired, where its light travelled, what that changed -
    /// motes washed and glass charged - and what fell afterwards.
    ///
    /// <para>
    /// <b>Every one of these is a position on the board <em>before</em> the wave was applied.</b>
    /// That is the whole contract with the view: the model settles the entire cascade before a
    /// frame is drawn, so a screen that re-read the live board mid-wave would draw the finished
    /// well behind a burst that has not happened. Budburst shipped exactly that bug — an effect
    /// that asked the settled board which neighbour was bare and fired out of cells that had
    /// never held anything — and this is the shape that makes it unrepresentable.
    /// </para>
    /// </summary>
    public readonly struct FallStep
    {
        /// <summary>Cells that reached white and were destroyed, at the positions they stood in.</summary>
        public readonly IReadOnlyList<int> Burst;

        /// <summary>Cells the light reached and changed, at the positions they stood in.</summary>
        public readonly IReadOnlyList<int> Washed;

        /// <summary>
        /// Lenses that filled up and fired, which are gone with the bursts.
        ///
        /// Separate from <see cref="Burst"/> because they are a different event to watch, and by
        /// some distance the bigger one: a mote bursting is light being spent, and a lens firing
        /// is three drops of charge going off at once in four directions. Drawing the two the
        /// same way would throw away the moment the chapter is built around.
        /// </summary>
        public readonly IReadOnlyList<int> Fired;

        /// <summary>
        /// Lenses that took a channel this wave without filling up.
        ///
        /// <b>The half a view would otherwise have no way to show.</b> Charging is most of what
        /// the player is doing - three drops for one shot - and if only the shot were reported
        /// the two drops before it would land on the board as nothing at all. It is what the
        /// gauge on the glass is drawn from.
        /// </summary>
        public readonly IReadOnlyList<int> Charged;

        /// <summary>
        /// What each cell of <see cref="Washed"/> was actually handed, in the same order.
        ///
        /// <b>A wave no longer carries one colour, which is why this exists.</b> A burst washes
        /// the drop's colour and a lens throws white, so a view that painted every washed cell in
        /// the drop's colour would draw a mote about to pop as one that had merely improved — the
        /// single most misleading thing this board could say. A cell reached by both takes both,
        /// so this is a mask rather than a channel.
        /// </summary>
        public readonly IReadOnlyList<int> WashedWith;

        /// <summary>
        /// What each lens of <see cref="Charged"/> was actually handed, in the same order. White
        /// when a beam struck it, which is a lens filling in one step rather than in one channel.
        /// </summary>
        public readonly IReadOnlyList<int> ChargedWith;

        /// <summary>
        /// Every beam thrown this wave, in the order the lenses were read: two per lens that was
        /// charged the ordinary way and four per lens another lens struck. Empty on a wave where
        /// none fired, which is most waves and every wave of the first chapter.
        ///
        /// A beam's light is always <see cref="Energy.All"/>, so what it lands on is completed
        /// rather than improved. <see cref="FallBeam.From"/> is what groups them by lens.
        /// </summary>
        public readonly IReadOnlyList<FallBeam> Beams;

        /// <summary>Which wave of this drop's chain, counting from one.</summary>
        public readonly int Wave;

        /// <summary>What slid where once the gaps opened.</summary>
        public readonly IReadOnlyList<FallMove> Moved;

        public FallStep(IReadOnlyList<int> burst, IReadOnlyList<int> fired,
                        IReadOnlyList<int> washed, IReadOnlyList<int> charged,
                        IReadOnlyList<int> washedWith, IReadOnlyList<int> chargedWith,
                        IReadOnlyList<FallBeam> beams, int wave, IReadOnlyList<FallMove> moved)
        {
            Burst = burst;
            Fired = fired;
            Washed = washed;
            Charged = charged;
            WashedWith = washedWith;
            ChargedWith = chargedWith;
            Beams = beams;
            Wave = wave;
            Moved = moved;
        }

        /// <summary>
        /// What the cell at <paramref name="index"/> of <see cref="Washed"/> took, falling back to
        /// <paramref name="fallback"/> when a caller built a step without the figures.
        ///
        /// Every step the board writes carries them; this exists so a fixture that constructs one
        /// by hand does not have to.
        /// </summary>
        public int WashGain(int index, int fallback)
            => WashedWith != null && index < WashedWith.Count ? WashedWith[index] : fallback;

        /// <summary>What the lens at <paramref name="index"/> of <see cref="Charged"/> took.</summary>
        public int ChargeGain(int index, int fallback)
            => ChargedWith != null && index < ChargedWith.Count ? ChargedWith[index] : fallback;
    }

    /// <summary>A mote sliding from one cell to another as the stack falls.</summary>
    public readonly struct FallMove
    {
        public readonly int From, To;
        public FallMove(int from, int to) { From = from; To = to; }
    }
}
