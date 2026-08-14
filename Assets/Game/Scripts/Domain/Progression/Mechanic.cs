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
        public static readonly Mechanic[] TeachingOrder =
        {
            FragileConduit, MoveBudget, RootedTile, ColourMixing,
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

            int fragile = -1, rooted = -1, blended = -1;

            for (int i = 0; i < board.C.Length; i++)
            {
                var cell = board.C[i];

                if (cell.fragile > 0 && fragile < 0) fragile = i;
                if (cell.locked && rooted < 0) rooted = i;

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
