using System;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// One shelf of the grove: a shop tab, an art scope, and a browse atlas — the same
    /// division three times, deliberately expressed once.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> The shop used to page by <see cref="HomesteadSlotKind"/>,
    /// which was nearly right and wrong in one place: a resident fits every slot, so
    /// <c>Fits</c> put the whole roster on every tab and the whole roster in every tab's asset
    /// scope. The fix is not a special case in the shop — it is admitting that "what a tab
    /// shows" is its own idea. A slot kind answers <em>where may this stand</em>; a shelf
    /// answers <em>where is this sold</em>, and the two agree for decor and differ for the two
    /// kinds that are not decor.
    /// </para>
    /// <para>
    /// <b>It is one concept because three things must agree about it.</b> The tab that draws
    /// a shelf, the atlas that packs its thumbnails and the scope that loads that atlas are
    /// three separate mechanisms keyed on the same division — and a second copy of the
    /// division is a second answer for a drop to put out of step with the first. That is
    /// <c>HomesteadCatalog.Emblem</c>'s argument, applied before the mistake rather than
    /// after it.
    /// </para>
    /// <para>
    /// The order here is the order the tabs are drawn. Residents lead because the top of the
    /// page should be the part that is <em>about</em> the player rather than about their
    /// balance, and the home trails because it is a ladder rather than a browse.
    /// </para>
    /// </summary>
    public enum GroveShelf
    {
        /// <summary>The companions, who are also this grove's residents. See <see cref="GroveResidents"/>.</summary>
        Residents = 0,

        Structure = 1,
        Canopy = 2,
        Bed = 3,
        Edge = 4,
        Path = 5,
        Ground = 6,

        /// <summary>The home ladder. One shelf, and the only one that is never placed by hand.</summary>
        Home = 7,

        /// <summary>
        /// Ground to build on. The only shelf that does not sell <em>pieces</em>.
        ///
        /// <para>
        /// It is a shelf because that is where a player looks for something to buy. Expansion
        /// used to be sold by tapping locked ground on the grove itself, which meant the screen
        /// had to draw the land you did not own — a wall of padlocks around a small lit patch,
        /// which is the opposite of what a screen about the place you built is for. The floor
        /// now shows only what you own, and the ground you could add is in the shop with
        /// everything else you could add.
        /// </para>
        /// <para>
        /// It has no browse atlas: a region is a rectangle rather than an object, and a
        /// thumbnail of a patch of grass is a picture of nothing. Its cells and its tab draw
        /// <c>Art.IsoTile</c>, generated, for the reason every other always-drawn shape here is.
        /// </para>
        /// </summary>
        Land = 8,
    }

    /// <summary>Which shelf a piece is sold on, and what each shelf is made of.</summary>
    public static class GroveShelves
    {
        /// <summary>Every shelf, in the order a shop draws them.</summary>
        public static readonly GroveShelf[] All =
        {
            GroveShelf.Residents,
            GroveShelf.Structure,
            GroveShelf.Canopy,
            GroveShelf.Bed,
            GroveShelf.Edge,
            GroveShelf.Path,
            GroveShelf.Ground,
            GroveShelf.Home,
            GroveShelf.Land,
        };

        /// <summary>
        /// The shelf a piece belongs on.
        ///
        /// Kind decides it for the two kinds that are not decor, and the slot kind decides it
        /// for decor — which is the whole content of this type. A decor piece whose slot kind
        /// this build does not know reads as ground, exactly as <c>HomesteadMapper</c> reads
        /// it, so a shop tab can never be a place a piece disappears into.
        /// </summary>
        public static GroveShelf Of(HomesteadPiece piece)
        {
            if (!piece.IsValid) return GroveShelf.Ground;
            if (piece.IsResident) return GroveShelf.Residents;
            if (piece.IsDwelling) return GroveShelf.Home;

            return Of(piece.Slot);
        }

        /// <summary>The shelf a slot kind's decor is sold on.</summary>
        public static GroveShelf Of(HomesteadSlotKind slot)
        {
            switch (slot)
            {
                case HomesteadSlotKind.Structure: return GroveShelf.Structure;
                case HomesteadSlotKind.Canopy: return GroveShelf.Canopy;
                case HomesteadSlotKind.Bed: return GroveShelf.Bed;
                case HomesteadSlotKind.Edge: return GroveShelf.Edge;
                case HomesteadSlotKind.Path: return GroveShelf.Path;
                case HomesteadSlotKind.Hearth: return GroveShelf.Home;
                default: return GroveShelf.Ground;
            }
        }

        /// <summary>
        /// A stable, file-safe name for a shelf: the key an atlas address and a scope are
        /// built from.
        ///
        /// Written out rather than taken from <c>ToString</c>, because these reach an asset
        /// address and an asset address is a permanent name — renaming the enum member must
        /// not silently orphan a bundle somebody already shipped.
        /// </summary>
        public static string Key(GroveShelf shelf)
        {
            switch (shelf)
            {
                case GroveShelf.Residents: return "residents";
                case GroveShelf.Structure: return "structure";
                case GroveShelf.Canopy: return "canopy";
                case GroveShelf.Bed: return "bed";
                case GroveShelf.Edge: return "edge";
                case GroveShelf.Path: return "path";
                case GroveShelf.Home: return "home";
                case GroveShelf.Land: return "land";
                default: return "ground";
            }
        }

        /// <summary>
        /// Whether a shelf has a browse atlas of its own.
        ///
        /// Every shelf that sells pieces does. Land does not, because a region is a rectangle
        /// rather than an object — there is nothing to photograph. The generator, the audit and
        /// the runtime scope all ask this rather than each carrying its own exception.
        /// </summary>
        public static bool HasAtlas(GroveShelf shelf) => shelf != GroveShelf.Land;

        /// <summary>The shelf a key names, or <see cref="GroveShelf.Ground"/>.</summary>
        public static GroveShelf FromKey(string key)
        {
            foreach (var shelf in All)
                if (string.Equals(Key(shelf), key, StringComparison.OrdinalIgnoreCase)) return shelf;

            return GroveShelf.Ground;
        }

        /// <summary>
        /// The loc key naming a shelf. Written out per member for invariant 6's reason: a key
        /// built by concatenation is a key the build gate's scan cannot see.
        /// </summary>
        public static string NameKey(GroveShelf shelf)
        {
            switch (shelf)
            {
                case GroveShelf.Residents: return "ui.shelf.residents";
                case GroveShelf.Structure: return "ui.shelf.structure";
                case GroveShelf.Canopy: return "ui.shelf.canopy";
                case GroveShelf.Bed: return "ui.shelf.bed";
                case GroveShelf.Edge: return "ui.shelf.edge";
                case GroveShelf.Path: return "ui.shelf.path";
                case GroveShelf.Home: return "ui.shelf.home";
                case GroveShelf.Land: return "ui.shelf.land";
                default: return "ui.shelf.ground";
            }
        }
    }
}
