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

        /// <summary>Whether anything at all stands here.</summary>
        public static bool Occupied(int cell) => cell != Empty;

        /// <summary>Whether this is glass.</summary>
        public static bool IsLens(int cell) => (cell & Lens) != 0;

        /// <summary>Whether this is a mote of light — the only kind that can be enriched or burst.</summary>
        public static bool IsMote(int cell) => cell != Empty && (cell & Lens) == 0;

        /// <summary>The channels standing here, whether they are a mote's or a lens's charge.</summary>
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
            => cell == Empty ? Energy.None : Energy.All & ~(cell & Energy.All);

        /// <summary>
        /// The letter this cell is authored with. Light is upper case and glass is lower, so a
        /// board reads at a glance as what is made of what.
        /// </summary>
        public static char Letter(int cell)
        {
            if (cell == Empty) return '.';
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
    /// <b>There are always four of them per firing lens, one per direction.</b> That is the
    /// payoff for charging it, and it is what makes the shot worth stopping the board for.
    /// </para>
    /// </summary>
    public readonly struct FallBeam
    {
        /// <summary>The cell the light leaves: the lens that fired.</summary>
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

        public FallBeam(int from, int dx, int dy, int steps, int hit)
        {
            From = from;
            Dx = dx;
            Dy = dy;
            Steps = steps;
            Hit = hit;
        }

        /// <summary>Whether it landed on something rather than leaving the well.</summary>
        public bool Landed => Hit >= 0;
    }
}
