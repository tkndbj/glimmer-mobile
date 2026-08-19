using System;
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
    /// <b>Pinch needs multi-touch, which the game turns off.</b> See
    /// <see cref="View.WantsMultiTouch"/> — the grove declares it and <see cref="Flow"/> applies
    /// it, so no screen has to remember to put it back.
    /// </para>
    /// </summary>
    public sealed class GroveFieldView : MonoBehaviour, IDragHandler, IBeginDragHandler, IPointerClickHandler
    {
        /// <summary>
        /// How far out and in the player may zoom.
        ///
        /// The upper bound is 1, deliberately: one is the size the art was cut for, and letting
        /// somebody zoom past it only buys them a closer look at the resolution cap. The lower
        /// bound is where a tile stops being a tappable target rather than where the field stops
        /// fitting — a floor you can see all of but cannot touch is a picture, not a screen.
        /// </summary>
        public const float MinZoom = .45f;
        public const float MaxZoom = 1f;

        /// <summary>Tiles realised beyond each edge of the viewport. See <see cref="Cull"/>.</summary>
        const int Overscan = 2;

        RectTransform _viewport, _field;
        GroveFloor _floor;

        Func<int, int, ITileCell> _make;
        readonly System.Collections.Generic.Dictionary<long, ITileCell> _live =
            new System.Collections.Generic.Dictionary<long, ITileCell>();
        readonly System.Collections.Generic.Stack<ITileCell> _free =
            new System.Collections.Generic.Stack<ITileCell>();
        readonly System.Collections.Generic.List<long> _retiring = new System.Collections.Generic.List<long>();

        float _zoom = .7f;
        Vector2 _pan;
        int _firstCol = 1, _lastCol = 0, _firstRow = 1, _lastRow = 0;
        float _pinchStart;
        float _zoomStart;

        /// <summary>Raised when a tile is tapped. Never fires for a drag.</summary>
        public Action<int, int> TileTapped;

        /// <summary>One tile of the field, built once and rebound as it is recycled.</summary>
        public interface ITileCell
        {
            RectTransform Root { get; }
            void Bind(int col, int row);
        }

        public float Zoom => _zoom;

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

            return view;
        }

        /// <summary>
        /// Centres the camera on one tile. Used on the first paint so a new player opens onto
        /// their hall rather than onto whichever corner the arithmetic happened to start at.
        /// </summary>
        public void CentreOn(int col, int row)
        {
            _pan = new Vector2(-GroveFloor.TileX(col, row), GroveFloor.TileY(col, row));
            Apply();
        }

        /// <summary>Rebinds every live tile without moving the camera. For an event repaint.</summary>
        public void Refresh()
        {
            foreach (var pair in _live) pair.Value.Bind(Col(pair.Key), Row(pair.Key));
        }

        /// <summary>Rebinds one tile, for a caller that knows exactly what changed.</summary>
        public void Refresh(int col, int row)
        {
            if (_live.TryGetValue(Key(col, row), out var cell)) cell.Bind(col, row);
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

            _firstCol = 1; _lastCol = 0;
            _firstRow = 1; _lastRow = 0;
        }

        // --------------------------------------------------------------- input
        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            // A two-finger gesture is a zoom, and treating its drag as a pan as well makes the
            // floor lurch away under the pinch.
            if (Input.touchCount >= 2) return;

            _pan += e.delta / Mathf.Max(.01f, _zoom);
            _dragged += e.delta.magnitude;
            Apply();
        }

        float _dragged;

        public void OnPointerClick(PointerEventData e)
        {
            // A pan that ends over a tile is not a tap on it. The threshold is in screen points
            // rather than tiles because it is about the finger, not the floor.
            if (_dragged > 12f) { _dragged = 0f; return; }
            _dragged = 0f;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _field, e.position, e.pressEventCamera, out var local)) return;

            GroveFloor.TileAt(local.x, -local.y, out int col, out int row);
            if (!_floor.Contains(col, row)) return;
            if (_visible != null && !_visible(col, row)) return;

            TileTapped?.Invoke(col, row);
        }

        void Update()
        {
            Pinch();
            Cull();
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
        /// </summary>
        void Cull()
        {
            if (_make == null || _floor.IsEmpty) return;

            float halfW = _viewport.rect.width * .5f / Mathf.Max(.01f, _zoom);
            float halfH = _viewport.rect.height * .5f / Mathf.Max(.01f, _zoom);

            float left = -_pan.x - halfW, right = -_pan.x + halfW;
            float top = _pan.y - halfH, bottom = _pan.y + halfH;

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
        /// So the whole window is ordered in one pass instead. It runs when the visible range
        /// changes rather than every frame, which is a few times a second while panning and
        /// never while still, and it sorts a screenful rather than a floor.
        /// </para>
        /// </summary>
        void Restack()
        {
            _ordered.Clear();
            foreach (var pair in _live) _ordered.Add(pair.Key);

            _ordered.Sort(Depth);

            for (int i = 0; i < _ordered.Count; i++)
                _live[_ordered[i]].Root.SetSiblingIndex(i);
        }

        readonly System.Collections.Generic.List<long> _ordered = new System.Collections.Generic.List<long>();

        /// <summary>
        /// Held rather than passed as a method group, which allocates a delegate at every call.
        /// This one runs on every pan that moves the window, so it is the one place in this
        /// screen where a per-call allocation would be continuous garbage under the thumb.
        /// </summary>
        static readonly System.Comparison<long> Depth = (a, b)
            => GroveFloor.DrawOrder(Col(a), Row(a)).CompareTo(GroveFloor.DrawOrder(Col(b), Row(b)));

        void Realise(int col, int row, long key)
        {
            ITileCell cell;
            if (_free.Count > 0)
            {
                cell = _free.Pop();
                cell.Root.gameObject.SetActive(true);
            }
            else
            {
                cell = _make(col, row);
                cell.Root.SetParent(_field, false);
            }

            var root = cell.Root;
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = new Vector2(GroveFloor.TileX(col, row), -GroveFloor.TileY(col, row));

            cell.Bind(col, row);

            // Depth is applied by Restack once the window has settled, never here — see there
            // for why setting it per tile silently does not sort.
            _live[key] = cell;
        }

        void Release(long key)
        {
            if (!_live.TryGetValue(key, out var cell)) return;

            _live.Remove(key);
            cell.Root.gameObject.SetActive(false);
            _free.Push(cell);
        }

        static long Key(int col, int row) => ((long)col << 32) | (uint)row;
        static int Col(long key) => (int)(key >> 32);
        static int Row(long key) => (int)(key & 0xFFFFFFFFL);
    }
}
