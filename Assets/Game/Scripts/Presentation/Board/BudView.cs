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
    /// <b>Budburst's board.</b> A grove of coloured flowers, critters shut in cocoons, and one
    /// tap that runs.
    ///
    /// <para>
    /// <b>Everything here exists to make the chain visible as it travels.</b> A bunch going off
    /// is not a flash and a jump to the end state: it throws a bolt of its own colour at every
    /// flower it touches, the bolt lands, and that flower visibly <em>turns</em> to the blend it
    /// has become. So a five-wave chain is five legible steps crossing the grove rather than one
    /// event, the count climbs while it is still running, and the pitch and the shake climb with
    /// it.
    /// </para>
    /// <para>
    /// <b>The burst art is real VFX rather than generated shapes</b> — a flash, a fire flipbook,
    /// an expanding ring, a lightning trail and the fork it lands with, all cut from a licensed
    /// pack by <c>Tools/make_bud_fx.py</c>. The pack's own prefabs cannot be used (world-space
    /// particle systems under a <c>ScreenSpaceOverlay</c> canvas draw under everything, and its
    /// shadergraph materials need a pipeline this project does not have), but its
    /// <em>textures</em> are exactly what a UI game draws with.
    /// </para>
    /// <para>
    /// <b>Which textures, though, is the part that went wrong once.</b> The first cut of this
    /// art took a colour ramp for a flare, a bubble mask for a bolt and a noise field for a
    /// shockwave — every one of them loaded, addressed, audited and drew, and the animations
    /// were simply made of the wrong pictures. Nothing in this repository can see that state, so
    /// the cut is a tool with a contact sheet rather than a folder somebody filled by hand: read
    /// the header of <c>make_bud_fx.py</c> before changing a single one of these names.
    /// </para>
    /// <para>
    /// <b>Three things carry the chain, and each does a different job.</b> A burst is a
    /// <em>flash</em> (instant, white, gone before the eye settles), a <em>fire</em> (the body
    /// of it, a tinted plume that takes most of a beat) and a <em>ring</em> (the reach of it,
    /// which is what says how far the wave got). What travels between two cells is a
    /// <em>bolt</em> that lashes out along its own path rather than a sprite sliding down it —
    /// a thing sliding reads as a thrown object and a thing striking reads as a chain reaction,
    /// which is what this is.
    /// </para>
    /// <para>
    /// <b>What is drawn is driven by what the model reported, never by re-reading the board.</b>
    /// <c>BudRun.Tap</c> settles the whole chain and hands back every burst and every ripening
    /// with the wave it belonged to, so by the time anything is animated the model is already at
    /// the end and the view is replaying facts. <see cref="Sync"/> is what puts the drawing back
    /// in step if anything ever interrupts.
    /// </para>
    /// </summary>
    public sealed class BudView : MonoBehaviour
    {
        /// <summary>Raised whenever anything the readouts count has moved.</summary>
        public Action Changed { get; set; }

        /// <summary>Every critter is out. Raised once, after the thicket has finished settling.</summary>
        public Action Solved { get; set; }

        /// <summary>The run is over and lost. The screen reads the verdict for which way.</summary>
        public Action Lost { get; set; }

        /// <summary>The first tap has been spent, so the run is now owed for.</summary>
        public Action Committed { get; set; }

        /// <summary>
        /// The closing chain has begun, so nothing else may end this run — <c>KeeperView</c>'s
        /// rule: the run is decided when the last cocoon opens and the panel arrives a beat later
        /// while the thicket is still going off.
        /// </summary>
        public Action Finishing { get; set; }

        /// <summary>Input off. Set by every panel that goes over this board.</summary>
        public bool Locked { get; set; }

        /// <summary>The run has not been allowed to begin yet — <c>RunHold</c>'s half.</summary>
        public bool Held { get; set; } = true;

        public BudRun Run { get; private set; }

        public bool TakingInput => Run != null && !Locked && !_busy && !_over;
        public bool Playable => TakingInput && !Held;

        // ------------------------------------------------------------------ the furniture
        BudLayout _layout;
        RectTransform _host, _grid, _field, _residents, _fx, _tray, _plate;
        Text _count, _left, _chain;

        Cell[] _cells;

        /// <summary>
        /// Who is out, standing where they were let out, by the cell they came from.
        ///
        /// <para>
        /// <b>A freed critter is a resident of the grove and no longer part of a cell, and that
        /// is the whole of why it is kept here.</b> Freeing one leaves its square <em>bare</em> in
        /// the model, so the grove immediately falls into it — and while the critter was drawn as
        /// a child of that cell, the flower landing on it took the critter down with it and
        /// <see cref="PaintCell"/> was free to paint a sleeping critter straight over the top of
        /// somebody the player had just let out. It was reported as critters falling and as
        /// flowers falling through them, and both are the same fault: the reward was being kept
        /// in the one place the board is allowed to rearrange.
        /// </para>
        /// <para>
        /// One per cell, because a cocoon can fall into a square somebody has already been freed
        /// from and be opened there too; the second one replaces the first rather than standing
        /// inside it.
        /// </para>
        /// </summary>
        Image[] _freed;

        readonly List<BudPulse> _pulses = new List<BudPulse>(64);
        readonly List<BudWash> _washes = new List<BudWash>(64);
        readonly List<BudDrop> _drops = new List<BudDrop>(64);
        readonly List<BudPulse> _peek = new List<BudPulse>(64);
        readonly List<int> _beside = new List<int>(4);

        Image _handChip;
        Image[] _queue;

        RectTransform _mark;
        int _hintAt = -1;
        Action _hintDone;

        /// <summary>
        /// Which mark is standing, counted rather than named by its cell.
        ///
        /// A cell is not an identity: two hints spent in a row can name the same flower, and the
        /// first one's give-up timer would then take the second one's mark away early.
        /// </summary>
        int _hintToken;

        float _cell, _size;
        Vector2 _origin;
        bool _busy, _over, _committed;
        int _hovered = -1, _ghostKey = int.MinValue;

        /// <summary>One cell: the ground, whatever is standing on it, and its halo.</summary>
        sealed class Cell
        {
            public RectTransform Rt;

            /// <summary>
            /// Everything that <em>travels</em> when the grove falls, under one transform.
            ///
            /// <para>
            /// <b>A fall moves this and nothing else, and that is the whole of why it lands.</b>
            /// The five pictures standing in a cell — the flower, its heart, its glow, the
            /// cocoon and the critter inside it — used to be moved one by one by a tween owned
            /// by whichever of them was falling. Everything else in this file that touches a
            /// flower kills that owner outright (<see cref="PaintCell"/> and
            /// <see cref="ThrowFlower"/> both call <c>Tween.KillAll(cell.Bud)</c>), and a killed
            /// tween never reaches its <c>OnDone</c> — so a flower that fell into a cell and
            /// burst on the next wave, or one still falling when the chain's last repaint ran,
            /// left all five pictures stranded at whatever offset the interruption caught them
            /// holding, for the rest of the run. That is the "flowers get stuck half way" this
            /// replaces, and it was reported from play because nothing here could see it: the
            /// board, the par and every gate are exactly right, and only the drawing is wrong.
            /// </para>
            /// <para>
            /// One transform, owned by nobody else, is what makes the rule statable: the fall
            /// supersedes itself on its own channel, and it declares where an interrupted one
            /// lands (<see cref="Tw.OnAbandon"/>) rather than being abandoned mid-air. The cell's
            /// own square — its ground tint, its hit target and its <c>Btn</c> — stays exactly
            /// where the layout put it, which is the one thing a falling board must not lose.
            /// </para>
            /// </summary>
            public RectTransform Piece;

            public Image Soil, Bud, Halo, Glow, Pod, Critter, Ring;
            public int Drawn = -1;

            /// <summary>Whether this flower is currently breathing because a tap on it pops.</summary>
            public bool Pops;
        }

        // ------------------------------------------------------------------ colour
        /// <summary>
        /// What a flower looks like — its colour, and the sprite that carries it.
        ///
        /// Both are <c>BudFlower</c>'s, because the band and the legend above the grove draw
        /// flowers too and three answers to "what does a flower look like" is two too many.
        /// </summary>
        static Color Petal(int mask) => BudFlower.Tint(mask);

        static Sprite Bloom(int mask) => BudFlower.Petals(mask);

        // ------------------------------------------------------------------ building
        public void Begin(RectTransform host, BudLayout layout, int budget)
        {
            _host = host;
            _layout = layout;

            StopAllCoroutines();
            Tween.KillAll(this);

            Run = new BudRun(layout, budget);

            Held = true;
            Locked = false;

            _busy = false;
            _over = false;
            _committed = false;
            _hovered = -1;
            _ghostKey = int.MinValue;
            _chain = null;
            _word = null;
            _mark = null;
            _hintAt = -1;
            _hintToken++;

            // A restart is not a hint being taken, so the caller is never told. What it *is* is
            // the mark's cell ceasing to exist, so the pending callback is dropped rather than
            // fired at a board that has gone.
            _hintDone = null;

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;

            float usableH = rect.height - BudBand.BandHeight;
            _cell = Mathf.Min(rect.width / layout.Width, usableH / layout.Height);
            _size = _cell * .92f;

            _grid = UIKit.Node("Thicket", host);
            UIKit.StretchTo(_grid, 0f, BudBand.BandHeight, 0f, 0f);

            _origin = new Vector2(-(layout.Width - 1) * _cell * .5f,
                                  (layout.Height - 1) * _cell * .5f);

            BuildGround();
            BuildBand(host);

            Sync();
            Enter();
        }

        /// <summary>
        /// How far the board's plate stands out past the grid itself, which is what the player
        /// sees as the edge of the grove and therefore where the clip has to be.
        ///
        /// <para>
        /// <b>It was a derived <em>overhang</em> instead — room left above the top row so a
        /// flower there could reach its full wind-up without being cut — and that is the wrong
        /// trade.</b> A quarter of a cell is invisible when a flower is swelling in it and
        /// perfectly visible when a flower is falling through it, which is exactly what came
        /// back: <em>"I still see them coming out of the grid slightly"</em>. So the clip sits on
        /// the plate's own lip, nothing enters the board from anywhere the player can see, and
        /// what it costs is about a tenth off the top of a top-row flower at the deepest
        /// wind-up — a moment, on one row, against something that was happening on every fall.
        /// </para>
        /// </summary>
        const float PlateLip = 13f;

        void BuildGround()
        {
            float w = _layout.Width * _cell, h = _layout.Height * _cell;

            var plate = UIKit.Img("Plate", _grid, Art.Round(30), new Color(.04f, .07f, .05f, .74f),
                                  new Vector2(w + 26f, h + 26f), new Vector2(.5f, .5f),
                                  Vector2.zero);
            _plate = (RectTransform)plate.transform;

            UIKit.Img("Edge", _plate, Art.RoundOutline(30, 3f), new Color(.86f, 1f, .74f, .14f),
                      new Vector2(w + 26f, h + 26f), new Vector2(.5f, .5f), Vector2.zero);

            // **The grove is clipped to itself, so nothing is ever seen falling in from
            // outside it.** A flower that grew back enters from `Grown() x _cell` above the cell
            // it lands in — three, four, five cells above the top row on a column that lost that
            // many — and it was drawn the whole way, hanging in the air over the board with
            // nothing under it. `_grid` is the whole screen below the band, so masking that
            // clips nothing; the mask has to be a node the size of the board.
            //
            // **It is deliberately not the same rect as the board**, and the margins are not
            // symmetric because what they are for is not. Above, it stops a little over a
            // quarter of a cell past the top row, which is the least that lets a top-row flower
            // reach its full wind-up (a flower is drawn at .78 of `_size` and swells to 2.20, so
            // it overhangs its own cell by .29 of one) and the most that still hides an entry.
            // At the sides and below there is nothing to hide, so they are generous and no
            // gesture is ever cut there at all.
            //
            // Only the *field* is masked. `_fx` and `_residents` are siblings of it, so a
            // burst's petals, its rings, the fireworks and a freed critter all still cross the
            // edge of the board — which they must, since leaving the board is the whole of what
            // makes the fireworks read as fireworks.
            float skirt = _cell * 1.5f;
            float lift = (skirt - PlateLip) * .5f;

            var clip = UIKit.Box("Field", _grid,
                                 new Vector2(w + _cell * 2.4f, h + PlateLip + skirt),
                                 new Vector2(.5f, .5f), new Vector2(0f, -lift));
            clip.gameObject.AddComponent<RectMask2D>();

            _field = UIKit.Box("Buds", clip, new Vector2(w, h), new Vector2(.5f, .5f),
                               new Vector2(0f, lift));

            // **Freed critters stand above the grove rather than in it.** See `Free`: a critter
            // the player has let out is not a tenant of a cell any more, so it is drawn on its
            // own layer over the field and under the fireworks — where nothing that falls can
            // drag it, cover it, or paint over it.
            _residents = UIKit.Node("Freed", _grid);
            UIKit.StretchTo(_residents, 0f, 0f, 0f, 0f);

            _fx = UIKit.Node("Fx", _grid);
            UIKit.StretchTo(_fx, 0f, 0f, 0f, 0f);

            _cells = new Cell[_layout.Count];
            _freed = new Image[_layout.Count];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = BuildCell(_field, i);
        }

        Cell BuildCell(RectTransform field, int index)
        {
            int at = index;
            var root = UIKit.Box("Cell" + index, field, Vector2.one * _cell,
                                 new Vector2(.5f, .5f), Where(index));

            var cell = new Cell { Rt = root };
            var kind = _layout.GroundAt(index);

            if (kind == BudGround.Stone)
            {
                UIKit.Img("Shadow", root, Art.Hex(96), new Color(0f, 0f, 0f, .34f),
                          Vector2.one * _size * .90f, new Vector2(.5f, .5f),
                          new Vector2(0f, -_size * .05f));

                var wood = UIKit.Img("Wood", root, Art.Hex(96), new Color(.36f, .30f, .24f, 1f),
                                     Vector2.one * _size * .88f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                UIKit.Img("Facet", wood.transform, Art.Hex(96), new Color(.50f, .42f, .33f, 1f),
                          Vector2.one * _size * .48f, new Vector2(.5f, .5f),
                          new Vector2(-_size * .09f, _size * .08f));
            }
            else
            {
                cell.Soil = UIKit.Img("Soil", root, Art.Round(16), new Color(1, 1, 1, .035f),
                                      Vector2.one * _size * .80f, new Vector2(.5f, .5f),
                                      Vector2.zero);
            }

            // **Every cell that is not old wood can draw either a flower or a cocoon, and on a
            // living grove it has to.** A cell used to be built as one thing for ever, which was
            // fine while nothing ever moved. Now the grove falls: a cocoon slides down into a
            // square that was dealt a flower, and a flower falls into one that held a cocoon. A
            // cell built as only one of the two would simply not draw what landed in it — an
            // invisible critter standing on the board, which is a bug nothing else here could
            // catch. Both sets are built and <see cref="PaintCell"/> shows whichever the board
            // says is standing there.
            if (kind != BudGround.Stone)
            {
                // Everything below stands on this rather than on the cell, so a fall is one
                // transform moving and the square underneath it never does. See `Cell.Piece`.
                cell.Piece = UIKit.Node("Piece", root);

                cell.Glow = UIKit.Img("Glow", cell.Piece, Art.Glow(128, 2.2f), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size * 1.5f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                cell.Bud = UIKit.Img("Flower", cell.Piece, Bloom(Energy.None), new Color(1, 1, 1, 0f),
                                     Vector2.one * _size * .78f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                // The heart of the flower, drawn in the same colour but brighter. It is what
                // makes a dark blend still read as a flower rather than as a hole.
                cell.Halo = UIKit.Img("Heart", cell.Piece, Art.Disc(96), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size * .22f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                cell.Pod = UIKit.Img("Cocoon", cell.Piece, Art.Crystal(128), new Color(1, 1, 1, 0f),
                                     Vector2.one * _size * .94f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                // The critter inside, drawn small and dim and asleep - and it is a *real*
                // critter, the same flipbook the glades and the roster use, so what comes out at
                // the end is somebody the player already knows.
                cell.Critter = UIKit.Img("Critter", cell.Piece, null, new Color(1, 1, 1, 0f),
                                         Vector2.one * _size * .46f, new Vector2(.5f, .5f),
                                         Vector2.zero);
                CritterArt(cell.Critter, index, false);
                cell.Critter.color = new Color(1, 1, 1, 0f);

                cell.Ring = UIKit.Img("Cracks", cell.Piece, Art.Ring(128, 6f), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size * 1.06f, new Vector2(.5f, .5f),
                                      Vector2.zero);
            }

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;

            var btn = root.gameObject.AddComponent<Btn>();
            btn.PressScale = 1f;
            btn.Setup(() => Tap(at), silent: true);

            var hover = root.gameObject.AddComponent<Hover>();
            hover.Enter = () => ShowGhost(at);
            hover.Exit = HideGhost;

            return cell;
        }

        /// <summary>
        /// Puts one of the game's own critters on an image, asleep or awake.
        ///
        /// <b>A real flipbook rather than a dot.</b> The five critter sets are already global
        /// (<c>AssetManifest.GlobalAssets</c>) because every glade draws them, so this costs
        /// nothing to load and it is what makes freeing one land: the thing that pops out of a
        /// cocoon is the same creature the player has been waking for four chapters.
        /// </summary>
        void CritterArt(Image target, int index, bool awake)
        {
            if (!target) return;

            var frames = Art.Frames("Critters/c" + (1 + (index % 5)));

            if (frames != null && frames.Length > 0)
            {
                Flipbook.Attach(target, frames, awake ? 15f : 6f);
                target.color = awake ? Color.white : new Color(.72f, .74f, .80f, .95f);
                return;
            }

            // A critter set that has not arrived yet is a white rectangle, which on a dark board
            // is worse than a plain disc. See the house rule about generated art.
            target.sprite = Art.Disc(96);
            target.color = awake ? Pal.A(Pal.Radiance, 1f) : Pal.A(Pal.Dormant, .9f);
        }

        /// <summary>
        /// The band under the grove: the colour in hand, the two behind it, taps left
        /// and critters left.
        ///
        /// <b>The colour in hand is the biggest thing on it</b>, because it is what
        /// decides which tap is worth anything: the same flower is worth tapping with
        /// green up next and worth nothing with red. Two more are shown behind it,
        /// which is what turns a tap into a plan.
        /// because this mode deals no procession — the whole level is the board.
        /// </summary>
        void BuildBand(RectTransform host)
        {
            _tray = UIKit.Box("Band", host, new Vector2(0f, BudBand.BandHeight),
                              new Vector2(.5f, 0f), new Vector2(0f, BudBand.BandHeight * .5f));
            _tray.anchorMin = new Vector2(0f, 0f);
            _tray.anchorMax = new Vector2(1f, 0f);
            _tray.sizeDelta = new Vector2(0f, BudBand.BandHeight);

            var plate = UIKit.Img("Plate", _tray, Art.Round(26), new Color(.05f, .08f, .05f, .78f),
                                  new Vector2(BudBand.PlateWidth, BudBand.PlateHeight),
                                  new Vector2(.5f, .5f), Vector2.zero);

            UIKit.Img("Edge", plate.transform, Art.RoundOutline(26, 3f),
                      new Color(.86f, 1f, .74f, .12f),
                      new Vector2(BudBand.PlateWidth, BudBand.PlateHeight),
                      new Vector2(.5f, .5f), Vector2.zero);

            // The colour in hand, and the two behind it.
            var seat = UIKit.Img("Seat", plate.transform, Art.RoundOutline(16, 3f),
                                 new Color(1, 1, 1, .22f),
                                 Vector2.one * BudBand.HandSeat, new Vector2(.5f, .5f),
                                 new Vector2(BudBand.HandX, 0f));

            _handChip = UIKit.Img("Hand", seat.transform, Bloom(Energy.None), Color.white,
                                  Vector2.one * BudBand.HandSize, new Vector2(.5f, .5f),
                                  Vector2.zero);
            Tween.Breathe(_handChip.transform, .05f, 1.9f);

            _queue = new Image[BudBand.Lookahead];
            for (int i = 0; i < _queue.Length; i++)
                _queue[i] = UIKit.Img("Next" + i, plate.transform, Bloom(Energy.None),
                                      Color.white, Vector2.one * BudBand.QueueSize,
                                      new Vector2(.5f, .5f),
                                      new Vector2(BudBand.QueueCentre(i), 2f));

            _count = UIKit.Titled("Taps", plate.transform, "0", 48, Pal.Cream,
                                  TextAnchor.MiddleCenter, new Vector2(160f, 66f),
                                  new Vector2(.5f, .5f), new Vector2(BudBand.TapsX, 10f), 4f, 3f);
            UIKit.Shrinkable(_count, 22);

            UIKit.Titled("TapsCap", plate.transform, Loc.Get("mode.bud.taps"), 18,
                         new Color(.92f, .96f, 1f, .55f), TextAnchor.MiddleCenter,
                         new Vector2(190f, 24f), new Vector2(.5f, .5f),
                         new Vector2(BudBand.TapsX, BudBand.LabelDrop), 3f, 0f);

            _left = UIKit.Titled("Left", plate.transform, "0", 48, Pal.Gold,
                                 TextAnchor.MiddleCenter, new Vector2(160f, 66f),
                                 new Vector2(.5f, .5f), new Vector2(BudBand.CrittersX, 10f), 4f, 3f);
            UIKit.Shrinkable(_left, 22);

            UIKit.Titled("LeftCap", plate.transform, Loc.Get("mode.bud.critters"), 18,
                         new Color(.92f, .96f, 1f, .55f), TextAnchor.MiddleCenter,
                         new Vector2(190f, 24f), new Vector2(.5f, .5f),
                         new Vector2(BudBand.CrittersX, BudBand.LabelDrop), 3f, 0f);

            PaintBand();
        }

        // ------------------------------------------------------------------ positions
        Vector2 Where(int index)
        {
            int x = index % _layout.Width, y = index / _layout.Width;
            return _origin + new Vector2(x * _cell, -y * _cell);
        }

        /// <summary>Where a lesson about the chain should point: the ripest bud on the board.</summary>
        public RectTransform ChainAnchor
        {
            get
            {
                if (_cells == null || Run == null) return null;

                int best = -1, ripe = 0;
                for (int i = 0; i < _cells.Length; i++)
                {
                    if (!Run.Board.IsFlower(i)) continue;
                    if (Run.Board.ValueAt(i) <= ripe) continue;
                    best = i;
                    ripe = Run.Board.ValueAt(i);
                }

                return best >= 0 ? _cells[best].Rt : (_cells.Length > 0 ? _cells[0].Rt : null);
            }
        }

        /// <summary>Where a lesson about a cocoon should point: the first one still shut.</summary>
        public RectTransform CocoonAnchor
        {
            get
            {
                if (_cells == null || Run == null) return null;

                for (int i = 0; i < _cells.Length; i++)
                    if (Run.Board.IsCocoon(i)) return _cells[i].Rt;

                return null;
            }
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Puts what is drawn back in step with what the thicket holds, instantly.
        ///
        /// <c>Show</c> animates and <c>Refresh</c> does not — this is a Refresh, for the two
        /// moments there is nothing to replay: the board arriving and a restart.
        /// </summary>
        void Sync()
        {
            for (int i = 0; i < _cells.Length; i++) PaintCell(i, false);
            PaintBand();
            PaintPops();
        }

        void PaintCell(int index, bool animate)
        {
            var cell = _cells[index];
            if (cell?.Bud == null) return;

            var board = Run.Board;

            // **What is standing here is asked of the board, not of how the cell was built.** On
            // a living grove a cocoon slides into a square that was dealt a flower, so the cell's
            // own history says nothing about what it should be drawing.
            bool flower = board.IsFlower(index);
            bool shut = board.IsCocoon(index);

            if (cell.Pod)
            {
                cell.Pod.color = shut
                    ? new Color(.84f, .78f, .60f, board.ValueAt(index) > 1 ? 1f : .86f)
                    : new Color(1, 1, 1, 0f);

                if (cell.Ring)
                    cell.Ring.color = shut && board.ValueAt(index) > 1
                        ? Pal.A(Pal.Rope, .78f) : new Color(1, 1, 1, 0f);

                if (cell.Critter)
                {
                    bool wasShut = cell.Critter.color.a > .01f;
                    cell.Critter.color = shut ? Pal.A(Pal.Dormant, .95f) : new Color(1, 1, 1, 0f);

                    if (shut && !wasShut)
                    {
                        CritterArt(cell.Critter, index, false);
                        cell.Critter.color = Pal.A(Pal.Dormant, .95f);
                        Tween.Breathe(cell.Critter.transform, .07f, 2.6f, index * .19f);
                    }
                }
            }

            int colour = flower ? board.ValueAt(index) : Energy.None;
            int drawn = shut ? -2 - board.ValueAt(index) : colour;

            if (cell.Drawn == drawn && !animate) return;
            cell.Drawn = drawn;

            if (!flower)
            {
                cell.Bud.color = new Color(1, 1, 1, 0f);
                if (cell.Halo) cell.Halo.color = new Color(1, 1, 1, 0f);
                if (cell.Glow)
                    cell.Glow.color = shut ? Pal.A(Pal.Rope, .18f) : new Color(1, 1, 1, 0f);

                Tween.KillAll(cell.Bud);
                cell.Pops = false;
                return;
            }

            var tint = Petal(colour);

            cell.Bud.sprite = Bloom(colour);
            cell.Bud.color = tint;
            if (cell.Halo) cell.Halo.color = Pal.Lift(tint, .55f);
            if (cell.Glow) cell.Glow.color = Pal.A(tint, colour == Energy.All ? .34f : .14f);

            // White holds every channel, so on a living grove it is the bomb — the loudest thing
            // on the board and the only one that moves while nobody is tapping.
            // KillAll takes the "this one pops" breath with it, so the flag has to come off too
            // or PaintPops will think it is still running and never restart it.
            Tween.KillAll(cell.Bud);
            cell.Pops = false;

            if (colour == Energy.All)
                Tween.Breathe(cell.Bud.transform, .11f, 1.35f, index * .13f);
            else
                cell.Bud.transform.localScale = Vector3.one;
        }

        void PaintBand()
        {
            if (Run == null) return;

            if (_handChip)
            {
                int hand = Run.Next;
                _handChip.sprite = Bloom(hand);
                _handChip.color = hand == Energy.None ? new Color(1, 1, 1, .15f) : Petal(hand);
            }

            if (_queue != null)
            {
                for (int i = 0; i < _queue.Length; i++)
                {
                    int colour = Run.Ahead(i + 1);
                    bool any = colour != Energy.None;

                    _queue[i].gameObject.SetActive(any);
                    if (!any) continue;

                    _queue[i].sprite = Bloom(colour);
                    _queue[i].color = Pal.A(Petal(colour), .80f);
                }
            }

            if (_count)
            {
                bool bounded = Run.Satchel.Bounded;
                _count.text = bounded ? Run.Satchel.Left.ToString()
                                      : Loc.Get("mode.bud.taps_free");

                _count.color = !bounded ? Pal.Cream
                             : Run.Satchel.Pressure == BudPressure.Critical ? Pal.Ember
                             : Run.Satchel.Pressure == BudPressure.Low ? Pal.Gold
                             : Pal.Cream;
            }

            if (_left) _left.text = Run.Left.ToString();
        }

        void Enter()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                int x = i % _layout.Width, y = i / _layout.Width;
                Tween.Pop(_cells[i].Rt, 0f, .36f,
                          BudTempo.EntranceDelay(x, y, _layout.Width, _layout.Height));
            }
        }

        // ------------------------------------------------------------------ the ghost
        /// <summary>
        /// What a tap would come to, shown on the grove before anything is spent.
        ///
        /// <b>Two things, and the first is the one that teaches the mode.</b> The flower under the
        /// thumb is drawn in the colour it <em>would become</em>, so "red plus green in hand makes
        /// yellow" is a thing the player watches happen rather than a rule they are told. Then
        /// every cell the chain would take is lit, so a big one can be seen coming — which is the
        /// whole pleasure of choosing — without the board saying which tap is best.
        /// </summary>
        void ShowGhost(int index)
        {
            _hovered = index;

            if (!Playable || !Run.CanTap(index)) { HideGhost(); return; }

            var chain = Run.Preview(index, _peek);
            int mixed = Run.Mixed(index);

            int key = ((index * 64 + chain.Burst) * 8 + chain.Freed) * 8 + mixed;
            if (key == _ghostKey) return;
            _ghostKey = key;

            HideGhostRings();

            for (int i = 0; i < _peek.Count; i++)
            {
                var pulse = _peek[i];
                var cell = _cells[pulse.Cell];
                if (cell.Soil == null) continue;

                cell.Soil.color = pulse.Kind == BudPulseKind.Freed ? Pal.A(Pal.Gold, .42f)
                                : pulse.Kind == BudPulseKind.Crack ? Pal.A(Pal.Rope, .34f)
                                : Pal.A(Petal(pulse.Colour), .22f);
            }

            // The flower itself, wearing the colour it is about to be.
            var under = _cells[index];
            if (under.Bud)
            {
                under.Bud.sprite = Bloom(mixed);
                under.Bud.color = Pal.Lift(Petal(mixed), .25f);
                if (under.Halo) under.Halo.color = Pal.Lift(Petal(mixed), .7f);
                _mixing = index;
            }
        }

        /// <summary>The one flower currently drawn as what it *would* be. -1 when none is.</summary>
        int _mixing = -1;

        void HideGhostRings()
        {
            if (_cells == null) return;

            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i].Soil) _cells[i].Soil.color = new Color(1, 1, 1, .035f);

            // And put the flower that was wearing its would-be colour back to its real one.
            if (_mixing >= 0)
            {
                int was = _mixing;
                _mixing = -1;
                PaintCell(was, true);
            }
        }

        void HideGhost()
        {
            _hovered = -1;
            _ghostKey = int.MinValue;
            HideGhostRings();
        }

        // ------------------------------------------------------------------ playing
        void Tap(int index)
        {
            if (!Playable || !Run.CanTap(index)) { Refuse(index); return; }

            // The mark goes the instant a tap lands, whether or not it was the marked one — it
            // is advice about a position that no longer exists. The caller is told once the
            // chain has settled, not here: a panel raised now would cover the cascade the hint
            // was bought to produce.
            HideMark();

            // Read *before* the tap. `BudRun.Tap` settles the entire chain before it returns, so
            // by the frame after it this flower may be bare ground and the colour in hand has
            // moved on — the same trap that fired a bolt of lightning out of blank soil.
            int made = Run.Mixed(index);
            bool bomb = Run.Board.IsBomb(index);

            var chain = Run.Tap(index, _pulses, _washes, _drops);

            if (!_committed)
            {
                _committed = true;
                Committed?.Invoke();
            }

            _busy = true;
            HideGhost();

            if (bomb) Detonate(index);
            else Struck(index, made);

            StartCoroutine(PlayChain(chain, ToPulses(_pulses), ToWashes(_washes),
                                     ToDrops(_drops)));
        }

        /// <summary>
        /// The flower the player actually touched, answering.
        ///
        /// <para>
        /// <b>The tap had no moment of its own, and everything after it suffered for that.</b>
        /// The colour changed and the chain began, so the one thing the player *did* was the one
        /// thing on screen with no animation against it — which makes even a good cascade read
        /// as something that happened rather than something they caused. A flower now spins
        /// through a full turn, swells, and flashes toward the colour it is becoming.
        /// </para>
        /// <para>
        /// The note is <c>enter</c>, which is the sound this game already plays when somebody
        /// commits to a level from the map. That is deliberate rather than thrift: it is the
        /// game's established "you have just done the thing" note, and a tap here is the same
        /// kind of moment one screen further in.
        /// </para>
        /// </summary>
        void Struck(int index, int made)
        {
            Audio.Sfx("enter", .52f, 1.06f);

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            // **The commonest event in the mode, so it is worth something.** Most taps set off
            // one wave or none at all, and until this the answer to an ordinary tap was a spin
            // nobody could see under their own thumb. A ring of the colour the flower is
            // *becoming*, thrown out from under the finger, says the one thing a tap always does
            // — you mixed something — whether or not anything went off.
            var paint = Petal(made);
            Shockwave(Where(index), paint, _size * 1.9f,
                      BudTempo.Strike(BudTempo.WaveFull) * 2.2f);
            Burst.Sparks(_fx, Where(index), paint, 8, 170f, 13f, .45f);

            var rt = cell.Rt;
            var bud = cell.Bud;
            var was = bud ? bud.color : Color.white;

            Tween.Run(BudTempo.Strike(BudTempo.WaveFull), Ease.OutQuint, t =>
            {
                if (!rt) return;

                rt.localRotation = Quaternion.Euler(0, 0, 360f * t);
                rt.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * .30f);

                // Lit on the way round and back to its real colour on the way out, so the mix
                // arrives *inside* the spin rather than a frame before it.
                if (bud) bud.color = Color.Lerp(Pal.Lift(was, .7f), was, t);
            }, rt, SpinChannel).OnAbandon(() =>
            {
                if (!rt) return;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            }).OnDone(() =>
            {
                if (!rt) return;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            });
        }

        static BudPulse[] ToPulses(List<BudPulse> from)
        {
            var copy = new BudPulse[from.Count];
            for (int i = 0; i < from.Count; i++) copy[i] = from[i];
            return copy;
        }

        static BudWash[] ToWashes(List<BudWash> from)
        {
            var copy = new BudWash[from.Count];
            for (int i = 0; i < from.Count; i++) copy[i] = from[i];
            return copy;
        }

        static BudDrop[] ToDrops(List<BudDrop> from)
        {
            var copy = new BudDrop[from.Count];
            for (int i = 0; i < from.Count; i++) copy[i] = from[i];
            return copy;
        }

        /// <summary>
        /// A white flower being set off, which is the loudest single tap in the mode.
        ///
        /// <b>It has to read as a different act from mixing</b>, because it is one: nothing is
        /// added, a block is cleared. So the flower does not spin and brighten — it flashes white
        /// and throws a hard ring out across the three cells around it, before the bursts
        /// themselves land a frame later.
        /// </summary>
        void Detonate(int index)
        {
            var where = Where(index);

            // **Two blops a fifth apart, and nothing that breaks.** This played `shatter` —
            // "DESTRUCTION Break Impact Wood", the one genuinely destructive sample in the pack
            // — over the top of a low `burst`, and it was reported exactly as it sounds:
            // metallic, explosive, and nothing like the rest of this game. A white flower going
            // off is the biggest thing in the mode and it still has to be made of the same
            // material as everything else, so it is the mode's own burst note struck twice: low,
            // then a fifth above it a beat later. Two notes of one instrument, which is the
            // reuse `sfx.tsv`'s head describes, and it is bigger than an ordinary burst by being
            // *lower* and *doubled* rather than by being a different kind of sound.
            Audio.Sfx("burst", .62f, .56f);
            Audio.Sfx("burst", .42f, .84f, .07f);

            Flow.Flash(Pal.A(Color.white, .22f), .10f, .40f);
            Shockwave(where, Color.white, _size * 4.6f, .40f);
            Shockwave(where, Pal.Gold, _size * 3.2f, .30f);
            Burst.Sparks(_fx, where, Color.white, 18, 340f, 18f, .7f);

            if (_grid) { Tween.Shake(_grid, 16f, .34f); Tween.Punch(_grid, .06f, .40f); }
        }

        /// <summary>
        /// A tap that cannot be honoured, said rather than swallowed. It matters most on a cocoon,
        /// which is the one cell a player is certain to try.
        /// </summary>
        void Refuse(int index)
        {
            if (!Playable || index < 0 || index >= _cells.Length) return;

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            Tween.Shake(cell.Rt, 6f, .24f);

            if (cell.Pod)
            {
                // On a cocoon it is worth saying *why*: the answer is the buds beside it, so they
                // are what flares.
                _layout.Beside(index, _beside);
                for (int i = 0; i < _beside.Count; i++)
                {
                    var nb = _cells[_beside[i]];
                    if (nb.Bud && Run.Board.IsFlower(_beside[i])) Tween.Punch(nb.Rt, .24f, .30f);
                }
            }

            Audio.Sfx("rotate_b", .26f, .8f);
        }

        /// <summary>
        /// The chain, wave by wave: every bud in the wave bursts, throws pollen at the cells
        /// beside it, and the pollen lands a moment later on the buds that swell.
        ///
        /// The whole sequence is bounded by <c>BudTempo</c>, which is what stops a nine-wave chain
        /// taking nine times as long as a one-wave one — and floored by it, because a chain the eye
        /// cannot follow pays out nothing.
        /// </summary>
        IEnumerator PlayChain(BudChainResult chain, BudPulse[] pulses, BudWash[] washes,
                              BudDrop[] drops)
        {
            float beat = BudTempo.Wave(Mathf.Max(1, chain.Waves));
            int shown = 0;

            float charge = BudTempo.Charge(beat);
            float burn = BudTempo.Burn(beat);

            for (int wave = 0; wave < chain.Waves; wave++)
            {
                // What this wave is, read once: how many went off, where the middle of it was,
                // and what colour the biggest bunch in it wore. Everything drawn over the top of
                // the wave is anchored on these rather than on the board, which by now holds the
                // position the whole chain ends in and carries no time at all.
                int inWave = 0, fattest = 0, waveColour = Energy.None;
                var heart = Vector2.zero;

                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave || pulses[i].Kind != BudPulseKind.Burst) continue;

                    inWave++;
                    heart += Where(pulses[i].Cell);

                    if (pulses[i].Bunch <= fattest) continue;
                    fattest = pulses[i].Bunch;
                    waveColour = pulses[i].Colour;
                }

                if (inWave > 0) heart /= inWave;
                var waveTint = waveColour == Energy.None ? Pal.Gold : Petal(waveColour);

                // ---------------------------------------------------------- the charge
                // **Every wave winds up before it goes off, and this is the beat the mode was
                // missing.** The bunch that matched spins in place, brightening, for a fraction
                // of a second — so there is a moment where the player can see *which flowers*
                // did it, before they stop existing. Without it a wave went straight from
                // "nothing" to "gone", which is why a perfectly good three-wave cascade read as
                // a flicker rather than as something they had caused.
                // Ascending, before this wave's flowers are lifted over their neighbours, so
                // that only the bunch currently winding up is ever out of order.
                RestoreDepth();

                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave || pulses[i].Kind != BudPulseKind.Burst)
                        continue;
                    Wind(pulses[i].Cell, pulses[i].Colour, charge, i, wave + 1);
                }

                // And the bunch is *wired together* while it winds up, which is the half the
                // charge could not say on its own. Three flowers spinning in three places on a
                // grid of fifty is three things happening; a line of light between them is one
                // thing about to happen, and it is the reading the player needs before they stop
                // existing.
                Wires(pulses, wave, waveTint, charge);

                if (inWave > 0)
                {
                    // Rising, so the wind-up is heard as well as seen and a deep chain climbs.
                    Audio.Sfx("whoosh", .30f, BudTempo.Pitch(wave + 1) * 1.1f);
                }

                yield return new WaitForSecondsRealtime(charge);
                if (!this) yield break;

                // ---------------------------------------------------------- and the burst
                // **The wave is dealt as a ripple.** The model says these all went off at once
                // and drawing it that way is what made the board's biggest tap — thirteen
                // flowers — read as one flat flicker. A few tens of milliseconds apart is
                // enough to count them, and `BudTempo` bounds the whole ripple to a fraction
                // of the beat so the wave still ends when it said it would.
                // **Each kind of thing is rippled across its own count, and that is a
                // correctness fix as much as a pacing one.** All four used to be dealt against
                // `inWave` — the number of *bursts* — so a wave washing twenty flowers ran its
                // index past the end of a ripple built for thirteen, and a wave freeing four
                // critters fired all four in the same frame because `Free` was never given a
                // delay at all. On the chapter's finale, whose opening tap frees ten at once,
                // that is the single loudest moment in the mode played as one chord.
                //
                // Counted first, because how far apart to deal them depends on how many there
                // are: `BudTempo.StaggerAt` shortens the step until the whole set fits its
                // allowance, so a wave of three is three clear beats and a wave of thirteen is
                // one long ripple, and neither is a clump.
                int cracks = 0, frees = 0, sends = 0;
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave) continue;
                    if (pulses[i].Kind == BudPulseKind.Crack) cracks++;
                    else if (pulses[i].Kind == BudPulseKind.Freed) frees++;
                }
                for (int i = 0; i < washes.Length; i++) if (washes[i].Wave == wave) sends++;

                int nth = 0, crack = 0, freed = 0;
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave) continue;

                    if (pulses[i].Kind == BudPulseKind.Freed)
                    {
                        // **A critter getting out is the thing the level is for, so two of them
                        // may never happen at once.** This is the one ripple where the delay is
                        // doing more than pacing: each of these carries a sound, a halo, a
                        // shockwave and a creature, and four on one frame is four of everything
                        // over the top of each other with no one of them legible.
                        Free(pulses[i].Cell, burn,
                             BudTempo.StaggerAt(freed++, frees, burn, BudTempo.GreetSpread));
                        continue;
                    }

                    if (pulses[i].Kind == BudPulseKind.Crack)
                    {
                        // Held back behind the bunch that did it, exactly as a wash is: a shell
                        // that splinters before the flower beside it has gone off reads as the
                        // cocoon having done it to itself.
                        Crack(pulses[i].Cell, burn, BudTempo.StaggerAt(crack++, cracks, burn));
                        continue;
                    }

                    Split(pulses[i].Cell, wave, pulses[i].Colour, pulses[i].Bunch, beat,
                          BudTempo.StaggerAt(nth, inWave, burn));
                    nth++;
                }

                // Colour lands on the flowers around the bunch a beat after it goes off, held
                // back by a ripple of its own, so a flower never turns before the bunch that
                // turned it has burst.
                int sent = 0;
                for (int i = 0; i < washes.Length; i++)
                {
                    if (washes[i].Wave != wave) continue;
                    Land(washes[i], beat, BudTempo.StaggerAt(sent++, sends, burn));
                }

                if (inWave > 0)
                {
                    shown = wave + 1;
                    if (BudChain.Counts(shown)) ShowChain(shown, chain.Waves);

                    // **The escalation, and it is in kinds of thing rather than in amounts.**
                    // Every wave switches a new one on and keeps the ones before it, so a
                    // five-wave chain is six different events arriving one after another rather
                    // than the same event five times a little louder — which is what the first
                    // version of this was, and it was reported as no change at all.
                    var layers = BudSpectacle.Of(shown);

                    Jolt(heart, layers.Ripple, burn, pulses, wave);
                    if (layers.Sweep) Sweep(heart, waveTint, burn);
                    if (layers.Fireworks) Fireworks(heart, waveTint, layers.Rockets, burn);
                    if (layers.Rays) Backlight(waveTint, burn);
                    if (layers.Confetti) Burst.Confetti(_fx, 18 + shown * 6);

                    float shake = BudTempo.Shake(shown);
                    if (shake > 0f && _grid) Tween.Shake(_grid, shake, burn * .9f);

                    // And the screen answers, harder every wave — in the wave's *own colour*
                    // from the second on, so what takes the screen says which colour is running
                    // rather than merely that something did.
                    float bloom = BudTempo.Bloom(shown);
                    if (bloom > 0f) Flow.Flash(Pal.A(new Color(1f, .96f, .84f), bloom),
                                               burn * .30f, burn * .70f);

                    if (layers.Tint > 0f)
                        Flow.Flash(Pal.A(waveTint, layers.Tint), burn * .22f, burn * .80f);

                    // The whole thicket heaves, harder every wave — the chain's escalation said
                    // at grove scale. It replaces a punch on the plate of between 1.2% and 3.6%,
                    // which is below the size at which a scale change on a whole screen is
                    // noticed: a player watching thirteen flowers go off has no attention spare
                    // for a 2% nudge behind them. On `_grid` rather than `_plate` so the flowers
                    // move with the ground they stand on — a plate that swells behind a static
                    // grid is a border thickening, not a board reacting.
                    //
                    // Safe beside the shake above: that borrows the position, this the scale.
                    if (_grid) Tween.Punch(_grid, BudTempo.Heave(shown), burn * .85f);
                }

                // **And the grove falls into the holes it just made.** Held back behind the
                // bursts of its own wave, so the player watches the flowers go and *then* watches
                // what was above them come down — which is the beat that makes a cascade read as
                // one thing collapsing rather than as two unrelated events.
                Rain(drops, wave, burn);

                PaintBand();
                Changed?.Invoke();

                yield return new WaitForSecondsRealtime(burn);
                if (!this) yield break;
            }

            // What grew back arrives on the wave after the last one, which is where the model
            // put it: growing happens once, after the chain has stopped (see BudBoard.Grow).
            Rain(drops, chain.Waves, burn);

            // **And the grove is allowed to land before anything else happens to it.** The word
            // used to slam in while the last flowers were still in the air and every cell was
            // repainted underneath it in the same frame, which is the "the board resets so
            // suddenly" this was reported as. It is the glade's hush (`GladeFanfare`) arrived at
            // from the other end: the beat before a celebration is part of the celebration.
            yield return new WaitForSecondsRealtime(BudTempo.Landing(chain.Waves));
            if (!this) yield break;

            // The last wave lifted its own bunch and there is no wave after it to tidy up, so
            // the settled board would keep that stacking for the rest of the run.
            RestoreDepth();

            // **Not animated, and that word is doing the work.** `PaintCell(i, true)` skips its
            // own "nothing changed" guard, so this loop used to kill every tween on every flower
            // and snap every scale back to one — thirty-six cells, in a frame, most of which
            // were already correct. Every white flower's breath and every "this one pops" hint
            // died at once and `PaintPops` restarted them from nothing, which is a whole board
            // flinching at the moment it should be settling. Asked without `animate`, a cell
            // whose colour has not moved is left exactly as it is.
            for (int i = 0; i < _cells.Length; i++) PaintCell(i, false);
            PaintBand();
            PaintPops();
            Changed?.Invoke();

            string word = BudChain.WordKey(chain.Waves);
            if (word != null)
            {
                yield return Fanfare(chain.Waves, word);
                if (!this) yield break;
            }
            else if (BudChain.Counts(chain.Waves))
            {
                yield return new WaitForSecondsRealtime(BudTempo.CountPop(chain.Waves) * 2f);
                if (!this) yield break;
                HideChain();
            }

            _busy = false;
            Settle();
            TellHint();
        }

        // ------------------------------------------------------ what a wave draws over the top
        /// <summary>
        /// Light strung between the flowers of one bunch while they wind up.
        ///
        /// <para>
        /// <b>Anchored on the pulses and on nothing else, which is the whole reason it is safe
        /// to draw a line between two cells at all.</b> A stroke of lightning was tried here
        /// once and fired out of blank soil, because it asked <c>Run.Board</c> which neighbour
        /// was bare — and the model settles the entire chain before a frame is drawn, so that
        /// question answers "empty once this is all over". These links are drawn between two
        /// cells that the model says burst <em>in this wave, in the same bunch</em>, which is a
        /// fact about the moment being animated rather than about the end of the chain.
        /// </para>
        /// <para>
        /// Two neighbours on this grid are always orthogonal, so a link is an axis-aligned bar
        /// and needs no rotation — which is also why it cannot be drawn pointing anywhere silly.
        /// </para>
        /// </summary>
        void Wires(BudPulse[] pulses, int wave, Color tint, float charge)
        {
            if (_fx == null || _layout == null) return;

            for (int i = 0; i < pulses.Length; i++)
            {
                if (pulses[i].Wave != wave || pulses[i].Kind != BudPulseKind.Burst) continue;

                _layout.Beside(pulses[i].Cell, _beside);

                for (int j = 0; j < _beside.Count; j++)
                {
                    int nb = _beside[j];

                    // Once per pair, and only where the model says both went off together.
                    if (nb <= pulses[i].Cell) continue;
                    if (!InWave(pulses, wave, nb, pulses[i].Bunch)) continue;

                    Wire(Where(pulses[i].Cell), Where(nb), tint, charge);
                }
            }
        }

        static bool InWave(BudPulse[] pulses, int wave, int cell, int bunch)
        {
            for (int i = 0; i < pulses.Length; i++)
                if (pulses[i].Wave == wave && pulses[i].Cell == cell
                    && pulses[i].Kind == BudPulseKind.Burst && pulses[i].Bunch == bunch)
                    return true;

            return false;
        }

        /// <summary>One link: a bar of light that draws itself out and brightens with the charge.</summary>
        void Wire(Vector2 a, Vector2 b, Color tint, float charge)
        {
            var mid = (a + b) * .5f;
            bool across = Mathf.Abs(a.x - b.x) > Mathf.Abs(a.y - b.y);
            float span = _cell;

            var bar = UIKit.Img("Wire", _fx, Art.Round(8), Pal.A(tint, 0f),
                                across ? new Vector2(span, _size * .10f)
                                       : new Vector2(_size * .10f, span),
                                new Vector2(.5f, .5f), mid);
            bar.raycastTarget = false;
            bar.transform.SetAsFirstSibling();

            var rt = (RectTransform)bar.transform;

            Tween.Run(Mathf.Max(charge, .10f), Ease.OutQuad, t =>
            {
                if (!bar) return;

                // Out from the middle, so the two flowers reach for each other rather than a
                // finished line simply appearing between them.
                float grow = Mathf.Min(1f, t * 2.2f);
                float fat = 1f + t * 1.4f;
                rt.sizeDelta = across ? new Vector2(span * grow, _size * .10f * fat)
                                      : new Vector2(_size * .10f * fat, span * grow);
                bar.color = Pal.A(Color.Lerp(tint, Color.white, t), .28f + t * .62f);
            }, bar).OnDone(() => { if (bar) Destroy(bar.gameObject); });
        }

        /// <summary>
        /// The rest of the grove jolting as the wave passes over it.
        ///
        /// <para>
        /// <b>This is the one that makes a small chain feel big, and it costs almost nothing.</b>
        /// A burst used to be an event that happened <em>to three cells</em> on a field of fifty
        /// that carried on standing still. Now the whole board answers: every other flower is
        /// knocked, in order, outward from where the wave went off, harder the nearer it was.
        /// </para>
        /// <para>
        /// Two rules keep it out of trouble. It skips the cells <em>in</em> this wave, because
        /// those are owned by <see cref="Wind"/> and two gestures on one transform is the bug
        /// this file has paid for twice — and where a jolted cell is wound up by a later wave,
        /// <c>Wind</c> kills the punch channel first, which is the same guard from the other
        /// end. And it is bounded by <c>BudSpectacle.RippleOver</c> to a fraction of the beat, so
        /// a jolt is never still crossing the board when the next wave charges.
        /// </para>
        /// </summary>
        void Jolt(Vector2 heart, float strength, float burn, BudPulse[] pulses, int wave)
        {
            if (_cells == null || strength <= 0f) return;

            float over = BudSpectacle.RippleOver(burn);
            float far = Mathf.Max(_layout.Width, _layout.Height) * _cell;

            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                if (cell?.Rt == null) continue;
                if (InWave(pulses, wave, i)) continue;

                float distance = Vector2.Distance(Where(i), heart);
                float force = BudSpectacle.RippleForce(strength, distance, far);
                if (force < .004f) continue;

                Tween.Punch(cell.Rt, force, over * .8f)
                     .Delay(BudSpectacle.RippleAt(distance, far) * over);
            }
        }

        static bool InWave(BudPulse[] pulses, int wave, int cell)
        {
            for (int i = 0; i < pulses.Length; i++)
                if (pulses[i].Wave == wave && pulses[i].Cell == cell) return true;

            return false;
        }

        /// <summary>
        /// A ring of the wave's own colour thrown right across the grove. Wave two and up.
        ///
        /// Bigger than the board on purpose: a ring that stops inside the grid is a decoration
        /// on one corner of it, where one that runs off the edges is the wave <em>leaving</em>.
        /// </summary>
        void Sweep(Vector2 heart, Color tint, float burn)
        {
            var sprite = Art.Ring(256, 9f);
            if (sprite == null || _fx == null) return;

            float reach = Mathf.Max(_layout.Width, _layout.Height) * _cell * 2.4f;

            var ring = UIKit.Img("Sweep", _fx, sprite, Pal.A(tint, .85f),
                                 Vector2.one * reach, new Vector2(.5f, .5f), heart);
            ring.raycastTarget = false;

            var rt = (RectTransform)ring.transform;
            float over = Mathf.Max(burn * 1.15f, .30f);

            Tween.Run(over, Ease.OutQuint, t =>
            {
                if (!ring) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.06f, 1f, t);
                ring.color = Pal.A(Color.Lerp(Color.white, tint, Mathf.Min(1f, t * 3f)),
                                   .85f * (1f - t) * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// Sparks arcing up out of the grove and going off above it. Wave three and up.
        ///
        /// <b>The first thing in this mode that leaves the board.</b> Everything else happens
        /// inside the grid, so a chain that has run far enough to throw something over the top of
        /// it is unmistakable without anybody having to compare it with the wave before.
        /// </summary>
        void Fireworks(Vector2 heart, Color tint, int rockets, float burn)
        {
            if (_fx == null || rockets <= 0) return;

            float rise = _layout.Height * _cell * .60f;
            float climb = Mathf.Max(burn * .55f, .18f);

            for (int i = 0; i < rockets; i++)
            {
                float lean = ((i % 3) - 1) * _cell * (1.1f + i * .35f);
                var from = heart;
                var to = new Vector2(heart.x + lean, heart.y + rise * (.72f + (i % 2) * .34f));
                var paint = i % 2 == 0 ? tint : Pal.Lift(tint, .55f);

                var spark = UIKit.Img("Rocket", _fx, Art.Glint(96, 4), Pal.A(paint, .95f),
                                      Vector2.one * _size * .30f, new Vector2(.5f, .5f), from);
                spark.raycastTarget = false;
                var rt = (RectTransform)spark.transform;

                Tween.Run(climb, Ease.OutQuad, t =>
                {
                    if (!spark) return;
                    rt.anchoredPosition = Vector2.Lerp(from, to, t);
                    rt.localScale = Vector3.one * (1f - t * .35f);
                    rt.localRotation = Quaternion.Euler(0, 0, 420f * t);
                    spark.color = Pal.A(paint, .95f * (1f - t * .3f));
                }, spark).Delay(i * climb * .13f).OnDone(() =>
                {
                    if (spark) Destroy(spark.gameObject);
                    if (!this) return;

                    // And it goes off where it got to.
                    // **A firework is a round pop of light.** It used to go off as a starburst
                    // of straight rays, which is a spotlight rather than a firework and read as
                    // exactly that against a board of soft round shapes.
                    Flare(to, paint, climb * .8f);
                    Shockwave(to, paint, _size * 2.2f, climb * 1.1f);
                    Burst.Sparks(_fx, to, paint, 12, 300f, 16f, climb * 1.4f);
                    Audio.Sfx("star", .26f, 1.25f + i * .06f);
                });
            }
        }

        /// <summary>
        /// A star lit behind the whole board. Wave four and up.
        ///
        /// It is the only thing here drawn <em>under</em> the grove rather than over it, which is
        /// what stops the deepest chains becoming a wall of light in front of the thing the
        /// player is trying to watch.
        /// </summary>
        void Backlight(Color tint, float burn)
        {
            var sprite = Art.Glow(256, 1.5f);
            if (sprite == null || _grid == null) return;

            float reach = Mathf.Max(_layout.Width, _layout.Height) * _cell * 2.9f;

            var glow = UIKit.Img("Backlight", _grid, sprite, Pal.A(tint, 0f),
                                 Vector2.one * reach, new Vector2(.5f, .5f), Vector2.zero);
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();

            var rt = (RectTransform)glow.transform;
            float over = Mathf.Max(burn * 1.6f, .45f);

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!glow) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.62f, 1.10f, t);

                float a = t < .18f ? t / .18f : 1f - (t - .18f) / .82f;
                glow.color = Pal.A(tint, a * .40f);
            }, glow).OnDone(() => { if (glow) Destroy(glow.gameObject); });
        }

        /// <summary>
        /// One clean swell and back, on something that is otherwise breathing.
        ///
        /// <para>
        /// <b>The breath is killed before the rest scale is read, and started again after.</b>
        /// A breathe <em>borrows</em> a scale for as long as it runs, so a gesture that reads its
        /// target's size while one is in flight captures mid-breath and hands that back as the
        /// resting size for ever — the fault this file has paid for twice, and the reason
        /// <c>Tween.Breathe</c>'s own remarks tell its callers to kill it first. Here the rest is
        /// not read at all: a freed critter's size is <see cref="FreedScale"/> and is known, which
        /// is stricter again.
        /// </para>
        /// </summary>
        /// <param name="then">
        /// What happens when it lands, <em>instead</em> of settling back into a breath — and it
        /// is a parameter rather than something the caller schedules alongside because those two
        /// are not the same thing. A breathe borrows the scale this tween is writing, so a
        /// caller that timed its own follow-on to the same duration would race the restart and
        /// sometimes lose: measured, the greeting's flight to the counter left the critter
        /// arriving at full size with an idle breath still driving it, because the breath was
        /// started by an <c>OnDone</c> a frame after the flight had begun. Chained, there is no
        /// ordering left to get wrong.
        /// </param>
        void Pump(Image who, float over, int seed, float swell = BudTempo.FreedPump,
                  Action then = null)
        {
            if (!who) return;

            var tr = who.transform;

            Tween.KillChannel(tr, "breathe");
            Tween.KillChannel(tr, PumpChannel);

            Action rest = () =>
            {
                if (!who) return;
                tr.localScale = Vector3.one * FreedScale;
            };

            rest();

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!who) return;

                // A half-sine: out and back exactly once, with no overshoot at either end.
                tr.localScale = Vector3.one * FreedScale * (1f + Mathf.Sin(t * Mathf.PI) * swell);
            }, who, PumpChannel).OnAbandon(rest).OnDone(() =>
            {
                rest();
                if (!this || !who) return;

                if (then != null) then();
                else Tween.Breathe(tr, .055f, 2.9f, seed * .21f);
            });
        }

        /// <summary>The channel a freed critter's pulse runs on, so one supersedes another.</summary>
        const string PumpChannel = "budpump";

        /// <summary>
        /// The critter leaving the grove for the counter that is keeping score of them.
        ///
        /// <para>
        /// <b>They cannot stay, and the reason is the model rather than the drawing.</b> Freeing
        /// empties that square — which is the point, because the grove falls into it and that is
        /// where a chain gets its compounding from — so a critter left standing there is standing
        /// exactly where a flower is about to come to rest. Blocking the square instead was built
        /// and measured, and it takes the cascades out of the boards (see
        /// <c>BudTempo.FreedFlight</c>). So the reward *moves*: it is celebrated where it was
        /// earned, and then it flies to the readout that has been counting it all along.
        /// </para>
        /// <para>
        /// <b>Across two coordinate spaces, which is the one thing here that cannot be typed.</b>
        /// The critter stands on the grid and the readout sits on the band, and the band is a
        /// different node inset by its own height — so the destination is read off the live
        /// object through the world and converted back, never computed from the layout numbers.
        /// A second copy of where the counter is would be a second thing to keep in step with
        /// <c>BudBand</c>.
        /// </para>
        /// </summary>
        /// <summary>
        /// The shine: a soft swell of light behind a critter that has just got out.
        ///
        /// <para>
        /// It is the one thing added to this beat while four were taken out of it, and it is
        /// what the ring around them cannot do on its own: a ring says <em>this one</em>, and
        /// light says <em>this one is worth something</em>. Behind the creature rather than over
        /// them, and gone before the pump ends, so it lifts them off the board without ever
        /// being a second thing to look at.
        /// </para>
        /// </summary>
        void Shine(int index, Vector2 where)
        {
            var sprite = Art.Glow(192, 1.6f);
            if (sprite == null || _residents == null) return;

            var glow = UIKit.Img("Shine", _residents, sprite, Pal.A(Pal.Cream, 0f),
                                 Vector2.one * _size * 2.2f, new Vector2(.5f, .5f), where);
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();

            var rt = (RectTransform)glow.transform;
            float over = BudTempo.FreedHold + BudTempo.FreedGreet;

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!glow) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.45f, 1.15f, t);

                float a = t < .22f ? t / .22f : 1f - (t - .22f) / .78f;
                glow.color = Pal.A(Pal.Cream, a * .70f);
            }, glow).OnDone(() => { if (glow) Destroy(glow.gameObject); });
        }

        /// <summary>
        /// And they are gone from the spot they came out on.
        ///
        /// <para>
        /// <b>This replaced a flight to the counter, and the flight was a critter falling off
        /// the bottom of the grove.</b> The readouts sit <em>under</em> the board
        /// (<c>BudBand</c>), so "they fly to where the score is kept" meant an arc that rose a
        /// little and then travelled the whole height of the grove downward — reported, twice,
        /// as the critters falling. The idea was that a number changing on its own becomes
        /// somewhere the reward visibly went; what it actually bought was the one motion this
        /// mode must never draw.
        /// </para>
        /// <para>
        /// So they leave where they arrived: they swell a little and fade, and the counter is
        /// punched as they go, which keeps the connection without anything crossing the screen.
        /// The square they leave is filled by the grove falling into it a beat later, and that
        /// is what says the slot is free — better than a creature vacating it, because the
        /// player is watching the board rather than the number.
        /// </para>
        /// </summary>
        void Vanish(int index)
        {
            if (_freed == null || index < 0 || index >= _freed.Length) return;

            var critter = _freed[index];
            if (!critter) return;

            // Off the books the moment it goes, so nothing counts it as standing in the grove.
            _freed[index] = null;

            var crt = (RectTransform)critter.transform;

            // Both borrow the scale this is about to write, and a borrowed value handed back
            // mid-fade would snap the critter back to resting size as it disappeared.
            Tween.KillChannel(crt, "breathe");
            Tween.KillChannel(crt, PumpChannel);

            Tween.Run(BudTempo.FreedLeave, Ease.OutQuad, t =>
            {
                if (!critter) return;

                crt.localScale = Vector3.one * (FreedScale * (1f + t * .42f));
                critter.color = new Color(1f, 1f, 1f, 1f - t * t);
            }, critter).OnDone(() =>
            {
                if (critter) Destroy(critter.gameObject);
                if (this) Landed();
            });
        }

        /// <summary>
        /// The counter answering a critter arriving on it.
        ///
        /// The number itself is the model's and has already moved — it ticks when the wave that
        /// freed them resolves, which is a beat earlier. What this adds is the counter being
        /// visibly <em>landed on</em>, so a number that changed on its own becomes somewhere the
        /// reward went.
        /// </summary>
        void Landed()
        {
            if (_left) Tween.Punch(_left.transform, .30f, .34f);

            if (_tray && _left)
                Burst.Sparks(_tray, _tray.InverseTransformPoint(_left.transform.position),
                             Pal.Gold, 7, 130f, 12f, .42f);

            Audio.Sfx("tick", .20f, 1.34f);
        }

        /// <summary>
        /// The ring that closes around a critter the moment they are out.
        ///
        /// <para>
        /// <b>It comes *in* rather than going out, and that is the whole difference between this
        /// and every other ring in the mode.</b> A shockwave leaves — it starts small, runs past
        /// the edge of the cell and fades, which says <em>something went off here</em>. This one
        /// starts wide and closes onto the creature, which says <em>this one</em>. Drawn on the
        /// residents layer so the grove falls behind it, in the same gold the freed critter's own
        /// light uses, and it is the last thing left standing when the shell's noise has gone.
        /// </para>
        /// <para>
        /// It holds a moment at the critter's own size, breathing with the pump rather than
        /// against it — one gesture in two shapes — and then fades where it stands. A ring that
        /// snapped away would take the eye with it, which is exactly the frame the player is
        /// meant to be looking at the creature in.
        /// </para>
        /// </summary>
        void Circle(int index, Vector2 where)
        {
            var sprite = Art.Ring(160, 9f);
            if (sprite == null || _residents == null) return;

            float size = _size * BudTempo.FreedRing;

            var ring = UIKit.Img("Greeting" + index, _residents, sprite, Pal.A(Pal.Gold, 0f),
                                 Vector2.one * size, new Vector2(.5f, .5f), where);
            var rt = (RectTransform)ring.transform;

            // Behind everybody who is out, so a creature swelling inside its own ring is never
            // drawn through it. Residents do not overlap, so one index is enough.
            rt.SetAsFirstSibling();

            Tween.Run(BudTempo.FreedHold * BudTempo.FreedRingOver, Ease.OutQuad, t =>
            {
                if (!ring) return;

                // In from wide, held, then gone — and the swell it holds at is the pump's own,
                // so the ring and the creature inside it are one gesture rather than two.
                float close = Mathf.Min(1f, t / BudTempo.FreedRingClose);
                float ease = 1f - (1f - close) * (1f - close);
                float held = Mathf.Max(0f, (t - BudTempo.FreedRingClose)
                                         / (1f - BudTempo.FreedRingClose));

                rt.localScale = Vector3.one
                              * (Mathf.Lerp(BudTempo.FreedRingFrom, 1f, ease)
                                 + Mathf.Sin(held * Mathf.PI) * BudTempo.FreedRingSwell);

                ring.color = Pal.A(Pal.Gold, Mathf.Min(1f, ease * 1.6f) * (1f - held * held));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        // ------------------------------------------------------------------ the grove falling
        /// <summary>
        /// Everything that moved on this wave, sliding down into the holes under it.
        ///
        /// <para>
        /// <b>The cells never move; what is drawn in them does.</b> A cell is a fixed square of
        /// the grid with a flower or a cocoon standing in it, so a fall is not a cell changing
        /// position — it is the <em>picture</em> being handed from one cell to the next, and then
        /// the receiving cell animating its own <see cref="Cell.Piece"/> in from where it came.
        /// That is what keeps the ground, the hit target and the <c>Btn</c> exactly where the
        /// layout put them, which is the one thing a falling board must not lose.
        /// </para>
        /// <para>
        /// <b>A column falls as a column, and the further it falls the longer it takes</b> — a
        /// flower that drops five rows and one that drops one row cannot take the same time
        /// without the tall one reading as teleporting, and two pieces of one column that start
        /// at different moments read as a shower rather than as a board collapsing. So the
        /// ripple is over <em>columns</em> and never over the order the model happened to list
        /// the drops in, which is what it used to be.
        /// </para>
        /// <para>
        /// <b>And what grew travels the height of its own hole.</b> Every new flower in a column
        /// enters from over the top of the grove, stacked in the order it will land, so a column
        /// that lost three moves three squares' worth of new flowers down by three squares —
        /// which is one distance for the whole column rather than one per row. The old
        /// arithmetic negated the grove's own origin, so on a seven-high board the top row's new
        /// flower rose <em>up</em> into place from three squares below and only the bottom rows
        /// fell at all.
        /// </para>
        /// </summary>
        void Rain(BudDrop[] drops, int wave, float burn)
        {
            if (drops == null || _cells == null) return;

            float over = BudTempo.Rain(burn);

            // **The bursts are left alone for a beat first.** The grove used to start falling
            // in the frame its own wave went off, so the flower above a burst was already
            // moving while the burst was still opening — the player's own doing, covered by the
            // consequence of it before they had seen it. `BudTempo.Settle` takes this out of the
            // fall's allowance rather than adding it beside one, so the grove is still back on
            // the ground before the next wave charges.
            float hold = BudTempo.Settle(burn);

            // How many are coming down, counted before any of them is dealt, because which of
            // them are struck depends on how many there are — see `BudChorus`.
            int falling = 0;
            for (int i = 0; i < drops.Length; i++)
                if (drops[i].Wave == wave && drops[i].Cell >= 0 && drops[i].Cell < _cells.Length)
                    falling++;

            int nth = 0;

            for (int i = 0; i < drops.Length; i++)
            {
                var drop = drops[i];
                if (drop.Wave != wave) continue;
                if (drop.Cell < 0 || drop.Cell >= _cells.Length) continue;

                int column = drop.Cell % _layout.Width;

                // Where it is coming from: the cell above it, or from over the top of the grove
                // for a flower that has just grown, which travels as far as its column is deep.
                float above = drop.Grew
                    ? Grown(drops, wave, column) * _cell
                    : Where(drop.From).y - Where(drop.Cell).y;

                Land(drop, above, over, column, hold, nth, falling);
                nth++;
            }
        }

        /// <summary>
        /// How many flowers grew into one column on this wave, which is how far every one of
        /// them falls.
        ///
        /// <para>
        /// A hole is always at the top of its column — the grove falls first and grows into what
        /// is left — so the new flowers of one column enter as a block from above the grove and
        /// come down together. Each therefore travels the same distance: the height of the hole
        /// the column lost, whatever row inside it any one of them ends up on.
        /// </para>
        /// </summary>
        int Grown(BudDrop[] drops, int wave, int column)
        {
            int count = 0;

            for (int i = 0; i < drops.Length; i++)
                if (drops[i].Wave == wave && drops[i].Grew
                    && drops[i].Cell >= 0 && drops[i].Cell % _layout.Width == column) count++;

            return count < 1 ? 1 : count;
        }

        /// <summary>
        /// One thing arriving in a cell, dropped in from <paramref name="above"/>.
        ///
        /// <para>
        /// <b>The offset is taken the instant the picture is handed over, not when the tween
        /// starts.</b> <see cref="PaintCell"/> draws what has landed straight away, so a piece
        /// left sitting at its destination for the length of its stagger and only then lifted to
        /// where it fell from is a flower that appears, jumps back up and falls again.
        /// </para>
        /// <para>
        /// <b>And an interrupted fall lands rather than being abandoned.</b> A fall arrives at a
        /// resting state it knows absolutely — the cell it belongs to — so it is
        /// <see cref="Tw.OnAbandon"/>'s second kind: it declares where a superseded one goes and
        /// <c>KillChannel</c> puts it there. Without that, a wave dropping twice into one cell
        /// left the first fall wherever the second caught it.
        /// </para>
        /// </summary>
        void Land(BudDrop drop, float above, float over, int column,
                  float hold = 0f, int nth = 0, int of = 1)
        {
            var cell = _cells[drop.Cell];
            if (cell?.Rt == null || cell.Piece == null) return;

            PaintCell(drop.Cell, true);

            var piece = cell.Piece;

            // Bounded *with* its stagger rather than beside it — see `BudTempo.Rainfall`, which
            // is where that arithmetic lives so it can be proved without an Editor.
            float rows = Mathf.Abs(above) / Mathf.Max(1f, _cell);
            BudTempo.Rainfall(column, rows, over, out float delay, out float fall);

            Action rest = () =>
            {
                if (!piece) return;
                piece.anchoredPosition = Vector2.zero;
                piece.localScale = Vector3.one;
            };

            // **A fall accelerates, and this is the curve the mode was most obviously wrong
            // about.** It was `OutQuad` - a piece that leaves fast and *decelerates* into the
            // ground, which is the one shape a falling thing cannot have. Nothing here is
            // pushed; it is dropped, so it gathers speed all the way down and stops dead. Read
            // beside the landing squash below, that swap alone is most of what separates a
            // board with weight from a board whose contents slide about.
            //
            // How far it has to travel decides how hard it lands and how much it stretches on
            // the way, so a flower falling the height of the grove arrives like one and a
            // flower nudged down a single row does not.
            float lean = Mathf.Min(.26f, rows * .060f);

            // **The landing of the *previous* fall into this cell is ended first, and that is
            // not tidying.** A squash outlives the wave that threw it by design — it is the
            // ground pushing back, so it settles after the piece has stopped — and this fall
            // writes the same `localScale` from a different channel. Two tweens on one value is
            // a bug however different their channels are, and this pair genuinely overlaps: a
            // cell that receives a fall, bursts on the next wave and receives another is an
            // ordinary thing for a cascade to do. Killing it hands the resting scale back
            // before the stretch below borrows it.
            Tween.KillChannel(piece, SquashChannel);

            // **Gentler than gravity, and that is a drawing decision rather than a physical
            // one.** `InQuad` is what a falling thing really does and it peaks at twice its own
            // average speed, so the last frames of a five-row drop cover a third of a cell each
            // and the eye reads it as skipping rather than as falling. `t^1.5` peaks at one and
            // a half times instead: still unmistakably accelerating, never fast enough to tear.
            // Shaped here with `Ease.Linear` rather than added to `Ease`, because it exists for
            // this one gesture and naming it in the shared set would invite it into others.
            Tween.Run(fall, Ease.Linear, t =>
            {
                if (!piece) return;

                float drop = t * Mathf.Sqrt(t);
                piece.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(above, 0f, drop));

                // Drawn out along the way it is travelling, and most drawn out where it is
                // fastest. It is a small number on purpose - enough that the eye reads speed,
                // never enough to be caught looking at.
                float pull = lean * drop;
                piece.localScale = new Vector3(1f - pull * .45f, 1f + pull, 1f);
            }, piece, FallChannel).Delay(hold + delay).OnAbandon(rest).OnDone(() =>
            {
                if (!piece) return;
                piece.anchoredPosition = Vector2.zero;

                // And the landing, which is the beat the old punch was standing in for. A
                // wobble says "something touched this"; a squash and a spring say "this had
                // weight and the ground stopped it".
                Squash(piece, lean, over);

                // **And it is heard.** A board that falls in silence is a board that is being
                // rearranged rather than one where things are dropping onto other things, and
                // this is the cheapest half of making a fall feel like one. Which pieces are
                // struck and at what note is `BudChorus`, in Domain, because "voice five of the
                // twenty and space them evenly" is a rule that is wrong for a year without
                // anybody being able to say why the board sounds thin.
                if (BudChorus.Voiced(nth, of))
                    Audio.Sfx("pop", .22f, BudChorus.Pitch(nth, of), .03f);
            });

            // **After the tween is registered, not before.** Registering supersedes whatever
            // fall was still running on this cell, and a superseded fall *lands* — so lifting
            // the piece first would be undone by the very kill that makes this one safe.
            piece.anchoredPosition = new Vector2(0f, above);
            piece.localScale = Vector3.one;
        }

        /// <summary>The channel a falling piece runs on, so one fall supersedes another.</summary>
        const string FallChannel = "budfall";

        /// <summary>And the channel its landing runs on, so one landing supersedes another.</summary>
        const string SquashChannel = "budsquash";

        /// <summary>
        /// A piece arriving: flattened by what it was carrying, then sprung back past square
        /// and settled.
        ///
        /// <para>
        /// <b>The single most valuable half-second in a falling board, and it costs nothing.</b>
        /// Every game of this shape does it and it is the reason their boards feel solid where
        /// this one felt like paper: a shape that arrives at exactly its resting size has not
        /// been <em>stopped</em> by anything, it has simply finished moving. Squashing on the
        /// frame it lands and springing out of it is the ground pushing back.
        /// </para>
        /// <para>
        /// <b>It writes <c>Cell.Piece</c>'s scale and nothing else in this file does</b>, which
        /// is what makes it safe beside the wind-up and the wash - both of those write the
        /// <em>cell's</em> scale, and two tweens on one value is a bug however different their
        /// channels are. It runs on a channel of its own so a second landing supersedes the
        /// first, and it declares where an interrupted one goes, because it borrows a resting
        /// value rather than travelling to a new one.
        /// </para>
        /// <para>
        /// The spring reads the squashed size <em>before</em> it registers, deliberately:
        /// registering supersedes the squash on this channel and a superseded one is put back
        /// to square, so a line that read it afterwards would spring from nothing every time.
        /// </para>
        /// </summary>
        void Squash(RectTransform piece, float force, float over)
        {
            if (piece == null) return;

            float squash = Mathf.Clamp(force, .07f, .26f);
            float down = Mathf.Max(.055f, over * .26f);
            float up = Mathf.Max(.16f, over * .74f);

            Action rest = () => { if (piece) piece.localScale = Vector3.one; };

            Tween.Run(down, Ease.OutQuad, t =>
            {
                if (!piece) return;
                float s = squash * t;
                piece.localScale = new Vector3(1f + s * .8f, 1f - s, 1f);
            }, piece, SquashChannel).OnAbandon(rest).OnDone(() =>
            {
                if (!piece) return;

                var from = piece.localScale;
                Tween.Run(up, Ease.OutBack, t =>
                {
                    if (!piece) return;
                    piece.localScale = Vector3.LerpUnclamped(from, Vector3.one, t);
                }, piece, SquashChannel).OnAbandon(rest);
            });
        }

        // ------------------------------------------------------------------ what would pop
        /// <summary>
        /// Every flower a tap would set something off on, breathing.
        ///
        /// <para>
        /// <b>This is the single change that took the arithmetic out of the mode.</b> Every game
        /// of this shape shows the player the matches and asks them to <em>pick</em>; Budburst
        /// made them work out, in their head, which cell the colour in hand would turn into a
        /// third of something — and then reported back, correctly, that it did not feel
        /// brain-dead. The board now says which taps pop. The choice is still entirely theirs,
        /// because most groves offer several and they differ enormously in size; what has gone is
        /// the sum they had to do before they could see any of them.
        /// </para>
        /// <para>
        /// <b>Recomputed only when the board or the colour in hand moves</b>, never per frame: it
        /// is one full preview per flower, which settles a whole chain each. Fifty of those is
        /// nothing once per tap and is a stall every frame.
        /// </para>
        /// </summary>
        void PaintPops()
        {
            if (_cells == null || Run == null) return;

            bool live = Playable;

            for (int i = 0; i < _cells.Length; i++)
            {
                var cell = _cells[i];
                if (cell?.Bud == null) continue;

                // White is skipped: it breathes on its own account in PaintCell, harder, and
                // it always pops — it is the bomb. Two breaths on one transform is the bug this
                // file has paid for twice.
                if (cell.Drawn == Energy.All) continue;

                bool pops = live && Run.Pops(i);
                if (pops == cell.Pops) continue;

                cell.Pops = pops;
                var rt = (RectTransform)cell.Bud.transform;

                if (!pops)
                {
                    Tween.KillChannel(rt, PopsChannel);
                    rt.localScale = Vector3.one;
                    continue;
                }

                // A slow, small breath. It has to be readable across a board of fifty and it has
                // to be quieter than anything that is actually happening, so it is the smallest
                // motion in the mode.
                Tween.Breathe(rt, BudTempo.PopsSwell, BudTempo.PopsBreath, i * .11f);
            }
        }

        /// <summary>The channel the "this one pops" breath runs on.</summary>
        const string PopsChannel = "breathe";

        /// <summary>The channel a cell's own spin runs on, so one spin supersedes another.</summary>
        const string SpinChannel = "budspin";

        /// <summary>
        /// How much bigger a critter is once it is out of its cocoon.
        ///
        /// Enough to read as free rather than merely uncaged, and not enough to reach the
        /// cells around it: a critter is drawn at .46 of a cell, so even at this it is a
        /// little over half a cell wide and a grove of four freed ones stays a grid.
        /// </summary>
        const float FreedScale = 1.24f;

        /// <summary>
        /// The critter that has just come out of the cocoon on <paramref name="index"/>, standing
        /// in the grove rather than in the cell.
        ///
        /// <para>
        /// It wears the same flipbook the cell's sleeping one wore, because it <em>is</em> that
        /// critter — the identity is a fact about the square it was shut in on, so a player who
        /// watched a particular creature sleeping there sees that creature get out. The cell's own
        /// critter is put away in the same breath: <see cref="PaintCell"/> hides it whenever the
        /// square is not a cocoon, but the square is bare for a frame or two before the grove
        /// falls into it and two of the same critter is exactly the moment somebody looks.
        /// </para>
        /// </summary>
        Image Resident(int index, Vector2 where)
        {
            if (_freed[index]) Destroy(_freed[index].gameObject);

            var cell = _cells[index];
            if (cell.Critter)
            {
                Tween.KillAll(cell.Critter);
                Tween.KillChannel(cell.Critter.transform, "breathe");
                cell.Critter.color = new Color(1, 1, 1, 0f);
            }

            var critter = UIKit.Img("Freed" + index, _residents, null, Color.white,
                                    Vector2.one * _size * .46f, new Vector2(.5f, .5f), where);
            CritterArt(critter, index, awake: true);

            _freed[index] = critter;
            return critter;
        }

        /// <summary>
        /// A flower winding up: spinning faster and faster in place, swelling, going white.
        ///
        /// <para>
        /// <b>It points at itself, which is the job.</b> The player has just made three of a
        /// colour touch somewhere on a grid of thirty-six, and the game's only chance to show
        /// them <em>where</em> is the moment before those three stop existing. So they spin —
        /// accelerating, because a constant turn reads as decoration and an accelerating one
        /// reads as something building — and they brighten toward white, so the burst that
        /// follows starts from a flower that is already too bright to stay.
        /// </para>
        /// <para>
        /// On the cell's <c>Rt</c> rather than its <c>Bud</c> so the ground under it turns too,
        /// and on a channel shared with the tap's own spin, so a flower the player just touched
        /// that is also in the first bunch hands over cleanly instead of being turned by two
        /// tweens at once.
        /// </para>
        /// </summary>
        void Wind(int index, int colour, float charge, int seed, int wave)
        {
            if (_cells == null || index < 0 || index >= _cells.Length) return;

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            var rt = cell.Rt;
            var bud = cell.Bud;
            var tint = Petal(colour);
            float lean = (seed % 2 == 0) ? 1f : -1f;
            float spin = BudTempo.WindSpin(wave);

            // Over its neighbours for as long as it is bigger than its cell, and put back by
            // the one pass in RestoreDepth. A flower swollen a third past its own square that
            // is drawn *behind* the untouched one beside it reads as clipped rather than as
            // crowding — and the taller the wind-up grows the worse it gets, which is why this
            // arrived with the swell rather than before it.
            rt.SetAsLastSibling();

            // A flower washed by the previous wave is punched by Turn, and a punch is still
            // running on this transform when the next wave winds it up. Two tweens on one value
            // is a bug however different their channels are: the punch borrows the scale it
            // finds, so it would take a mid-wind-up size as the one to squash around and hand
            // *that* back when it ended, leaving the flower permanently oversized. Killing it
            // first restores the real rest scale and gives the wind-up sole ownership. It was
            // survivable while the swell was .34; it is not at .82.
            Tween.KillChannel(rt, "punch");

            Tween.Run(charge, Ease.Linear, t =>
            {
                if (!rt) return;

                // t squared, so it starts almost still and is whipping round by the end.
                rt.localRotation = Quaternion.Euler(0, 0, lean * spin * t * t);
                rt.localScale = Vector3.one * BudTempo.WindScale(t, wave);

                // Held back to two thirds of the way to white until the flower has stopped
                // growing, then pushed the rest of the way. The charge exists to show *which*
                // flowers matched, so it may not go white while that is still being said — and
                // the hold at the end is somewhere safe to spend the rest. See BudTempo.WindWhite.
                if (bud) bud.color = Color.Lerp(tint, Color.white, BudTempo.WindWhite(t));
            }, rt, SpinChannel).OnAbandon(() =>
            {
                if (!rt) return;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            });
        }

        /// <summary>
        /// Puts every cell back in the order it was built in, in <b>one ascending pass</b>.
        ///
        /// <para>
        /// <see cref="Wind"/> lifts a charging flower over its neighbours, so something has to
        /// put it back — and the way not to do it is to restore each cell's remembered index as
        /// its own wave finishes. <c>SetSiblingIndex</c> <em>inserts</em>, so every restore
        /// shifts the cells after it and the next remembered index no longer means what it
        /// meant, which is <c>GroveFieldView</c>'s lesson in the file that had no reason to
        /// learn it twice. Walking ascending and assigning ascending indices is exact, costs
        /// nothing at this size, and needs nothing remembered.
        /// </para>
        /// <para>
        /// It matters at rest and not only mid-burst: a cell's glow is drawn half again as wide
        /// as its square, so the order flowers are stacked in is visible on a settled board.
        /// </para>
        /// </summary>
        void RestoreDepth()
        {
            if (_cells == null) return;

            for (int i = 0; i < _cells.Length; i++)
                if (_cells[i]?.Rt) _cells[i].Rt.SetSiblingIndex(i);
        }

        /// <summary>
        /// One flower going off: it comes apart into its own petals, under a hard white flash,
        /// inside a ring.
        ///
        /// <para>
        /// <b>There is no smoke and there must never be any.</b> The first version drew a real
        /// fire flipbook out of the licensed pack over every burst, and it came back from play
        /// as <em>"when I burst buds a smoke/dust comes out — what is that?"</em>, which is an
        /// exactly correct reading: a plume is a <em>volume</em> event, authored to be seen at
        /// the size of a rocket exhaust, and shrunk onto a 170-point cell and drawn thirteen
        /// times in one wave it is dust. A puzzle grid wants a <b>silhouette</b> event.
        /// </para>
        /// <para>
        /// So the flower is taken apart into shapes with clean edges: six <b>petals</b> thrown
        /// outward, spinning and falling, in the flower's own colour; a <b>flash</b> at the
        /// centre; a <b>starburst of rays</b> that snaps out and is gone; a <b>ring</b> that says
        /// how far it reached; and sparks and embers over the top. Every one of those is legible
        /// at cell size, which is the only test that matters here — and it is what this genre
        /// actually ships, because Royal Match and Toy Blast burst in shards and light and have
        /// no smoke anywhere near the board.
        /// </para>
        /// <para>
        /// Named <c>Split</c> rather than the obvious word because <c>Burst</c> is the game's own
        /// spark emitter and a method here would shadow it inside this class.
        /// </para>
        /// </summary>
        void Split(int index, int wave, int colour, int bunch, float beat, float delay)
        {
            if (delay > 0f)
            {
                Tween.After(delay,
                            () => { if (this) Split(index, wave, colour, bunch, beat, 0f); },
                            this);
                return;
            }

            var where = Where(index);
            var cell = _cells[index];
            var tint = Petal(colour);

            // **How big this one was, said in the drawing rather than in a number afterwards.**
            // Three alike is the rule being met and nine alike is a third of the grove going at
            // once, and both used to draw the same six petals and the same ring.
            // <c>BudChain.Blast</c> is where the rungs live, for the reason every other ladder in
            // this mode is in Domain: it is exactly the decision that gets retuned.
            var blast = BudChain.Blast(bunch);
            float force = BudChain.Force(blast);

            // White is the one flower the player can never change again, so a bunch of them is
            // the ceiling of the mode reached — and it gets the one shape nothing else draws.
            bool prism = colour == Energy.All;

            // **Read before the kill, and that ordering is the whole of it.** The wind-up
            // declares an OnAbandon that puts the cell back to square, and KillChannel honours
            // it — so a line below this that asked how swollen the flower was would be told
            // "not at all", every time.
            //
            // What it is read *for*: the ground goes back to square, and the flower carries its
            // size on into the burst. Discarding it made the flower visibly collapse on the
            // frame it went off — invisible while the wind-up only reached 1.34, and the exact
            // opposite of the gesture once it reaches 1.82. A thing that shrinks before it
            // explodes is not building, it is deflating.
            float swollen = cell.Rt ? cell.Rt.localScale.x : 1f;
            float turned = cell.Rt ? cell.Rt.localEulerAngles.z : 0f;

            Tween.KillChannel(cell.Rt, SpinChannel);
            if (cell.Rt) { cell.Rt.localRotation = Quaternion.identity; cell.Rt.localScale = Vector3.one; }

            ThrowFlower(cell, tint, swollen, turned);

            float life = BudTempo.Shrapnel(beat);
            // The cap was .18s, which was the whole of what a burst's core was ever allowed
            // to be. It was set when a wave lasted .27s and a flash that outlived its own wave
            // was a real hazard; the wave is more than twice that now, so the cap was the one
            // thing still holding the loudest instant in the mode to a sixth of a second.
            float hot = Mathf.Min(life * .28f, .32f);

            // **The hot core, and it is round.** It was `Art.Flash`, which draws a
            // twelve-pointed spiky star — so every burst on the board fired a little searchlight,
            // and thirteen of them in a wave read as exactly that. A burst here is *light*, and
            // light at this size is a bright centre with a fast falloff: `Art.Glow` at a high
            // power is that, and it sits over the wide soft bloom below to make the two-layer
            // core every game of this genre draws. Nothing spiky, nothing with a straight edge
            // in it, and nothing that needs to spin to look alive.
            //
            // White rather than tinted for the first instant, because a burst is brighter than
            // any colour on this board and reading it as light rather than as paint is what
            // makes it feel like something went off.
            var core = UIKit.Img("Flash", _fx, Art.Glow(256, 3.4f), Color.white,
                                 Vector2.one * _size * 1.55f * force, new Vector2(.5f, .5f),
                                 where);
            var crt = (RectTransform)core.transform;

            Tween.Run(hot, Ease.OutQuint, t =>
            {
                if (!core) return;
                crt.localScale = Vector3.one * Mathf.Lerp(.20f, 1.45f, t);
                // **White for two frames and then the flower's own colour, not a lightened
                // one.** `Pal.Lift(tint, .3f)` plus a slow lerp was still nearly white by the
                // time the eye got there, and a wave of thirteen washed the board out — losing
                // which colour had gone off, which is half of what the player is reading.
                core.color = Pal.A(Color.Lerp(Color.white, tint, Mathf.Min(1f, t * 6f)),
                                   1f - t * t);
            }, core).OnDone(() => { if (core) Destroy(core.gameObject); });

            // The bloom under everything, which is what stops a dark blend's burst reading as a
            // hole punched in the board.
            var flare = UIKit.Img("Bloom", _fx, Art.Glow(256, 1.8f), Pal.A(tint, .78f),
                                  Vector2.one * _size * 2.3f * force, new Vector2(.5f, .5f),
                                  where);
            flare.transform.SetAsFirstSibling();

            var frt = (RectTransform)flare.transform;
            Tween.Run(life * 1.05f, Ease.OutQuad, t =>
            {
                if (!flare) return;
                frt.localScale = Vector3.one * Mathf.Lerp(.35f, 1.55f, t);
                flare.color = Pal.A(tint, .78f * (1f - t) * (1f - t));
            }, flare).OnDone(() => { if (flare) Destroy(flare.gameObject); });

            Shockwave(where, tint, _size * 3.1f * force, life * .85f);

            // A second ring chasing the first, from five alike upward. It is the cheapest thing
            // here that reads as *more* rather than as *bigger*: one ring is a burst, and two is
            // a burst that had somewhere to go.
            if (blast != BudBlast.Small)
                Tween.After(life * .16f, () =>
                {
                    if (this) Shockwave(where, Pal.Lift(tint, .5f), _size * 4.4f * force,
                                        life * .95f);
                }, this);

            Burst.Sparks(_fx, where, tint, Mathf.RoundToInt(8f * force), 210f * force, 14f,
                         life * .8f);

            // Budburst's own slot, and it had to be: this is struck thirteen times in a wave
            // and pitched up through a chain, where `pop` is a wooden clunk eight other things
            // are tuned around. The wood breaking that used to be layered under it is gone with
            // the same argument the smoke went with — there is no timber in a flower.
            // Louder and *lower* the bigger the bunch, which is the opposite of the chain's
            // own ladder and deliberately so: the chain climbs in pitch across waves, so a fat
            // single bunch dropping underneath it is what keeps the two readings apart.
            float weight = blast == BudBlast.Huge ? .62f : blast == BudBlast.Big ? .50f : .40f;
            float drop = blast == BudBlast.Huge ? .82f : blast == BudBlast.Big ? .90f : 1f;
            Audio.Sfx("burst", weight, BudTempo.Pitch(wave + 1) * drop);

            if (blast == BudBlast.Huge) Audio.Sfx("pop2", .34f, .78f, .04f);
            // A bunch of white, which is the ceiling of the mode reached. It was a bell, and
            // a bell is the one thing this board must not sound like — every other voice in the
            // grove is a struck block or a pop. It is `free`'s wooden pop taken up an octave
            // instead: the brightest blip in the set, and still the same instrument.
            if (prism) Audio.Sfx("free", .40f, 1.62f, .06f);
        }

        /// <summary>
        /// A cocoon taking a crack and holding — the beat this mode used to skip entirely.
        ///
        /// <para>
        /// <b>It was drawn by nothing at all.</b> A cocoon taking the first of its two cracks
        /// changed one ring's alpha on the next repaint, so the most encouraging thing that can
        /// happen short of freeing somebody arrived as a colour quietly appearing behind thirteen
        /// flowers going off. The model now says so (<c>BudPulseKind.Crack</c>) and this is what
        /// it says: the shell jolts, splinters come off it, and the ring round it flares in the
        /// rope colour it is about to keep.
        /// </para>
        /// <para>
        /// Deliberately smaller than <see cref="Free"/> in every dimension and pitched under it.
        /// The two are one gesture at two strengths — nearly, and there — so somebody who has
        /// seen a crack knows what is coming, and somebody who has seen both can tell them apart
        /// without reading anything.
        /// </para>
        /// </summary>
        void Crack(int index, float beat, float delay)
        {
            if (delay > 0f)
            {
                Tween.After(delay, () => { if (this) Crack(index, beat, 0f); }, this);
                return;
            }

            if (_cells == null || index < 0 || index >= _cells.Length) return;

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            var where = Where(index);
            float life = Mathf.Max(beat * 1.2f, .34f);

            Tween.Shake(cell.Rt, 9f, life * .55f);

            if (cell.Pod)
            {
                var pod = cell.Pod;
                var prt = (RectTransform)pod.transform;
                var was = pod.color;

                Tween.Run(life * .5f, Ease.OutQuad, t =>
                {
                    if (!pod) return;
                    prt.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * .16f);
                    pod.color = Color.Lerp(Color.white, was, t);
                }, pod).OnDone(() =>
                {
                    if (!pod) return;
                    prt.localScale = Vector3.one;
                    pod.color = was;
                });
            }

            if (cell.Ring)
            {
                var ring = cell.Ring;
                var rrt = (RectTransform)ring.transform;

                Tween.Run(life, Ease.OutQuad, t =>
                {
                    if (!ring) return;
                    rrt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, t);
                    ring.color = Pal.A(Pal.Rope, Mathf.Lerp(.95f, .78f, t));
                }, ring).OnDone(() =>
                {
                    if (!ring) return;
                    rrt.localScale = Vector3.one;

                    // Put back whatever the board says rather than what this animation left, so
                    // a crack drawn over a cocoon the *next* wave opens cannot leave a ring
                    // standing on bare ground.
                    PaintCell(index, true);
                });
            }

            Splinters(where, life);
            Shockwave(where, Pal.Rope, _size * 1.9f, life * .7f);
            Burst.Sparks(_fx, where, Pal.Rope, 7, 150f, 14f, life * .8f);

            // A shell taking a crack, and it is not the shell breaking — that is `Free`. The
            // wood-break sample that used to play here went with the white flower's, for the
            // same reason: there is nothing in this grove made of anything that shatters.
            Audio.Sfx("pop", .30f, .74f);
        }

        /// <summary>
        /// Three chips off a shell that held. <see cref="Shards"/>'s little brother, and a method
        /// of its own rather than a parameter because the two say different things: this is
        /// debris coming <em>off</em> something still standing, so the pieces are fewer, smaller
        /// and thrown sideways rather than outward on a ring.
        /// </summary>
        void Splinters(Vector2 at, float life)
        {
            var sprite = Art.Crystal(128);
            if (sprite == null) return;

            for (int i = 0; i < 3; i++)
            {
                float ang = -.4f + i * 1.9f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Abs(Mathf.Sin(ang)) * .7f);
                float reach = _size * (.5f + i * .12f);
                float spin = (i % 2 == 0 ? 1f : -1f) * (240f + i * 60f);

                var chip = UIKit.Img("Splinter", _fx, sprite, new Color(.94f, .90f, .74f, 1f),
                                     Vector2.one * _size * .18f, new Vector2(.5f, .5f), at);
                var rt = (RectTransform)chip.transform;

                Tween.Run(life * .8f, Ease.OutCubic, t =>
                {
                    if (!chip) return;
                    rt.anchoredPosition = at + dir * reach * t
                                        + new Vector2(0f, -_size * .5f * t * t);
                    rt.localRotation = Quaternion.Euler(0, 0, spin * t);
                    rt.localScale = Vector3.one * (1f - t * .4f);
                    chip.color = new Color(.94f, .90f, .74f, 1f - t * t);
                }, chip).OnDone(() => { if (chip) Destroy(chip.gameObject); });
            }
        }

        /// <summary>
        /// The flower coming apart, which is the tenth of a second the burst used to skip.
        ///
        /// It is drawn on the flower's own <c>Image</c> rather than on a copy, so there is
        /// nothing to tidy up and nothing that can be left behind if the run ends mid-chain:
        /// <see cref="PaintCell"/> puts it back wherever the board says it should be.
        /// </summary>
        /// <param name="from">
        /// How swollen the wind-up left it, handed over so the growth is continuous through the
        /// frame it bursts on. The cell's own square goes back to normal; only the flower keeps
        /// the size, which is what makes it read as the flower tearing free of its ground rather
        /// than as the whole tile inflating.
        /// </param>
        /// <param name="turned">
        /// And how far round it had spun, for the same reason. A flower whipping through two
        /// turns that snaps upright to burst has stopped dead first, which is a beat of stillness
        /// in the one place the sequence cannot afford one.
        /// </param>
        void ThrowFlower(Cell cell, Color tint, float from = 1f, float turned = 0f)
        {
            if (cell.Bud == null)
            {
                cell.Drawn = Energy.None;
                return;
            }

            var bud = cell.Bud;
            var brt = (RectTransform)bud.transform;

            Tween.KillAll(bud);
            if (cell.Halo) cell.Halo.color = new Color(1, 1, 1, 0f);
            if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);
            cell.Drawn = Energy.None;

            // **It tears free rather than fading out, and it takes long enough to see.** At
            // .11s and a uniform grow this was a flower blinking off: the wind-up spent a third
            // of a second building to it and then nothing came of it. Now it is thrown - pulled
            // long as it leaves, released past its own size on an out-back so the shape
            // overshoots the way a thing under pressure does, whipped round harder than the
            // wind-up left it, and gone. The alpha is held back deliberately: it stays fully
            // lit until the shape has finished moving, so the last thing the eye keeps is the
            // flower at its largest rather than a ghost of it.
            Tween.Run(BudTempo.Burst, Ease.OutBack, t =>
            {
                if (!bud) return;

                float grow = from * (1f + t * 1.05f);
                float draw = .22f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                brt.localScale = new Vector3(grow * (1f + draw), grow * (1f - draw * .7f), 1f);
                brt.localRotation = Quaternion.Euler(0, 0, turned + t * 130f);
                // Clamped because `OutBack` overshoots past one on purpose, and an alpha is
                // the one thing here that must not follow it out.
                float u = Mathf.Clamp01(t);
                bud.color = Pal.A(Color.Lerp(tint, Color.white, Mathf.Min(1f, t * 2.2f)),
                                  1f - u * u * u);
            }, bud).OnDone(() =>
            {
                if (!bud) return;
                brt.localScale = Vector3.one;
                brt.localRotation = Quaternion.identity;
                bud.color = new Color(1, 1, 1, 0f);
            });
        }

        /// <summary>
        /// Colour arriving on a flower beside a bunch that has just gone off, and that flower
        /// turning.
        ///
        /// <para>
        /// <b>There is no bolt between the two cells, and there was, and it was wrong.</b> A
        /// stroke of lightning was drawn from the bursting neighbour to this flower, which read
        /// beautifully in principle and in practice fired from the wrong place — reported from
        /// play as <em>"random electric effects at positions unrelated to where the flowers are
        /// rotating"</em>, which is exactly what it was.
        /// </para>
        /// <para>
        /// The cause is worth keeping, because it is a trap this whole view is built over.
        /// <c>BudRun.Tap</c> settles the <em>entire</em> chain before a single frame is drawn,
        /// so <c>Run.Board</c> is the board as it will be at the <em>end</em>. Asking it which
        /// neighbour is <c>Bare</c> therefore answers "any cell that is empty when this is all
        /// over" — which is a flower that has not gone off yet, and, worse, a cell that was bare
        /// ground in the authored layout and never held a flower at all. So bolts flew out of
        /// blank soil. And where no neighbour matched, the source fell back to the target's own
        /// cell and the bolt was drawn pointing off to the right into nothing.
        /// </para>
        /// <para>
        /// The lesson generalises past the bolt: <b>anything here that needs to know what the
        /// board looked like <em>during</em> a wave must read the pulses, never
        /// <c>Run.Board</c>.</b> The pulses carry their wave; the board does not carry time at
        /// all. Every other effect in this file is anchored on the cell it belongs to, which is
        /// why the bolt is the only one that could be wrong about a position.
        /// </para>
        /// <para>
        /// What is left says the same thing without a line between two cells: a small flash
        /// where the colour lands, and the flower whitening for a frame before settling into
        /// what it has become.
        /// </para>
        /// </summary>
        void Land(BudWash wash, float beat, float delay)
        {
            if (delay > 0f)
            {
                Tween.After(delay, () => { if (this) Land(wash, beat, 0f); }, this);
                return;
            }

            var cell = _cells[wash.Cell];
            var tint = Petal(wash.To);

            float strike = BudTempo.Strike(beat);

            // Held back by a beat of its own so the colour arrives *after* the bunch that sent
            // it has gone off rather than in the same frame.
            Tween.After(strike, () =>
            {
                if (!this) return;
                Flare(Where(wash.Cell), tint, BudTempo.Linger(beat));
                Turn(wash.Cell, cell, tint, strike);
            }, this);
        }

        /// <summary>
        /// The small flash where colour lands on a flower.
        ///
        /// Drawn in the colour that arrived rather than in white, which is the whole point of
        /// it: a white flash over a flower that then turns yellow makes the player work out
        /// afterwards what happened, where a yellow one says it as it happens. Smaller than a
        /// burst's flash and gone faster, because this flower did not go off — something
        /// reached it.
        /// </summary>
        void Flare(Vector2 at, Color tint, float life)
        {
            // Round, for the core's reason: this was a ten-pointed star flipped and rotated by
            // index, so a wave washing twenty flowers drew twenty little searchlights at
            // twenty angles. What it has to say is "colour arrived here", and a soft swell of
            // that colour says it without adding a shape to the board.
            var sprite = Art.Glow(128, 2.6f);
            if (sprite == null) return;

            var fork = UIKit.Img("Flare", _fx, sprite, Pal.A(Pal.Lift(tint, .45f), .95f),
                                 Vector2.one * _size * 1.05f, new Vector2(.5f, .5f), at);
            var rt = (RectTransform)fork.transform;

            Tween.Run(Mathf.Max(life * 1.4f, .10f), Ease.OutQuad, t =>
            {
                if (!fork) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.42f, 1.25f, t);
                fork.color = Pal.A(Color.Lerp(Pal.Lift(tint, .45f), tint, t), .95f * (1f - t));
            }, fork).OnDone(() => { if (fork) Destroy(fork.gameObject); });
        }

        /// <summary>
        /// A flower taking its new colour: a white frame, then the colour, then a swell.
        ///
        /// The white frame is what makes a small change legible. Two adjacent blends can differ
        /// by very little — red-and-green to red-and-green-and-blue is a step a player will not
        /// catch in a grove of thirty-six going off — so the flower is over-lit for an instant
        /// first, which nobody can miss, and lands on the real colour afterwards.
        /// </summary>
        void Turn(int index, Cell cell, Color tint, float over)
        {
            PaintCell(index, true);
            if (cell.Bud == null) return;

            var bud = cell.Bud;
            var real = bud.color;

            Tween.Run(Mathf.Max(over * 1.7f, .20f), Ease.OutCubic, t =>
            {
                if (!bud) return;
                bud.color = Color.Lerp(Pal.Lift(tint, .85f), real, t);
            }, bud).OnDone(() => { if (bud) bud.color = real; });

            // **A swell rather than a wobble, and it stays on the punch channel for a reason.**
            // `Tween.Punch` is a damped sine through three half-cycles, which is right for a
            // control being pressed and wrong for a flower being *changed* - a thing that
            // shivers has been disturbed, where a thing that swells and settles has become
            // something. It is deliberately still registered as "punch", because `Wind` kills
            // that channel before a wind-up takes the same transform over, and this gesture is
            // the whole reason it does: two tweens on one value is a bug however different
            // their channels are, and a wash caught mid-flight by the next wave used to hand
            // its own half-swollen scale back as the flower's resting size.
            var rt = cell.Rt;
            if (rt)
            {
                float pop = Mathf.Max(over * 1.5f, .26f);

                Tween.Run(pop, Ease.Linear, t =>
                {
                    if (!rt) return;
                    float s = 1f + .30f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    rt.localScale = new Vector3(s, s, 1f);
                }, rt, "punch").OnAbandon(() => { if (rt) rt.localScale = Vector3.one; });
            }

            Audio.Sfx("tick", .16f, .94f + Bright(tint) * .52f);
        }

        /// <summary>How light a colour is, which is what a flower's note is pitched by.</summary>
        static float Bright(Color c) => (c.r + c.g + c.b) / 3f;

        /// <summary>
        /// A cocoon breaking open, and the critter actually coming out of it.
        ///
        /// <para>
        /// <b>The critter is a real one</b> — the same flipbook the glades and the companion
        /// roster draw — and it wakes as it goes: dim and slow inside the pod, then full colour
        /// and full speed as the shell breaks. That is the whole point of the mode arriving on
        /// screen, so it is the one thing here that gets its own second of attention.
        /// </para>
        /// <para>
        /// <b>The shell breaks into pieces of itself, and that is generated rather than cut.</b>
        /// A four-frame flipbook was tried and is the wrong tool twice over: four frames of
        /// anything at this size is a flicker, and no sheet in the pack breaks a *crystal* — the
        /// one it was cut from was four grey smudges, so a cocoon opening looked like a cocoon
        /// evaporating. Throwing half a dozen shrunken <c>Art.Crystal</c> chips outward costs no
        /// asset at all and is exactly right, because the chips are the shell: the same shape,
        /// the same colour, in pieces.
        /// </para>
        /// </summary>
        void Free(int index, float beat, float delay = 0f)
        {
            if (delay > 0f)
            {
                Tween.After(delay, () => { if (this) Free(index, beat, 0f); }, this);
                return;
            }

            var where = Where(index);
            var cell = _cells[index];

            if (cell.Ring) cell.Ring.color = new Color(1, 1, 1, 0f);
            if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);

            float life = Mathf.Max(beat * 2.4f, .55f);

            // The shell: it swells, whitens, and is gone in a fifth of a second — the chips are
            // what carries the rest.
            if (cell.Pod)
            {
                var pod = cell.Pod;
                var prt = (RectTransform)pod.transform;

                Tween.Run(life * .28f, Ease.OutQuad, t =>
                {
                    if (!pod) return;
                    prt.localScale = Vector3.one * (1f + t * .55f);
                    pod.color = new Color(1f, Mathf.Lerp(.96f, 1f, t), Mathf.Lerp(.60f, 1f, t),
                                          1f - t);
                }, pod).OnDone(() =>
                {
                    if (!pod) return;
                    prt.localScale = Vector3.one;
                    pod.color = new Color(1, 1, 1, 0f);
                });
            }

            Shards(where, life);

            // And the critter comes out — and **stays**, which is the whole point of it.
            //
            // It used to leap out and fade to nothing over the last third of the animation, so
            // a grove that had freed everybody was an empty field: the thing the player spent
            // the level earning was the one thing not on the board at the end. Now it jumps
            // clear of the shell, settles back onto the square it was let out on a little larger
            // than it was shut in, and breathes there for the rest of the run.
            //
            // **And it leaves the cell to do it.** Freeing empties that square in the model, so
            // the grove falls into it within the same wave — and while the critter was a child of
            // the cell, the flower landing on it dragged the critter down and `PaintCell` could
            // paint a sleeping one straight over somebody the player had just let out. So it
            // stands on `_residents` at the position it was freed at: nothing that falls can move
            // it, cover it, or repaint it, and the grove comes down behind it. See `_freed`.
            //
            // Two beats rather than one, because a single tween cannot do both halves: the
            // leap is fast and overshoots, the settle is slower and lands. One eased curve
            // across the whole thing reads as a float rather than as getting out.
            {
                var critter = Resident(index, where);
                var crt = (RectTransform)critter.transform;

                // **They do not travel. At all.**
                //
                // Three versions of this have been wrong and every one of them was wrong for
                // the same reason: the critter was given somewhere to *go*. First it leapt out
                // and fell back onto the square, bouncing past it on an `OutBack`. Then it rose
                // out of the shell and stood there — better, but it still travelled, and it
                // still ended by flying all the way down to the counter under the board, which
                // is a critter falling off the bottom of the grove.
                //
                // **A creature getting out is a moment, not a journey.** It appears where the
                // cocoon was, swells, shines, and is gone from that spot — and the grove drops a
                // flower into the square it left, which is the thing that actually says the
                // slot is free again. Nothing moves across the screen, so there is nothing that
                // can read as falling, and the gesture is four beats the player can follow
                // instead of a shape wandering about.
                //
                // Only the **scale** is animated, and it is the only thing that may overshoot:
                // a size springing past itself is a pop, and a position springing past itself
                // is a drop.
                float pop = Mathf.Min(life * .26f, .40f);
                crt.anchoredPosition = where;

                Tween.Run(pop, Ease.OutBack, t =>
                {
                    if (!critter) return;

                    crt.localScale = Vector3.one * Mathf.LerpUnclamped(.28f, FreedScale, t);
                    critter.color = Color.white;
                }, critter).OnDone(() =>
                {
                    if (!this || !critter) return;

                    // Put back exactly rather than left wherever the curve stopped, because
                    // what happens next borrows this scale.
                    crt.localScale = Vector3.one * FreedScale;
                    crt.localRotation = Quaternion.identity;

                    // **And here is the moment the level was for, said once and plainly.**
                    // Everything before this is the shell coming apart — light, chips, a
                    // shockwave — and none of it is *about the critter*: it is about the cocoon
                    // it was in, and the creature arrives in the middle of it as one more thing
                    // moving. So the noise stops, a ring closes around them and they pump inside
                    // it. It was reported as not being visible at all, which is what happens
                    // when the payoff is drawn in the same register as the packaging.
                    Circle(index, where);

                    // The shine: a soft swell of light behind them, so the beat where they
                    // are the only thing moving is also the brightest thing on the board.
                    Shine(index, where);

                    // And then they are gone from that spot, chained off the pump rather than
                    // timed to match it, so the idle breath a finished pump would start can
                    // never be driving the scale the fade is writing. See `Vanish`.
                    Pump(critter, BudTempo.FreedHold, index, BudTempo.FreedGreet,
                         () => Vanish(index));
                });
            }

            Shockwave(where, Pal.Gold, _size * 3.4f, life * .7f);
            Burst.Sparks(_fx, where, Pal.Gold, 12, 230f, 18f, life * .8f);

            var halo = UIKit.Img("Freed", _fx, Art.Glow(256, 1.8f), Pal.A(Pal.Gold, .85f),
                                 Vector2.one * _size * 2.4f, new Vector2(.5f, .5f), where);
            var hrt = (RectTransform)halo.transform;

            Tween.Run(life * .8f, Ease.OutQuint, t =>
            {
                if (!halo) return;
                hrt.localScale = Vector3.one * Mathf.Lerp(.2f, 1.6f, t);
                halo.color = Pal.A(Pal.Gold, .85f * (1f - t));
            }, halo).OnDone(() => { if (halo) Destroy(halo.gameObject); });

            // One note, and a small one. A bell with a chime a tenth of a second behind it
            // used to play here — the loudest thing in the game, fired once per cocoon, up to
            // four inside a chain over thirteen bursts already sounding. This is `menu`'s block
            // of wood struck a fifth higher: heard clearly over a burst, gone before the next.
            Audio.Sfx("free", .55f, 1f);
        }

        /// <summary>
        /// The shell in pieces: six chips of the cocoon's own shape thrown outward.
        ///
        /// Turned as they go and shrinking rather than fading alone, because a chip that only
        /// fades reads as a ghost and a chip that tumbles reads as debris. They are drawn from
        /// <c>Art.Crystal</c> — the pod's own sprite — so the pieces are unmistakably pieces of
        /// the thing that just broke.
        /// </summary>
        void Shards(Vector2 at, float life)
        {
            var sprite = Art.Crystal(128);
            if (sprite == null) return;

            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + .35f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float reach = _size * (.85f + (i % 3) * .22f);
                float spin = (i % 2 == 0 ? 1f : -1f) * (200f + i * 40f);
                float size = _size * (.30f - (i % 3) * .05f);

                var chip = UIKit.Img("Chip", _fx, sprite, new Color(.94f, .90f, .74f, 1f),
                                     Vector2.one * size, new Vector2(.5f, .5f), at);
                var rt = (RectTransform)chip.transform;

                Tween.Run(life * .78f, Ease.OutCubic, t =>
                {
                    if (!chip) return;

                    // Thrown out and pulled down, so the shell falls apart rather than
                    // expanding evenly like a ring.
                    rt.anchoredPosition = at + dir * reach * t
                                        + new Vector2(0f, -_size * .55f * t * t);
                    rt.localRotation = Quaternion.Euler(0, 0, spin * t);
                    rt.localScale = Vector3.one * (1f - t * .45f);
                    chip.color = new Color(.94f, .90f, .74f, 1f - t * t);
                }, chip).OnDone(() => { if (chip) Destroy(chip.gameObject); });
            }
        }

        /// <summary>
        /// The ring a burst throws, which is the part that says how far it reached.
        ///
        /// Its <em>size</em> is tweened rather than its scale, so the ring's own line stays the
        /// same thickness however wide it gets — a scaled ring thickens as it grows, which reads
        /// as a bubble inflating rather than as a wave leaving.
        /// </summary>
        void Shockwave(Vector2 at, Color tint, float to, float seconds)
        {
            var ring = UIKit.Img("Wave", _fx, Art.Wave(256, 9f), Pal.A(tint, .85f),
                                 Vector2.one * (_size * .4f), new Vector2(.5f, .5f), at);
            var rt = (RectTransform)ring.transform;

            Tween.Run(seconds, Ease.OutQuint, t =>
            {
                if (!ring) return;
                rt.sizeDelta = Vector2.one * Mathf.Lerp(_size * .4f, to, t);
                ring.color = Pal.A(tint, .85f * (1f - t) * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// The running count, while the chain is still going. Wave by wave rather than at the
        /// end, because nobody watching the fourth wave knows yet whether there is a fifth.
        /// </summary>
        void ShowChain(int nth, int of)
        {
            if (_chain == null)
            {
                float above = _origin.y + _cell * .66f;

                _chain = UIKit.Titled("Chain", _fx, "", 80, Pal.Gold, TextAnchor.MiddleCenter,
                                      new Vector2(540f, 160f), new Vector2(.5f, .5f),
                                      new Vector2(0f, above), 6f, 6f);
            }

            _chain.gameObject.SetActive(true);
            _chain.transform.SetAsLastSibling();
            _chain.text = Loc.Format("mode.bud.multiplier", nth);
            _chain.fontSize = BudChain.PointsFor(nth);

            // The count climbs through the rung colours as it goes, so the word at the end is
            // the colour the number had already got to rather than a surprise.
            int rung = BudChain.Rung(nth);
            _chain.color = rung < 0 ? Pal.Gold : RungColour(rung);

            var rt = (RectTransform)_chain.transform;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            Tween.Pop(rt, .45f, BudTempo.CountPop(of));
        }

        /// <summary>The four rungs' colours, in order. GREAT, AMAZING, EPIC, LEGENDARY.</summary>
        static Color RungColour(int rung)
        {
            switch (rung)
            {
                case 0: return Pal.Mint;
                case 1: return Pal.Gold;
                case 2: return Pal.Bloom;
                default: return Pal.Radiance;
            }
        }

        /// <summary>
        /// The word at the end, which is the loudest thing this mode says.
        ///
        /// <para>
        /// <b>It is the score, said out loud, and it is built to be believed.</b> It used to be
        /// a caption swap on the running count — same size, same place, same colour — so the
        /// biggest moment in the mode arrived as the smallest change on the screen, which is
        /// exactly the fault <c>WheelPrizeOverlay</c> was built to fix one feature over. It now
        /// gets its own arrival: a ring thrown out behind it, the word slamming in from
        /// oversize, a rung colour, a shockwave, sparks, and a flash whose brightness is the
        /// rung's.
        /// </para>
        /// <para>
        /// <b>Every rung gets all of it, only louder.</b> A ladder that withholds the
        /// celebration below its top rung teaches the player that most of what they do is not
        /// worth celebrating — which on a mode built to be generous (invariant 20k) is the
        /// wrong lesson twice over.
        /// </para>
        /// </summary>
        IEnumerator Fanfare(int waves, string wordKey)
        {
            if (_chain == null) yield break;

            int rung = BudChain.Rung(waves);
            if (rung < 0) rung = 0;

            var tint = RungColour(rung);
            float weight = .55f + rung * .15f;

            // The running count goes; the word takes the middle of the grove.
            HideChain();

            var word = Word();
            word.text = Loc.Get(wordKey);
            word.color = tint;
            float slam = Fit(word, BudChain.WordPointsFor(waves));

            var rt = (RectTransform)word.transform;
            rt.gameObject.SetActive(true);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            // A bloom laid under it first, so the word arrives out of light rather than simply
            // appearing over the board. It is what lets a word be drawn across a grid of small
            // bright shapes and still be the brightest thing on the screen.
            var halo = UIKit.Img("WordGlow", _fx, Art.Glow(256, 1.7f), Pal.A(tint, 0f),
                                 new Vector2(_size * 9f, _size * 3.4f), new Vector2(.5f, .5f),
                                 Vector2.zero);
            halo.transform.SetAsFirstSibling();
            var hrt = (RectTransform)halo.transform;

            Tween.Run(BudTempo.Fanfare * .9f, Ease.OutQuad, t =>
            {
                if (!halo) return;
                hrt.localScale = Vector3.one * Mathf.Lerp(.55f, 1.15f, Mathf.Min(1f, t * 3f));
                float a = t < .12f ? t / .12f : 1f - (t - .12f) / .88f;
                halo.color = Pal.A(tint, a * (.30f + rung * .06f));
            }, halo).OnDone(() => { if (halo) Destroy(halo.gameObject); });

            // A ring thrown out from behind the word, so it arrives *out of* something - and
            // a second one chasing it a beat later, which is `Split`'s trick at grove scale:
            // one ring is an arrival and two is an arrival that had somewhere to go.
            Shockwave(Vector2.zero, tint, _size * (4.5f + rung * 1.6f), .55f);
            Tween.After(.14f, () =>
            {
                if (this) Shockwave(Vector2.zero, Pal.Lift(tint, .45f),
                                    _size * (6.5f + rung * 2.2f), .78f);
            }, this);

            // The slam. In from oversize and past its resting size, which is the one motion that
            // reads as impact rather than as an entrance.
            Tween.Run(.34f, Ease.OutQuint, t =>
            {
                if (!word) return;
                float k = Mathf.Lerp(slam, 1f, t) + Mathf.Sin(t * Mathf.PI) * .12f;
                rt.localScale = Vector3.one * k;
                rt.localRotation = Quaternion.Euler(0, 0, (1f - t) * (rung % 2 == 0 ? 7f : -7f));
                word.color = Color.Lerp(Color.white, tint, t);
            }, word).OnDone(() =>
            {
                if (!word) return;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;

                // **It holds for well over a second, so it may not hold still.** The slam is
                // over in a third of a second and everything else on screen is settling; a word
                // frozen at rest for the remaining beat stops being the loudest thing on the
                // board and starts being a caption. One slow breath keeps it alive without
                // competing with the grove underneath, and it is the only motion left running
                // by then, so nothing is borrowing the scale it borrows.
                Tween.Punch(rt, .10f, .40f);
                Tween.After(.44f, () =>
                {
                    if (this && rt) Tween.Breathe(rt, .035f, .95f);
                }, this);
            });

            Flow.Flash(Pal.A(new Color(1f, .96f, .82f), .16f + rung * .07f), .16f, .44f);
            Burst.Sparks(_fx, Vector2.zero, tint, 16 + rung * 8, 320f + rung * 120f,
                         22f + rung * 5f, .85f);
            if (_grid)
            {
                Tween.Shake(_grid, 8f + rung * 6f, .34f);
                Tween.Punch(_grid, .035f + rung * .015f, .46f);
            }

            Audio.Sfx("win", weight, 1f + rung * .07f);
            Audio.Sfx("star", .40f + rung * .08f, 1f + rung * .05f, .10f);

            // The top two rungs get the thing nothing else in this mode gets, and how much of
            // it is the rung. It used to be the top rung alone, which on a chapter whose groves
            // mostly run two and three waves meant almost nobody ever saw any.
            if (rung >= 2) Burst.Confetti(_fx, 26 + rung * 14);

            yield return new WaitForSecondsRealtime(BudTempo.Fanfare);
            if (!this) yield break;

            HideWord();
        }

        /// <summary>The most the word ever slams in at, when the word is short enough for it.</summary>
        const float WordSlam = 1.85f;

        /// <summary>And the least, below which an entrance stops being one.</summary>
        const float WordSlamLeast = 1.12f;

        /// <summary>
        /// How much of the screen the word is allowed to take at rest, leaving the remainder for
        /// the slam to be drawn in.
        /// </summary>
        const float WordShare = .80f;

        /// <summary>
        /// Sizes the word to the screen and answers how hard it may slam in.
        ///
        /// <para>
        /// <b>LEGENDARY ran off both edges, and it was over before the slam was involved:</b>
        /// measured against the game's own font, the top rung's 194 points draw it
        /// <b>1195px</b> wide on a canvas that has 1024 to give. AMAZING fitted at rest and
        /// overflowed at 1.85. Two things had to be true for that — the label was built as wide
        /// as the <em>grove</em> plus a margin, so on a five-wide board the box was narrower
        /// than the phone, and the ladder hands out points by rung without knowing how many
        /// letters the word has or what language it is in.
        /// </para>
        /// <para>
        /// <b>The resting size is fitted first and the slam takes what is left, which is the
        /// order that matters.</b> Shrinking the font until the <em>slam</em> fits is the
        /// obvious reading and it is the wrong trade: it takes LEGENDARY down to 102 points to
        /// buy an 85% overshoot nobody asked for, when the resting word is the thing actually
        /// being read. Fitted this way it stays at 132 and slams at 1.26 — measured, along with
        /// GREAT and EPIC keeping the full 1.85 because they are short enough to have it.
        /// </para>
        /// <para>
        /// The authored size is therefore a <b>ceiling</b> rather than an instruction, and every
        /// number comes off the font at run time (<c>preferredWidth</c>) rather than out of a
        /// constant — which is what makes it safe in a language where the word for LEGENDARY is
        /// twice as long. <c>UIKit.Squeeze</c>'s bargain, which the buttons already make.
        /// </para>
        /// </summary>
        float Fit(Text word, int points)
        {
            if (word == null) return WordSlam;

            var rt = (RectTransform)word.transform;
            float room = (_grid != null ? _grid.rect.width : 1080f) - 56f;
            if (room < 120f) room = 120f;

            rt.sizeDelta = new Vector2(room, rt.sizeDelta.y);

            // Best-fit would override the size chosen below at draw time, a frame or two later,
            // once the dynamic font's texture had been regenerated - which is the caption
            // arriving crushed and then springing out that `UIKit.OneLine` exists to prevent.
            word.resizeTextForBestFit = false;
            word.horizontalOverflow = HorizontalWrapMode.Overflow;
            word.fontSize = points;

            float rest = word.preferredWidth;
            float want = room * WordShare;

            if (rest > want && rest > 1f)
            {
                word.fontSize = Mathf.Max(40, Mathf.FloorToInt(points * want / rest));
                rest = word.preferredWidth;
            }

            if (rest <= 1f) return WordSlam;
            return Mathf.Clamp(room / rest, WordSlamLeast, WordSlam);
        }

        Text _word;

        /// <summary>
        /// The label the word is drawn on, built once.
        ///
        /// <b>Its own label, in the middle of the grove, rather than the running count's.</b> The
        /// count belongs at the top, out of the way, because it is *information* arriving while
        /// the player is trying to watch the board. The word is the opposite: it is the payoff,
        /// it belongs where the eye already is, and it is allowed to cover flowers for a second
        /// — which is exactly why it cannot share a label with something that must not.
        /// </summary>
        Text Word()
        {
            if (_word != null) return _word;

            _word = UIKit.Titled("Word", _fx, "", 120, Pal.Gold, TextAnchor.MiddleCenter,
                                 new Vector2(_layout.Width * _cell + 120f, 260f),
                                 new Vector2(.5f, .5f), Vector2.zero, 8f, 8f);
            _word.raycastTarget = false;
            return _word;
        }

        void HideWord()
        {
            if (_word == null) return;

            var text = _word;
            var rt = (RectTransform)text.transform;

            // **The breathe is killed before anything reads or writes this scale.** The hold
            // leaves one running on this very transform, and the exit below writes the same
            // value from a different owner - two tweens on one value, which is a bug however
            // different their channels are. Killing it hands the resting scale back first, so
            // the exit starts from square rather than from wherever the breath had reached.
            Tween.KillChannel(rt, "breathe");

            // Up and out rather than simply fading, so the last thing the grove does is move.
            Tween.Run(.30f, Ease.InQuad, t =>
            {
                if (!text) return;
                rt.anchoredPosition = new Vector2(0f, t * _size * .8f);
                rt.localScale = Vector3.one * (1f + t * .25f);
                text.color = Pal.A(text.color, 1f - t);
            }, text).OnDone(() =>
            {
                if (!text) return;
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
                text.gameObject.SetActive(false);
            });
        }

        void HideChain()
        {
            if (_chain == null) return;

            var text = _chain;
            Tween.Run(.24f, Ease.InQuad, t =>
            {
                if (!text) return;
                text.color = Pal.A(text.color, 1f - t);
                text.transform.localScale = Vector3.one * (1f - t * .3f);
            }, _chain).OnDone(() => { if (text) text.gameObject.SetActive(false); });
        }

        // ------------------------------------------------------------------ the hint
        /// <summary>
        /// Whether there is anything to point at at all.
        ///
        /// <para>
        /// <b>Cheap on purpose.</b> The screen asks this on every repaint, and the honest answer
        /// — "is there a tap that keeps this grove winnable" — is a search
        /// (<see cref="BudHint"/>) costing tens of thousands of positions. So this asks the
        /// question a button can afford: is any tap legal with the colour in hand. That is
        /// exactly the refusal <c>HintPrompt</c> needs, because the only state it has to keep
        /// somebody out of is one where a hint would be spent on nothing.
        /// </para>
        /// <para>
        /// <c>BoardView.CanHint</c> is the same contract one mode over, and the same reasoning
        /// applies to why it is asked *before* the pool: a board with nothing to point at cannot
        /// cost anybody a hint, and nobody is sold a video for one that could not have been
        /// spent.
        /// </para>
        /// </summary>
        public bool CanHint
        {
            get
            {
                if (Run == null || !TakingInput || Held) return false;
                if (Run.Verdict.IsOver) return false;

                for (int i = 0; i < Run.Board.Count; i++)
                    if (Run.CanTap(i)) return true;

                return false;
            }
        }

        /// <summary>
        /// Marks a flower worth tapping, and shows what tapping it would set off.
        ///
        /// <para>
        /// <b>It points; it does not play.</b> A glade's hint turns the conduit, because a glade
        /// has one right answer per tile and turning it is the whole of the advice. A grove has
        /// many, the tap is one thumb-width away, and taking it for the player would spend a tap
        /// out of their satchel on their behalf — which is the difference between a hint and a
        /// move. So the mark stands and the player decides.
        /// </para>
        /// <para>
        /// <paramref name="taken"/> fires when the mark goes away — the tap landed and its chain
        /// settled, or it stood its full <c>BudTempo.HintHold</c> and gave up — and never when a
        /// restart takes the board out from under it. That ordering is the whole of why it is a
        /// callback rather than a return: <c>PlayScreen</c> raises the empty-pool offer the
        /// instant its reveal ends, and it can, because a glade's hint is *consumed* by the
        /// reveal. Here the advice is still standing, so a panel thrown up then would cover the
        /// one thing the hint was spent on.
        /// </para>
        /// </summary>
        public bool Hint(Action taken = null)
        {
            if (!Playable) return false;

            var spot = BudHint.Best(Run);
            if (!spot.Any) return false;

            // Superseding a mark that is still standing drops its callback rather than firing
            // it: two offers stacked over one board is the fault the pause menu shipped with.
            HideMark();
            _hintDone = taken;
            _hintAt = spot.Cell;

            ShowMark(spot);
            Ripple(spot.Cell);

            Audio.Sfx("tip", .5f, 1.05f);
            OnChanged();
            return true;
        }

        void OnChanged() => Changed?.Invoke();

        /// <summary>
        /// The mark itself: a ring on the flower, a halo under it, and the colour it would
        /// become floating over it.
        ///
        /// <para>
        /// <b>The chip above the ring is the half that teaches.</b> A ring says "here" and says
        /// nothing about why, and the reason this cell is worth the tap is that the colour in
        /// hand turns it into something that matches its neighbours. Drawing the result — as a
        /// flower, in <c>BudFlower</c>'s own silhouette, so it is unmistakably the same kind of
        /// thing as what is on the board — says the whole sentence.
        /// </para>
        /// </summary>
        void ShowMark(BudSpot spot)
        {
            if (_fx == null || spot.Cell < 0) return;

            var where = Where(spot.Cell);
            var tint = Petal(spot.Colour);

            // A box rather than a stretched node, so everything on it is placed against its
            // own centre and the mark can be scaled and thrown away as one object.
            _mark = UIKit.Box("Mark", _fx, Vector2.one * _size * 2.4f,
                              new Vector2(.5f, .5f), where);
            _mark.SetAsLastSibling();

            // One handle on the whole mark's opacity, so taking it away is one tween rather than
            // three that have to be kept in step.
            var veil = _mark.gameObject.AddComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;

            int token = ++_hintToken;

            var glow = UIKit.Img("Halo", _mark, Art.Glow(256, 1.9f), Pal.A(tint, 0f),
                                 Vector2.one * _size * 2.2f, new Vector2(.5f, .5f), Vector2.zero);
            glow.raycastTarget = false;

            var ring = UIKit.Img("Ring", _mark, Art.Ring(128, 7f), Pal.A(Pal.Cream, 0f),
                                 Vector2.one * _size * 1.5f, new Vector2(.5f, .5f), Vector2.zero);
            ring.raycastTarget = false;

            var chip = UIKit.Img("Becomes", _mark, BudFlower.Petals(spot.Colour), Pal.A(tint, 0f),
                                 Vector2.one * _size * .46f, new Vector2(.5f, .5f),
                                 new Vector2(0f, _cell * .62f));
            chip.raycastTarget = false;

            var crt = (RectTransform)chip.transform;
            var rrt = (RectTransform)ring.transform;

            // In from oversize, so the mark arrives rather than appearing.
            Tween.Run(BudTempo.HintArrive, Ease.OutBack, t =>
            {
                if (!ring || !glow || !chip) return;

                rrt.localScale = Vector3.one * Mathf.Lerp(2.1f, 1f, t);
                ring.color = Pal.A(Pal.Cream, .92f * t);
                glow.color = Pal.A(tint, .30f * t);
                chip.color = Pal.A(tint, t);
                crt.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, t);
            }, _mark).OnDone(() =>
            {
                if (!this || _mark == null) return;

                // And then it breathes, for as long as it is standing. It has to move, because
                // a still ring on a grove where nothing else is moving reads as part of the
                // board rather than as something the game is saying.
                if (ring) Tween.Breathe(rrt, .12f, BudTempo.HintPulse);
                if (chip) Tween.Bob(crt, _cell * .06f, BudTempo.HintPulse * 1.3f);
            });

            // It gives up on its own. See BudTempo.HintHold.
            Tween.After(BudTempo.HintHold, () =>
            {
                if (!this || _hintToken != token) return;
                HideMark();
                TellHint();
            }, this);
        }

        /// <summary>
        /// Every cell the marked tap would reach, lit in the order the chain would reach it.
        ///
        /// <para>
        /// Drawn on <c>_fx</c> rather than on each cell's own soil, which the hover ghost owns —
        /// two writers on one colour is the bug this file has already paid for twice, and these
        /// are transient objects that clean themselves up.
        /// </para>
        /// </summary>
        void Ripple(int cell)
        {
            if (_fx == null || Run == null) return;

            Run.Preview(cell, _peek);
            if (_peek.Count == 0) return;

            int waves = 1;
            for (int i = 0; i < _peek.Count; i++)
                if (_peek[i].Wave + 1 > waves) waves = _peek[i].Wave + 1;

            float step = BudTempo.HintRipple / waves;

            for (int i = 0; i < _peek.Count; i++)
            {
                var pulse = _peek[i];
                var tint = pulse.Kind == BudPulseKind.Freed ? Pal.Gold
                         : pulse.Kind == BudPulseKind.Crack ? Pal.Rope
                         : Petal(pulse.Colour);

                var tile = UIKit.Img("Peek", _fx, Art.Round(18), Pal.A(tint, 0f),
                                     Vector2.one * _size * .84f, new Vector2(.5f, .5f),
                                     Where(pulse.Cell));
                tile.raycastTarget = false;
                tile.transform.SetAsFirstSibling();

                var rt = (RectTransform)tile.transform;
                float peak = pulse.Kind == BudPulseKind.Burst ? .30f : .52f;

                Tween.Run(BudTempo.HintRipple * 1.6f, Ease.OutQuad, t =>
                {
                    if (!tile) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1.05f, Mathf.Min(1f, t * 4f));
                    float a = t < .22f ? t / .22f : 1f - (t - .22f) / .78f;
                    tile.color = Pal.A(tint, a * peak);
                }, tile).Delay(pulse.Wave * step)
                        .OnDone(() => { if (tile) Destroy(tile.gameObject); });
            }
        }

        /// <summary>Takes the mark away. Says nothing to anybody — <see cref="TellHint"/> does.</summary>
        void HideMark()
        {
            _hintAt = -1;
            _hintToken++;

            if (_mark == null) return;

            var mark = _mark;
            var veil = mark.GetComponent<CanvasGroup>();
            _mark = null;

            // Out and up rather than simply gone, so the mark leaves the way every other piece
            // of furniture in this game leaves.
            Tween.Run(.18f, Ease.InQuad, t =>
            {
                if (!mark) return;
                mark.localScale = Vector3.one * (1f + t * .3f);
                if (veil) veil.alpha = 1f - t;
            }, mark).OnDone(() => { if (mark) Destroy(mark.gameObject); });
        }

        /// <summary>
        /// Tells whoever spent the hint that it has now been taken. Exactly once, or not at all.
        /// </summary>
        void TellHint()
        {
            var done = _hintDone;
            _hintDone = null;
            done?.Invoke();
        }

        // ------------------------------------------------------------------ the ending
        void Settle()
        {
            if (_over || Run == null) return;

            var verdict = Run.Verdict;
            if (!verdict.IsOver) return;

            _over = true;
            Locked = true;
            HideGhost();
            HideMark();

            if (verdict.IsWon)
            {
                Finishing?.Invoke();
                StartCoroutine(Triumph());
                return;
            }

            Lost?.Invoke();
        }

        IEnumerator Triumph()
        {
            // **Not `win`.** The panel plays that a beat later, and the two were firing seven
            // tenths of a second apart — the same cue twice, which is the "celebrate once" house
            // rule broken by a file that could not see the other half of it. This is the grove's
            // own note and it climbs; the fanfare belongs to the panel.
            Audio.Sfx("chime", .70f, 1f);
            Audio.Sfx("star", .55f, 1.12f, .18f);

            // Three rings out of the middle, which is what makes the finish read as the *grove*
            // answering rather than as every tile being nudged at once.
            for (int r = 0; r < 3; r++)
            {
                float at = r * BudTempo.Sweep * .28f;
                var tint = r == 1 ? Pal.Cream : Pal.Gold;
                float reach = _size * (4f + r * 3.2f);

                Tween.After(at, () =>
                {
                    if (this) Shockwave(Vector2.zero, tint, reach, BudTempo.Sweep);
                }, this);
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                int x = i % _layout.Width, y = i / _layout.Width;
                float delay = BudTempo.EntranceDelay(x, y, _layout.Width, _layout.Height)
                            * (BudTempo.Hush / BudTempo.Entrance);

                Tween.Punch(_cells[i].Rt, .22f, .34f).Delay(delay);
            }

            // And the counter answers, because that is where everybody went.
            //
            // The finish used to be said with the freed critters standing on the board, one hop
            // after another — and there are none standing any more, deliberately: a critter left
            // in a square is a critter standing where the grove is about to drop a flower (see
            // `FlyToCount`). The readout they each flew to is the one thing on this screen that
            // holds all of them at once, so it is what the grove finishes on.
            if (_left) Tween.Punch(_left.transform, .34f, .46f);
            if (_plate) Tween.Punch(_plate, .06f, .5f);

            yield return new WaitForSecondsRealtime(BudTempo.Hush);
            if (!this) yield break;

            Solved?.Invoke();
        }

        // ------------------------------------------------------------------ one more go
        public void Grant(int taps)
        {
            if (Run == null || taps <= 0) return;

            Run.Grant(taps);

            _over = false;
            Locked = false;

            PaintBand();
            Changed?.Invoke();

            if (_count) Tween.Punch(_count.transform, .35f, .4f);

            Settle();
        }

        // ------------------------------------------------------------------ housekeeping
        bool _wasPlayable;

        void Update()
        {
            // The halos are a fact about whether the run is *running*, which is written every
            // frame by RunScreen and by nothing else — the same edge the hint key was painted on
            // the wrong side of. Watched rather than recomputed: PaintPops is a full preview per
            // flower, which is nothing once and a stall every frame.
            if (Playable != _wasPlayable)
            {
                _wasPlayable = Playable;
                PaintPops();
            }

            if (_hovered < 0) return;

            if (!Playable) { HideGhost(); return; }

            ShowGhost(_hovered);
        }
    }
}
