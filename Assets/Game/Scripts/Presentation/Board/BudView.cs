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
        RectTransform _host, _grid, _field, _fx, _tray, _plate;
        Text _count, _left, _chain;

        Cell[] _cells;

        readonly List<BudPulse> _pulses = new List<BudPulse>(64);
        readonly List<BudWash> _washes = new List<BudWash>(64);
        readonly List<BudPulse> _peek = new List<BudPulse>(64);
        readonly List<int> _beside = new List<int>(4);

        Image _handChip;
        Image[] _queue;

        float _cell, _size;
        Vector2 _origin;
        bool _busy, _over, _committed;
        int _hovered = -1, _ghostKey = int.MinValue;

        /// <summary>One cell: the ground, whatever is standing on it, and its halo.</summary>
        sealed class Cell
        {
            public RectTransform Rt;
            public Image Soil, Bud, Halo, Glow, Pod, Critter, Ring;
            public int Drawn = -1;
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

        void BuildGround()
        {
            float w = _layout.Width * _cell, h = _layout.Height * _cell;

            var plate = UIKit.Img("Plate", _grid, Art.Round(30), new Color(.04f, .07f, .05f, .74f),
                                  new Vector2(w + 26f, h + 26f), new Vector2(.5f, .5f),
                                  Vector2.zero);
            _plate = (RectTransform)plate.transform;

            UIKit.Img("Edge", _plate, Art.RoundOutline(30, 3f), new Color(.86f, 1f, .74f, .14f),
                      new Vector2(w + 26f, h + 26f), new Vector2(.5f, .5f), Vector2.zero);

            _field = UIKit.Node("Buds", _grid);
            UIKit.StretchTo(_field, 0f, 0f, 0f, 0f);

            _fx = UIKit.Node("Fx", _grid);
            UIKit.StretchTo(_fx, 0f, 0f, 0f, 0f);

            _cells = new Cell[_layout.Count];
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

            if (kind == BudGround.Flower)
            {
                cell.Glow = UIKit.Img("Glow", root, Art.Glow(128, 2.2f), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size * 1.5f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                cell.Bud = UIKit.Img("Flower", root, Bloom(Energy.None), Color.white,
                                     Vector2.one * _size * .78f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                // The heart of the flower, drawn in the same colour but brighter. It is what
                // makes a dark blend still read as a flower rather than as a hole.
                cell.Halo = UIKit.Img("Heart", root, Art.Disc(96), Color.white,
                                      Vector2.one * _size * .22f, new Vector2(.5f, .5f),
                                      Vector2.zero);
            }
            else if (kind == BudGround.Cocoon)
            {
                cell.Glow = UIKit.Img("Glow", root, Art.Glow(128, 2.2f), Pal.A(Pal.Rope, .18f),
                                      Vector2.one * _size * 1.5f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                cell.Pod = UIKit.Img("Cocoon", root, Art.Crystal(128),
                                     new Color(.84f, .78f, .60f, 1f),
                                     Vector2.one * _size * .94f, new Vector2(.5f, .5f),
                                     Vector2.zero);

                // The critter inside, drawn small and dim and asleep - and it is a *real*
                // critter, the same flipbook the glades and the roster use, so what comes out at
                // the end is somebody the player already knows.
                cell.Critter = UIKit.Img("Critter", root, null, Pal.A(Pal.Dormant, .95f),
                                         Vector2.one * _size * .46f, new Vector2(.5f, .5f),
                                         Vector2.zero);
                CritterArt(cell.Critter, index, false);

                cell.Ring = UIKit.Img("Cracks", root, Art.Ring(128, 6f), new Color(1, 1, 1, 0f),
                                      Vector2.one * _size * 1.06f, new Vector2(.5f, .5f),
                                      Vector2.zero);

                Tween.Breathe(cell.Critter.transform, .07f, 2.6f, index * .19f);
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
                                 Vector2.one * (BudBand.HandSize + 14f), new Vector2(.5f, .5f),
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
        }

        void PaintCell(int index, bool animate)
        {
            var cell = _cells[index];
            var board = Run.Board;

            if (cell.Bud != null)
            {
                bool there = board.IsFlower(index);
                int colour = there ? board.ValueAt(index) : Energy.None;

                if (cell.Drawn == colour && !animate) return;
                cell.Drawn = colour;

                if (!there)
                {
                    cell.Bud.color = new Color(1, 1, 1, 0f);
                    if (cell.Halo) cell.Halo.color = new Color(1, 1, 1, 0f);
                    if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);
                    Tween.KillAll(cell.Bud);
                    return;
                }

                var tint = Petal(colour);

                cell.Bud.sprite = Bloom(colour);
                cell.Bud.color = tint;
                if (cell.Halo) cell.Halo.color = Pal.Lift(tint, .55f);
                if (cell.Glow) cell.Glow.color = Pal.A(tint, colour == Energy.All ? .34f : .14f);

                // White is one channel from nothing left to add, so it is the flower a player
                // should be looking at. It is the only one that moves while nobody is tapping.
                Tween.KillAll(cell.Bud);
                if (colour == Energy.All)
                    Tween.Breathe(cell.Bud.transform, .08f, 1.6f, index * .13f);
                else
                    cell.Bud.transform.localScale = Vector3.one;

                return;
            }

            if (cell.Pod != null)
            {
                bool shut = board.IsCocoon(index);

                if (!shut)
                {
                    cell.Pod.color = new Color(1, 1, 1, 0f);
                    if (cell.Ring) cell.Ring.color = new Color(1, 1, 1, 0f);
                    if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);
                    return;
                }

                int cracks = board.ValueAt(index);
                cell.Pod.color = new Color(.84f, .78f, .60f, cracks > 1 ? 1f : .86f);
                if (cell.Ring) cell.Ring.color = Pal.A(Pal.Rope, cracks > 1 ? .78f : 0f);
            }
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

                cell.Soil.color = pulse.Freed ? Pal.A(Pal.Gold, .42f)
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

            var chain = Run.Tap(index, _pulses, _washes);

            if (!_committed)
            {
                _committed = true;
                Committed?.Invoke();
            }

            _busy = true;
            HideGhost();

            Struck(index);
            StartCoroutine(PlayChain(chain, ToPulses(_pulses), ToWashes(_washes)));
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
        void Struck(int index)
        {
            Audio.Sfx("enter", .52f, 1.06f);

            var cell = _cells[index];
            if (cell?.Rt == null) return;

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
        IEnumerator PlayChain(BudChainResult chain, BudPulse[] pulses, BudWash[] washes)
        {
            float beat = BudTempo.Wave(Mathf.Max(1, chain.Waves));
            int shown = 0;

            float charge = BudTempo.Charge(beat);
            float burn = BudTempo.Burn(beat);

            for (int wave = 0; wave < chain.Waves; wave++)
            {
                int inWave = 0;
                for (int i = 0; i < pulses.Length; i++)
                    if (pulses[i].Wave == wave && !pulses[i].Freed) inWave++;

                // ---------------------------------------------------------- the charge
                // **Every wave winds up before it goes off, and this is the beat the mode was
                // missing.** The bunch that matched spins in place, brightening, for a fraction
                // of a second — so there is a moment where the player can see *which flowers*
                // did it, before they stop existing. Without it a wave went straight from
                // "nothing" to "gone", which is why a perfectly good three-wave cascade read as
                // a flicker rather than as something they had caused.
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave || pulses[i].Freed) continue;
                    Wind(pulses[i].Cell, pulses[i].Colour, charge, i);
                }

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
                int nth = 0;
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave) continue;

                    if (pulses[i].Freed)
                    {
                        Free(pulses[i].Cell, burn);
                        continue;
                    }

                    Split(pulses[i].Cell, wave, pulses[i].Colour, beat,
                          BudTempo.StaggerAt(nth, inWave, burn));
                    nth++;
                }

                // Colour lands on the flowers around the bunch a beat after it goes off, held
                // back by the same ripple, so a flower never turns before the bunch that turned
                // it has burst.
                int sent = 0;
                for (int i = 0; i < washes.Length; i++)
                {
                    if (washes[i].Wave != wave) continue;
                    Land(washes[i], beat, BudTempo.StaggerAt(sent++, inWave, burn));
                }

                if (inWave > 0)
                {
                    shown = wave + 1;
                    if (BudChain.Counts(shown)) ShowChain(shown, chain.Waves);

                    float shake = BudTempo.Shake(shown);
                    if (shake > 0f && _grid) Tween.Shake(_grid, shake, burn * .9f);

                    // And the screen answers, harder every wave. Nought on a one-wave tap,
                    // deliberately: a flash on everything is a flash that says nothing.
                    float bloom = BudTempo.Bloom(shown);
                    if (bloom > 0f) Flow.Flash(Pal.A(new Color(1f, .96f, .84f), bloom),
                                               burn * .30f, burn * .70f);

                    if (_plate) Tween.Punch(_plate, .012f + shown * .004f, burn * .8f);
                }

                PaintBand();
                Changed?.Invoke();

                yield return new WaitForSecondsRealtime(burn);
                if (!this) yield break;
            }

            for (int i = 0; i < _cells.Length; i++) PaintCell(i, true);
            PaintBand();
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
        }

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
        void Wind(int index, int colour, float charge, int seed)
        {
            if (_cells == null || index < 0 || index >= _cells.Length) return;

            var cell = _cells[index];
            if (cell?.Rt == null) return;

            var rt = cell.Rt;
            var bud = cell.Bud;
            var tint = Petal(colour);
            float lean = (seed % 2 == 0) ? 1f : -1f;

            Tween.Run(charge, Ease.Linear, t =>
            {
                if (!rt) return;

                // t squared, so it starts almost still and is whipping round by the end.
                rt.localRotation = Quaternion.Euler(0, 0, lean * 420f * t * t);
                rt.localScale = Vector3.one * (1f + t * t * .34f);

                // Only two thirds of the way to white. The charge exists to show *which*
                // flowers matched, and a bunch that goes fully white has thrown that away in
                // the half-second it was meant to be saying it.
                if (bud) bud.color = Color.Lerp(tint, Color.white, t * t * .62f);
            }, rt, SpinChannel).OnAbandon(() =>
            {
                if (!rt) return;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
            });
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
        void Split(int index, int wave, int colour, float beat, float delay)
        {
            if (delay > 0f)
            {
                Tween.After(delay, () => { if (this) Split(index, wave, colour, beat, 0f); }, this);
                return;
            }

            var where = Where(index);
            var cell = _cells[index];
            var tint = Petal(colour);

            // The spin the charge started is over, and the cell it was turning goes back to
            // square before anything is thrown off it.
            Tween.KillChannel(cell.Rt, SpinChannel);
            if (cell.Rt) { cell.Rt.localRotation = Quaternion.identity; cell.Rt.localScale = Vector3.one; }

            ThrowFlower(cell, tint);
            Petals(where, tint, colour, BudTempo.Shrapnel(beat), index);

            float life = BudTempo.Shrapnel(beat);
            float hot = Mathf.Min(life * .28f, .18f);

            // The flash. White rather than tinted for the first instant, because a burst is
            // brighter than any colour on this board and reading it as light rather than as
            // paint is what makes it feel like something went off.
            var core = UIKit.Img("Flash", _fx, Art.Flash(256, 12), Color.white,
                                 Vector2.one * _size * 1.30f, new Vector2(.5f, .5f), where);
            var crt = (RectTransform)core.transform;
            float spin = 40f + (index % 7) * 9f;

            Tween.Run(hot, Ease.OutQuint, t =>
            {
                if (!core) return;
                crt.localScale = Vector3.one * Mathf.Lerp(.22f, 1.5f, t);
                crt.localRotation = Quaternion.Euler(0, 0, spin * t);
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
                                  Vector2.one * _size * 2.3f, new Vector2(.5f, .5f), where);
            flare.transform.SetAsFirstSibling();

            var frt = (RectTransform)flare.transform;
            Tween.Run(life * 1.05f, Ease.OutQuad, t =>
            {
                if (!flare) return;
                frt.localScale = Vector3.one * Mathf.Lerp(.35f, 1.55f, t);
                flare.color = Pal.A(tint, .78f * (1f - t) * (1f - t));
            }, flare).OnDone(() => { if (flare) Destroy(flare.gameObject); });

            // The rays, which are the loud half of the flash: a hard star that snaps out and is
            // gone inside a fifth of a second, so the burst has an edge rather than a glow.
            Rays(where, tint, hot * 2.2f, index);

            Shockwave(where, tint, _size * 3.1f, life * .85f);
            Burst.Sparks(_fx, where, tint, 13, 240f, 18f, life);
            Embers(where, tint, life, index);

            // Budburst's own slot, and it had to be: this is struck thirteen times in a wave
            // and pitched up through a chain, where `pop` is a wooden clunk eight other things
            // are tuned around. The wood breaking that used to be layered under it is gone with
            // the same argument the smoke went with — there is no timber in a flower.
            Audio.Sfx("burst", .40f, BudTempo.Pitch(wave + 1));
        }

        /// <summary>
        /// The flower coming apart into its own petals — the body of a burst, and the thing a
        /// plume of smoke was standing in for.
        ///
        /// <para>
        /// Six of them, thrown out on an even ring with a little scatter, spinning hard, pulled
        /// down as they go so they <em>fall</em> rather than drift. Drawn from <c>Art.Leaf</c>
        /// in the flower's own colour: generated, so there is no address to miss and nothing to
        /// draw as a white rectangle, and a leaf is the one shape in this game's kit that reads
        /// as a torn-off piece of a flower at forty pixels.
        /// </para>
        /// <para>
        /// Two of the six are drawn white and land last, which is what stops six identical
        /// shapes reading as a mechanism.
        /// </para>
        /// </summary>
        void Petals(Vector2 at, Color tint, int colour, float life, int index)
        {
            var sprite = Art.Leaf(96);
            if (sprite == null) return;

            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                bool pale = (index + i) % 3 == 0;

                float ang = (i / (float)count) * Mathf.PI * 2f + (index % 5) * .21f;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                float reach = _size * (.95f + (i % 3) * .26f);
                float drop = _size * (.75f + (i % 2) * .35f);
                float spin = (i % 2 == 0 ? 1f : -1f) * (300f + i * 70f);
                float size = _size * (pale ? .38f : .48f);
                var paint = pale ? Pal.Lift(tint, .75f) : tint;

                var petal = UIKit.Img("Petal", _fx, sprite, paint,
                                      new Vector2(size * .72f, size), new Vector2(.5f, .5f), at);
                var rt = (RectTransform)petal.transform;
                rt.localRotation = Quaternion.Euler(0, 0, ang * Mathf.Rad2Deg);

                float over = life * (pale ? 1.15f : 1f);
                Tween.Run(over, Ease.OutQuad, t =>
                {
                    if (!petal) return;

                    // Out fast and down under gravity, which is what makes six shapes read as
                    // debris rather than as a ring opening.
                    rt.anchoredPosition = at + dir * reach * t
                                        + new Vector2(0f, -drop * t * t);
                    rt.localRotation = Quaternion.Euler(0, 0, ang * Mathf.Rad2Deg + spin * t);
                    rt.localScale = Vector3.one * (1f - t * .35f);
                    petal.color = Pal.A(paint, 1f - t * t);
                }, petal).OnDone(() => { if (petal) Destroy(petal.gameObject); });
            }
        }

        /// <summary>
        /// The star a burst throws: hard rays, out and gone, over before the petals have landed.
        ///
        /// It is what gives the burst its *edge*. The bloom underneath says how big and the ring
        /// says how far; without something with a straight line in it the whole event is round
        /// and soft, which is the shape of a puff rather than of a bang.
        /// </summary>
        void Rays(Vector2 at, Color tint, float life, int index)
        {
            var sprite = Art.Rays(256, 16);
            if (sprite == null) return;

            var star = UIKit.Img("Rays", _fx, sprite, Pal.A(Color.white, .95f),
                                 Vector2.one * _size * 2.6f, new Vector2(.5f, .5f), at);
            var rt = (RectTransform)star.transform;
            rt.localRotation = Quaternion.Euler(0, 0, (index % 6) * 15f);

            Tween.Run(Mathf.Max(life, .12f), Ease.OutQuint, t =>
            {
                if (!star) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.25f, 1.35f, t);
                star.color = Pal.A(Color.Lerp(Color.white, tint, Mathf.Min(1f, t * 4f)),
                                   .95f * (1f - t) * (1f - t));
            }, star).OnDone(() => { if (star) Destroy(star.gameObject); });
        }

        /// <summary>
        /// The flower coming apart, which is the tenth of a second the burst used to skip.
        ///
        /// It is drawn on the flower's own <c>Image</c> rather than on a copy, so there is
        /// nothing to tidy up and nothing that can be left behind if the run ends mid-chain:
        /// <see cref="PaintCell"/> puts it back wherever the board says it should be.
        /// </summary>
        void ThrowFlower(Cell cell, Color tint)
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

            Tween.Run(.11f, Ease.OutQuad, t =>
            {
                if (!bud) return;
                brt.localScale = Vector3.one * (1f + t * .75f);
                bud.color = Pal.A(Color.Lerp(tint, Color.white, t), 1f - t);
            }, bud).OnDone(() =>
            {
                if (!bud) return;
                brt.localScale = Vector3.one;
                bud.color = new Color(1, 1, 1, 0f);
            });
        }

        /// <summary>
        /// The glints that lift out of a burst once the fire has gone.
        ///
        /// Small, few and slow — they are the only part of an explosion here that outlives its
        /// own beat, and they are what stops a cell that has just gone off from being a hole in
        /// the grove while the rest of the chain runs.
        /// </summary>
        void Embers(Vector2 where, Color tint, float life, int index)
        {
            var sprite = Art.Glint(96, 4);
            if (sprite == null) return;

            for (int i = 0; i < 3; i++)
            {
                float lean = ((index + i * 7) % 5 - 2) * .28f;
                float size = _size * (.30f + (i % 2) * .10f);
                float rise = _size * (.9f + i * .28f);
                float turn = 90f + i * 60f;

                var glint = UIKit.Img("Ember", _fx, sprite, Pal.A(Pal.Lift(tint, .55f), 0f),
                                      Vector2.one * size, new Vector2(.5f, .5f), where);
                var rt = (RectTransform)glint.transform;

                Tween.Run(life * 1.5f, Ease.OutQuad, t =>
                {
                    if (!glint) return;
                    rt.anchoredPosition = where + new Vector2(lean * _size * t, rise * t);
                    rt.localRotation = Quaternion.Euler(0, 0, turn * t);
                    rt.localScale = Vector3.one * Mathf.Lerp(.5f, 1.05f, Mathf.Min(1f, t * 3f));

                    // In fast, out slow, so it reads as a spark catching the light rather than
                    // as a shape that was always there and is now leaving.
                    float a = t < .18f ? t / .18f : 1f - (t - .18f) / .82f;
                    glint.color = Pal.A(Pal.Lift(tint, .55f), a * .9f);
                }, glint).Delay(i * life * .12f)
                         .OnDone(() => { if (glint) Destroy(glint.gameObject); });
            }
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
                Flare(Where(wash.Cell), tint, BudTempo.Linger(beat), wash.Cell);
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
        void Flare(Vector2 at, Color tint, float life, int index)
        {
            var sprite = Art.Flash(128, 10);
            if (sprite == null) return;

            var fork = UIKit.Img("Fork", _fx, sprite, Pal.A(Pal.Lift(tint, .45f), .95f),
                                 Vector2.one * _size * .86f, new Vector2(.5f, .5f), at);
            var rt = (RectTransform)fork.transform;
            rt.localRotation = Quaternion.Euler(0, 0, (index % 4) * 90f);

            float flip = (index % 2) == 0 ? 1f : -1f;
            rt.localScale = new Vector3(flip, 1f, 1f);

            Tween.Run(Mathf.Max(life * 1.4f, .10f), Ease.OutQuad, t =>
            {
                if (!fork) return;
                rt.localScale = new Vector3(flip * (1f + t * .35f), 1f + t * .35f, 1f);
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

            Tween.Run(Mathf.Max(over * .9f, .09f), Ease.OutQuad, t =>
            {
                if (!bud) return;
                bud.color = Color.Lerp(Pal.Lift(tint, .85f), real, t);
            }, bud).OnDone(() => { if (bud) bud.color = real; });

            Tween.Punch(cell.Rt, .34f, Mathf.Max(over, .12f));
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
        void Free(int index, float beat)
        {
            var where = Where(index);
            var cell = _cells[index];

            if (cell.Ring) cell.Ring.color = new Color(1, 1, 1, 0f);
            if (cell.Glow) cell.Glow.color = new Color(1, 1, 1, 0f);

            float life = Mathf.Max(beat * 2.4f, .55f);

            // The star behind everything, drawn first so the shell breaks in front of it.
            Ray(where, life);

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
            // clear of the shell, settles back onto its own cell a little larger than it was
            // shut in, and breathes there for the rest of the run.
            //
            // Two beats rather than one, because a single tween cannot do both halves: the
            // leap is fast and overshoots, the settle is slower and lands. One eased curve
            // across the whole thing reads as a float rather than as getting out.
            if (cell.Critter)
            {
                var critter = cell.Critter;
                var crt = (RectTransform)critter.transform;

                Tween.KillAll(critter);
                Tween.KillChannel(crt, "breathe");
                CritterArt(critter, index, awake: true);
                critter.transform.SetAsLastSibling();

                float leap = life * .52f;
                float top = _cell * .62f;

                Tween.Run(leap, Ease.OutQuad, t =>
                {
                    if (!critter) return;

                    // Up and out, with a wobble, growing past where it will end up.
                    float lift = Mathf.Sin(t * Mathf.PI * .5f);
                    crt.anchoredPosition = new Vector2(Mathf.Sin(t * 9f) * _cell * .09f, lift * top);
                    crt.localScale = Vector3.one * Mathf.Lerp(1f, FreedScale * 1.30f, lift);
                    crt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 7f) * 12f);
                    critter.color = Color.white;
                }, critter).OnDone(() =>
                {
                    if (!this || !critter) return;

                    var from = crt.anchoredPosition;
                    float scale = crt.localScale.x;

                    Tween.Run(life * .48f, Ease.OutBack, t =>
                    {
                        if (!critter) return;
                        crt.anchoredPosition = Vector2.Lerp(from, Vector2.zero, t);
                        crt.localScale = Vector3.one * Mathf.Lerp(scale, FreedScale, t);
                        crt.localRotation = Quaternion.Euler(0, 0, (1f - t) * Mathf.Sin(t * 7f) * 10f);
                    }, critter).OnDone(() =>
                    {
                        if (!this || !critter) return;

                        // Landed. Everything is put back exactly rather than left wherever the
                        // curve stopped, because what happens next borrows this scale.
                        crt.anchoredPosition = Vector2.zero;
                        crt.localScale = Vector3.one * FreedScale;
                        crt.localRotation = Quaternion.identity;

                        Tween.Breathe(crt, .055f, 2.9f, index * .21f);
                    });
                });
            }

            Shockwave(where, Pal.Gold, _size * 3.8f, life * .8f);
            Shockwave(where, Pal.Cream, _size * 2.6f, life * .55f);
            Burst.Sparks(_fx, where, Pal.Gold, 18, 250f, 24f, life * .9f);
            Embers(where, Pal.Gold, life, index);

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

        /// <summary>The star a freed critter comes out of, spun slowly so its rays sweep.</summary>
        void Ray(Vector2 at, float life)
        {
            var sprite = Art.Rays(256, 16);
            if (sprite == null) return;

            var ray = UIKit.Img("Ray", _fx, sprite, Pal.A(Pal.Cream, 0f),
                                Vector2.one * _size * 3.4f, new Vector2(.5f, .5f), at);
            ray.transform.SetAsFirstSibling();

            var rt = (RectTransform)ray.transform;
            Tween.Run(life, Ease.OutQuad, t =>
            {
                if (!ray) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.30f, 1.25f, t);
                rt.localRotation = Quaternion.Euler(0, 0, 26f * t);

                float a = t < .14f ? t / .14f : 1f - (t - .14f) / .86f;
                ray.color = Pal.A(Pal.Cream, a * .95f);
            }, ray).OnDone(() => { if (ray) Destroy(ray.gameObject); });
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
            word.fontSize = BudChain.WordPointsFor(waves);
            word.color = tint;

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

            // A ring thrown out from behind the word, so it arrives *out of* something.
            Shockwave(Vector2.zero, tint, _size * (4.5f + rung * 1.6f), .55f);

            // The slam. In from oversize and past its resting size, which is the one motion that
            // reads as impact rather than as an entrance.
            Tween.Run(.34f, Ease.OutQuint, t =>
            {
                if (!word) return;
                float k = Mathf.Lerp(1.85f, 1f, t) + Mathf.Sin(t * Mathf.PI) * .12f;
                rt.localScale = Vector3.one * k;
                rt.localRotation = Quaternion.Euler(0, 0, (1f - t) * (rung % 2 == 0 ? 7f : -7f));
                word.color = Color.Lerp(Color.white, tint, t);
            }, word).OnDone(() =>
            {
                if (!word) return;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                Tween.Punch(rt, .10f, .40f);
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

            // The top rung is the one a player tells somebody about, so it gets the thing
            // nothing else in this mode gets.
            if (rung >= 3) Burst.Confetti(_fx, 40);

            yield return new WaitForSecondsRealtime(BudTempo.Fanfare);
            if (!this) yield break;

            HideWord();
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

        // ------------------------------------------------------------------ the ending
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

            Lost?.Invoke();
        }

        IEnumerator Triumph()
        {
            Audio.Sfx("win", .9f);

            for (int i = 0; i < _cells.Length; i++)
            {
                int x = i % _layout.Width, y = i / _layout.Width;
                float delay = BudTempo.EntranceDelay(x, y, _layout.Width, _layout.Height)
                            * (BudTempo.Hush / BudTempo.Entrance);

                Tween.Punch(_cells[i].Rt, .22f, .34f).Delay(delay);
            }

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
        void Update()
        {
            if (_hovered < 0) return;

            if (!Playable) { HideGhost(); return; }

            ShowGhost(_hovered);
        }
    }
}
