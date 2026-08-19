namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// What sort of thing a piece is, which is now a <em>shop</em> fact rather than a placement
    /// rule.
    ///
    /// <para>
    /// <b>This used to decide where a piece could stand and no longer does.</b> On the islands
    /// every slot was authored, so a slot could have a role — the rim took fences, the back took
    /// trees — and that rule is what stopped a pre-placed sprinkle of dots looking accidental.
    /// The floor removes the dots: every tile is identical and empty, and the composition is the
    /// thing the player makes rather than the thing they fill in. Freedom is the feature there,
    /// so a piece fits anywhere except the hall's own tile.
    /// </para>
    /// <para>
    /// The kind survives because the <em>shop</em> still needs it: it is what a shelf is (see
    /// <see cref="GroveShelf"/>), which is a tab, an asset scope and a browse atlas. So it went
    /// from a rule the player could hit to a way of finding things, which is where it belonged.
    /// </para>
    /// </summary>
    public enum HomesteadSlotKind
    {
        /// <summary>Open ground: rocks, logs, crates, anything that just sits there.</summary>
        Ground,

        /// <summary>
        /// The hall's own tile. Never placed into — see
        /// <see cref="HomesteadLedger.BestDwelling"/>, which draws the best home the player owns.
        /// </summary>
        Hearth,

        /// <summary>A built thing that anchors a stretch of floor: a well, a cave mouth, a spire.</summary>
        Structure,

        /// <summary>Planted ground: flowers, sprouts, bushes.</summary>
        Bed,

        /// <summary>A step of a route. Laid in chains so a path leads somewhere.</summary>
        Path,

        /// <summary>Fences, signs and lanterns.</summary>
        Edge,

        /// <summary>Drawn tall: trees and anything that towers.</summary>
        Canopy,
    }

    /// <summary>
    /// One tile of the floor: a place something can stand, and the key it is stored under.
    ///
    /// <para>
    /// <b>Slots are derived now, not authored.</b> An island listed its slots by hand, each with
    /// a position, a scale and a kind — so adding somewhere to put a bench was a content edit.
    /// A floor has a slot at every tile, so this is a value computed from a pair of coordinates
    /// rather than a row in a file, and the catalog carries none of them.
    /// </para>
    /// <para>
    /// What did not change is the only part that reaches disk: the <see cref="Id"/>. It is a
    /// permanent name under invariant 1 — never renamed, never reused — which is why it is
    /// built from <em>absolute</em> floor coordinates and why the floor may only ever grow right
    /// and down. See <see cref="GroveFloor.TileId"/>.
    /// </para>
    /// </summary>
    public readonly struct HomesteadSlot
    {
        /// <summary>The tile's permanent id, as written into the save file.</summary>
        public readonly string Id;

        /// <summary>Position on the floor, in absolute tile coordinates.</summary>
        public readonly int Col, Row;

        public HomesteadSlot(int col, int row)
        {
            Col = col;
            Row = row;
            Id = GroveFloor.TileId(col, row);
        }

        public HomesteadSlot(string id, int col, int row)
        {
            Id = id;
            Col = col;
            Row = row;
        }

        public bool IsValid => !string.IsNullOrEmpty(Id);

        /// <summary>How far down the screen this tile draws. See <see cref="GroveFloor.DrawOrder"/>.</summary>
        public int Depth => GroveFloor.DrawOrder(Col, Row);

        public override string ToString() => Id ?? "(none)";
    }
}
