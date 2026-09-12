using System;
using GlimmerGrove.Homestead;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One tile of the grove floor: the ground, and whatever is anchored on it.
    ///
    /// <para>
    /// Built once and rebound as the camera moves it across the field — see
    /// <see cref="GroveFieldView"/>. Everything that can differ between tiles is a field here
    /// rather than a fresh object, because the alternative is building and destroying a subtree
    /// per tile per pan, which is the cost that made a floor look impossible before culling
    /// existed.
    /// </para>
    /// <para>
    /// <b>Two nodes, one per layer.</b> The ground lives in the field's ground layer and the art
    /// in its piece layer, so no tile's skirt can paint over the base of a piece behind it — see
    /// the field for the report that bought this. A tile a footprint reaches over draws its
    /// ground and nothing else: the art is the anchor's.
    /// </para>
    /// <para>
    /// <b>It draws a tile and decides nothing about placing on one.</b> It used to be a nested
    /// class of <c>HomesteadScreen</c> holding a back-reference to it, which is how a screen
    /// becomes the only place a floor can be drawn: a second caller wanting the same tile — a
    /// visited grove, a bench — had to have the whole screen. What it genuinely needs from its
    /// owner is one question, so it is handed one function rather than a screen.
    /// </para>
    /// <para>
    /// <b>No mark on an empty tile.</b> Every buildable tile used to carry a breathing ring,
    /// which was right while tapping a tile was how the inventory opened: the ring was the
    /// invitation. Placing is begun from a button and aimed with a ghost now
    /// (<see cref="GroveDraftView"/>), so a ring on every empty tile would be a hundred and
    /// ninety-six invitations to a gesture that no longer does anything — and the breath under
    /// it was half of what "the tiles reload while I am building" was (invariant 16k).
    /// </para>
    /// </summary>
    public sealed class GroveTileCell : GroveFieldView.ITileCell
    {
        /// <summary>
        /// Asked once per bind: is this tile ground the player has just paid for that nobody has
        /// drawn arriving yet? It consumes the answer, so a tile rises once.
        /// </summary>
        readonly Func<int, int, bool> _arrived;

        readonly Image _ground, _art;

        /// <summary>
        /// Everything each node draws, held one level below it.
        ///
        /// <para>
        /// <b>The split exists so a tile can be moved without moving the tile.</b> The nodes'
        /// positions are the field's — written by <c>GroveFieldView</c> every time a cell is
        /// recycled onto new coordinates, and the pick box is derived from the same arithmetic.
        /// A rise animated on them would therefore be fighting the one transform that has to be
        /// authoritative, and a cell recycled mid-rise would drag its old destination onto its
        /// new tile. Animating a child means the offset is purely cosmetic and can be abandoned
        /// at any moment by writing two zeroes.
        /// </para>
        /// </summary>
        readonly RectTransform _body, _groundBody;

        bool _rising;
        int _riseCol, _riseRow;

        public RectTransform Ground { get; }

        public RectTransform Root { get; }

        public int Depth { get; private set; }

        public GroveTileCell(Func<int, int, bool> arrived)
        {
            _arrived = arrived;

            Ground = UIKit.Node("Tile", null);
            Ground.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

            _groundBody = UIKit.Node("B", Ground);

            _ground = UIKit.Img("G", _groundBody, null, Color.white,
                                new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                new Vector2(.5f, .5f), Vector2.zero);
            _ground.raycastTarget = false;
            _ground.preserveAspect = false;

            Root = UIKit.Node("Stand", null);
            Root.sizeDelta = new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight);

            _body = UIKit.Node("B", Root);

            _art = UIKit.Img("A", _body, null, Color.white, new Vector2(140f, 140f),
                             new Vector2(.5f, .5f), Vector2.zero);
            _art.preserveAspect = true;
            _art.raycastTarget = false;
        }

        public void Bind(int col, int row)
        {
            // A cell recycled onto another tile while it was still rising has to let go of the
            // rise, or the new tile inherits the old one's offset for the rest of it.
            if (_rising && (col != _riseCol || row != _riseRow)) EndRise();

            var catalog = HomesteadCatalog.Current;

            GroveTileArt.LayGround(_ground, catalog.Floor);

            // What this tile shows is whatever is anchored on it — the best home the player owns
            // on the hall, whatever they placed, or the starter companion on the one tile that
            // has one and has never been touched (see HomesteadLayout.Shown). A tile another
            // stand reaches over shows the ground and nothing else.
            var index = HomesteadLayout.Occupancy(catalog);
            bool anchored = index.TryAnchored(col, row, out var stand);

            var piece = anchored ? GroveTileArt.PieceOf(catalog, stand) : default;
            bool drawn = anchored && piece.IsValid;

            Depth = anchored ? stand.Depth : GroveFootprint.Single.Depth(col, row);

            _art.gameObject.SetActive(drawn);
            if (drawn) GroveTileArt.LayPiece(_art, piece, stand);

            // Ground the player has just paid for arrives out of the floor rather than switching
            // on. Asked last, so the tile is fully drawn before it starts moving.
            if (_arrived != null && _arrived(col, row)) BeginRise(col, row);
        }

        /// <summary>
        /// One tile of new ground travelling up into its place, overshooting a little as it
        /// lands. Both nodes travel together, so what stands on the tile rises with it.
        ///
        /// <para>
        /// On a channel so a second rise replaces the first rather than running beside it, and
        /// owned by the body so it dies with the cell. The overshoot is <c>OutBack</c> read
        /// unclamped — clamping it would flatten precisely the part of the motion that makes
        /// ground feel like it has weight.
        /// </para>
        /// </summary>
        void BeginRise(int col, int row)
        {
            _rising = true;
            _riseCol = col;
            _riseRow = row;

            Tween.Run(GroveGrowth.RiseSeconds, Ease.OutBack, t =>
            {
                var at = new Vector2(0f, Mathf.LerpUnclamped(-GroveRise.Lift, 0f, t));
                var scale = Vector3.one * Mathf.LerpUnclamped(GroveRise.RiseFrom, 1f, t);

                if (_body) { _body.anchoredPosition = at; _body.localScale = scale; }
                if (_groundBody) { _groundBody.anchoredPosition = at; _groundBody.localScale = scale; }
            }, _body, "rise").OnDone(EndRise);
        }

        void EndRise()
        {
            _rising = false;
            Tween.KillChannel(_body, "rise");

            if (_body) { _body.anchoredPosition = Vector2.zero; _body.localScale = Vector3.one; }
            if (_groundBody) { _groundBody.anchoredPosition = Vector2.zero; _groundBody.localScale = Vector3.one; }
        }
    }
}
