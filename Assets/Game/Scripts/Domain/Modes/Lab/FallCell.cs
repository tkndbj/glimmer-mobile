namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What may stand in a cell of a well: light, glass, or nothing.
    ///
    /// <para>
    /// <b>A well's cell was an <see cref="Energy"/> mask and nothing else, and the lens is the
    /// first thing in this mode that is occupied without being made of light.</b> That
    /// distinction has to exist somewhere, and every other place it could have gone is worse. A
    /// parallel <c>bool[]</c> beside <c>_cells</c> would be a second array for <c>Fork</c>,
    /// <c>Settle</c> and <c>Signature</c> to keep in step, and the search forks hundreds of
    /// thousands of boards — one of those three forgetting it is a divergence nothing could see,
    /// because a board that settles differently still settles. A sentinel inside the colour mask
    /// would collide with <see cref="Energy.All"/> and burst.
    /// </para>
    /// <para>
    /// So a lens is a bit <em>above</em> the three channels, and it carries its charge in the
    /// three below. Everything that reads a cell for occupancy — gravity, the mote count, the
    /// brim, the fingerprint — asks the same "non-zero" question it always did and is correct
    /// with no change at all. Everything that reads a cell as <em>light</em> asks
    /// <see cref="IsMote"/>, and everything that reads it as glass asks <see cref="IsLens"/>.
    /// </para>
    /// <para>
    /// <b><see cref="Wants"/> is the one thing both kinds answer the same way, and that is the
    /// mechanic.</b> A mote wants what it needs to reach white and <em>burst</em>; a lens wants
    /// what it needs to reach white and <em>fire</em>. One sentence covers both — light fills a
    /// thing up and then it goes off — which is why glass needed no new rule taught, only a new
    /// consequence.
    /// </para>
    /// <para>
    /// <b>A mirror is the third kind, and it is the first cell here that is <em>none</em> of
    /// that.</b> It holds no light, wants nothing, takes nothing and can never be cooked; what
    /// it does is turn a beam ninety degrees and carry on. So it is a second bit above the
    /// channels rather than a value among them, for exactly the reason the lens is: a sentinel
    /// inside the mask would collide with a colour, and a parallel array would be a third thing
    /// <c>Fork</c>, <c>Settle</c> and <c>Signature</c> had to keep in step across hundreds of
    /// thousands of forked boards.
    /// </para>
    /// <para>
    /// <b>Two predicates had to be narrowed for it and both were silent failures.</b>
    /// <see cref="IsMote"/> was "occupied and not glass", which reads a mirror as a mote that
    /// wants all three — so a wash would have poured colour into it and a drop would have been
    /// absorbed by it, both of them changing a cell whose whole point is that nothing changes
    /// it. That is the same fault the lens laid on <c>Wanted</c> and <c>Enriches</c>, one
    /// mechanic later, which is why <see cref="Takes"/> now exists here rather than being spelt
    /// out at each of the three call sites that ask it.
    /// </para>
    /// </summary>
    public static class FallCell
    {
        /// <summary>Bare ground.</summary>
        public const int Empty = Energy.None;

        /// <summary>
        /// A lens: one bit above the three channels, so it is occupied and is not light.
        ///
        /// It can never equal <see cref="Energy.All"/>, which is what keeps "a mote that reached
        /// white bursts" correct for glass with no clause of its own — glass reaching white is
        /// <see cref="Full"/>, a different value and a different consequence.
        /// </summary>
        public const int Lens = 8;

        /// <summary>
        /// Glass holding all three, which is the state that fires.
        ///
        /// Never authored: <c>w</c> is refused at parse exactly as <c>W</c> is for a mote,
        /// because a board that goes off before anybody has touched it is a board whose author
        /// meant something else, and reading it as an opening cascade would hide the mistake
        /// behind a very pretty animation.
        /// </summary>
        public const int Full = Lens | Energy.All;

        /// <summary>Empty glass, as authored.</summary>
        public const char LensLetter = 'O';

        /// <summary>
        /// A mirror: the bit above <see cref="Lens"/>, so it is occupied, is not light, and is
        /// not glass.
        ///
        /// <para>
        /// It carries no channels at all — <see cref="Held"/> is nought for one and always will
        /// be — so it can never equal <see cref="Energy.All"/> or <see cref="Full"/> and the two
        /// "has this reached white" tests in <c>FallBoard.Resolve</c> are correct for it with no
        /// clause of their own. A mirror never goes off; it is spent by turning somebody else's
        /// light.
        /// </para>
        /// </summary>
        public const int Mirror = 16;

        /// <summary>
        /// Which way a mirror leans: set for <c>/</c>, clear for <c>\</c>.
        ///
        /// A bit rather than two unrelated values so that <see cref="IsMirror"/> is one mask
        /// test, and above the channels so that <see cref="Held"/> keeps answering nought.
        /// </summary>
        public const int Tilt = 32;

        /// <summary>A mirror leaning <c>/</c>: light arriving from the left leaves upward.</summary>
        public const int Fore = Mirror | Tilt;

        /// <summary>A mirror leaning <c>\</c>: light arriving from the left leaves downward.</summary>
        public const int Back = Mirror;

        /// <summary>The two letters a mirror is authored with, and they are the two it draws as.</summary>
        public const char ForeLetter = '/', BackLetter = '\\';

        /// <summary>Whether anything at all stands here.</summary>
        public static bool Occupied(int cell) => cell != Empty;

        /// <summary>Whether this is glass.</summary>
        public static bool IsLens(int cell) => (cell & Lens) != 0;

        /// <summary>Whether this is a mirror — the one thing here that light passes through.</summary>
        public static bool IsMirror(int cell) => (cell & Mirror) != 0;

        /// <summary>
        /// Whether this is a mote of light — the only kind that can be enriched or burst.
        ///
        /// <b>Both other kinds are excluded and the second one had to be added.</b> This read
        /// "occupied and not glass", which answers <c>true</c> for a mirror — so a mirror would
        /// have been washed by a burst beside it and would have taken a drop that landed on it,
        /// on the one cell in this well that nothing is allowed to change.
        /// </summary>
        public static bool IsMote(int cell) => cell != Empty && (cell & (Lens | Mirror)) == 0;

        /// <summary>
        /// The channels standing here, whether they are a mote's or a lens's charge. Nought for
        /// a mirror, which holds none and never will — its bits are above the mask.
        /// </summary>
        public static int Held(int cell) => cell & Energy.All;

        /// <summary>The channels a lens is holding. Nought for a mote and for bare ground.</summary>
        public static int Charge(int cell) => IsLens(cell) ? cell & Energy.All : Energy.None;

        /// <summary>
        /// The channels this cell still lacks before it goes off. Nought for bare ground.
        ///
        /// See the remarks on the class: this is deliberately the same question for both kinds,
        /// because it is the same question.
        /// </summary>
        public static int Wants(int cell)
            => cell == Empty || IsMirror(cell)
             ? Energy.None
             : Energy.All & ~(cell & Energy.All);

        /// <summary>
        /// Whether this cell would <em>take</em> a drop of this colour rather than let it come
        /// to rest above and the stack grow a row.
        ///
        /// <para>
        /// <b>The one question three callers ask, said once here rather than three times
        /// there.</b> <c>FallBoard.Landing</c>, <c>FallBoard.Takes</c> and the view's ghost all
        /// need it, and the version spelt out at each of them — <c>(cell | colour) != cell</c> —
        /// is right for a mote, right for a lens and <em>wrong for a mirror</em>, which holds no
        /// channels and so appears to lack every one of them. A drop would have been swallowed
        /// by the glass wedge and turned it into something no rule here has a name for.
        /// </para>
        /// <para>
        /// The lens paid for this lesson already: <c>Enriches</c> is <c>IsMote(...) &amp;&amp;
        /// ...</c>, so it answers false for a charging drop, and standing in for this question
        /// it left a pane hanging in the air over an emptied column. A question with three
        /// answers asked as three predicates is one some caller will ask a third of.
        /// </para>
        /// </summary>
        public static bool Takes(int cell, int colour)
            => cell != Empty && !IsMirror(cell) && (cell | colour) != cell;

        /// <summary>
        /// Turns a beam on this mirror: the way in, and the way out.
        ///
        /// <para>
        /// <b>It lives here because it exists three times.</b> <c>FallBoard.Shoot</c> is what
        /// ships, <c>FallSolver.Blast</c> reads where a lens is pointing for an author, and
        /// <c>Tools/verify/fall.py</c> mirrors both offline — and the two in this assembly must
        /// not be two transcriptions of one diagram (invariant 9a). The Python copy is pinned to
        /// this one by <c>fall-vectors.json</c>.
        /// </para>
        /// <para>
        /// Cell space counts rows <em>downward</em>, which is the whole of what makes this easy
        /// to get backwards. A <c>/</c> leans from the low left to the high right, so light
        /// travelling right (<c>+1,0</c>) leaves upward (<c>0,-1</c>): that is <c>(-dy,-dx)</c>.
        /// A <c>\</c> leans the other way and is <c>(dy,dx)</c>. Both are their own inverse, so
        /// a beam retracing its path leaves the way it came — which is the only reason the guard
        /// against a beam chasing its own tail is a guard rather than a mechanic.
        /// </para>
        /// </summary>
        public static void Turn(int cell, int dx, int dy, out int ndx, out int ndy)
        {
            if ((cell & Tilt) != 0) { ndx = -dy; ndy = -dx; }     // '/'
            else                    { ndx = dy;  ndy = dx;  }     // ''
        }

        /// <summary>
        /// The letter this cell is authored with. Light is upper case and glass is lower, so a
        /// board reads at a glance as what is made of what.
        /// </summary>
        public static char Letter(int cell)
        {
            if (cell == Empty) return '.';
            if (IsMirror(cell)) return (cell & Tilt) != 0 ? ForeLetter : BackLetter;
            if (!IsLens(cell)) return Energy.Letter(cell);

            int charge = cell & Energy.All;
            return charge == Energy.None
                 ? LensLetter
                 : char.ToLowerInvariant(Energy.Letter(charge));
        }

        /// <summary>
        /// Reads an authored cell letter: everything <see cref="Energy"/> understands in upper
        /// case, the same set in lower case for glass already holding that much, and the two
        /// ways of writing bare ground.
        ///
        /// <para>
        /// <b>Pre-charged glass is the chapter's difficulty dial, which is why it is authorable
        /// at all.</b> A drop's whole chain carries that drop's colour, so an empty lens needs
        /// three separate drops of three separate colours each engineered to burst beside it —
        /// measured, that leaves 7 boards in 90 solvable where two-thirds-full glass leaves 50.
        /// The charge is therefore how much a board asks, and it ramps across a chapter the way
        /// mote count and headroom do.
        /// </para>
        /// </summary>
        public static bool TryParse(char c, out int cell)
        {
            if (c == '.' || c == '-') { cell = Empty; return true; }
            if (c == LensLetter) { cell = Lens; return true; }
            if (c == ForeLetter) { cell = Fore; return true; }
            if (c == BackLetter) { cell = Back; return true; }

            bool glass = c >= 'a' && c <= 'z';
            char letter = glass ? char.ToUpperInvariant(c) : c;

            if (Energy.TryParse(letter, out int mask) && mask != Energy.None)
            {
                cell = glass ? Lens | mask : mask;
                return true;
            }

            cell = Empty;
            return false;
        }
    }

    /// <summary>
    /// One beam of light thrown by a lens that filled up, from the glass to whatever stopped it.
    ///
    /// <para>
    /// <b>It exists so the view can draw the shot where and when it happened.</b> A drop settles
    /// the whole cascade before a single frame is drawn (<c>FallRun.Drop</c>), so the board the
    /// screen can read carries no time at all — and a beam is the one thing in this mode whose
    /// whole point is that it <em>travelled</em>. Budburst paid for this lesson twice by asking
    /// the settled board which neighbour was bare and drawing lightning out of cells that had
    /// never held anything: the model has to say what happened, or the view will invent it.
    /// </para>
    /// <para>
    /// <b>Two per lens charged the ordinary way and four per lens another lens struck</b>, plus
    /// one more segment for every mirror any of them turns on. That is the payoff for charging
    /// it, and it is what makes the shot worth stopping the board for.
    /// </para>
    /// </summary>
    public readonly struct FallBeam
    {
        /// <summary>
        /// The cell the light leaves: the lens that fired, or the mirror that turned it.
        ///
        /// <b>A bend is two segments rather than a bent one</b>, which is what lets a view that
        /// knows how to draw a straight shot draw a ricochet with no new machinery — and is why
        /// <see cref="Leg"/> exists to say which is which.
        /// </summary>
        public readonly int From;

        /// <summary>Which way it goes. One of the four; never diagonal, never zero.</summary>
        public readonly int Dx, Dy;

        /// <summary>
        /// How many cells it crossed. As many as the well is wide for one that leaves it, and the
        /// endpoint is then one cell outside the wall — which is exactly where it should go out.
        /// </summary>
        public readonly int Steps;

        /// <summary>
        /// The cell that stopped it, or -1 when it left the well.
        ///
        /// A hit is not the same as a change: a mote that already holds the colour absorbs the
        /// beam and takes nothing, which is the clause that makes what is standing in the line a
        /// decision rather than scenery. Both are drawn, because the player has to be able to see
        /// the difference.
        /// </summary>
        public readonly int Hit;

        /// <summary>
        /// How many mirrors this light had already turned on before this segment began. Nought
        /// for the shot leaving the lens.
        ///
        /// <para>
        /// The view spends it as a delay, so a bend is watched as light arriving at the mirror
        /// and then leaving it rather than as two beams drawn at once — which is the whole of
        /// what makes a ricochet read as one travelling thing.
        /// </para>
        /// </summary>
        public readonly int Leg;

        /// <summary>
        /// Whether <see cref="Hit"/> is a mirror this shot turned on rather than something it
        /// spent itself against.
        ///
        /// <para>
        /// <b>Both are a hit and only one is an arrival</b>, which the view cannot work out for
        /// itself: it draws a shockwave where light lands, and a shockwave on a mirror would say
        /// the shot stopped there. Asking the live board instead would be asking a board that
        /// has already settled — the fault this whole step structure exists to make
        /// unrepresentable.
        /// </para>
        /// </summary>
        public readonly bool Turned;

        public FallBeam(int from, int dx, int dy, int steps, int hit,
                        int leg = 0, bool turned = false)
        {
            From = from;
            Dx = dx;
            Dy = dy;
            Steps = steps;
            Hit = hit;
            Leg = leg;
            Turned = turned;
        }

        /// <summary>Whether it reached something rather than leaving the well.</summary>
        public bool Landed => Hit >= 0;

        /// <summary>Whether it spent itself on what it reached, which a turn does not.</summary>
        public bool Absorbed => Hit >= 0 && !Turned;
    }
}
