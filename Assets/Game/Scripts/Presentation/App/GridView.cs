using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One cell of a <see cref="GridView"/>: a small object that knows how to redraw itself
    /// for whatever row it has been given.
    ///
    /// <para>
    /// The split is the whole design. A cell is <b>built once</b> — its plate, its edge, its
    /// image, its two labels — and then <b>bound many times</b>, so scrolling and switching
    /// shelves cost a handful of assignments rather than a teardown and a rebuild. That is
    /// what makes a four-hundred-piece catalog draw exactly as fast as a forty-piece one, and
    /// it is what stops a repaint from restarting every entrance animation on screen.
    /// </para>
    /// <para>
    /// <see cref="Bind"/> is called with an index into whatever list the owner is showing. The
    /// cell reads that list itself: a callback carrying the item would have to be generic,
    /// and a generic <c>MonoBehaviour</c> is a thing Unity cannot serialise and this project
    /// has no use for.
    /// </para>
    /// </summary>
    public interface IGridCell
    {
        RectTransform Root { get; }

        /// <summary>Draw this cell as row <paramref name="index"/>. Never called out of range.</summary>
        void Bind(int index);
    }

    /// <summary>
    /// A scrolling grid that keeps only the rows you can see.
    ///
    /// <para>
    /// <b>Why this exists.</b> Every grid in the game built one <c>GameObject</c> tree per
    /// item and destroyed the lot on every repaint. At forty pieces that is invisible; at four
    /// hundred it is four hundred subtrees — several thousand objects — created to show the
    /// nine that fit on a phone, and destroyed again the moment anything changed. The shop's
    /// asset scopes were already bounded by one shelf, so <em>memory</em> scaled; the object
    /// count did not, and that is the half that shows up as a stutter on the tab a player
    /// taps. A window of live cells makes both bounded by the screen instead of by the
    /// catalog.
    /// </para>
    /// <para>
    /// <b>And why it is not just a performance fix.</b> Rebuilding is also what made a repaint
    /// visible: every cell entered with a pop from scale zero, so a screen that repainted
    /// twice — once when the shelf changed and once when its art arrived — played that
    /// entrance twice, which is exactly the flicker players reported. Here a repaint is
    /// <see cref="Refresh"/>: the same objects, rebound, with no animation and no scroll jump.
    /// The entrance is spent once, on <see cref="Show"/>, where it means "this is a different
    /// shelf".
    /// </para>
    /// <para>
    /// It owns the <c>ScrollRect</c> and the content sizing, so an owner writes no scroll
    /// arithmetic. What an owner supplies is a factory for one cell and a count.
    /// </para>
    /// </summary>
    public sealed class GridView : MonoBehaviour
    {
        /// <summary>
        /// Rows kept live beyond each edge of the viewport.
        ///
        /// One is enough and two is the wrong trade: a row is realised when its top edge is
        /// still a full cell height away, so at any ordinary flick speed it is bound before it
        /// could be seen, and every extra row of overscan is cells built for nobody.
        /// </summary>
        const int Overscan = 1;

        RectTransform _content, _viewport;
        ScrollRect _scroll;

        int _columns = 1;
        float _cellW, _cellH, _padTop, _padBottom;

        Func<RectTransform, IGridCell> _make;

        readonly Dictionary<int, IGridCell> _live = new Dictionary<int, IGridCell>();
        readonly Stack<IGridCell> _free = new Stack<IGridCell>();

        int _count;
        int _firstRow = 1, _lastRow = 0;      // deliberately empty: nothing is realised yet
        bool _animate;
        float _shownAt;

        /// <summary>How many rows of items there are, for an owner sizing something else.</summary>
        public int RowCount => _count <= 0 ? 0 : (_count + _columns - 1) / _columns;

        /// <summary>The scroll view, for an owner that wants to jump to the top.</summary>
        public ScrollRect Scroll => _scroll;

        /// <summary>
        /// Builds the viewport, the mask, the scroll view and the content node, and returns the
        /// grid that drives them.
        ///
        /// <para>
        /// The viewport is handed in already positioned, because where a grid sits is the
        /// screen's business and how it scrolls is this type's. Everything inside it belongs to
        /// this: an owner that reaches in and moves the content is fighting the window
        /// arithmetic.
        /// </para>
        /// </summary>
        public static GridView Attach(RectTransform viewport, int columns, float cellW, float cellH,
                                      Func<RectTransform, IGridCell> make,
                                      float padTop = 12f, float padBottom = 40f)
        {
            // Something must catch the drag, or a flick that starts on a gap between cells does
            // nothing at all — a scroll view is only as reliable as its dead space.
            var catcher = viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            viewport.gameObject.AddComponent<RectMask2D>();

            var content = UIKit.Node("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            var grid = viewport.gameObject.AddComponent<GridView>();
            grid._viewport = viewport;
            grid._content = content;
            grid._scroll = scroll;
            grid._columns = Mathf.Max(1, columns);
            grid._cellW = cellW;
            grid._cellH = cellH;
            grid._padTop = padTop;
            grid._padBottom = padBottom;
            grid._make = make;

            return grid;
        }

        /// <summary>
        /// Shows a list of <paramref name="count"/> items, from the top.
        ///
        /// <para>
        /// For a <em>different</em> list: a new shelf, a filter, the first paint. It resets the
        /// scroll — a shop that opens a tab halfway down is a shop that has hidden its own first
        /// row — and it lets the cells enter with the staggered pop that says the page changed.
        /// When the same list is merely redrawn, call <see cref="Refresh"/> instead.
        /// </para>
        /// </summary>
        public void Show(int count, bool animate = true)
        {
            _count = Mathf.Max(0, count);
            _animate = animate;
            _shownAt = Time.unscaledTime;

            Recycle();
            Resize();

            // Straight to the content rather than through verticalNormalizedPosition, which a
            // ScrollRect resolves against bounds it recomputes in its own LateUpdate — so in
            // the frame the content is resized it is read against the *old* height. That cost
            // this project a whole screen once; see HomesteadScreen.
            _content.anchoredPosition = Vector2.zero;
            if (_scroll) _scroll.velocity = Vector2.zero;

            _firstRow = 1;
            _lastRow = 0;
            Window(force: true);
        }

        /// <summary>
        /// Redraws what is on screen without disturbing it.
        ///
        /// <para>
        /// The same objects, in the same places, at the same scroll position, with no entrance
        /// — because this is what an <em>event</em> triggers: art arriving, a purchase landing,
        /// a run finishing somewhere else and changing what is held. Every one of those used to
        /// destroy and rebuild the grid, which is why the shop flickered and why a player who
        /// bought something lost their place in it.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            foreach (var pair in _live) pair.Value.Bind(pair.Key);
        }

        /// <summary>Redraws one row, for an owner that knows exactly what changed.</summary>
        public void Refresh(int index)
        {
            if (_live.TryGetValue(index, out var cell)) cell.Bind(index);
        }

        void Update() => Window(force: false);

        // ------------------------------------------------------------- windowing
        void Resize()
        {
            float height = _padTop + RowCount * _cellH + _padBottom;

            // Never shorter than the viewport: a content rect smaller than its window makes a
            // ScrollRect bounce against nothing, which reads as a broken list.
            _content.sizeDelta = new Vector2(0f, Mathf.Max(height, _viewport.rect.height));
        }

        void Window(bool force)
        {
            if (_make == null) return;

            float top = _content.anchoredPosition.y;
            float view = _viewport.rect.height;

            int first = Mathf.FloorToInt((top - _padTop) / _cellH) - Overscan;
            int last = Mathf.FloorToInt((top + view - _padTop) / _cellH) + Overscan;

            first = Mathf.Max(0, first);
            last = Mathf.Min(RowCount - 1, last);

            if (!force && first == _firstRow && last == _lastRow) return;

            _firstRow = first;
            _lastRow = last;

            // Retire first, so a cell scrolling off the top is available to the row arriving at
            // the bottom in the same pass — otherwise a fast flick allocates a screenful.
            _retiring.Clear();
            foreach (var pair in _live)
            {
                int row = pair.Key / _columns;
                if (row < first || row > last) _retiring.Add(pair.Key);
            }

            foreach (int index in _retiring) Release(index);

            for (int row = first; row <= last; row++)
                for (int column = 0; column < _columns; column++)
                {
                    int index = row * _columns + column;
                    if (index >= _count) break;
                    if (_live.ContainsKey(index)) continue;

                    Realise(index, row, column);
                }
        }

        readonly List<int> _retiring = new List<int>();

        void Realise(int index, int row, int column)
        {
            IGridCell cell;
            if (_free.Count > 0)
            {
                cell = _free.Pop();
                cell.Root.gameObject.SetActive(true);
            }
            else
            {
                cell = _make(_content);
            }

            var root = cell.Root;
            root.anchorMin = root.anchorMax = new Vector2(.5f, 1f);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = new Vector2(
                (column - (_columns - 1) * .5f) * _cellW,
                -(_padTop + row * _cellH + _cellH * .5f));

            cell.Bind(index);

            // A recycled cell can arrive mid-pop from its last life, and an interrupted pop
            // leaves a transform at scale zero for ever. Killing the channel and restoring the
            // scale is what makes reuse invisible rather than a source of missing cells.
            Tween.KillChannel(root, "scale");
            root.localScale = Vector3.one;

            // The entrance belongs to the first screenful of a new list and to nothing else. A
            // row realised by scrolling has already been "entered" by the scroll itself, and one
            // realised a second later by a stray repaint would pop for no reason a player could
            // name — which is the flicker this whole type exists to remove.
            if (_animate && Time.unscaledTime - _shownAt < .05f)
                Tween.Pop(root, 0f, .42f, .03f * Mathf.Min(index, 12));

            _live[index] = cell;
        }

        void Release(int index)
        {
            if (!_live.TryGetValue(index, out var cell)) return;

            _live.Remove(index);

            Tween.KillChannel(cell.Root, "scale");
            cell.Root.localScale = Vector3.one;
            cell.Root.gameObject.SetActive(false);
            _free.Push(cell);
        }

        void Recycle()
        {
            _retiring.Clear();
            foreach (var pair in _live) _retiring.Add(pair.Key);
            foreach (int index in _retiring) Release(index);
        }
    }
}
