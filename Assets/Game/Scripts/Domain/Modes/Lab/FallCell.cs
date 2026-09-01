namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What may stand in a cell of a well: light, glass, a whorl, or nothing.
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
    /// thing up and then it goes off — which is why the lens needed no new rule taught, only a
    /// new consequence.
    /// </para>
    /// <para>
    /// <b>A whorl is the third kind, and it is the one this mode had to be argued into.</b> The
    /// third chapter shipped a <em>mirror</em> first and a <em>wick</em> second, and both were
    /// played and both came back as the same complaint: they were the lens again. A mirror only
    /// ever bent somebody else's beam, so on a board with no glass it did nothing at all. A wick
    /// held one authored colour and washed it into the four cells beside it when any light
    /// touched it — which is a <em>burst</em> with the colour changed, on an object with no
    /// decision in it: its colour was fixed by the author, its trigger was free, and the player
    /// never chose anything about it at all. See <see cref="Whorl"/> for what replaced them, and
    /// why it is a different kind of object rather than a stronger one.
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
        /// A whorl: a mouth in the well that, when light reaches it, draws the motes standing
        /// either side of it together and <b>mixes them into one</b>.
        ///
        /// <para>
        /// <b>It is the mode's own arithmetic applied to a pair of operands it never had.</b>
        /// Everything in Lightfall is <c>|</c> — a drop adds one channel to a mote, a wash adds
        /// one channel to a neighbour, a beam adds all three. In every one of those the second
        /// operand is a <em>colour</em>. A whorl is the only place two <em>motes</em> are ever
        /// combined, so a cyan and a red that would each have needed a drop of their own become
        /// one white on the spot. Nothing has to be taught for it: a player who has cooked a
        /// single mote already knows what yellow and blue make.
        /// </para>
        /// <para>
        /// <b>It pulls sideways, and that is a rule about gravity rather than a choice.</b> The
        /// well falls; the one direction nothing here ever travels in is across. A lens fires
        /// sideways for exactly this reason — up is the open air and down is the thing holding it
        /// up — and a whorl is that same observation turned into a verb. It is the only object in
        /// this mode that <em>moves</em> a mote, which is what makes it unmistakable on the
        /// board: two lights slide together and fuse.
        /// </para>
        /// <para>
        /// <b>What makes it hard is the pair, not the trigger.</b> Any light opens a whorl — a
        /// burst beside it, a beam, or a drop straight onto it — for the reason the wick was
        /// given that rule and the lens had one added after a player was stranded (invariant
        /// 26f): an object only a chain can reach is an object that can strand a well. The price
        /// is paid somewhere else entirely. What a whorl gives back is decided by <em>what is
        /// standing either side of it at the instant it turns</em>, and the well collapses under
        /// every chain — so the player is engineering two particular motes into two particular
        /// cells and then choosing the moment. A lens asks for three drops of three colours in
        /// any order at all; a whorl asks for one arrangement, which is a harder and a far more
        /// interesting thing to ask for.
        /// </para>
        /// <para>
        /// <b>It draws light and nothing else.</b> Glass beside a whorl stays where it is, and so
        /// does another whorl: pulling glass in would mean deciding what a lens and a mote mix
        /// into, and two whorls tugging at each other is a rule nobody can read off a board. A
        /// whorl that turns with nothing beside it simply closes and is gone, which is what keeps
        /// it removable and the well winnable.
        /// </para>
        /// <para>
        /// It holds no channels of its own — bits nought to two are always clear on one — so
        /// <see cref="Wants"/> is nought for it and it can never be enriched, charged or burst.
        /// </para>
        /// </summary>
        public const int Whorl = 16;

        /// <summary>
        /// A whorl that has caught the light and turns on the next wave.
        ///
        /// <para>
        /// A bit on the cell rather than a parallel array, so <c>Fork</c>, <c>Settle</c> and
        /// <c>Signature</c> carry it for nothing — which is the whole reason the lens's own
        /// "struck" flag was the awkward part of that mechanic. It is never authored: a board
        /// that begins turning is a board that rearranges itself before anybody has touched it.
        /// </para>
        /// </summary>
        public const int Lit = 32;

        /// <summary>
        /// A whorl, as authored. A spiral rather than a letter, because the one thing in this
        /// well that is not a colour must not look like one.
        /// </summary>
        public const char WhorlLetter = '@';

        /// <summary>
        /// The three digits a wick was authored with. <b>Retired, and refused by name.</b>
        ///
        /// A chapter file carrying one is content written for a build that no longer exists, and
        /// reading it as anything at all would put a cell on the board no rule here knows what to
        /// do with. Invariant 5f's rule for the duskcap, applied to the mechanic that this one
        /// replaced.
        /// </summary>
        public const string RetiredWickLetters = "123";

        /// <summary>Whether anything at all stands here.</summary>
        public static bool Occupied(int cell) => cell != Empty;

        /// <summary>Whether this is glass.</summary>
        public static bool IsLens(int cell) => (cell & Lens) != 0;

        /// <summary>Whether this is a whorl.</summary>
        public static bool IsWhorl(int cell) => (cell & Whorl) != 0;

        /// <summary>Whether this whorl has caught the light and turns on the next wave.</summary>
        public static bool IsLit(int cell) => (cell & Lit) != 0;

        /// <summary>
        /// Whether this is a mote of light — the only kind that can be enriched, burst, or drawn
        /// into a whorl.
        ///
        /// <b>Both other kinds are excluded, and each had to be added when it arrived.</b> Read
        /// as "occupied and not glass" this answers <c>true</c> for a whorl, which would let a
        /// wash pour colour into a cell that holds none, let a drop be swallowed by one, and let
        /// a whorl draw in another whorl.
        /// </summary>
        public static bool IsMote(int cell) => cell != Empty && (cell & (Lens | Whorl)) == 0;

        /// <summary>The channels a lens is holding. Nought for a mote and for bare ground.</summary>
        public static int Charge(int cell) => IsLens(cell) ? cell & Energy.All : Energy.None;

        /// <summary>
        /// The channels this cell still lacks before it goes off. Nought for bare ground and for
        /// a whorl, which never fills up — it opens, which is a different thing entirely.
        /// </summary>
        public static int Wants(int cell)
            => cell == Empty || IsWhorl(cell)
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
        /// is right for a mote, right for a lens and wrong for a whorl, which holds no channels
        /// at all and whose answer does not depend on the colour.
        /// </para>
        /// <para>
        /// <b>A drop opens an unlit whorl, whatever colour it is</b>, and that is a rule rather
        /// than a convenience: it is what stops a well ever becoming unwinnable. A whorl is only
        /// otherwise reached by a chain, and a player who cleared every mote around one would be
        /// left tapping at a board that could not be finished — which is exactly the state the
        /// lens shipped with and had to have a valve added for (invariant 26f). Here the valve is
        /// the rule from the start.
        /// </para>
        /// </summary>
        public static bool Takes(int cell, int colour)
        {
            if (cell == Empty) return false;
            if (IsWhorl(cell)) return !IsLit(cell);

            return (cell | colour) != cell;
        }

        /// <summary>
        /// The letter this cell is authored with. Light is upper case, glass is lower, and a
        /// whorl is a spiral — so a board says at a glance what is made of what.
        ///
        /// A whorl that has caught writes as an ordinary one: <see cref="Lit"/> is state rather
        /// than content, and it cannot be authored.
        /// </summary>
        public static char Letter(int cell)
        {
            if (cell == Empty) return '.';
            if (IsWhorl(cell)) return WhorlLetter;
            if (!IsLens(cell)) return Energy.Letter(cell);

            int charge = cell & Energy.All;
            return charge == Energy.None
                 ? LensLetter
                 : char.ToLowerInvariant(Energy.Letter(charge));
        }

        /// <summary>
        /// Reads an authored cell letter: everything <see cref="Energy"/> understands in upper
        /// case, the same set in lower case for glass already holding that much, the two ways of
        /// writing bare ground, and <see cref="WhorlLetter"/> for a whorl.
        ///
        /// <para>
        /// <b>Pre-charged glass is a chapter's difficulty dial, which is why it is authorable at
        /// all.</b> A drop's whole chain carries that drop's colour, so an empty lens needs three
        /// separate drops of three separate colours each engineered to burst beside it —
        /// measured, that leaves 7 boards in 90 solvable where two-thirds-full glass leaves 50.
        /// </para>
        /// <para>
        /// A whorl has no such dial and authors no state at all: what it gives back is decided by
        /// what the player has arranged beside it, which is the whole of the mechanic.
        /// </para>
        /// </summary>
        public static bool TryParse(char c, out int cell)
        {
            if (c == '.' || c == '-') { cell = Empty; return true; }
            if (c == LensLetter) { cell = Lens; return true; }
            if (c == WhorlLetter) { cell = Whorl; return true; }

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
    /// Two per lens charged the ordinary way and four per lens another lens struck. That is the
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

    /// <summary>
    /// One whorl turning: the motes it drew in, and the mote they became.
    ///
    /// <para>
    /// <b>It exists for <see cref="FallBeam"/>'s reason.</b> The model settles the whole cascade
    /// before a frame is drawn, so the board a screen can read holds the position the chain
    /// <em>ends</em> in — and a merge is two motes that were somewhere else a moment ago. Asked
    /// of the settled board, "which motes did this whorl take" has no answer at all: their cells
    /// are bare, and bare is also what a cell the author left empty looks like.
    /// </para>
    /// <para>
    /// <see cref="Into"/> is carried rather than derived from the two sources, because a view
    /// that OR-ed them itself would be a second copy of the one rule this object exists to
    /// perform (invariant 9a, at the smallest scale it appears at).
    /// </para>
    /// </summary>
    public readonly struct FallFuse
    {
        /// <summary>The whorl's own cell, where the merged mote comes to rest.</summary>
        public readonly int At;

        /// <summary>The cell to its left that was drawn in, or -1.</summary>
        public readonly int Left;

        /// <summary>The cell to its right that was drawn in, or -1.</summary>
        public readonly int Right;

        /// <summary>
        /// The mote the whorl leaves behind: the union of what it drew in. Nought when it drew in
        /// nothing at all, which is a whorl closing rather than a whorl merging.
        /// </summary>
        public readonly int Into;

        public FallFuse(int at, int left, int right, int into)
        {
            At = at;
            Left = left;
            Right = right;
            Into = into;
        }

        /// <summary>How many motes it drew in: nought, one, or two.</summary>
        public int Drawn => (Left >= 0 ? 1 : 0) + (Right >= 0 ? 1 : 0);

        /// <summary>
        /// Whether the merge itself completed a mote, which is the whole payoff and the one
        /// reading a board is authored against. See <c>FallBoard.Kindled</c>.
        /// </summary>
        public bool Kindled => Into == Energy.All;
    }
}
