using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>Groovekeeper's board.</b> A grove to grow, beds that have to bloom, and a basket of
    /// tiles in an order you can see coming.
    ///
    /// <para>
    /// <b>The decision is made with the eyes, before anything is committed.</b> A finger held over
    /// a cell shows a ghost of the tile that would go there and a ring that says what it would do:
    /// cream, it is only ground; gold with a number, it opens that many flowers; and the beds it
    /// would open pulse while the finger is down. That preview is the whole reason a permanent
    /// planting is fair — nothing here is hidden, so a wrong tile is a misjudgement rather than a
    /// surprise.
    /// </para>
    /// <para>
    /// <b>The board says what it knows.</b> A bed wears a halo in the colours it is still short
    /// of, so the grove reads as a landscape of things that are nearly ready rather than as a grid
    /// of dots; a heartbed wears its own colour, so the one cell that refuses a tile says so
    /// before it is tapped; and every seam of unlike colour is drawn in the blend it makes, which
    /// is the mode's one rule drawn rather than described. All of them are facts a careful player
    /// could work out by squinting, and drawing them is what makes the mode legible in the second
    /// it takes to choose a cell.
    /// </para>
    /// <para>
    /// <b>What is drawn is driven by the gain a planting reports, never by re-reading the
    /// board.</b> <c>KeeperRun.Plant</c> settles the grove completely and hands back the cells
    /// that burst, so by the time anything is animated the model is already at the end.
    /// <see cref="Sync"/> is what puts the drawing back in step if anything ever interrupts.
    /// </para>
    /// </summary>
    public sealed class KeeperView : MonoBehaviour
    {
        /// <summary>Raised whenever anything the readouts count has moved.</summary>
        public Action Changed { get; set; }

        /// <summary>Every bed is open. Raised once, after the last flower has opened.</summary>
        public Action Solved { get; set; }

        /// <summary>
        /// The run is over and lost. The screen reads <see cref="Run"/>'s verdict for which of the
        /// two ways it was.
        /// </summary>
        public Action Lost { get; set; }

        /// <summary>The first tile has been spent, so the run is now owed for.</summary>
        public Action Committed { get; set; }

        /// <summary>
        /// A tap on ground the grove cannot reach yet — the one refusal this board cannot answer
        /// for itself. See <see cref="Refuse"/>.
        /// </summary>
        public Action Unreachable { get; set; }

        /// <summary>
        /// The closing cascade has begun, so nothing else may end this run.
        ///
        /// <c>RippleView.Finishing</c>'s rule and <c>FallView.Finishing</c>'s: the run is decided
        /// when the last bed opens and the panel arrives a beat later while the flowers are still
        /// opening, so everything that could still end a run has to stop at the first of those two
        /// moments rather than the second.
        /// </summary>
        public Action Finishing { get; set; }

        /// <summary>Input off. Set by every panel that goes over this board.</summary>
        public bool Locked { get; set; }

        /// <summary>
        /// The run has not been allowed to begin yet — the half of the answer no mode can see.
        ///
        /// Written only by <c>KeeperScreen</c>, from <c>RunScreen</c>'s frame, and it is a second
        /// latch rather than more uses of <see cref="Locked"/> on purpose: that one has several
        /// writers, and a board held for two reasons has to release them independently or the one
        /// that writes <c>false</c> last cancels the other.
        /// </summary>
        public bool Held { get; set; } = true;

        /// <summary>The run being played. Null until <see cref="Begin"/>.</summary>
        public KeeperRun Run { get; private set; }

        /// <summary>
        /// The mode's own half of "is this board taking input": it exists, nothing is over it, no
        /// cascade is playing and the run has not ended.
        /// </summary>
        public bool TakingInput => Run != null && !Locked && !_busy && !_over;

        /// <summary>Whether a tile may actually be laid right now. Both halves.</summary>
        public bool Playable => TakingInput && !Held;

        // ------------------------------------------------------------------ the furniture
        KeeperLayout _layout;
        RectTransform _host, _grid, _field, _seamNode, _fx, _tray, _plate;
        Image _ghost, _ghostRing, _compostKey;
        Text _ghostCount, _count, _flourish;
        Btn _compost;

        Cell[] _cells;
        Tile[] _at;
        readonly Stack<Tile> _spare = new Stack<Tile>();
        readonly List<Image> _seams = new List<Image>();
        readonly List<int> _bloomed = new List<int>(KeeperFlourish.Most);
        Image[] _queue, _prisms;
        RectTransform[] _seats;

        RectTransform _coach;

        float _cell, _size;
        Vector2 _origin;
        bool _busy, _over, _committed;
        int _hovered = -1, _ghostKey = int.MinValue;

        /// <summary>One cell of the ground: the plate under it, and what a bed draws on top.</summary>
        sealed class Cell
        {
            public RectTransform Rt;
            public Image Plate, Bud, Halo, Glow;
        }

        /// <summary>One tile on screen: the body, the sheen over it and the flower it may grow.</summary>
        sealed class Tile
        {
            public Image Body, Sheen, Prism;
            public RectTransform Rt;
        }

        // ------------------------------------------------------------------ building
        public void Begin(RectTransform host, KeeperLayout layout, int budget)
        {
            _host = host;
            _layout = layout;

            StopAllCoroutines();
            Tween.KillAll(this);

            Run = new KeeperRun(layout, budget);

            // Held until the screen says otherwise, which is the safe direction: a frame of a run
            // the player has not been shown is a frame they did not get.
            Held = true;

            // And handed back, which is the other half and the one every board here has had to
            // learn. Every way a run ends latches this board, so a rebuild that left the flag
            // alone would produce a fresh grove behind a latch belonging to a run that no longer
            // exists — and every tap would be ignored for the rest of the screen's life.
            Locked = false;

            _busy = false;
            _over = false;
            _committed = false;
            _hovered = -1;
            _flourish = null;

            // The same fault as the latch above, one size smaller: the ghost only redraws when
            // what it would say has changed, so a key left over from the previous board can make
            // the first hover of a fresh one draw nothing at all.
            _ghostKey = int.MinValue;

            _spare.Clear();
            _seams.Clear();

            // Its objects are children of the host, which the loop below is about to empty.
            _coach = null;

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;

            // Measured rather than assumed: a fixed cell size is a grove that overflows on
            // somebody's phone, and this mode has a basket to find room for as well.
            float usableH = rect.height - KeeperBand.BasketHeight;
            _cell = Mathf.Min(rect.width / layout.Width, usableH / layout.Height);
            _size = _cell * .88f;

            _grid = UIKit.Node("Grove", host);
            UIKit.StretchTo(_grid, 0f, KeeperBand.BasketHeight, 0f, 0f);

            _origin = new Vector2(-(layout.Width - 1) * _cell * .5f,
                                  (layout.Height - 1) * _cell * .5f);

            BuildGround();
            BuildBasket(host);

            _at = new Tile[layout.Count];

            Sync();
            Enter();
            PaintBasket();
        }

        void BuildGround()
        {
            float w = _layout.Width * _cell, h = _layout.Height * _cell;

            var plate = UIKit.Img("Plate", _grid, Art.Round(28), new Color(.035f, .055f, .105f, .70f),
                                  new Vector2(w + 24f, h + 24f), new Vector2(.5f, .5f), Vector2.zero);
            _plate = (RectTransform)plate.transform;

            UIKit.Img("Edge", _plate, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .10f),
                      new Vector2(w + 24f, h + 24f), new Vector2(.5f, .5f), Vector2.zero);

            // Ground, then seams, then tiles, then effects. A seam is drawn under the tiles it
            // joins so it reads as light in the gap rather than as a bar laid across them.
            var ground = UIKit.Node("Ground", _grid);
            UIKit.StretchTo(ground, 0f, 0f, 0f, 0f);

            _seamNode = UIKit.Node("Seams", _grid);
            UIKit.StretchTo(_seamNode, 0f, 0f, 0f, 0f);

            _field = UIKit.Node("Tiles", _grid);
            UIKit.StretchTo(_field, 0f, 0f, 0f, 0f);

            _fx = UIKit.Node("Fx", _grid);
            UIKit.StretchTo(_fx, 0f, 0f, 0f, 0f);

            _cells = new Cell[_layout.Count];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = BuildCell(ground, i);

            _ghost = UIKit.Img("Ghost", _fx, Art.Round(18), new Color(1, 1, 1, 0f),
                               Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);

            _ghostRing = UIKit.Img("GhostRing", _fx, Art.Ring(96, 7f), new Color(1, 1, 1, 0f),
                                   Vector2.one * _size * 1.2f, new Vector2(.5f, .5f), Vector2.zero);

            _ghostCount = UIKit.Titled("GhostCount", _fx, "", 46, Pal.Gold, TextAnchor.MiddleCenter,
                                       new Vector2(_cell, _cell * .6f), new Vector2(.5f, .5f),
                                       Vector2.zero, 4f, 3f);
            _ghostCount.gameObject.SetActive(false);
        }

        /// <summary>
        /// One cell: what it is made of, and the tap target over it.
        ///
        /// A button per cell rather than per row or column, because here the cell <em>is</em> the
        /// decision — and stone gets one too, so tapping a rock is a refusal the player can feel
        /// rather than a tap that lands on whatever is behind it.
        /// </summary>
        Cell BuildCell(RectTransform ground, int index)
        {
            int at = index;
            var root = UIKit.Box("Cell" + index, ground, Vector2.one * _cell,
                                 new Vector2(.5f, .5f), Where(index));

            var cell = new Cell { Rt = root };
            var kind = _layout.GroundAt(index);

            if (kind == KeeperGround.Stone)
            {
                // Drawn as a shape rather than a tint: stone is the one thing on this board that
                // is never going to change, and a colour alone is a difference only some people
                // can see. It is also drawn *light* on dark ground, which is the opposite of the
                // first attempt — a dark rock on a dark board reads as a hole in the grove
                // rather than as something standing in it, and the one cell a player must not
                // aim at is the last one that should be hard to see.
                UIKit.Img("Shadow", root, Art.Hex(96), new Color(0f, 0f, 0f, .35f),
                          Vector2.one * _size * .94f, new Vector2(.5f, .5f),
                          new Vector2(0f, -_size * .05f));

                cell.Plate = UIKit.Img("Stone", root, Art.Hex(96), new Color(.42f, .47f, .55f, 1f),
                                       Vector2.one * _size * .92f, new Vector2(.5f, .5f),
                                       Vector2.zero);

                // A lit facet up and to the left, which is where every other painted thing in
                // this game takes its light from.
                UIKit.Img("Facet", cell.Plate.transform, Art.Hex(96), new Color(.60f, .66f, .74f, 1f),
                          Vector2.one * _size * .52f, new Vector2(.5f, .5f),
                          new Vector2(-_size * .10f, _size * .09f));
            }
            else
            {
                cell.Plate = UIKit.Img("Soil", root, Art.Round(16), new Color(1, 1, 1, .045f),
                                       Vector2.one * _size * .86f, new Vector2(.5f, .5f),
                                       Vector2.zero);
            }

            if (_layout.IsBed(index))
            {
                // A bed is a ring and a bud inside it. The ring carries the colour it insists on
                // — cream for one that takes any — so the cell that refuses a tile says so before
                // anybody taps it.
                int wants = _layout.Wants(index);
                var tint = wants == Energy.None ? Pal.Cream : Pal.EnergyColour(wants);

                cell.Halo = UIKit.Img("Halo", root, Art.Ring(128, 8f), Pal.A(tint, .55f),
                                      Vector2.one * _size * 1.02f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                // A soft lantern under the ring, so the cells the whole run is about are the ones
                // the eye goes to first. It was the quietest thing on the board without it - a
                // grey ring on dark ground, in a mode whose every other element is lit.
                var glow = UIKit.Img("Glow", root, Art.Glow(128, 2.2f), Pal.A(tint, .16f),
                                     Vector2.one * _size * 1.5f, new Vector2(.5f, .5f),
                                     Vector2.zero);
                glow.transform.SetAsFirstSibling();
                cell.Glow = glow;

                cell.Bud = UIKit.Img("Bud", root, Art.Bloom(96, 6, .25f), Pal.A(tint, .70f),
                                     Vector2.one * _size * .46f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                Tween.Breathe(cell.Halo.transform, .045f, 2.3f, index * .13f);
                Tween.Breathe(glow.transform, .10f, 2.3f, index * .13f + 1.1f);
            }

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;

            var btn = root.gameObject.AddComponent<Btn>();
            btn.PressScale = 1f;
            btn.Setup(() => Plant(at), silent: true);

            var hover = root.gameObject.AddComponent<Hover>();
            hover.Enter = () => ShowGhost(at);
            hover.Exit = HideGhost;

            return cell;
        }

        /// <summary>
        /// The basket: the tile in hand, what is queued behind it, how many are left, and the key
        /// that turns one back into the ground.
        ///
        /// Where each piece sits comes from <c>KeeperBand</c> rather than from numbers typed here,
        /// so whether the queue clears the count is arithmetic a test can hold rather than a
        /// screenshot on one aspect ratio.
        /// </summary>
        void BuildBasket(RectTransform host)
        {
            _tray = UIKit.Box("Basket", host, new Vector2(0f, KeeperBand.BasketHeight),
                              new Vector2(.5f, 0f),
                              new Vector2(0f, KeeperBand.BasketHeight * .5f));
            _tray.anchorMin = new Vector2(0f, 0f);
            _tray.anchorMax = new Vector2(1f, 0f);
            _tray.sizeDelta = new Vector2(0f, KeeperBand.BasketHeight);

            var plate = UIKit.Img("Plate", _tray, Art.Round(28),
                                  new Color(.045f, .065f, .125f, .78f),
                                  new Vector2(KeeperBand.PlateWidth, KeeperBand.PlateHeight),
                                  new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .12f),
                      new Vector2(KeeperBand.PlateWidth, KeeperBand.PlateHeight),
                      new Vector2(.5f, .5f), Vector2.zero);

            _queue = new Image[KeeperBand.Lookahead];
            _prisms = new Image[KeeperBand.Lookahead];
            _seats = new RectTransform[KeeperBand.Lookahead];

            for (int i = 0; i < _queue.Length; i++)
            {
                bool hand = i == 0;
                float size = hand ? KeeperBand.HandSize : KeeperBand.QueueSize;
                float x = hand ? KeeperBand.HandX : KeeperBand.QueueCentre(i - 1);

                var seat = UIKit.Img("Seat" + i, plate.transform, Art.RoundOutline(14, 3f),
                                     new Color(1, 1, 1, hand ? .24f : .10f),
                                     Vector2.one * (size + 14f), new Vector2(.5f, .5f),
                                     new Vector2(x, 0f));
                _seats[i] = (RectTransform)seat.transform;

                _queue[i] = UIKit.Img("Tile" + i, seat.transform, Art.Round(14), Color.white,
                                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);

                // The prism's own mark, shown only when the chip is carrying one. Cream alone is
                // what a bloomed tile wears, so the one tile in the procession that breaks the
                // mode's rule has to say so in a second way.
                _prisms[i] = UIKit.Img("Prism", _queue[i].transform, Art.PrismRing(96, 9f),
                                       new Color(1, 1, 1, 0f), Vector2.one * size * .74f,
                                       new Vector2(.5f, .5f), Vector2.zero);

                if (hand) Tween.Breathe(_queue[i].transform, .05f, 1.9f);
            }

            // How many are left, which is the fail line said in one number. It sits with the
            // procession rather than only in the header because this is what the player is
            // looking at while they choose.
            _count = UIKit.Titled("Left", plate.transform, "0", 52, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(180f, 70f),
                                  new Vector2(.5f, .5f), new Vector2(KeeperBand.CountX, 10f), 4f, 3f);
            UIKit.Shrinkable(_count, 24);

            UIKit.Titled("LeftCap", plate.transform, Loc.Get("mode.keeper.basket"), 20,
                         new Color(.92f, .96f, 1f, .55f), TextAnchor.MiddleCenter,
                         new Vector2(210f, 26f), new Vector2(.5f, .5f),
                         new Vector2(KeeperBand.CountX, -38f), 3f, 0f);

            BuildCompost(plate.transform);
        }

        /// <summary>
        /// The compost key: spend the tile in hand without planting it.
        ///
        /// <para>
        /// It lives inside the basket rather than in the header, and that is the whole of what it
        /// says: it is a thing you do to the tile you are holding, not a way out of the run. The
        /// header's key is the pause, for <c>FallScreen</c>'s reason — a restart deals a fresh
        /// basket, so it belongs one deliberate tap inside a menu.
        /// </para>
        /// </summary>
        void BuildCompost(Transform plate)
        {
            var key = UIKit.Box("Compost", plate, Vector2.one * KeeperBand.CompostSize,
                                new Vector2(.5f, .5f), new Vector2(KeeperBand.CompostX, 4f));

            _compostKey = UIKit.Img("Face", key, Art.Round(20), new Color(1, 1, 1, .08f),
                                    Vector2.one * KeeperBand.CompostSize, new Vector2(.5f, .5f),
                                    Vector2.zero);

            UIKit.Img("Ring", key, Art.RoundOutline(20, 3f), new Color(1, 1, 1, .18f),
                      Vector2.one * KeeperBand.CompostSize, new Vector2(.5f, .5f), Vector2.zero);

            UIKit.Img("Leaf", key, Art.Leaf(96), Pal.A(Pal.Mint, .85f),
                      Vector2.one * (KeeperBand.CompostSize * .52f), new Vector2(.5f, .5f),
                      new Vector2(0f, 4f)).transform.localRotation = Quaternion.Euler(0, 0, 34f);

            UIKit.Titled("Cap", key, Loc.Get("mode.keeper.compost"), 18,
                         new Color(.92f, .96f, 1f, .62f), TextAnchor.MiddleCenter,
                         new Vector2(150f, 24f), new Vector2(.5f, .5f), new Vector2(0f, -42f),
                         3f, 0f);

            var hit = key.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;

            _compost = key.gameObject.AddComponent<Btn>();
            _compost.Setup(Compost, silent: true);
        }

        // ------------------------------------------------------------------ positions
        Vector2 Where(int index)
        {
            int x = index % _layout.Width, y = index / _layout.Width;
            return _origin + new Vector2(x * _cell, -y * _cell);
        }

        /// <summary>The first bed still waiting, or -1 when the grove has none left.</summary>
        int FirstBed()
        {
            if (_cells == null || Run == null) return -1;

            for (int i = 0; i < _cells.Length; i++)
                if (_layout.IsBed(i) && !Run.Board.IsOpen(i)) return i;

            return -1;
        }

        /// <summary>Where a lesson about a bed should point. The first one still waiting.</summary>
        public RectTransform BedAnchor
        {
            get
            {
                if (_cells == null) return null;

                int at = FirstBed();
                if (at >= 0) return _cells[at].Rt;

                return _cells.Length > 0 ? _cells[0].Rt : null;
            }
        }

        /// <summary>Where a lesson about a heartbed should point, or null if this grove has none.</summary>
        public RectTransform HeartbedAnchor
        {
            get
            {
                if (_cells == null) return null;

                for (int i = 0; i < _cells.Length; i++)
                    if (_layout.IsBed(i) && _layout.Wants(i) != Energy.None) return _cells[i].Rt;

                return null;
            }
        }

        /// <summary>Where a lesson about stone should point, or null if this grove has none.</summary>
        public RectTransform StoneAnchor
        {
            get
            {
                if (_cells == null) return null;

                for (int i = 0; i < _cells.Length; i++)
                    if (_layout.GroundAt(i) == KeeperGround.Stone) return _cells[i].Rt;

                return null;
            }
        }

        /// <summary>Where a lesson about the basket should point.</summary>
        public RectTransform BasketAnchor => _seats != null && _seats.Length > 0 ? _seats[0] : null;

        /// <summary>Where a lesson about composting should point.</summary>
        public RectTransform CompostAnchor
            => _compost ? (RectTransform)_compost.transform : null;

        /// <summary>
        /// Where a lesson about the prism should point: the first one in what the basket is
        /// showing, or the tile in hand if none of it is a prism yet.
        /// </summary>
        public RectTransform PrismAnchor
        {
            get
            {
                if (_seats == null || Run == null) return null;

                for (int i = 0; i < _seats.Length; i++)
                    if (Run.Ahead(i) == Energy.All) return _seats[i];

                return _seats.Length > 0 ? _seats[0] : null;
            }
        }

        // ------------------------------------------------------------------ the first tap
        /// <summary>
        /// Points a hand at the bed this grove wants opened, until the player taps anywhere.
        ///
        /// <para>
        /// <b>Shown, not described</b>, and it is <c>CoachStroke</c>'s argument for a mode that
        /// is tapped rather than dragged. The bloom lesson says what a bloom <em>is</em>; a
        /// player meeting the mode for the first time still has to find the one cell where the
        /// sentence is true, on a board of identical bare squares, with no undo and a counted
        /// basket. Every other cell on the opening grove is a legal move that teaches nothing.
        /// </para>
        /// <para>
        /// <b>It goes away on the first tap, whether or not that tap lands.</b> A pointer that
        /// only cleared on a successful planting would stand over a board the player is already
        /// working on the moment they tried something else, and a hand still asking for a cell
        /// somebody has just declined reads as the game not noticing them.
        /// </para>
        /// <para>
        /// Refused rather than pointed anywhere when the bed cannot actually take the tile in
        /// hand: a demonstration of a move that would be turned down is worse than none, which
        /// is the rule the coaching route is already held to. In practice that never fires —
        /// <c>KeeperScreen</c> only asks on a board nobody has touched yet.
        /// </para>
        /// </summary>
        public void CoachTap()
        {
            HideCoach();

            if (Run == null || _fx == null || _cells == null) return;

            int at = FirstBed();
            if (at < 0 || !Run.CanPlant(at)) return;

            // The cells and the effects layer are both stretched to the grove, so a cell's own
            // position is already the point a fingertip has to press.
            _coach = CoachHand.Tap(_fx, _cells[at].Rt.anchoredPosition, Pal.Gold, this);
        }

        /// <summary>Puts the pointer away. Safe to call when there is none.</summary>
        public void HideCoach()
        {
            if (_coach) Destroy(_coach.gameObject);
            _coach = null;
        }

        // ------------------------------------------------------------------ the pool
        Tile Take()
        {
            if (_spare.Count > 0)
            {
                var reused = _spare.Pop();
                reused.Rt.gameObject.SetActive(true);
                reused.Rt.localScale = Vector3.one;
                reused.Rt.localRotation = Quaternion.identity;
                return reused;
            }

            var body = UIKit.Img("Tile", _field, Art.Round(18), Color.white,
                                 Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);

            var sheen = UIKit.Img("Sheen", body.transform, Art.Glow(128, 2.4f),
                                  new Color(1, 1, 1, .16f), Vector2.one * _size * 1.4f,
                                  new Vector2(.5f, .5f), Vector2.zero);
            sheen.transform.SetAsFirstSibling();

            // Only ever shown on a prism, which is the one tile whose colour cannot say what it
            // is: white is what a bloom wears, so a plain white square would read as an opened
            // flower rather than as a tile carrying every channel.
            var prism = UIKit.Img("Prism", body.transform, Art.PrismRing(96, 8f),
                                  new Color(1, 1, 1, 0f), Vector2.one * _size * .72f,
                                  new Vector2(.5f, .5f), Vector2.zero);

            return new Tile
            {
                Body = body,
                Sheen = sheen,
                Prism = prism,
                Rt = (RectTransform)body.transform,
            };
        }

        void Give(Tile tile)
        {
            if (tile == null) return;

            Tween.KillAll(tile.Body);
            tile.Rt.gameObject.SetActive(false);
            tile.Rt.localScale = Vector3.one;
            tile.Body.color = Color.white;
            _spare.Push(tile);
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Puts what is drawn back in step with what the board holds, instantly and without
        /// animating anything.
        ///
        /// <c>Show</c> animates and <c>Refresh</c> does not — this is a Refresh, and it is used
        /// for the two moments there is nothing to replay: the board arriving and a restart.
        /// </summary>
        void Sync()
        {
            var board = Run.Board;

            for (int i = 0; i < _at.Length; i++)
            {
                int colour = board.At(i);

                if (colour == Energy.None)
                {
                    if (_at[i] != null) { Give(_at[i]); _at[i] = null; }
                    continue;
                }

                if (_at[i] == null) _at[i] = Take();

                _at[i].Rt.anchoredPosition = Where(i);
                Paint(_at[i], colour);
            }

            PaintSeams();
            PaintBeds();
        }

        static void Paint(Tile tile, int colour)
        {
            tile.Body.color = colour == Energy.All ? Pal.Radiance : Pal.EnergyColour(colour);
            tile.Prism.color = new Color(1, 1, 1, colour == Energy.All ? .85f : 0f);
        }

        /// <summary>
        /// Every seam on the grove, drawn in the blend it makes.
        ///
        /// <para>
        /// <b>This is the mode's rule, drawn.</b> "Unlike edges are worth something" is a sentence;
        /// a red tile and a green one with a bar of amber light between them is the thing itself,
        /// and it is what a player reads while deciding rather than a fact they have to hold.
        /// </para>
        /// <para>
        /// Repainted whole rather than added to. A grove is at most eighty edges and a repaint is
        /// two loops over integers, where tracking which seams a planting made would be a second
        /// answer for the drawing and the board to disagree about.
        /// </para>
        /// </summary>
        void PaintSeams()
        {
            int used = 0;
            var board = Run.Board;

            for (int y = 0; y < _layout.Height; y++)
                for (int x = 0; x < _layout.Width; x++)
                {
                    int at = _layout.Index(x, y);
                    int here = board.At(at);
                    if (here == Energy.None) continue;

                    if (x + 1 < _layout.Width) used = Seam(at, at + 1, true, used);
                    if (y + 1 < _layout.Height) used = Seam(at, at + _layout.Width, false, used);
                }

            for (int i = used; i < _seams.Count; i++)
                if (_seams[i]) _seams[i].gameObject.SetActive(false);
        }

        int Seam(int a, int b, bool across, int used)
        {
            int one = Run.Board.At(a), two = Run.Board.At(b);
            if (one == Energy.None || two == Energy.None || one == two) return used;

            var image = SeamAt(used);
            var rt = (RectTransform)image.transform;

            var mid = (Where(a) + Where(b)) * .5f;
            float length = _cell - _size + 16f;

            rt.anchoredPosition = mid;
            rt.sizeDelta = across ? new Vector2(length, _size * .62f)
                                  : new Vector2(_size * .62f, length);

            image.color = Pal.A(Pal.EnergyColour(one | two), .92f);
            image.gameObject.SetActive(true);

            return used + 1;
        }

        Image SeamAt(int index)
        {
            while (_seams.Count <= index)
            {
                var made = UIKit.Img("Seam", _seamNode, Art.Round(10), Color.white,
                                     Vector2.one * 8f, new Vector2(.5f, .5f), Vector2.zero);
                _seams.Add(made);
            }

            return _seams[index];
        }

        /// <summary>
        /// Every bed's halo, in the colours it is still short of.
        ///
        /// <para>
        /// <b>The halo answers "what does this still want", which is the only question a player
        /// asks of a bed.</b> An empty bed wears the colour it insists on, or cream for one that
        /// takes any; a planted bed wears whatever its neighbours have not brought it yet; and an
        /// open one has no halo at all, because a bed that is finished should stop asking.
        /// </para>
        /// </summary>
        void PaintBeds()
        {
            var board = Run.Board;

            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                if (cell.Halo == null) continue;

                if (board.IsOpen(i))
                {
                    // A bed that is finished stops asking. Everything it was drawing to say so
                    // goes, the lantern included - a bed still glowing under an opened flower
                    // reads as a bed that has not been done.
                    cell.Halo.color = new Color(1, 1, 1, 0f);
                    if (cell.Bud) cell.Bud.color = new Color(1, 1, 1, 0f);
                    if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);
                    continue;
                }

                bool bare = board.At(i) == Energy.None;
                int wants = bare ? _layout.Wants(i) : board.Wanting(i);
                var tint = wants == Energy.None ? Pal.Cream : Pal.EnergyColour(wants);

                cell.Halo.color = Pal.A(tint, bare ? .55f : .80f);
                if (cell.Bud) cell.Bud.color = Pal.A(tint, bare ? .70f : 0f);
                if (cell.Glow) cell.Glow.color = Pal.A(tint, bare ? .16f : .22f);
            }
        }

        /// <summary>The tile in hand, what is behind it, and how many are left.</summary>
        void PaintBasket()
        {
            if (Run == null || _queue == null) return;

            for (int i = 0; i < _queue.Length; i++)
            {
                int colour = Run.Ahead(i);
                bool any = colour != Energy.None;

                _queue[i].gameObject.SetActive(any);
                if (!any) continue;

                _queue[i].color = colour == Energy.All ? Pal.Radiance : Pal.EnergyColour(colour);

                if (_prisms[i])
                    _prisms[i].color = new Color(1, 1, 1, colour == Energy.All ? .85f : 0f);
            }

            if (_count)
            {
                bool bounded = Run.Basket.Bounded;
                _count.text = bounded ? Run.Basket.Left.ToString()
                                      : Loc.Get("mode.keeper.basket_free");

                _count.color = !bounded ? Pal.Cream
                             : Run.Basket.Pressure == KeeperPressure.Critical ? Pal.Ember
                             : Run.Basket.Pressure == KeeperPressure.Low ? Pal.Gold
                             : Pal.Cream;
            }

            if (_compostKey)
                _compostKey.color = new Color(1, 1, 1, Run.CanCompost ? .08f : .03f);
        }

        void Enter()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                int x = i % _layout.Width, y = i / _layout.Width;
                float delay = KeeperTempo.EntranceDelay(x, y, _layout.Width, _layout.Height);

                Tween.Pop(_cells[i].Rt, 0f, .38f, delay);
                if (_at[i] != null) Tween.Pop(_at[i].Rt, 0f, .40f, delay + .04f);
            }
        }

        // ------------------------------------------------------------------ the ghost
        void ShowGhost(int index)
        {
            _hovered = index;

            if (!Playable || !Run.CanPlant(index)) { HideGhost(); return; }

            int colour = Run.Next;
            var gain = Run.Preview(index);

            // Everything the ghost says, in one integer. Update asks every frame — the board moves
            // under a held finger — and rebuilding it unconditionally would restart the ring's
            // pulse on every one of them, which draws as a ring that never moves.
            int key = ((index * 8 + colour) * 8 + gain.Blooms) * 8 + gain.Beds;
            if (key == _ghostKey) return;
            _ghostKey = key;

            var at = Where(index);
            var tint = colour == Energy.All ? Pal.Radiance : Pal.EnergyColour(colour);

            ((RectTransform)_ghost.transform).anchoredPosition = at;
            ((RectTransform)_ghostRing.transform).anchoredPosition = at;
            ((RectTransform)_ghostCount.transform).anchoredPosition = at + new Vector2(0f, _cell * .06f);

            _ghost.color = Pal.A(tint, .46f);

            // Gold when something opens, cream when this is only ground. The number is the whole
            // point of the preview — it is what turns "somewhere legal" into "the best cell on the
            // board" without the player having to count neighbours.
            bool opens = gain.Blooms > 0;
            _ghostRing.color = Pal.A(opens ? Pal.Gold : Pal.Cream, opens ? .95f : .40f);

            _ghostCount.gameObject.SetActive(opens);
            if (opens)
            {
                _ghostCount.text = gain.Blooms > 1 ? Loc.Format("mode.keeper.multiplier", gain.Blooms)
                                                   : string.Empty;
                _ghostCount.fontSize = KeeperFlourish.PointsFor(gain.Blooms) / 2;
                _ghostCount.color = Pal.Gold;
            }

            // Breathe kills and restores its own channel, so re-asking for one on every change
            // of the ghost is a supersede rather than a second pulse fighting the first.
            Tween.Breathe(_ghostRing.transform, opens ? .09f : .04f, .9f);
        }

        void HideGhost()
        {
            _hovered = -1;
            _ghostKey = int.MinValue;

            if (_ghost) _ghost.color = new Color(1, 1, 1, 0f);
            if (_ghostRing) _ghostRing.color = new Color(1, 1, 1, 0f);
            if (_ghostCount) _ghostCount.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ playing
        void Plant(int index)
        {
            // Whatever comes of it, the player has answered the hand. See CoachTap.
            HideCoach();

            if (!Playable || !Run.CanPlant(index)) { Refuse(index); return; }

            int colour = Run.Next;
            var gain = Run.Plant(index, _bloomed);

            // The run is what decides whether a tile landed, and it is the only thing that took
            // one from the basket. Reading the board back rather than the gain, because a tile
            // laid in the open makes no seam and no bloom and is still a tile that landed.
            if (!Run.Board.Standing(index)) return;

            Spent();

            _busy = true;
            HideGhost();

            StartCoroutine(PlayPlanting(index, colour, gain, ToArray(_bloomed)));
        }

        static int[] ToArray(List<int> from)
        {
            var copy = new int[from.Count];
            for (int i = 0; i < from.Count; i++) copy[i] = from[i];
            return copy;
        }

        /// <summary>
        /// A tap that cannot be honoured, said rather than swallowed.
        ///
        /// <para>
        /// It matters most on the one cell that refuses for a reason: a heartbed takes only its
        /// own colour, and a tap that simply does nothing there reads as a broken button. The cell
        /// shakes and its ring flares in the colour it is holding out for, which is the answer to
        /// the question that was actually asked.
        /// </para>
        /// </summary>
        void Refuse(int index)
        {
            if (!Playable || index < 0 || index >= _cells.Length) return;

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            Tween.Shake(cell.Rt, 7f, .26f);

            if (cell.Halo)
            {
                var halo = cell.Halo;
                var was = halo.color;
                Tween.Tint(halo, Pal.A(was, 1f), .10f)
                     .OnDone(() => { if (halo) Tween.Tint(halo, was, .24f); });
            }

            Audio.Sfx("rotate_b", .28f, .8f);

            // The shake is the whole answer everywhere else: stone is drawn as a rock, an
            // occupied cell already holds a tile, and a heartbed has just flared the colour it
            // is holding out for. Bare ground away from the grove looks exactly like bare
            // ground beside it, so there the shake is a control that did nothing — which is the
            // state a rule the board cannot show always produces (invariant 20g).
            if (Unreachable != null && Run != null && Run.Basket.Any
                && Run.Board.Adrift(Run.Next, index))
                Unreachable();
        }

        /// <summary>Spends a tile without planting it, and says so.</summary>
        public void Compost()
        {
            HideCoach();

            if (!Playable || !Run.CanCompost) return;

            int colour = Run.Next;
            if (!Run.Compost()) return;

            Spent();

            // The tile leaves the basket downward rather than simply changing colour, so that a
            // move which alters nothing on the board still looks like a move.
            if (_queue != null && _queue.Length > 0 && _queue[0])
            {
                var mote = UIKit.Img("Composted", _tray, Art.Round(14),
                                     Pal.A(colour == Energy.All ? Pal.Radiance
                                                                : Pal.EnergyColour(colour), .95f),
                                     Vector2.one * KeeperBand.HandSize * .8f,
                                     new Vector2(.5f, .5f),
                                     new Vector2(KeeperBand.HandX, 0f));

                var rt = (RectTransform)mote.transform;
                var from = rt.anchoredPosition;

                Tween.Run(.34f, Ease.InQuad, t =>
                {
                    if (!rt) return;
                    rt.anchoredPosition = from + new Vector2(0f, -70f * t);
                    rt.localScale = Vector3.one * (1f - t * .7f);
                    var c = mote.color; c.a = .95f * (1f - t); mote.color = c;
                }, mote).OnDone(() => { if (mote) Destroy(mote.gameObject); });
            }

            Audio.Sfx("whoosh", .30f, .78f);

            PaintBasket();
            Changed?.Invoke();
            Settle();
        }

        /// <summary>The first tile spent is what makes the run owed for.</summary>
        void Spent()
        {
            if (_committed) return;

            _committed = true;
            Committed?.Invoke();
        }

        /// <summary>
        /// The tile lands, the seams light, and whatever it finished opens.
        ///
        /// The whole sequence is bounded by <c>KeeperTempo</c>, which is what stops a five-flower
        /// flourish taking five times as long as a one-flower one: the board is latched for
        /// exactly this long, so an unbounded cascade is an unbounded freeze.
        /// </summary>
        IEnumerator PlayPlanting(int index, int colour, KeeperGain gain, int[] bloomed)
        {
            var tile = _at[index] = Take();
            tile.Rt.anchoredPosition = Where(index);
            Paint(tile, colour);

            tile.Rt.localScale = Vector3.one * 1.45f;
            Tween.Scale(tile.Rt, 1f, KeeperTempo.Land, Ease.OutBack);
            Tween.Fade(tile.Body, 1f, KeeperTempo.Land * .6f);

            Audio.SfxVaried("pop", .42f);

            yield return new WaitForSecondsRealtime(KeeperTempo.Land);
            if (!this) yield break;

            // The seams first, and separately: a planting that makes only seams is still a move
            // worth watching, and on a board where most of them are, this is the feedback.
            PaintSeams();
            PaintBeds();
            PaintBasket();
            Changed?.Invoke();

            if (gain.Seams > 0)
            {
                Tween.Punch(tile.Rt, .16f, KeeperTempo.Squash);
                Audio.Sfx("lit", .30f, 1.15f + gain.Seams * .05f);
            }

            if (bloomed.Length == 0)
            {
                _busy = false;
                Settle();
                yield break;
            }

            yield return new WaitForSecondsRealtime(KeeperTempo.Seam * .5f);
            if (!this) yield break;

            yield return Cascade(bloomed);
            if (!this) yield break;

            _busy = false;
            Settle();
        }

        /// <summary>
        /// The flowers opening, one after another, inside one bounded cascade.
        ///
        /// The order is the order <c>KeeperBoard.Plant</c> reported them in — the planted cell
        /// first, then the neighbours it finished — so the eye is led outward from the tile the
        /// player just laid rather than arriving everywhere at once.
        /// </summary>
        IEnumerator Cascade(int[] bloomed)
        {
            int count = bloomed.Length;
            float petal = KeeperTempo.Petal(count);

            for (int i = 0; i < count; i++)
            {
                int at = bloomed[i];
                OpenFlower(at, i + 1, count);

                if (KeeperFlourish.Counts(i + 1)) ShowFlourish(i + 1, count);

                if (KeeperTempo.Shake(i + 1) > 0f) ShakeBoard(KeeperTempo.Shake(i + 1));

                yield return new WaitForSecondsRealtime(petal);
                if (!this) yield break;
            }

            PaintBeds();
            Changed?.Invoke();

            string word = KeeperFlourish.WordKey(count);
            if (word != null)
            {
                yield return Fanfare(count, word);
                if (!this) yield break;
            }
            else if (KeeperFlourish.Counts(count))
            {
                // A counted flourish that earns no word still has to come down. Only the fanfare
                // used to clear it, so a two — by far the commonest — left its number sitting on
                // the grove until the next planting happened to overwrite it, which reads as a
                // label rather than as a celebration.
                yield return new WaitForSecondsRealtime(KeeperTempo.CountPop(count) * 2f);
                if (!this) yield break;

                HideFlourish();
            }
        }

        /// <summary>
        /// One tile bursting into bloom: the flower, the rays behind it and the ring going out.
        ///
        /// A bed gets the same flower one size larger and its halo thrown outward, because a bed
        /// opening is the only thing on this board that is progress — every other bloom is
        /// beautiful and optional, and the two must not read the same.
        /// </summary>
        void OpenFlower(int at, int nth, int of)
        {
            var tile = _at[at];
            if (tile == null) return;

            bool bed = _layout.IsBed(at) && Run.Board.IsOpen(at);
            var where = Where(at);
            float size = _size * (bed ? 1.35f : 1.05f);

            var rays = UIKit.Img("Rays", _fx, Art.Rays(256, bed ? 14 : 10),
                                 Pal.A(Pal.Radiance, .55f), Vector2.one * size * 2.1f,
                                 new Vector2(.5f, .5f), where);

            var flower = UIKit.Img("Flower", _fx, Art.Bloom(128, bed ? 8 : 6, 1f),
                                   Pal.A(Pal.Radiance, .98f), Vector2.one * size,
                                   new Vector2(.5f, .5f), where);

            var rt = (RectTransform)flower.transform;
            var raysRt = (RectTransform)rays.transform;

            float spin = bed ? 42f : 24f;
            float life = KeeperTempo.Petal(of) * 2.4f;

            Tween.Run(life, Ease.OutQuint, t =>
            {
                if (!flower) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.05f, 1f, Mathf.Min(1f, t * 2.2f));
                rt.localRotation = Quaternion.Euler(0, 0, spin * t);
                flower.color = Pal.A(Pal.Radiance, t < .55f ? .98f : .98f * (1f - (t - .55f) / .45f));
            }, flower).OnDone(() => { if (flower) Destroy(flower.gameObject); });

            Tween.Run(life * .8f, Ease.OutQuad, t =>
            {
                if (!rays) return;
                raysRt.localScale = Vector3.one * Mathf.Lerp(.2f, 1.25f, t);
                raysRt.localRotation = Quaternion.Euler(0, 0, -spin * .6f * t);
                rays.color = Pal.A(Pal.Radiance, .5f * (1f - t));
            }, rays).OnDone(() => { if (rays) Destroy(rays.gameObject); });

            Shockwave(where, bed ? Pal.Gold : Pal.Radiance, size * (bed ? 3.6f : 2.6f), life * .7f);
            Burst.Sparks(_fx, where, bed ? Pal.Gold : Pal.Radiance, bed ? 14 : 8,
                         bed ? 220f : 150f, bed ? 22f : 15f, life * .9f);

            // The tile itself goes to white and stays there: a bloomed tile is a bloomed tile for
            // the rest of the run, and it is the record of what the player built.
            Tween.Tint(tile.Body, Pal.Radiance, KeeperTempo.Petal(of) * .7f);
            Tween.Punch(tile.Rt, bed ? .42f : .28f, KeeperTempo.Petal(of) * 1.1f);

            Audio.Sfx(bed ? "chime" : "lit", bed ? .62f : .42f, KeeperTempo.Pitch(nth));
        }

        void Shockwave(Vector2 at, Color tint, float to, float seconds)
        {
            var ring = UIKit.Img("Wave", _fx, Art.Ring(128, 6f), Pal.A(tint, .8f),
                                 Vector2.one * (_size * .5f), new Vector2(.5f, .5f), at);

            var rt = (RectTransform)ring.transform;

            Tween.Run(seconds, Ease.OutQuint, t =>
            {
                if (!ring) return;
                rt.sizeDelta = Vector2.one * Mathf.Lerp(_size * .5f, to, t);
                ring.color = Pal.A(tint, .8f * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        void ShakeBoard(float amount)
        {
            if (_plate == null || _grid == null) return;
            Tween.Shake(_grid, amount, .26f);
        }

        /// <summary>
        /// The running count, while the cascade is still going.
        ///
        /// It appears wave by wave rather than at the end, because nobody watching the third
        /// flower open knows yet whether there is a fourth — which is the whole tension of a big
        /// flourish, and it is lost entirely if the number arrives once it is over.
        /// </summary>
        void ShowFlourish(int nth, int of)
        {
            if (_flourish == null)
            {
                // Above the top row rather than over the middle of the grove: it is drawn last
                // and would otherwise sit on the very flowers it is counting.
                // The origin is the *top* row and y climbs, so this is above it. Getting the sign
                // wrong here put the count under the middle of the grove, which is exactly where
                // it must not be.
                float above = _origin.y + _cell * .62f;

                _flourish = UIKit.Titled("Flourish", _fx, "", 80, Pal.Gold, TextAnchor.MiddleCenter,
                                         new Vector2(520f, 160f), new Vector2(.5f, .5f),
                                         new Vector2(0f, above), 6f, 6f);
            }

            _flourish.gameObject.SetActive(true);
            _flourish.text = Loc.Format("mode.keeper.multiplier", nth);
            _flourish.fontSize = KeeperFlourish.PointsFor(nth);
            _flourish.color = Pal.Gold;

            // Pop rather than a bare Scale, because a count that arrives while the previous one
            // is still springing would otherwise take a half-grown size as the one to spring to.
            var rt = (RectTransform)_flourish.transform;
            rt.localScale = Vector3.one;
            Tween.Pop(rt, .4f, KeeperTempo.CountPop(of));
        }

        /// <summary>
        /// The word at the end of a named flourish, and the one beat here outside the cascade's
        /// own ceiling — it is the pay-off, and it happens once the board is finished moving.
        /// </summary>
        IEnumerator Fanfare(int blooms, string wordKey)
        {
            if (_flourish == null) yield break;

            _flourish.text = Loc.Get(wordKey);
            _flourish.fontSize = KeeperFlourish.WordPointsFor(blooms);
            _flourish.color = Pal.Radiance;

            var rt = (RectTransform)_flourish.transform;
            rt.localScale = Vector3.one;
            Tween.Pop(rt, .6f, .22f);

            if (blooms >= KeeperTempo.BigFrom)
            {
                Flow.Flash(Pal.A(Pal.Radiance, .34f), .28f, .38f);
                Burst.Sparks(_fx, Vector2.zero, Pal.Gold, 18, 380f, 24f, .8f);
            }

            Audio.Sfx("win", .62f, 1f + KeeperFlourish.Tier(blooms) * .06f);

            yield return new WaitForSecondsRealtime(KeeperTempo.Fanfare);
            if (!this) yield break;

            HideFlourish();
        }

        void HideFlourish()
        {
            if (_flourish == null) return;

            var text = _flourish;
            Tween.Run(.24f, Ease.InQuad, t =>
            {
                if (!text) return;
                text.color = Pal.A(text.color, 1f - t);
                text.transform.localScale = Vector3.one * (1f - t * .3f);
            }, _flourish).OnDone(() => { if (text) text.gameObject.SetActive(false); });
        }

        // ------------------------------------------------------------------ the ending
        /// <summary>
        /// Reads the run and reports it, once.
        ///
        /// Called on the edges that can end one and never from a poll — the same argument
        /// <c>FallView.Settle</c> makes, and it matters here for the same reason: the verdict
        /// walks the grove.
        /// </summary>
        void Settle()
        {
            if (_over || Run == null) return;

            var verdict = Run.Verdict;
            if (!verdict.IsOver) return;

            _over = true;
            Locked = true;
            HideGhost();

            if (verdict.IsWon)
            {
                Finishing?.Invoke();
                StartCoroutine(Triumph());
                return;
            }

            if (verdict.Ending == KeeperEnding.Overgrown) StartCoroutine(Overgrown());
            else Lost?.Invoke();
        }

        /// <summary>
        /// Every bed is open. The grove lights up from the middle outward, which is the one
        /// flourish this mode has that no other could: the thing being celebrated is the shape the
        /// player made, so the celebration walks it.
        /// </summary>
        IEnumerator Triumph()
        {
            Audio.Sfx("win", .9f);

            float far = Mathf.Max(_layout.Width, _layout.Height);

            for (int i = 0; i < _at.Length; i++)
            {
                if (_at[i] == null) continue;

                int x = i % _layout.Width, y = i / _layout.Width;
                float delay = KeeperTempo.EntranceDelay(x, y, _layout.Width, _layout.Height)
                            * (KeeperTempo.Ripple / KeeperTempo.Entrance);

                var tile = _at[i];
                Tween.Punch(tile.Rt, .26f, .34f).Delay(delay);

                var sheen = tile.Sheen;
                Tween.Run(.36f, Ease.OutQuad, t =>
                {
                    if (!sheen) return;
                    sheen.color = new Color(1, 1, 1, .16f + .5f * Mathf.Sin(t * Mathf.PI));
                }, sheen).Delay(delay);
            }

            if (_plate) Tween.Punch(_plate, .06f, .5f);

            yield return new WaitForSecondsRealtime(KeeperTempo.Ripple);
            if (!this) yield break;

            Solved?.Invoke();
        }

        /// <summary>
        /// The grove has nowhere left to grow. Said on the board, in the place the rule lives,
        /// before the panel arrives — so a player knows what happened rather than being told.
        ///
        /// The beds still waiting are the report: they flare and then go grey, which is the exact
        /// difference between this ending and running out of tiles.
        /// </summary>
        IEnumerator Overgrown()
        {
            Audio.Sfx("pop", .5f, .55f);
            ShakeBoard(18f);

            for (int i = 0; i < _cells.Length; i++)
            {
                if (!_layout.IsBed(i) || Run.Board.IsOpen(i)) continue;

                var halo = _cells[i].Halo;
                if (halo == null) continue;

                Tween.Tint(halo, Pal.A(Pal.Rose, .95f), .18f);
                Tween.Punch(_cells[i].Rt, .3f, .4f);
            }

            Flow.Flash(Pal.A(Pal.Rose, .30f), .28f, .38f);

            yield return new WaitForSecondsRealtime(.42f);
            if (!this) yield break;

            Lost?.Invoke();
        }

        // ------------------------------------------------------------------ one more go
        /// <summary>
        /// More tiles, because a continue was paid for. The grove stands exactly as it stood.
        ///
        /// It re-reads its own verdict afterwards, so if a grant somehow left the run lost the
        /// fail state fires again and the player is <em>asked again</em> rather than silently left
        /// on a dead board.
        /// </summary>
        public void Grant(int tiles)
        {
            if (Run == null || tiles <= 0) return;

            Run.Grant(tiles);

            _over = false;
            Locked = false;

            PaintBasket();
            Changed?.Invoke();

            if (_count) Tween.Punch(_count.transform, .35f, .4f);

            Settle();
        }

        // ------------------------------------------------------------------ housekeeping
        /// <summary>
        /// Keeps the ghost honest while a finger is held still.
        ///
        /// The board moves underneath a held finger — a cascade lands, the procession advances —
        /// so a ghost drawn once at the moment of touch would go on promising an opening that is
        /// no longer there.
        /// </summary>
        void Update()
        {
            if (_hovered < 0) return;

            if (!Playable) { HideGhost(); return; }

            ShowGhost(_hovered);
        }
    }
}
