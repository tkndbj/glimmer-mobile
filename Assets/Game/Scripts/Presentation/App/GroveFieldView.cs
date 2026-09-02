using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The camera over the grove floor: drag to move, pinch to zoom, and only the tiles you can
    /// see actually exist.
    ///
    /// <para>
    /// <b>Why this is not a <c>ScrollRect</c>.</b> Unity's scroll view moves one axis at a time
    /// against a fixed content size, which is right for a list and wrong for a field — a floor
    /// is panned in both directions at once and its content size changes with the zoom. It also
    /// resolves its position against bounds it recomputes in its own <c>LateUpdate</c>, which
    /// this project has already been caught by once (see <c>HomesteadScreen</c>'s parking latch
    /// and <c>GridView.Show</c>). Everything here is set directly.
    /// </para>
    /// <para>
    /// <b>Culling is not an optimisation here, it is the feature.</b> A field is hundreds of
    /// tiles and a phone shows a few dozen; building the whole floor would be thousands of
    /// objects for a screen that can draw one screenful, and it would get worse every time the
    /// floor grew. Tiles are realised as they come into view and returned to a pool as they
    /// leave, which is <see cref="GridView"/>'s bargain in two dimensions.
    /// </para>
    /// <para>
    /// <b>The ground is one layer and everything standing on it is another.</b> A cell used to
    /// hold its tile and its piece together, sorted as one — so the tile in front, painted
    /// later, painted its top face and its skirt over the base of whatever stood behind it.
    /// Every piece drawn standing <em>on</em> the ground rather than floating above its point
    /// lost its feet: the cottage's plinth, a log, every path. Reported from play as objects
    /// "buried in the tiles". The ground plane is flat and nothing on it can be behind it, so
    /// all of it is drawn first and every piece is drawn over all of it; a cell now owns a
    /// node in each layer, and the field positions and sorts both.
    /// </para>
    /// <para>
    /// <b>Pinch needs multi-touch, which the game turns off.</b> See
    /// <see cref="View.WantsMultiTouch"/> — the grove declares it and <see cref="Flow"/> applies
    /// it, so no screen has to remember to put it back.
    /// </para>
    /// </summary>
    public sealed class GroveFieldView : MonoBehaviour, IDragHandler, IBeginDragHandler,
                                         IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>
        /// How long a finger has to rest on a tile before it counts as a long press.
        ///
        /// <para>
        /// Long enough that it cannot be reached by a tap somebody meant as a tap — the floor's
        /// ordinary gesture is a tap, and the cost of firing this by accident is a panel
        /// appearing over the thing the player was looking at. Short enough that it is
        /// discoverable by holding, which is the only way anybody finds a long press.
        /// </para>
        /// </summary>
        public const float HoldSeconds = .45f;

        /// <summary>
        /// How far the finger may travel and still be resting rather than panning.
        ///
        /// The same threshold that separates a pan from a tap, and deliberately the same
        /// number: a press that has moved far enough not to be a tap has not moved far enough
        /// to be something else as well.
        /// </summary>
        const float PressSlop = 12f;

        /// <summary>
        /// How far out and in the player may zoom, and where the camera opens.
        ///
        /// <para>
        /// The tile is the target, and the band is set by what a thumb can hit. At the old
        /// opening zoom of .7 a tile's diamond was 154 by 87 canvas units — under a quarter of
        /// an inch tall on a phone — and the floor could be pulled out to .45, where nothing on
        /// it was a target at all. Reported as "hard to tap on some tiles" and "make tiles
        /// bigger". So the floor opens at <see cref="DefaultZoom"/>, a tile 187 wide and 105
        /// tall; the far end of the pinch stops at <see cref="MinZoom"/>, where a tile is
        /// still a fingertip; and the near end goes past one, because zooming in to place
        /// something precisely is a real need and a little softness in the art at the closest
        /// zoom is a fair price for it.
        /// </para>
        /// </summary>
        public const float MinZoom = .55f;
        public const float MaxZoom = 1.2f;
        public const float DefaultZoom = .85f;

        /// <summary>Tiles realised beyond each edge of the viewport, on top of <see cref="SetReach"/>.</summary>
        const int Overscan = 1;

        RectTransform _viewport, _field, _groundLayer, _pieceLayer;
        GroveFloor _floor;

        Func<int, int, ITileCell> _make;
        readonly Dictionary<long, ITileCell> _live = new Dictionary<long, ITileCell>();
        readonly Stack<ITileCell> _free = new Stack<ITileCell>();
        readonly List<long> _retiring = new List<long>();

        float _zoom = DefaultZoom;
        Vector2 _pan;
        int _firstCol = 1, _lastCol = 0, _firstRow = 1, _lastRow = 0;
        float _pinchStart;
        float _zoomStart;

        /// <summary>How far a piece's art can reach beyond its tile, in floor pixels. See <see cref="SetReach"/>.</summary>
        float _reachUp = GroveFloor.TileHeight * 2f, _reachSide = GroveFloor.TileWidth;

        /// <summary>Raised when a tile is tapped. Never fires for a drag, or after a hold.</summary>
        public Action<int, int> TileTapped;

        /// <summary>
        /// Raised when a finger rests on a tile. Fires once per press, while the finger is
        /// still down, and cancels the tap that press would otherwise have produced.
        /// </summary>
        public Action<int, int> TileHeld;

        /// <summary>
        /// Raised when a tap landed on the field but on no tile — the sky around the floor, or
        /// ground the player does not own.
        ///
        /// <para>
        /// A tap that resolves to nothing is still a tap, and anything the player has opened
        /// over the floor has to be able to hear it. Without this the sky was the one place on
        /// the screen where tapping did nothing at all, so a panel raised by a long press could
        /// only be dismissed by tapping something else — which is the opposite of what tapping
        /// away from a thing means everywhere else in the game.
        /// </para>
        /// </summary>
        public Action TappedNothing;

        /// <summary>
        /// One tile of the field, built once and rebound as it is recycled.
        ///
        /// <para>
        /// Two nodes, one per layer: <see cref="Ground"/> draws the tile and <see cref="Root"/>
        /// whatever stands on it. Both are positioned by the field at the tile's point; the
        /// cell offsets its art from there. <see cref="Depth"/> is what the piece layer is
        /// sorted by, and a cell answers it after <see cref="Bind"/> — a two-deep house stands
        /// in front of everything up to its front tile, not merely its anchor.
        /// </para>
        /// </summary>
        public interface ITileCell
        {
            RectTransform Ground { get; }
            RectTransform Root { get; }
            int Depth { get; }
            void Bind(int col, int row);
        }

        public float Zoom => _zoom;

        /// <summary>
        /// Stops the field taking input, while it goes on drawing and culling normally.
        ///
        /// <para>
        /// <b>A scrim is not enough here and that is the whole reason this exists.</b> A drag,
        /// a tap and a long press all arrive through the event system, so an invisible blocker
        /// over the screen stops all three — but <see cref="Pinch"/> reads <c>Input.GetTouch</c>
        /// directly, because a two-finger gesture has no single pointer to route, and polled
        /// input does not care what is drawn on top of it. Left ungated, two fingers laid on a
        /// ceremony that is moving the camera would fight it for the whole of it.
        /// </para>
        /// <para>
        /// Culling deliberately keeps running: what is frozen is the player's hold on the
        /// camera, not the field, and a ceremony that adds ground needs tiles to keep being
        /// realised while it does.
        /// </para>
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>
        /// Which tiles exist at all. Ground the player does not own is simply absent — see
        /// <see cref="SetVisible"/>.
        /// </summary>
        Func<int, int, bool> _visible;

        /// <summary>
        /// Sets which tiles are drawn, and re-measures what the camera may be dragged over.
        ///
        /// <para>
        /// <b>Unowned ground is not drawn dimmed, it is not there.</b> The first version drew
        /// the whole field with a padlock on everything unbought, which made the grove a wall of
        /// locked squares with a small lit patch in the middle — the opposite of what the screen
        /// is for. The floor is now exactly the land the player owns, so buying a region is
        /// visibly the ground growing rather than a padlock disappearing.
        /// </para>
        /// </summary>
        public void SetVisible(Func<int, int, bool> visible, int minCol, int minRow,
                               int maxCol, int maxRow)
        {
            _visible = visible;
            Measure(minCol, minRow, maxCol, maxRow);
        }

        /// <summary>
        /// How far beyond its tile a piece's art may reach — up the screen and to either side,
        /// in floor pixels — so the culling window realises a tile whose picture is in view
        /// while its ground is not.
        ///
        /// <para>
        /// Without it a tall tree whose tile sat just under the bottom edge of the viewport
        /// was culled with its canopy in full view, and popped in as the floor was dragged
        /// another inch. The window used to be padded by two tiles, which is a quarter of an
        /// oak. The screen reads the number off the catalog once (<c>GroveTileArt.Reach</c>);
        /// the price is a row or two of extra cells at the bottom of the screen.
        /// </para>
        /// </summary>
        public void SetReach(float up, float side)
        {
            _reachUp = Mathf.Max(GroveFloor.TileHeight, up);
            _reachSide = Mathf.Max(GroveFloor.TileWidth * .5f, side);
            Revisit();
        }

        public static GroveFieldView Attach(RectTransform viewport, GroveFloor floor,
                                            Func<int, int, ITileCell> make)
        {
            // Something has to catch the drag, or a pan that starts on the gap between two
            // tiles does nothing — the same reason GridView puts an invisible image on its
            // viewport.
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();

            var field = UIKit.Node("Field", viewport);
            field.anchorMin = field.anchorMax = new Vector2(.5f, .5f);
            field.pivot = new Vector2(.5f, .5f);
            field.sizeDelta = Vector2.zero;

            var view = viewport.gameObject.AddComponent<GroveFieldView>();
            view._viewport = viewport;
            view._field = field;
            view._floor = floor ?? GroveFloor.Empty;
            view._make = make;

            // Ground first, pieces over it. Sibling order is draw order, so the two layers
            // being two nodes is the whole of the layering rule.
            view._groundLayer = UIKit.Node("Ground", field);
            view._pieceLayer = UIKit.Node("Pieces", field);

            return view;
        }

        /// <summary>
        /// Centres the camera on one tile. Used on the first paint so a new player opens onto
        /// their hall rather than onto whichever corner the arithmetic happened to start at.
        ///
        /// <para>
        /// Fractional coordinates are legal — see <see cref="GroveFloor.TileX"/>. That is what
        /// lets a camera move be eased between two places by calling this every frame, rather
        /// than by a second copy of the transform living beside the one that draws the floor.
        /// </para>
        /// </summary>
        public void CentreOn(float col, float row)
        {
            _pan = new Vector2(-GroveFloor.TileX(col, row), GroveFloor.TileY(col, row));
            Apply();
        }

        /// <summary>
        /// Sets the zoom directly, clamped to the same band a pinch is.
        ///
        /// For a ceremony that frames something the player did not choose to look at — see
        /// <c>GroveRise</c>. Ordinary use is the pinch, which owns <see cref="_zoom"/> through
        /// <see cref="Pinch"/>; nothing about a screen's own layout should be reaching in here.
        /// </summary>
        public void ZoomTo(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            Apply();
        }

        /// <summary>
        /// Re-tests which tiles exist, keeping the ones already drawn.
        ///
        /// <para>
        /// <see cref="Cull"/> only does any work when the <em>window</em> moves, which is
        /// exactly right while the floor is a fixed set of tiles being panned over — and
        /// exactly wrong while ground is arriving under a still camera, where the window never
        /// changes and the answer to <see cref="_visible"/> does. Forcing the range to an
        /// impossible one makes the next frame re-evaluate it; tiles already live are left
        /// alone rather than rebuilt, which is what separates this from <see cref="Rebuild"/>
        /// and what stops the whole grove flashing every time one tile lands.
        /// </para>
        /// </summary>
        public void Revisit()
        {
            _firstCol = 1; _lastCol = 0;
            _firstRow = 1; _lastRow = 0;
        }

        /// <summary>
        /// A node in the field's own space, for effects that must pan and zoom with the floor.
        ///
        /// <para>
        /// A mark naming a tile has to travel with that tile, and the only way to be sure of
        /// that is to be a child of the same transform — <c>HomesteadScreen</c>'s edit bar is
        /// the alternative and it has to be re-placed every frame from <c>LateUpdate</c>,
        /// which is right for one bar and absurd for thirty diamonds. Parented after both
        /// layers, so it is drawn over every tile and every piece rather than sorted among them.
        /// </para>
        /// </summary>
        public RectTransform Layer(string name)
            => _field == null ? null : UIKit.Node(name, _field);

        /// <summary>
        /// Rebinds every live tile without moving the camera, and re-sorts them. For an event
        /// repaint — a bind can change what a cell stands for and therefore its depth.
        /// </summary>
        public void Refresh()
        {
            foreach (var pair in _live) pair.Value.Bind(Col(pair.Key), Row(pair.Key));
            Restack();
        }

        /// <summary>Rebinds one tile, for a caller that knows exactly what changed.</summary>
        public void Refresh(int col, int row)
        {
            if (!_live.TryGetValue(Key(col, row), out var cell)) return;

            cell.Bind(col, row);
            Restack();
        }

        /// <summary>
        /// Takes a new floor. The camera keeps its zoom, because a content refresh is not
        /// something the player asked for and having the view jump under them would say it was.
        /// </summary>
        public void SetFloor(GroveFloor floor) => _floor = floor ?? GroveFloor.Empty;

        /// <summary>
        /// The box the camera may be dragged over: the tiles that actually exist, not the field.
        ///
        /// Re-measured whenever the floor or the owned land changes, which is the only time it
        /// can move. Clamping to the whole field instead would let a player drag away over
        /// ground that is not drawn and lose their grove off the edge of the screen.
        /// </summary>
        void Measure(int minCol, int minRow, int maxCol, int maxRow)
        {
            if (minCol > maxCol || minRow > maxRow)
            {
                _minX = _maxX = _minY = _maxY = 0f;
                return;
            }

            // The four corners of the owned box, which are the extremes of the diamond it maps
            // to — a rectangle in tile space is a diamond on screen, so its bounding box is the
            // box of its corners and nothing between them can be further out.
            _minX = GroveFloor.TileX(minCol, maxRow);
            _maxX = GroveFloor.TileX(maxCol, minRow);
            _minY = GroveFloor.TileY(minCol, minRow);
            _maxY = GroveFloor.TileY(maxCol, maxRow);

            float half = GroveFloor.TileWidth * .5f;
            _minX -= half; _maxX += half;
            _minY -= GroveFloor.TileHeight; _maxY += GroveFloor.TileHeight;
        }

        float _minX, _maxX, _minY, _maxY;

        /// <summary>Throws every tile away, so the next frame rebuilds them. For a new floor.</summary>
        public void Rebuild()
        {
            _retiring.Clear();
            foreach (var pair in _live) _retiring.Add(pair.Key);
            foreach (long key in _retiring) Release(key);

            Revisit();
        }

        // --------------------------------------------------------------- input
        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (Locked) return;

            // A two-finger gesture is a zoom, and treating its drag as a pan as well makes the
            // floor lurch away under the pinch.
            if (Input.touchCount >= 2) return;

            _pan += e.delta / Mathf.Max(.01f, _zoom);
            _dragged += e.delta.magnitude;
            Apply();
        }

        float _dragged;
        bool _pressing, _held;
        float _pressAt;
        Vector2 _pressPos;
        Camera _pressCam;

        public void OnPointerDown(PointerEventData e)
        {
            if (Locked) return;

            _pressing = true;
            _held = false;
            _dragged = 0f;
            _pressAt = Time.unscaledTime;
            _pressPos = e.position;
            _pressCam = e.pressEventCamera;
        }

        public void OnPointerUp(PointerEventData e) => _pressing = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (Locked) return;

            // A pan that ends over a tile is not a tap on it. The threshold is in screen points
            // rather than tiles because it is about the finger, not the floor.
            if (_dragged > PressSlop) { _dragged = 0f; return; }
            _dragged = 0f;

            // A press that already became a hold has been answered. Without this the player
            // gets the hold's panel and then, on lifting the finger, whatever a tap does — two
            // responses to one gesture, the second of them unasked for.
            if (_held) return;

            if (!TryTileAt(e.position, e.pressEventCamera, out int col, out int row))
            {
                TappedNothing?.Invoke();
                return;
            }

            TileTapped?.Invoke(col, row);
        }

        /// <summary>
        /// The box and mask of the art drawn from a tile, in this field's own space — a hit
        /// that <see cref="GroveHit.IsDrawn"/> answers false for on a tile drawing nothing.
        ///
        /// <para>
        /// Supplied by the screen rather than worked out here, because what stands on a tile
        /// and how big it draws are the screen's business — and it must be the <em>same</em>
        /// answer the screen paints with, or the player would be picking pieces that are not
        /// where the picture says they are. The screen answers for a stand's anchor tile only;
        /// a tile a footprint reaches over answers nothing and lets the anchor's art speak.
        /// </para>
        /// </summary>
        public Func<int, int, GroveHit> Hit;

        /// <summary>
        /// Which visible tile a screen point is over. False for a point off the floor, or over
        /// ground the player does not own.
        ///
        /// <para>
        /// What is <em>drawn</em> over the point beats what the ground under it says — see
        /// <see cref="GrovePick"/>. Without that, only a tile's bare diamond is touchable and
        /// every piece standing on one is scenery. A drawn answer is the stand's anchor; a
        /// ground answer is the tile itself, which the caller may resolve to whatever covers it.
        /// </para>
        /// </summary>
        public bool TryTileAt(Vector2 screenPos, Camera cam, out int col, out int row)
        {
            col = row = 0;

            if (_field == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _field, screenPos, cam, out var local)) return false;

            if (TryDrawnAt(local, out col, out row)) return true;

            GroveFloor.TileAt(local.x, -local.y, out col, out row);

            if (!_floor.Contains(col, row)) return false;
            return _visible == null || _visible(col, row);
        }

        /// <summary>
        /// The frontmost piece drawn over a point in field space.
        ///
        /// <para>
        /// Only the live tiles are considered, which is the same set the field is painting and
        /// therefore the same set the player can see. The list is held rather than allocated,
        /// because this runs on every frame of a move drag as well as on every tap.
        /// </para>
        /// </summary>
        bool TryDrawnAt(Vector2 local, out int col, out int row)
        {
            col = row = 0;
            if (Hit == null) return false;

            _hits.Clear();

            foreach (var pair in _live)
            {
                var hit = Hit(Col(pair.Key), Row(pair.Key));
                if (hit.IsDrawn) _hits.Add(hit);
            }

            return GrovePick.Topmost(_hits, local.x, local.y, out col, out row)
                && (_visible == null || _visible(col, row));
        }

        readonly List<GroveHit> _hits = new List<GroveHit>();

        /// <summary>
        /// Where a tile is on the screen right now, in world space.
        ///
        /// <para>
        /// Asked of the view rather than read off the tile's own object because a tile that has
        /// been panned out of view does not have one — culling is the feature here — and
        /// anything anchored to a tile has to keep answering while the tile is off screen, if
        /// only to know that it should hide. Fractional coordinates are legal, so a footprint's
        /// centre can be asked for as easily as a tile.
        /// </para>
        /// </summary>
        public Vector3 TileWorld(float col, float row)
            => _field == null
                ? Vector3.zero
                : _field.TransformPoint(new Vector3(GroveFloor.TileX(col, row),
                                                    -GroveFloor.TileY(col, row), 0f));

        void Update()
        {
            // Culling runs whatever else is going on — see Locked. Everything above it is the
            // player's hold on the camera, and that is what a ceremony takes away.
            if (!Locked)
            {
                Pinch();
                Hold();
            }

            Cull();
        }

        /// <summary>
        /// Turns a finger resting on the floor into <see cref="TileHeld"/>.
        ///
        /// <para>
        /// Polled rather than scheduled on the press, for <c>RunScreen.Tick</c>'s reason: a timer
        /// started in <c>OnPointerDown</c> would have to be unwound by every way a press can
        /// end — a lift, a drag, a second finger, the screen being torn down underneath it —
        /// and the one that gets forgotten is the one that fires a panel over the next screen.
        /// Here the press either still satisfies the conditions this frame or it does not.
        /// </para>
        /// </summary>
        void Hold()
        {
            if (!_pressing || _held) return;

            // A second finger is a pinch. Left running, the hold would fire in the middle of a
            // zoom, on whichever tile the first finger happened to have started over.
            if (Input.touchCount >= 2) { _pressing = false; return; }

            if (_dragged > PressSlop) { _pressing = false; return; }
            if (Time.unscaledTime - _pressAt < HoldSeconds) return;

            _held = true;
            if (TryTileAt(_pressPos, _pressCam, out int col, out int row))
                TileHeld?.Invoke(col, row);
        }

        /// <summary>
        /// Two fingers moving apart or together, scaled about the point between them.
        ///
        /// Anchored on the midpoint rather than on the screen's centre, which is what makes a
        /// pinch feel like it is holding the floor rather than operating a slider: the ground
        /// under the fingers stays under them.
        /// </summary>
        void Pinch()
        {
            if (Input.touchCount < 2)
            {
                _pinchStart = 0f;
                return;
            }

            var a = Input.GetTouch(0);
            var b = Input.GetTouch(1);
            float gap = (a.position - b.position).magnitude;

            if (_pinchStart <= 0f)
            {
                _pinchStart = gap;
                _zoomStart = _zoom;
                return;
            }

            float wanted = Mathf.Clamp(_zoomStart * (gap / Mathf.Max(1f, _pinchStart)),
                                       MinZoom, MaxZoom);
            if (Mathf.Approximately(wanted, _zoom)) return;

            var mid = (a.position + b.position) * .5f;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _viewport, mid, null, out var before))
            {
                float ratio = wanted / _zoom;
                _pan -= before * (ratio - 1f) / wanted;
            }

            _zoom = wanted;
            Apply();
        }

        void Apply()
        {
            if (_field == null) return;

            Clamp();
            _field.localScale = Vector3.one * _zoom;
            _field.anchoredPosition = _pan * _zoom;
        }

        /// <summary>
        /// Keeps the field from being dragged off the screen.
        ///
        /// The rule is that the field's own box may not leave the viewport, with a margin so a
        /// player can see a little space around the edge of their grove. When the field is
        /// smaller than the viewport — which happens at the far end of a zoom-out — it is
        /// centred instead, because clamping a small thing to the edges of a big window pins it
        /// to a corner.
        /// </summary>
        void Clamp()
        {
            const float Margin = 240f;

            float halfW = _viewport.rect.width * .5f / Mathf.Max(.01f, _zoom);
            float halfH = _viewport.rect.height * .5f / Mathf.Max(.01f, _zoom);

            float minX = -(_maxX + Margin) + halfW;
            float maxX = -(_minX - Margin) - halfW;
            float minY = _minY - Margin + halfH;
            float maxY = _maxY + Margin - halfH;

            _pan.x = minX > maxX ? (minX + maxX) * .5f : Mathf.Clamp(_pan.x, minX, maxX);
            _pan.y = minY > maxY ? (minY + maxY) * .5f : Mathf.Clamp(_pan.y, minY, maxY);
        }

        // -------------------------------------------------------------- culling
        /// <summary>
        /// Realises the tiles inside the viewport and retires the ones outside it.
        ///
        /// <para>
        /// The visible range is worked out in floor space and then converted to tiles rather
        /// than the other way round, because the isometric transform turns a rectangle of screen
        /// into a diamond of tiles — so the honest answer is the bounding box of that diamond,
        /// which is what the four corners give. Tiles inside the box but outside the view cost
        /// one cell each and are cheaper than getting the geometry wrong.
        /// </para>
        /// <para>
        /// The rectangle is the viewport grown by <see cref="SetReach"/>: downward by how far
        /// a piece's art reaches up, sideways by how far it reaches out. A piece is drawn from
        /// its anchor tile, so a tile whose art is in view must be live even when its ground
        /// is off the bottom of the screen.
        /// </para>
        /// </summary>
        void Cull()
        {
            if (_make == null || _floor.IsEmpty) return;

            float halfW = _viewport.rect.width * .5f / Mathf.Max(.01f, _zoom);
            float halfH = _viewport.rect.height * .5f / Mathf.Max(.01f, _zoom);

            float left = -_pan.x - halfW - _reachSide, right = -_pan.x + halfW + _reachSide;
            float top = _pan.y - halfH - GroveFloor.TileHeight, bottom = _pan.y + halfH + _reachUp;

            int minCol = int.MaxValue, maxCol = int.MinValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;

            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? left : right;
                float y = (i & 2) == 0 ? top : bottom;

                GroveFloor.TileAt(x, y, out int col, out int row);
                minCol = Mathf.Min(minCol, col); maxCol = Mathf.Max(maxCol, col);
                minRow = Mathf.Min(minRow, row); maxRow = Mathf.Max(maxRow, row);
            }

            minCol = Mathf.Max(0, minCol - Overscan);
            minRow = Mathf.Max(0, minRow - Overscan);
            maxCol = Mathf.Min(_floor.Cols - 1, maxCol + Overscan);
            maxRow = Mathf.Min(_floor.Rows - 1, maxRow + Overscan);

            if (minCol == _firstCol && maxCol == _lastCol
                && minRow == _firstRow && maxRow == _lastRow) return;

            _firstCol = minCol; _lastCol = maxCol;
            _firstRow = minRow; _lastRow = maxRow;

            _retiring.Clear();
            foreach (var pair in _live)
            {
                int col = Col(pair.Key), row = Row(pair.Key);
                if (col < minCol || col > maxCol || row < minRow || row > maxRow)
                    _retiring.Add(pair.Key);
            }
            foreach (long key in _retiring) Release(key);

            for (int col = minCol; col <= maxCol; col++)
                for (int row = minRow; row <= maxRow; row++)
                {
                    if (_visible != null && !_visible(col, row)) continue;

                    long key = Key(col, row);
                    if (_live.ContainsKey(key)) continue;

                    Realise(col, row, key);
                }

            Restack();
        }

        /// <summary>
        /// Puts every live tile in depth order, once, after the window has settled.
        ///
        /// <para>
        /// <b>Sibling index cannot be assigned per tile as it is realised, and that is not
        /// obvious until it is wrong.</b> <c>SetSiblingIndex</c> <em>inserts</em>: everything
        /// after the position shifts up by one, so a tile placed at index 12 renumbers the
        /// eleven behind it and the next tile's intended index no longer means what it meant.
        /// The result was a field that looked sorted and was not — the hall drew in front of
        /// the companion standing one tile nearer the viewer, which is exactly the failure
        /// depth sorting exists to prevent.
        /// </para>
        /// <para>
        /// So the whole window is ordered in one pass instead, once per layer: the ground by
        /// its tile, so each skirt is covered by the tile in front; the pieces by what each
        /// cell reports it stands for. It runs when the visible range changes or a bind does
        /// rather than every frame, and it sorts a screenful rather than a floor.
        /// </para>
        /// </summary>
        void Restack()
        {
            _ordered.Clear();
            foreach (var pair in _live) _ordered.Add(pair.Key);

            _ordered.Sort(GroundDepth);
            for (int i = 0; i < _ordered.Count; i++)
                _live[_ordered[i]].Ground.SetSiblingIndex(i);

            // Made once, on first use, rather than in a constructor a MonoBehaviour must not
            // declare or a field initialiser that may not capture `this`.
            _pieceDepth ??= (a, b) =>
            {
                int byDepth = _live[a].Depth.CompareTo(_live[b].Depth);
                return byDepth != 0 ? byDepth : GroundDepth(a, b);
            };

            _ordered.Sort(_pieceDepth);
            for (int i = 0; i < _ordered.Count; i++)
                _live[_ordered[i]].Root.SetSiblingIndex(i);
        }

        readonly List<long> _ordered = new List<long>();

        /// <summary>
        /// Held rather than passed as a method group, which allocates a delegate at every call.
        /// This one runs on every pan that moves the window, so it is the one place in this
        /// screen where a per-call allocation would be continuous garbage under the thumb.
        /// </summary>
        static readonly Comparison<long> GroundDepth = (a, b)
            => GroveFloor.DrawOrder(Col(a), Row(a)).CompareTo(GroveFloor.DrawOrder(Col(b), Row(b)));

        Comparison<long> _pieceDepth;

        void Realise(int col, int row, long key)
        {
            ITileCell cell;
            if (_free.Count > 0)
            {
                cell = _free.Pop();
                cell.Ground.gameObject.SetActive(true);
                cell.Root.gameObject.SetActive(true);
            }
            else
            {
                cell = _make(col, row);
                cell.Ground.SetParent(_groundLayer, false);
                cell.Root.SetParent(_pieceLayer, false);
            }

            Place(cell.Ground, col, row);
            Place(cell.Root, col, row);

            cell.Bind(col, row);

            // Depth is applied by Restack once the window has settled, never here — see there
            // for why setting it per tile silently does not sort.
            _live[key] = cell;
        }

        static void Place(RectTransform node, int col, int row)
        {
            node.anchorMin = node.anchorMax = new Vector2(.5f, .5f);
            node.pivot = new Vector2(.5f, .5f);
            node.anchoredPosition = new Vector2(GroveFloor.TileX(col, row), -GroveFloor.TileY(col, row));
        }

        void Release(long key)
        {
            if (!_live.TryGetValue(key, out var cell)) return;

            _live.Remove(key);
            cell.Ground.gameObject.SetActive(false);
            cell.Root.gameObject.SetActive(false);
            _free.Push(cell);
        }

        static long Key(int col, int row) => ((long)col << 32) | (uint)row;
        static int Col(long key) => (int)(key >> 32);
        static int Row(long key) => (int)(key & 0xFFFFFFFFL);
    }
}
