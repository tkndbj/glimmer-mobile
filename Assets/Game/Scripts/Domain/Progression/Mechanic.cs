using System.Collections.Generic;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// One thing a player has to be taught once.
    ///
    /// <para>
    /// The id is permanent and travels in the save file, exactly like a level id: it
    /// records that a particular person has already been shown a particular idea, and
    /// renaming one would re-teach the whole player base something they know. Add
    /// freely, never rename, never reuse.
    /// </para>
    /// <para>
    /// Its strings are derived from the id — <c>ui.tip.&lt;id&gt;.title</c> and
    /// <c>.body</c> — for the same reason a level's are: anything holding a mechanic
    /// can name it without a lookup table to keep in step.
    /// </para>
    /// </summary>
    public readonly struct Mechanic
    {
        public readonly string Id;

        Mechanic(string id) => Id = id;

        public static readonly Mechanic FragileConduit = new Mechanic("fragile");
        public static readonly Mechanic MoveBudget = new Mechanic("moves");
        public static readonly Mechanic RootedTile = new Mechanic("rooted");

        /// <summary>Two heart colours, and a critter that wants them blended.</summary>
        public static readonly Mechanic ColourMixing = new Mechanic("mixing");

        /// <summary>A creature the light must never reach.</summary>
        public static readonly Mechanic Duskcap = new Mechanic("duskcap");

        /// <summary>Conduits sharing a taproot, which turn as one however far apart they are.</summary>
        public static readonly Mechanic BoundConduit = new Mechanic("bound");

        /// <summary>A conduit carrying two flows that pass through one another and never meet.</summary>
        public static readonly Mechanic Crossing = new Mechanic("crossing");

        /// <summary>A conduit with two of its four ways thorned shut, and one tap swaps which.</summary>
        public static readonly Mechanic Briar = new Mechanic("briar");

        // ------------------------------------------------------------- screens
        // Four things a glade board cannot teach, because they are not on one. They ride this type
        // rather than a parallel one because everything about a lesson is already here and
        // already stored: the id is permanent, the strings derive from it, and TipLedger is a
        // union-joined set that reaches the cloud with no new field. A second "thing to teach
        // once" type would be a second ledger, a second merge rule and a second save field,
        // for two strings and a ring.
        //
        // They are deliberately absent from TeachingOrder, which is the *board* scan's queue —
        // see the remarks there. A screen tip is raised by the screen that owns it, because
        // nothing about a board implies the player has opened the Grovement, and nothing about
        // a glade implies they have ever met a second mode.

        /// <summary>
        /// Lightweave's rule: join every pair, and never let two channels cross.
        ///
        /// <para>
        /// A board very nearly shows this on its own — a refused drag says "not here" the first
        /// time a finger tries to cut across somebody — but "nearly" is doing a lot of work in a
        /// mode a player meets after four chapters of tapping tiles, where the very first thing
        /// they must know is that this one is dragged rather than tapped. Two sentences before
        /// the first grove costs a few seconds once in a lifetime.
        /// </para>
        /// <para>
        /// <b>The retired id here is <c>weave_fill</c>, and it must never be reused.</b> It
        /// taught the mode's old win condition: every critter awake <em>and</em> no bare ground
        /// left anywhere. That rule is gone — it was invisible on the board, it made the sensible
        /// route almost always wrong, and the state it produced (every critter awake, nothing
        /// happening) was reported from play as a bug and was indistinguishable from one. What
        /// replaced it is <see cref="WeaveBead"/>, which asks for the same thinking and can be
        /// pointed at. A lesson id travels in the save file exactly like a level id, so the old
        /// one stays spent for ever rather than being re-pointed at a rule it never described.
        /// </para>
        /// </summary>
        public static readonly Mechanic WeaveJoin = new Mechanic("weave_join");

        /// <summary>
        /// A bead: a cell one channel must be threaded through, and no other channel may enter.
        ///
        /// <para>
        /// The half of it a board cannot show is which half it is being. A ring in a colour is
        /// plainly <em>something</em>, and a player meeting one will read it either as a place to
        /// go or as a thing to avoid — and both readings are correct, for different colours, at
        /// the same time. That is not something to be discovered by losing a run to it.
        /// </para>
        /// </summary>
        public static readonly Mechanic WeaveBead = new Mechanic("weave_bead");

        /// <summary>What the Grovement is, shown once on the player's first visit.</summary>
        public static readonly Mechanic Grove = new Mechanic("grove");

        /// <summary>Where the things a grove is built from are bought.</summary>
        public static readonly Mechanic GroveShop = new Mechanic("grove_shop");

        /// <summary>
        /// Teaching order, most disruptive first.
        ///
        /// Only one tip is ever shown on entering a glade — two modal lessons before a
        /// player has touched anything is a tutorial, not a hint. When a glade brings
        /// several ideas at once this decides which gets the moment, and the rest wait
        /// for a later glade that has them.
        ///
        /// A glade may teach more than one thing; they are shown in this order, one
        /// after another, rather than the rest waiting for a later glade that happens
        /// to repeat them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two duskcap-era entries sit where they do for one reason. A duskcap changes what
        /// winning <em>is</em>, and a player who has not been told cannot work it out — the
        /// glade simply refuses to settle with every critter awake. A taproot announces itself
        /// the first time it is tapped, because two tiles visibly move. So the rule nothing on
        /// screen can explain goes first.
        /// </para>
        /// <para>
        /// A crossing sits between them by the same measure. It cannot be worked out but it
        /// can be <em>misread</em>, which is worse than not knowing: a tile with four arms is a
        /// crossroads everywhere else in this game, so a player who has not been told does not
        /// discover a new rule, they conclude the board is broken. It goes after the duskcap
        /// because a duskcap can lose a run and a misread crossing only costs turns.
        /// </para>
        /// <para>
        /// A briar sits directly after the crossing, and for the same reason one notch weaker.
        /// It is the other tile here that wears four arms and is not a crossroads, so it is
        /// misread in exactly the way a crossing is — but a briar shows its own rule, because
        /// the thorns are drawn across the ways they have closed and the light stops at them
        /// while the player watches. What it still cannot show is that the thorns *move*, and
        /// that is what the lesson is for.
        /// </para>
        /// </remarks>
        public static readonly Mechanic[] TeachingOrder =
        {
            FragileConduit, MoveBudget, RootedTile, ColourMixing, Duskcap, Crossing, Briar,
            BoundConduit,
        };

        /// <summary>
        /// Every lesson that exists, board and screen alike.
        ///
        /// <para>
        /// It is what the build gate walks to prove each one has its two strings, and that is
        /// the whole reason it exists separately from <see cref="TeachingOrder"/>: a mechanic
        /// added without them compiles, validates and ships, and the first player to reach it
        /// reads <c>ui.tip.grove.title</c> off the screen. That check used to walk the
        /// teaching order, which was the same list until a lesson appeared that no board can
        /// bring — after which the order would have quietly stopped being the set of
        /// everything, and the check with it.
        /// </para>
        /// </summary>
        public static readonly Mechanic[] All =
        {
            FragileConduit, MoveBudget, RootedTile, ColourMixing, Duskcap, Crossing, Briar,
            BoundConduit, WeaveJoin, WeaveBead, Grove, GroveShop,
        };

        public bool IsValid => !string.IsNullOrEmpty(Id);

        public string TitleKey => "ui.tip." + Id + ".title";
        public string BodyKey => "ui.tip." + Id + ".body";

        public bool Equals(Mechanic other) => string.Equals(Id, other.Id, System.StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Mechanic m && Equals(m);
        public override int GetHashCode() => Id == null ? 0 : Id.GetHashCode();
        public override string ToString() => Id;
    }

    /// <summary>Where a mechanic can be pointed at on the board. -1 when it has no home.</summary>
    public readonly struct MechanicSighting
    {
        public readonly Mechanic Mechanic;

        /// <summary>The cell to ring, or -1 for a rule that lives off the board.</summary>
        public readonly int CellIndex;

        public MechanicSighting(Mechanic mechanic, int cellIndex)
        {
            Mechanic = mechanic;
            CellIndex = cellIndex;
        }

        public bool HasCell => CellIndex >= 0;
    }

    /// <summary>
    /// Reads a board and reports which ideas it contains.
    ///
    /// Derived from the board rather than declared per level, which is the whole point:
    /// a chapter shipped a year from now that happens to use brittle conduits gets its
    /// tip with no authoring, no list to update and nothing to forget. It also means a
    /// tip can never point at a mechanic a level does not actually have.
    ///
    /// It reads a built <see cref="Puzzle"/> rather than a definition so it costs
    /// nothing extra — the board is already parsed by the time anybody asks.
    /// </summary>
    public static class MechanicScan
    {
        public static List<MechanicSighting> InBoard(Puzzle board)
        {
            var found = new List<MechanicSighting>();
            if (board == null) return found;

            int fragile = -1, rooted = -1, blended = -1, duskcap = -1, bound = -1, crossing = -1;
            int briar = -1;

            for (int i = 0; i < board.C.Length; i++)
            {
                var cell = board.C[i];

                if (cell.fragile > 0 && fragile < 0) fragile = i;
                if (cell.locked && rooted < 0) rooted = i;
                if (cell.kind == Kind.Duskcap && duskcap < 0) duskcap = i;
                if (cell.kind == Kind.Crossing && crossing < 0) crossing = i;
                if (cell.kind == Kind.Briar && briar < 0) briar = i;

                // Asked of the board rather than of the cell, because a rune only one
                // conduit carries binds nothing — the validator refuses that level, and
                // pointing a lesson at it would teach a rule the board does not follow.
                if (bound < 0 && board.IsBound(i)) bound = i;

                // A critter asking for more than one channel is the only proof that
                // blending is actually required here — two heart colours on their own
                // may just as well mean "keep these apart".
                if (cell.kind == Kind.Lamp && cell.colour != 0 &&
                    (cell.colour & (cell.colour - 1)) != 0 && blended < 0) blended = i;
            }

            if (fragile >= 0) found.Add(new MechanicSighting(Mechanic.FragileConduit, fragile));

            // The budget has no cell to ring — it lives in the counter at the top.
            if (board.HasBudget) found.Add(new MechanicSighting(Mechanic.MoveBudget, -1));

            if (rooted >= 0) found.Add(new MechanicSighting(Mechanic.RootedTile, rooted));
            if (blended >= 0) found.Add(new MechanicSighting(Mechanic.ColourMixing, blended));
            if (duskcap >= 0) found.Add(new MechanicSighting(Mechanic.Duskcap, duskcap));
            if (crossing >= 0) found.Add(new MechanicSighting(Mechanic.Crossing, crossing));
            if (briar >= 0) found.Add(new MechanicSighting(Mechanic.Briar, briar));
            if (bound >= 0) found.Add(new MechanicSighting(Mechanic.BoundConduit, bound));

            return found;
        }

        /// <summary>
        /// Every idea on this board the player has not met, in teaching order.
        ///
        /// A glade can bring two at once — a rooted tile and a blend, say — and holding
        /// the second back until some later glade repeats it means the player meets it
        /// unexplained in between. Shown one after another instead, which is a short
        /// queue rather than a wall of text: the list is empty on almost every glade
        /// after the first few.
        /// </summary>
        public static List<MechanicSighting> Unseen(Puzzle board, System.Func<Mechanic, bool> seen)
        {
            var present = InBoard(board);
            var queue = new List<MechanicSighting>();

            foreach (var candidate in Mechanic.TeachingOrder)
            {
                if (seen != null && seen(candidate)) continue;

                foreach (var sighting in present)
                    if (sighting.Mechanic.Equals(candidate)) { queue.Add(sighting); break; }
            }

            return queue;
        }
    }
}
