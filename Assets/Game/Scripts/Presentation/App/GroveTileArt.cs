using GlimmerGrove.Homestead;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// How a tile's ground and the thing standing on it are laid out — the one copy of the
    /// arithmetic the player's grove, a visited grove and the pick boxes all read.
    ///
    /// <para>
    /// <b>Two screens draw a floor and one function lays it out.</b> <c>HomesteadScreen</c> and
    /// <c>GroveVisitScreen</c> each held a cell with its own copy of "size the art, lift it,
    /// mirror it", and <c>Tools/render_grove.py</c> holds a third in Python. Three copies of a
    /// layout agree until one is edited, and the failure is a visited grove that is not the
    /// grove its owner sees. So the numbers live here, and the renderer pins them.
    /// </para>
    /// <para>
    /// <b>A piece is drawn from its anchor's cell at its footprint's centre.</b> The cell sits
    /// on the anchor tile because that is the tile the field positions; a two-wide piece is
    /// then offset to the middle of the tiles it covers, which for an even side is the point
    /// between two tiles — legal, see <c>GroveFloor.TileX</c>. Depth is the footprint's, so
    /// the field sorts the cell as if it stood on its front tile.
    /// </para>
    /// </summary>
    public static class GroveTileArt
    {
        /// <summary>
        /// Art pixels to floor pixels for a piece standing on a tile.
        ///
        /// One number for the whole field rather than a scale per slot, which is what the
        /// islands had. A slot's scale existed to compose a fixed picture — front and centre
        /// bigger than back and left — and on a field every tile is the same distance from the
        /// eye, so the only honest scale is the one that makes a piece the right size against
        /// a tile. What varies is the piece's own <c>Scale</c>, which is a fact about the thing
        /// rather than about where it stands.
        /// </summary>
        public const float PieceScale = 1.15f;

        /// <summary>
        /// Lays the ground sprite so its top face lands on the tile's point.
        ///
        /// The ground is a block, not a flat lozenge: its side wall is painted below the top
        /// face, so the sprite hangs by half the skirt. Derived from the art — see
        /// <see cref="HomesteadArt.TileDraw"/>.
        /// </summary>
        public static void LayGround(Image ground, GroveFloor floor)
        {
            if (ground == null) return;

            var rt = (RectTransform)ground.transform;
            rt.sizeDelta = HomesteadArt.TileDraw(floor, out float drop);
            rt.anchoredPosition = new Vector2(0f, -drop);

            ground.sprite = HomesteadArt.Tile(floor);
            ground.color = Color.white;
        }

        /// <summary>
        /// Where a stand's art is centred, relative to its anchor tile's point, before zoom.
        /// The footprint's centre plus the piece's own lift. Y grows upward.
        /// </summary>
        public static Vector2 Offset(HomesteadPiece piece, GroveStand stand, Vector2 size)
        {
            float dx = GroveFloor.TileX(stand.CentreCol, stand.CentreRow)
                     - GroveFloor.TileX(stand.AnchorCol, stand.AnchorRow);
            float dy = GroveFloor.TileY(stand.CentreCol, stand.CentreRow)
                     - GroveFloor.TileY(stand.AnchorCol, stand.AnchorRow);

            return new Vector2(dx, -dy + size.y * (piece.IsValid ? piece.Lift : 0f));
        }

        /// <summary>
        /// Sizes, places, paints and faces a stand's art on the image its anchor cell holds.
        ///
        /// The facing is written on every call rather than only when it is mirrored: cells are
        /// pooled and rebound as the camera pans, so a scale left behind by a flipped fence
        /// would be inherited by whatever tile reused the object.
        /// </summary>
        public static void LayPiece(Image art, HomesteadPiece piece, GroveStand stand)
        {
            if (art == null) return;

            var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
            var rt = (RectTransform)art.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = Offset(piece, stand, size);

            HomesteadArt.Paint(art, piece);

            art.transform.localScale = new Vector3(stand.Flipped && !stand.IsHall ? -1f : 1f, 1f, 1f);
        }

        /// <summary>
        /// The box a stand's art covers in field space, with the piece's mask — what
        /// <see cref="GrovePick"/> tests a tap against. The same size and the same offset
        /// <see cref="LayPiece"/> lays the art out with, so the box is the sprite's own
        /// rectangle rather than an approximation of it.
        /// </summary>
        public static GroveHit Hit(HomesteadPiece piece, GroveStand stand)
        {
            if (!piece.IsValid || !stand.IsValid)
                return new GroveHit(stand.AnchorCol, stand.AnchorRow, 0f, 0f, 0f, 0f);

            var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
            var offset = Offset(piece, stand, size);

            float cx = GroveFloor.TileX(stand.AnchorCol, stand.AnchorRow) + offset.x;
            float cy = -GroveFloor.TileY(stand.AnchorCol, stand.AnchorRow) + offset.y;

            return new GroveHit(stand.AnchorCol, stand.AnchorRow, cx, cy, size.x * .5f, size.y * .5f,
                                stand.Depth, piece.Hit, stand.Flipped && !stand.IsHall);
        }

        /// <summary>
        /// How far a piece can reach beyond its tile, in floor pixels: up the screen and to
        /// either side. The field widens its culling window by this so a tall tree whose
        /// tile is just below the viewport does not pop in and out at the edge.
        ///
        /// Read off the whole catalog once rather than off what is placed, because the answer
        /// changes when the player places something and the window must already be wide
        /// enough when it does.
        /// </summary>
        public static void Reach(HomesteadCatalog catalog, out float up, out float side)
        {
            up = GroveFloor.TileHeight;
            side = GroveFloor.TileWidth * .5f;
            if (catalog == null) return;

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsValid) continue;

                var size = HomesteadArt.SizeOnFloor(piece, PieceScale);
                float lift = piece.Lift;

                // The top of the art above the anchor, plus the width of the widest footprint's
                // centre offset; a two-deep piece is drawn a tile further down than its anchor.
                float top = size.y * lift + size.y * .5f
                          + GroveFloor.TileHeight * (piece.Footprint.Cols + piece.Footprint.Rows - 2) * .5f;
                float half = size.x * .5f
                           + GroveFloor.TileWidth * (Mathf.Max(piece.Footprint.Cols, piece.Footprint.Rows) - 1) * .5f;

                if (top > up) up = top;
                if (half > side) side = half;
            }
        }
    }
}
