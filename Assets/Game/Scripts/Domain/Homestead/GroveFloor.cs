using System;
using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// One rectangle of the floor, sold as a unit.
    ///
    /// <para>
    /// <b>Land is bought in regions rather than by the tile, and that is a save-file decision
    /// before it is a design one.</b> What the player owns has to be an entitlement — a set of
    /// permanent ids joined by union, invariant 15 — because a <em>count</em> of tiles owned is
    /// exactly the shape invariant 11b forbids. Per tile that set would grow to several hundred
    /// short strings on a filled floor, uploaded and merged on every sync. Per region it stays
    /// at a dozen or so, for ever.
    /// </para>
    /// <para>
    /// It is also the better shop. Buying a single square is a transaction; buying a stretch of
    /// land is a visible jump in the size of the place you are building, which is the thing the
    /// player is actually paying for.
    /// </para>
    /// <para>
    /// A region's <em>boundaries</em> are content and may be retuned. Its tiles are not named
    /// after it — see <see cref="GroveFloor.TileId"/> — so moving a boundary changes which
    /// region you must buy to reach a tile and never changes what is standing on it.
    /// </para>
    /// </summary>
    public sealed class GroveRegion
    {
        /// <summary>
        /// Permanent id. It is written into the save file as an entitlement, so invariant 1
        /// applies in full: never renamed, never reused, never derived from position.
        /// </summary>
        public readonly string Id;

        /// <summary>Column and row of this region's top-left tile, in absolute floor coordinates.</summary>
        public readonly int Col, Row;

        public readonly int Cols, Rows;

        /// <summary>
        /// Credits that buy this region, or zero when it is not sold for credits — either free
        /// from the first launch, or priced in <see cref="Gems"/>.
        ///
        /// Zero rather than a separate flag for <c>HomesteadPiece.Cost</c>'s reason:
        /// <c>JsonUtility</c> writes a zero into every field an older file never had, so
        /// "absent" and "not for credits" have to be the same fact or a catalog written before
        /// this existed would give away the whole floor.
        /// </summary>
        public readonly int Cost;

        /// <summary>
        /// Gems that buy this region instead, or zero. Exactly one of the two prices is set —
        /// <c>ContentValidation</c> refuses a region carrying both.
        ///
        /// <para>
        /// <b>Nothing here converts one into the other, and that is deliberate.</b> The one
        /// place a credit figure is read off a region is the grove's worth (invariant 16g), and
        /// gem-priced land is worth nothing there — see <c>GroveRegionDto.gems</c> for why the
        /// leaderboard cannot price a gem. Every sum on both sides already asks for
        /// <see cref="Cost"/> above zero, so it falls out rather than needing a clause.
        /// </para>
        /// </summary>
        public readonly int Gems;

        /// <summary>
        /// This region's rung on the ladder, counting from 1, or zero on starter land.
        ///
        /// Authored rather than derived from price, because the floor is sold in two currencies
        /// and because a retune must never reorder a ladder a player is part-way up. See
        /// <c>GroveRegionDto.order</c>.
        /// </summary>
        public readonly int Order;

        public GroveRegion(string id, int col, int row, int cols, int rows,
                           int cost, int gems = 0, int order = 0)
        {
            Id = id;
            Col = Math.Max(0, col);
            Row = Math.Max(0, row);
            Cols = Math.Max(0, cols);
            Rows = Math.Max(0, rows);
            Cost = Math.Max(0, cost);
            Gems = Math.Max(0, gems);
            Order = Math.Max(0, order);
        }

        public bool IsValid => !string.IsNullOrEmpty(Id) && Cols > 0 && Rows > 0;

        /// <summary>
        /// True for land nothing gates — what a new player builds on.
        ///
        /// <b>Both prices, not just credits.</b> This used to read <c>Cost &lt;= 0</c>, which was
        /// the whole rule while credits were the only currency and becomes "every gem-priced
        /// region is free" the moment they are not. It gates the ladder, the shop shelf, the
        /// hall's ground and what is written into the save, so the narrow reading would have
        /// handed over half the floor at launch.
        /// </summary>
        public bool IsStarter => Cost <= 0 && Gems <= 0;

        /// <summary>Whether this stretch is sold for gems rather than credits.</summary>
        public bool IsGemPriced => Gems > 0;

        /// <summary>
        /// Which wallet buys this region. Meaningless on starter land.
        ///
        /// Named for the question rather than for the type, because a property called
        /// <c>Currency</c> would shadow <see cref="GlimmerGrove.Persistence.Currency"/> inside
        /// this class and every use of the constants would have to be qualified to compile.
        /// </summary>
        public string PaidIn
            => IsGemPriced ? Persistence.Currency.Gems : Persistence.Currency.Credits;

        /// <summary>
        /// What it costs, in whichever currency it is sold in. A caller drawing a price needs
        /// this and <see cref="Currency"/> together and must never pick one field by hand —
        /// reading <see cref="Cost"/> alone on a gem region prints a free stretch of land.
        /// </summary>
        public int Price => IsGemPriced ? Gems : Cost;

        public int TileCount => Cols * Rows;

        public bool Holds(int col, int row)
            => col >= Col && col < Col + Cols && row >= Row && row < Row + Rows;

        /// <summary>
        /// A region's name is a pure function of its id, with no override — the rule every
        /// named thing in this project is under (invariant 5a).
        /// </summary>
        public string NameKey => "ui.land." + Id;

        public override string ToString() => Id ?? "(none)";
    }

    /// <summary>
    /// The ground the grove is built on: one isometric tile field, and which parts of it are
    /// for sale.
    ///
    /// <para>
    /// <b>This replaces the floating islands.</b> The islands were a ladder of fixed
    /// compositions — an author placed every slot, gave it a kind, and the player chose which
    /// of eleven pre-placed dots got which sticker. A floor inverts that: every tile is
    /// identical and empty, and the composition is the thing the player makes. That is more
    /// freedom rather than less, which is why the slot-kind rule went with the islands (see
    /// <see cref="HomesteadPiece.Fits"/>) — it existed to stop a sprinkle of dots looking
    /// accidental, and there are no dots any more.
    /// </para>
    /// <para>
    /// <b>Nothing about it reaches the save file except tile ids.</b> A tile is a slot, and
    /// <see cref="HomesteadLayout"/> is unchanged: a map from slot id to what stands there,
    /// where an untouched tile has no row at all. So an empty three-hundred-tile floor costs
    /// nothing, and a floor with two things on it costs two rows.
    /// </para>
    /// <para>
    /// <b>Tile ids are absolute floor coordinates, not region-relative.</b> That is what makes
    /// a region's boundary a tuneable number rather than a permanent one: re-drawing the map of
    /// what is for sale changes which region a tile belongs to and never changes the tile's
    /// name, so nothing anybody placed moves. The consequence to respect is the other half of
    /// it — <b>the floor may only ever grow right and down.</b> Inserting a column at the left
    /// would renumber every tile in the world, which is invariant 1 in its most literal form.
    /// </para>
    /// </summary>
    public sealed class GroveFloor
    {
        public static readonly GroveFloor Empty =
            new GroveFloor(0, 0, string.Empty, string.Empty, string.Empty, Array.Empty<GroveRegion>());

        readonly GroveRegion[] _regions;
        readonly Dictionary<string, GroveRegion> _byId;

        /// <summary>How many tiles across and deep the whole field is.</summary>
        public readonly int Cols, Rows;

        /// <summary>Art key for one floor tile, relative to <c>Art/</c>.</summary>
        public readonly string TileArt;

        /// <summary>
        /// The tile the grove hall stands on, and the one tile nothing can be placed into.
        ///
        /// The hearth's rule, moved onto the floor: the hall is <em>drawn</em> from the best
        /// dwelling the player owns rather than placed by hand, because a home somebody has to
        /// remember to put down is a home they can buy and not see.
        /// </summary>
        public readonly string HallTile;

        /// <summary>
        /// Where the starter companion stands until the player moves them.
        ///
        /// <b>Shown, never stored.</b> Writing a placement at first launch would be a stored
        /// default, which is precisely what invariant 11c forbids: a fresh install would stamp
        /// it with <em>now</em>, outrank a device where the player had cleared that tile, and
        /// put the companion back. So the tile simply draws the starter when nothing has ever
        /// been placed on it, exactly as <c>Wallet</c> shows a default name it never writes.
        /// Clearing it is a real instruction and does get a row.
        /// </summary>
        public readonly string StarterTile;

        /// <summary>
        /// The tiles the hall covers, anchored at <see cref="HallTile"/>.
        ///
        /// <para>
        /// A fact about the <em>floor</em> rather than about whichever dwelling stands there,
        /// deliberately: the home ladder is bought rung by rung, and if a manor covered more
        /// ground than the cabin, buying one would evict whatever the player had planted beside
        /// their door. So every dwelling authors the same footprint, <c>ContentValidation</c>
        /// refuses one that does not, and the tiles a home takes are fixed for the life of the
        /// grove.
        /// </para>
        /// </summary>
        public readonly GroveFootprint HallFootprint;

        readonly int _hallCol, _hallRow;
        readonly bool _hasHall;

        public GroveFloor(int cols, int rows, string tileArt, string hallTile, string starterTile,
                          IReadOnlyList<GroveRegion> regions)
            : this(cols, rows, tileArt, hallTile, starterTile, regions, GroveFootprint.Single)
        {
        }

        public GroveFloor(int cols, int rows, string tileArt, string hallTile, string starterTile,
                          IReadOnlyList<GroveRegion> regions, GroveFootprint hallFootprint)
        {
            Cols = Math.Max(0, cols);
            Rows = Math.Max(0, rows);
            TileArt = tileArt ?? string.Empty;
            HallTile = hallTile ?? string.Empty;
            StarterTile = starterTile ?? string.Empty;
            HallFootprint = hallFootprint.Cols < 1 ? GroveFootprint.Single : hallFootprint;
            _hasHall = TryParse(HallTile, out _hallCol, out _hallRow);

            if (regions == null || regions.Count == 0)
            {
                _regions = Array.Empty<GroveRegion>();
            }
            else
            {
                _regions = new GroveRegion[regions.Count];
                for (int i = 0; i < regions.Count; i++) _regions[i] = regions[i];
            }

            _byId = new Dictionary<string, GroveRegion>(_regions.Length, StringComparer.Ordinal);
            foreach (var region in _regions)
                if (region != null && region.IsValid) _byId[region.Id] = region;
        }

        public bool IsEmpty => Cols <= 0 || Rows <= 0;

        public int TileCount => Cols * Rows;

        public IReadOnlyList<GroveRegion> Regions => _regions;

        public GroveRegion Region(string id)
            => _byId.TryGetValue(id ?? string.Empty, out var region) ? region : null;

        // -------------------------------------------------------------- tile ids
        /// <summary>Prefix on every tile id. Short, because it is a key in the save file.</summary>
        public const string TilePrefix = "t_";

        /// <summary>
        /// The permanent id of one tile.
        ///
        /// Zero-padded to three digits so ids are fixed-length and sort in a stable order —
        /// <see cref="SaveDelta"/> walks the placement rows in order, and an ordering that
        /// changed with the size of a number would make an unchanged save look changed. Three
        /// digits caps the floor at 1000×1000, which is four orders of magnitude more than any
        /// grove will ever be.
        /// </summary>
        public static string TileId(int col, int row) => $"{TilePrefix}{col:000}_{row:000}";

        /// <summary>The tile an id names, or false when the string is not a tile id at all.</summary>
        public static bool TryParse(string tileId, out int col, out int row)
        {
            col = row = 0;
            if (string.IsNullOrEmpty(tileId)) return false;
            if (!tileId.StartsWith(TilePrefix, StringComparison.Ordinal)) return false;

            int split = tileId.IndexOf('_', TilePrefix.Length);
            if (split < 0) return false;

            return int.TryParse(tileId.Substring(TilePrefix.Length, split - TilePrefix.Length), out col)
                && int.TryParse(tileId.Substring(split + 1), out row);
        }

        public bool Contains(int col, int row)
            => col >= 0 && row >= 0 && col < Cols && row < Rows;

        public bool Contains(string tileId)
            => TryParse(tileId, out int col, out int row) && Contains(col, row);

        /// <summary>
        /// True for any tile the hall covers. Nothing else may go there.
        ///
        /// Asked of the footprint rather than of the one anchor tile, because the hall is two
        /// tiles wide and a fence planted under its eaves was the first thing every tester did.
        /// </summary>
        public bool IsHall(string tileId)
            => TryParse(tileId, out int col, out int row) && IsHall(col, row);

        public bool IsHall(int col, int row)
            => _hasHall && HallFootprint.Holds(_hallCol, _hallRow, col, row);

        /// <summary>True for the hall's anchor tile itself — the one its dwelling is drawn from.</summary>
        public bool IsHallAnchor(int col, int row) => _hasHall && col == _hallCol && row == _hallRow;

        /// <summary>The hall as a stand, for an occupancy index. Invalid when the floor has no hall.</summary>
        public GroveStand HallStand(string dwellingId)
            => _hasHall && !string.IsNullOrEmpty(dwellingId)
                ? new GroveStand(_hallCol, _hallRow, dwellingId, false, HallFootprint, true)
                : default;

        /// <summary>
        /// The piece the starter tile shows while nothing has been placed on it.
        ///
        /// <b>Derived from the roster, not authored.</b> It is whichever companion nothing
        /// gates — <c>AvatarCatalog.Starter</c>, the one every account already holds — so a
        /// content drop that changes who a new player begins with moves the friend standing
        /// beside the hall with it, and there is no second place naming somebody that a retune
        /// could put out of step.
        /// </summary>
        public string StarterPiece
            => string.IsNullOrEmpty(StarterTile)
                ? string.Empty
                : GroveResidents.PieceId(Progression.AvatarCatalog.Starter.Id);

        /// <summary>The starter piece if this is the starter tile, or empty. See <see cref="StarterPiece"/>.</summary>
        public string StarterPieceOn(string tileId)
            => !string.IsNullOrEmpty(StarterTile)
            && string.Equals(tileId, StarterTile, StringComparison.Ordinal)
                ? StarterPiece
                : string.Empty;

        /// <summary>
        /// Whether the hall stands inside this region — the one tile it holds that nothing can
        /// be placed on, and therefore the one that must not count towards how full it is.
        /// </summary>
        public bool HallIsIn(GroveRegion region)
            => HallTilesIn(region) > 0;

        /// <summary>How many of a region's tiles the hall covers — the ones that can never be filled.</summary>
        public int HallTilesIn(GroveRegion region)
        {
            if (region == null || !region.IsValid || !_hasHall) return 0;

            int count = 0;
            for (int c = 0; c < HallFootprint.Cols; c++)
                for (int r = 0; r < HallFootprint.Rows; r++)
                    if (region.Holds(_hallCol + c, _hallRow + r)) count++;

            return count;
        }

        /// <summary>The region a tile belongs to, or null for ground nobody sells.</summary>
        public GroveRegion RegionOf(int col, int row)
        {
            foreach (var region in _regions)
                if (region.IsValid && region.Holds(col, row)) return region;

            return null;
        }

        public GroveRegion RegionOf(string tileId)
            => TryParse(tileId, out int col, out int row) ? RegionOf(col, row) : null;

        // ------------------------------------------------------------- geometry
        /// <summary>
        /// How wide one tile is drawn, in the floor's own pixels. Height is half of it —
        /// see <see cref="TileHeight"/>.
        /// </summary>
        public const float TileWidth = 220f;

        /// <summary>
        /// How tall a tile's <em>top face</em> is as a fraction of its width — the vertical step
        /// of the grid.
        ///
        /// <para>
        /// <b>It is not 0.5, and assuming it was is what stopped the floor tessellating.</b>
        /// "Isometric" is loosely used to mean a 2:1 grid, and this art pack is not drawn that
        /// way: measured off the shipped tile, the face is 199 wide by 112 tall — 0.5628. A grid
        /// stepped by half the width therefore overlapped every tile by six pixels vertically,
        /// which reads as a floor that will not line up with itself no matter how the tiles are
        /// nudged.
        /// </para>
        /// <para>
        /// A constant here rather than a number in the content, because it is a property of the
        /// <em>pack</em> — every prop, every plot and every tile in it was drawn to the same
        /// projection, so a floor that disagreed with it would put every object standing on it
        /// at an angle its own art denies. And it is checked rather than trusted:
        /// <c>ContentValidation</c> measures the shipped tile's alpha and fails the build if the
        /// art and this number disagree, which is the only way a constant describing a picture
        /// stays true. That is <c>HomesteadMap</c>'s old lesson — the number a layout must agree
        /// with lives in a PNG nobody can see from the code.
        /// </para>
        /// </summary>
        public const float TileFaceRatio = .5628f;

        /// <summary>The vertical step of the grid. See <see cref="TileFaceRatio"/>.</summary>
        public const float TileHeight = TileWidth * TileFaceRatio;

        /// <summary>
        /// Where a tile's centre sits in floor space, measured from the field's own origin.
        ///
        /// The standard isometric transform: a step along a column moves right and down, a step
        /// along a row moves left and down. Y grows downward, which is the convention every
        /// other measured thing in this project uses.
        ///
        /// <para>
        /// <b>Fractional coordinates are legal and that is deliberate.</b> Every caller that
        /// names a tile passes whole numbers, but three things want the points <em>between</em>
        /// them: the corners of a region's outline, which sit half a tile outside its edge
        /// tiles; a camera easing from one place to another; and the centre of a rectangle with
        /// an even number of tiles in it, which is not a tile. Widening the parameters is what
        /// stops each of those growing its own copy of the transform, which is the mistake
        /// <c>Puzzle.Alike</c> and <c>TweenCycle</c> both exist to record.
        /// </para>
        /// </summary>
        public static float TileX(float col, float row) => (col - row) * TileWidth * .5f;

        public static float TileY(float col, float row) => (col + row) * TileHeight * .5f;

        /// <summary>
        /// How far left of the origin the field reaches — the bottom-left corner of the diamond.
        /// </summary>
        public float MinX => -(Rows - 1) * TileWidth * .5f - TileWidth * .5f;

        public float MaxX => (Cols - 1) * TileWidth * .5f + TileWidth * .5f;

        public float MaxY => (Cols + Rows - 2) * TileHeight * .5f + TileHeight;

        /// <summary>The whole field's bounding box, which is what the camera is clamped to.</summary>
        public float Width => MaxX - MinX;

        public float Height => MaxY;

        /// <summary>
        /// Draw order for anything standing on a tile: nearer the viewer is later.
        ///
        /// <para>
        /// <b>This is the one thing a tile floor needs that the islands did not.</b> An island
        /// authored its slots in draw order and a human checked it looked right. On a field the
        /// player fills themselves, a tree on one tile has to be drawn behind whatever stands
        /// on the tile in front of it, whichever pieces those turn out to be — and the answer
        /// for an isometric grid is simply how far down the screen the tile is, which is
        /// <c>col + row</c>. Ties are broken by column so the order is total and stable.
        /// </para>
        /// </summary>
        public static int DrawOrder(int col, int row) => (col + row) * 1024 + col;

        /// <summary>
        /// The tile under a point in floor space, whether or not the field has one there.
        ///
        /// The inverse of <see cref="TileX"/>/<see cref="TileY"/>, which is what turns a tap
        /// into a tile. Rounded rather than floored: the transform puts a tile's <em>centre</em>
        /// at the computed point, so the nearest whole step is the tile the finger is on.
        /// </summary>
        public static void TileAt(float x, float y, out int col, out int row)
        {
            float c = (y / TileHeight) + (x / TileWidth);
            float r = (y / TileHeight) - (x / TileWidth);

            col = (int)Math.Round(c);
            row = (int)Math.Round(r);
        }
    }
}
