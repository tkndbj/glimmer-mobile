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
    /// <b>Lightfall's board.</b> A well of motes to empty, a procession to empty it with, and a
    /// brim you must not reach.
    ///
    /// <para>
    /// <b>The decision is made with the eyes, before anything is committed.</b> A finger held
    /// over a column shows a ghost of where the mote will land and a ring that says which of
    /// three things this drop is: cream, it enriches; amber, it heightens and costs a row of
    /// headroom; red, it comes to rest on the brim and very probably ends the run. If it would
    /// light a mote all the way to white the ghost pulses. That preview is the whole reason this
    /// verb works where tapping a cell did not — and it stops at the <em>spark</em>. How far the
    /// chain runs is never previewed, because that is the thinking.
    /// </para>
    /// <para>
    /// <b>The board says what it knows.</b> A mote one channel from white wears a halo in the
    /// colour it is waiting for, so the well reads as a landscape of things that are nearly
    /// ready rather than as a grid of dots — and the brim band reddens as the stack climbs into
    /// it. Both are facts a careful player could work out by squinting; drawing them is what
    /// makes the mode legible in the second it takes to choose a column, which is all the time
    /// a thumb-driven game gets.
    /// </para>
    /// <para>
    /// <b>What is drawn is driven by the resolution's steps, never by re-reading the board.</b>
    /// <c>FallRun.Drop</c> settles the well completely and hands back the waves in order, so by
    /// the time anything is animated the model is already at the end. <see cref="_shown"/> is
    /// this view's own copy of what is on screen, walked forward one wave at a time, and
    /// <see cref="Sync"/> is what puts it back in step if anything ever interrupts. Reading the
    /// live board mid-cascade would draw the finished well behind a burst that has not happened.
    /// </para>
    /// </summary>
    public sealed class FallView : MonoBehaviour
    {
        /// <summary>Raised whenever anything the readouts count has moved.</summary>
        public Action Changed { get; set; }

        /// <summary>The well is empty. Raised once, after the last burst has played out.</summary>
        public Action Solved { get; set; }

        /// <summary>
        /// The run is over and lost. The screen reads <see cref="Run"/>'s verdict for which of
        /// the two ways it was.
        /// </summary>
        public Action Lost { get; set; }

        /// <summary>The first mote has landed, so the run is now owed for.</summary>
        public Action Committed { get; set; }

        /// <summary>
        /// The closing cascade has begun, so nothing else may end this run.
        ///
        /// <c>KeeperView.Finishing</c>'s rule, and it earns its place here for the same reason:
        /// the run is decided when the last mote bursts and the panel opens a beat later while
        /// the chain plays out, so everything that could still end a run has to stop at the
        /// first of those two moments rather than the second.
        /// </summary>
        public Action Finishing { get; set; }

        /// <summary>Input off. Set by every panel that goes over this board.</summary>
        public bool Locked { get; set; }

        /// <summary>
        /// The run has not been allowed to begin yet — the half of the answer no mode can see.
        ///
        /// <para>
        /// Written only by <c>FallScreen</c>, from <c>RunScreen.Tick</c>, and it is a second
        /// latch rather than more uses of <see cref="Locked"/> on purpose. <see cref="Locked"/>
        /// has several writers — every panel that goes over this board — and a board held for
        /// two reasons has to be able to release them independently, or the one that writes
        /// <c>false</c> last cancels the other. That is the exact bug <c>RunHold</c> exists
        /// because of, one screen over: an intro animation unlatched a board a first-timer's
        /// tip was holding.
        /// </para>
        /// </summary>
        public bool Held { get; set; } = true;

        /// <summary>The run being played. Null until <see cref="Begin"/>.</summary>
        public FallRun Run { get; private set; }

        /// <summary>
        /// The mode's own half of "is this board taking input": it exists, nothing is over it,
        /// no cascade is playing and the run has not ended.
        /// </summary>
        public bool TakingInput => Run != null && !Locked && !_busy && !_over;

        /// <summary>Whether a mote may actually be dropped right now. Both halves.</summary>
        public bool Playable => TakingInput && !Held;

        // ------------------------------------------------------------------ the furniture
        FallLayout _layout;
        RectTransform _host, _grid, _well, _fx, _tray;
        Image _plate, _brimBand, _brimLine, _ghost, _ghostRing;
        Image[] _queue;
        Text _supply;
        Text _count;
        Btn[] _columns;
        RectTransform[] _strips;
        Image[] _stripGlow;

        MoteView[] _at;                 // the widget drawing each cell, or null
        int[] _shown;                   // what this view believes each cell holds
        readonly Stack<MoteView> _spare = new Stack<MoteView>();

        float _cell, _size, _trayHeight;
        Vector2 _origin;
        bool _busy, _over, _committed;
        int _hovered = -1, _ghostKey = int.MinValue;

        /// <summary>
        /// One cell's widget: the body, the sheen over it, the halo round it, and — when the cell
        /// is glass rather than light — the four-point glint inside it.
        ///
        /// <para>
        /// One pooled widget for both rather than two pools, because a lens is a mote in every
        /// way that matters to this file: it stands in a cell, it falls when the well collapses,
        /// it is drawn, and it is given back when it goes. What differs is the sprite, the tint
        /// and one extra child, and <see cref="Paint"/> switches between them — so gravity, the
        /// pool and the collapse have no idea the distinction exists.
        /// </para>
        /// </summary>
        sealed class MoteView
        {
            public Image Body, Sheen, Halo, Facet;

            /// <summary>
            /// The three channel pips a lens wears, or null on a widget that has never drawn
            /// glass. Built on first use rather than with the widget, because most cells in most
            /// wells are motes and three objects each would be three hundred nobody looks at.
            /// </summary>
            public Image[] Pips;

            public RectTransform Rt;
        }

        // ------------------------------------------------------------------ building
        public void Begin(RectTransform host, FallLayout layout, int budget)
        {
            _host = host;
            _layout = layout;

            StopAllCoroutines();
            Tween.KillAll(this);

            Run = new FallRun(layout, budget);

            // Held until the screen says otherwise, which is the safe direction: a frame of a
            // run the player has not been shown is a frame they did not get.
            Held = true;

            // And handed back, which is the other half and the one that was missing. Every way
            // a run ends latches this board - Settle latches it, and the screen's Concede and
            // Lose each latch it again before their panel goes up - so a rebuild that left the
            // flag alone produced a fresh well behind a latch belonging to a run that no longer
            // existed. Reported from play: run out of motes, decline the offer, press TRY AGAIN,
            // and every tap is ignored for the rest of the screen's life.
            //
            // It belongs here rather than in the caller because there are three callers and only
            // two of them happened to unlatch: RunScreen.RestartLevel runs `Rewind(); Resume();`
            // and the Resume was doing it, while RetryAfterDefeat is a mode's own override with
            // no such pairing. A rule that holds only when the caller remembers is one the
            // fourth caller breaks. See FallViewTests.
            Locked = false;

            _busy = false;
            _over = false;
            _committed = false;
            _hovered = -1;
            _count = null;

            // The same fault as the latch above, one size smaller: the ghost only redraws when
            // what it would say has changed, so a key left over from the previous board can make
            // the first hover of a fresh one draw nothing at all.
            _ghostKey = int.MinValue;

            _spare.Clear();

            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                Destroy(old);
            }

            var rect = host.rect;

            // Measured rather than assumed: a fixed cell size is a well that overflows on
            // somebody's phone, and this mode has both a tray and a brim band to find room for.
            _trayHeight = 196f;
            float usableH = rect.height - _trayHeight;
            _cell = Mathf.Min(rect.width / layout.Width, usableH / layout.Height);
            _size = _cell * .86f;

            _grid = UIKit.Node("Well", host);
            UIKit.StretchTo(_grid, 0f, _trayHeight, 0f, 0f);

            _origin = new Vector2(-(layout.Width - 1) * _cell * .5f,
                                  (layout.Height - 1) * _cell * .5f);

            BuildWell();
            BuildColumns();
            BuildTray(host);

            _at = new MoteView[layout.Count];
            _shown = new int[layout.Count];

            Sync();
            Enter();
            PaintTray();
        }

        void BuildWell()
        {
            float w = _layout.Width * _cell, h = _layout.Height * _cell;

            _plate = UIKit.Img("Plate", _grid, Art.Round(28), new Color(.035f, .055f, .105f, .70f),
                               new Vector2(w + 22f, h + 22f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Edge", _plate.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .10f),
                      new Vector2(w + 22f, h + 22f), new Vector2(.5f, .5f), Vector2.zero);

            // Everything that is drawn goes above the plate and below the effects.
            _well = UIKit.Node("Motes", _grid);
            UIKit.StretchTo(_well, 0f, 0f, 0f, 0f);

            // ---- the brim. A band across the row a mote may not come to rest in, and a hard
            // line under it. It is drawn rather than explained, which is the whole reason this
            // fail state needed no lesson of its own: the rule is "do not let it reach the
            // line", and the line is on the board.
            float brimY = _origin.y - FallLayout.Brim * _cell;

            _brimBand = UIKit.Img("BrimBand", _grid, Art.Pixel, Pal.A(Pal.Ember, .08f),
                                  new Vector2(w, _cell), new Vector2(.5f, .5f),
                                  new Vector2(0f, brimY));
            _brimBand.raycastTarget = false;

            _brimLine = UIKit.Img("BrimLine", _grid, Art.Pixel, Pal.A(Pal.Ember, .45f),
                                  new Vector2(w, 4f), new Vector2(.5f, .5f),
                                  new Vector2(0f, brimY - _cell * .5f));
            _brimLine.raycastTarget = false;

            _fx = UIKit.Node("Fx", _grid);
            UIKit.StretchTo(_fx, 0f, 0f, 0f, 0f);

            _ghost = UIKit.Img("Ghost", _fx, Art.Disc(96), new Color(1, 1, 1, 0f),
                               Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);
            _ghost.raycastTarget = false;

            _ghostRing = UIKit.Img("GhostRing", _fx, Art.Ring(96, 7f), new Color(1, 1, 1, 0f),
                                   Vector2.one * _size * 1.24f, new Vector2(.5f, .5f), Vector2.zero);
            _ghostRing.raycastTarget = false;
        }

        /// <summary>
        /// One tall button per column rather than a button per cell. A column is the unit of
        /// decision, so it should be the unit of touch — asking a thumb to hit one cell of a
        /// ten-row well is asking it to be a mouse.
        /// </summary>
        void BuildColumns()
        {
            _columns = new Btn[_layout.Width];
            _strips = new RectTransform[_layout.Width];
            _stripGlow = new Image[_layout.Width];

            for (int x = 0; x < _layout.Width; x++)
            {
                int column = x;
                var strip = UIKit.Box("Col" + x, _grid, new Vector2(_cell, _layout.Height * _cell),
                                      new Vector2(.5f, .5f),
                                      new Vector2(_origin.x + x * _cell, 0f));
                _strips[x] = strip;

                // Under the motes, so a column lighting up reads as the well itself waking.
                _stripGlow[x] = UIKit.Img("Glow", strip, Art.FadeUp(64), new Color(1, 1, 1, 0f),
                                          new Vector2(_cell, _layout.Height * _cell),
                                          new Vector2(.5f, .5f), Vector2.zero);
                _stripGlow[x].raycastTarget = false;
                strip.SetSiblingIndex(1);

                var hit = strip.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                hit.raycastTarget = true;

                var btn = strip.gameObject.AddComponent<Btn>();
                btn.PressScale = 1f;
                btn.Setup(() => Drop(column), silent: true);
                _columns[x] = btn;

                var hover = strip.gameObject.AddComponent<Hover>();
                hover.Enter = () => ShowGhost(column);
                hover.Exit = HideGhost;
            }
        }

        void BuildTray(RectTransform host)
        {
            _tray = UIKit.Box("Tray", host, new Vector2(0f, _trayHeight), new Vector2(.5f, 0f),
                              new Vector2(0f, _trayHeight * .5f));
            _tray.anchorMin = new Vector2(0f, 0f);
            _tray.anchorMax = new Vector2(1f, 0f);
            _tray.sizeDelta = new Vector2(0f, _trayHeight);

            var plate = UIKit.Img("Plate", _tray, Art.Round(28), new Color(.045f, .065f, .125f, .78f),
                                  new Vector2(560f, 132f), new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f), new Color(1, 1, 1, .12f),
                      new Vector2(560f, 132f), new Vector2(.5f, .5f), Vector2.zero);

            _queue = new Image[Lookahead];
            for (int i = 0; i < _queue.Length; i++)
            {
                bool next = i == 0;
                float size = next ? 84f : 46f;
                float x = next ? -196f : -78f + (i - 1) * 70f;

                var seat = UIKit.Img("Seat" + i, plate.transform, Art.Ring(96, 5f),
                                     new Color(1, 1, 1, next ? .24f : .10f),
                                     Vector2.one * (size + 16f), new Vector2(.5f, .5f),
                                     new Vector2(x, 0f));
                seat.raycastTarget = false;

                _queue[i] = UIKit.Img("Mote" + i, seat.transform, Art.Disc(96), Color.white,
                                      Vector2.one * size, new Vector2(.5f, .5f), Vector2.zero);
                _queue[i].raycastTarget = false;

                if (next) Tween.Breathe(_queue[i].transform, .05f, 1.9f);
            }

            // How many are left, which is the fail line said in one number. It sits with the
            // procession rather than only in the header because this is what the player is
            // looking at while they choose.
            _supply = UIKit.Titled("Supply", plate.transform, "0", 52, Pal.Cream,
                                   TextAnchor.MiddleCenter, new Vector2(190f, 74f),
                                   new Vector2(.5f, .5f), new Vector2(150f, 10f), 4f, 3f);
            UIKit.Shrinkable(_supply, 24);

            UIKit.Titled("SupplyCap", plate.transform, Loc.Get("mode.fall.supply"), 20,
                         new Color(.92f, .96f, 1f, .55f), TextAnchor.MiddleCenter,
                         new Vector2(220f, 26f), new Vector2(.5f, .5f), new Vector2(150f, -38f),
                         3f, 0f);
        }

        /// <summary>How much of the procession the tray shows. Three is enough to plan, few enough to hold.</summary>
        public const int Lookahead = 3;

        // ------------------------------------------------------------------ positions
        Vector2 Where(int index)
        {
            int x = index % _layout.Width, y = index / _layout.Width;
            return _origin + new Vector2(x * _cell, -y * _cell);
        }

        // ------------------------------------------------------------------ the pool
        MoteView Take()
        {
            if (_spare.Count > 0)
            {
                var reused = _spare.Pop();
                reused.Rt.gameObject.SetActive(true);
                reused.Rt.localScale = Vector3.one;
                reused.Rt.localRotation = Quaternion.identity;
                return reused;
            }

            var body = UIKit.Img("Mote", _well, Art.Disc(96), Color.white,
                                 Vector2.one * _size, new Vector2(.5f, .5f), Vector2.zero);
            body.raycastTarget = false;

            var halo = UIKit.Img("Halo", body.transform, Art.Ring(128, 6f), new Color(1, 1, 1, 0f),
                                 Vector2.one * _size * 1.34f, new Vector2(.5f, .5f), Vector2.zero);
            halo.raycastTarget = false;
            halo.transform.SetAsFirstSibling();

            var sheen = UIKit.Img("Sheen", body.transform, Art.Glow(128, 2.4f),
                                  new Color(1, 1, 1, .18f), Vector2.one * _size * 1.5f,
                                  new Vector2(.5f, .5f), Vector2.zero);
            sheen.raycastTarget = false;
            sheen.transform.SetAsFirstSibling();

            // Off until a lens needs it. Built here rather than on demand so a well that turns
            // out to hold glass does not allocate in the middle of the cascade it holds it for.
            var facet = UIKit.Img("Facet", body.transform, Art.Glint(96, 4), new Color(1, 1, 1, 0f),
                                  Vector2.one * _size * 1.02f, new Vector2(.5f, .5f), Vector2.zero);
            facet.raycastTarget = false;
            facet.gameObject.SetActive(false);

            return new MoteView { Body = body, Halo = halo, Sheen = sheen, Facet = facet,
                                  Rt = (RectTransform)body.transform };
        }

        /// <summary>
        /// Hands a widget back to the pool.
        ///
        /// Hidden before it is released rather than destroyed, which is <c>GridView</c>'s
        /// bargain and matters more here: a well is up to a hundred and twelve cells and a
        /// cascade retires a dozen of them at a time, so destroying and remaking would churn
        /// objects in the middle of the one animation this mode exists for.
        /// </summary>
        void Give(MoteView mote)
        {
            if (mote == null) return;

            Tween.KillAll(mote.Body);

            // **And everything the transform owns, which is where the gravity bug lived.**
            // `Slide` puts the collapse on `mote.Rt` (channel "move"), because that is what it
            // moves; every other gesture here is owned by `mote.Body`. So a widget recycled while
            // its slide was still running went into the pool with a live tween writing its
            // position, came back out as the next falling drop or as a cell `Sync` had just
            // placed, and was dragged to wherever the *old* cell had been - a mote that visibly
            // refused to fall, or fell to the wrong square, on a board the model had settled
            // perfectly. It is easy to hit: a slide is dealt a stagger by column, so it finishes
            // up to a third of a beat after the wave that threw it, and the next wave is already
            // bursting by then.
            Tween.KillAll(mote.Rt);

            // Put back as a mote, whatever it was drawing. The falling widget in PlayDrop takes
            // one straight out of the pool and sets its colour without going through Paint, so a
            // recycled lens that kept its rim would fall as a hollow ring of the wrong colour.
            if (mote.Facet)
            {
                Tween.KillAll(mote.Facet);
                mote.Facet.gameObject.SetActive(false);
                mote.Facet.transform.localRotation = Quaternion.identity;
                mote.Facet.color = new Color(1, 1, 1, 0f);
            }

            if (mote.Pips != null)
                for (int i = 0; i < mote.Pips.Length; i++)
                    if (mote.Pips[i]) mote.Pips[i].color = new Color(1, 1, 1, 0f);

            mote.Rt.gameObject.SetActive(false);
            mote.Rt.localScale = Vector3.one;
            mote.Rt.localRotation = Quaternion.identity;
            mote.Body.sprite = Art.Disc(96);
            mote.Body.color = Color.white;
            mote.Sheen.color = new Color(1, 1, 1, .18f);
            _spare.Push(mote);
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

            for (int i = 0; i < _shown.Length; i++)
            {
                int colour = board.At(i);
                _shown[i] = colour;

                if (colour == Energy.None)
                {
                    if (_at[i] != null) { Give(_at[i]); _at[i] = null; }
                    continue;
                }

                if (_at[i] == null) _at[i] = Take();

                _at[i].Rt.anchoredPosition = Where(i);
                Paint(_at[i], colour, Run.Next, i);
            }

            PaintBrim();
        }

        /// <summary>
        /// One mote's colour, and whether the mote at the front of the tray would finish it.
        ///
        /// <para>
        /// <b>The halo answers "what could this drop start", not "what is nearly ready".</b>
        /// Ringing every mote a single channel short was the first version and it is the wrong
        /// question: on a well of blends that is most of the board, so twenty rings in three
        /// colours arrive at once and the player has to do the colour arithmetic anyway to work
        /// out which of them the mote in their hand is any use to. Asked against the procession
        /// instead, the board lights up as <em>this</em> drop's opportunities and goes dark
        /// again as the queue moves on — so the tray and the well are one thing to look at
        /// rather than two, and what is drawn is exactly the question being decided.
        /// </para>
        /// <para>
        /// It stops at the spark, which is the same line the ghost draws. A halo says a burst
        /// could start here; how far the chain then runs is the thinking, and is never shown.
        /// </para>
        /// </summary>
        void Paint(MoteView mote, int cell, int next, int index)
        {
            if (FallCell.IsLens(cell)) { PaintGlass(mote, cell, index); return; }

            if (mote.Facet && mote.Facet.gameObject.activeSelf)
            {
                Tween.KillAll(mote.Facet);
                mote.Facet.gameObject.SetActive(false);
            }

            if (mote.Pips != null)
                for (int i = 0; i < mote.Pips.Length; i++)
                    if (mote.Pips[i]) mote.Pips[i].color = new Color(1, 1, 1, 0f);

            Tween.KillChannel(mote.Rt, "tremble");

            mote.Body.sprite = Art.Disc(96);
            mote.Body.color = Pal.EnergyColour(cell);
            mote.Sheen.color = new Color(1, 1, 1, .18f);

            // A mote the next drop would finish, and only that. Buried ones are ringed too:
            // no drop can land on them, but a chain can reach them, and that is worth seeing.
            bool ripe = next != Energy.None && cell != Energy.All &&
                        (cell | next) == Energy.All;

            mote.Halo.color = ripe ? Pal.A(Pal.EnergyColour(next), .60f) : new Color(1, 1, 1, 0f);
            mote.Halo.gameObject.SetActive(ripe);
        }

        /// <summary>
        /// Re-asks every mote whether the next drop would finish it.
        ///
        /// Raised when the procession moves rather than only when the board does, because the
        /// board can be perfectly still and the answer still change — which is the whole reason
        /// the halo is drawn against the tray.
        /// </summary>
        void PaintHalos()
        {
            if (_at == null || Run == null) return;

            int next = Run.Next;
            for (int i = 0; i < _at.Length; i++)
                if (_at[i] != null) Paint(_at[i], _shown[i], next, i);
        }

        /// <summary>
        /// The brim, coloured by how close the well is to it.
        ///
        /// One row of clearance is the last warning there is, so that is where it goes loud.
        /// </summary>
        void PaintBrim()
        {
            if (!_brimBand || !_brimLine) return;

            int room = Run.Board.Headroom;

            float heat = room <= 0 ? 1f : room == 1 ? .72f : room == 2 ? .38f : 0f;

            _brimBand.color = Pal.A(Pal.Ember, .06f + heat * .20f);
            _brimLine.color = Pal.A(heat > .6f ? Pal.Rose : Pal.Ember, .35f + heat * .55f);

            Tween.KillChannel(_brimLine, "brim");
            if (heat < .6f) return;

            // Only once it is genuinely urgent. A line that always pulses is a line nobody reads.
            var line = _brimLine;
            Tween.Run(.9f, Ease.InOutSine, t =>
            {
                if (!line) return;
                line.color = Pal.A(Pal.Rose, Mathf.Lerp(.45f, 1f, t));
            }, _brimLine, "brim").Loop(-1);
        }

        void PaintTray()
        {
            for (int i = 0; i < _queue.Length; i++)
            {
                if (!_queue[i]) continue;

                int colour = Run.Ahead(i);
                bool has = colour != Energy.None;

                _queue[i].gameObject.SetActive(has);
                if (has) _queue[i].color = Pal.EnergyColour(colour);
            }

            if (!_supply) return;

            int left = Run.Supply.Bounded ? Run.Supply.Left : 0;
            _supply.text = Run.Supply.Bounded ? left.ToString() : Loc.Get("mode.fall.supply_free");

            _supply.color = Run.Supply.Pressure == FallPressure.Critical ? Pal.Rose
                          : Run.Supply.Pressure == FallPressure.Low ? Pal.Gold
                          : Pal.Cream;
        }

        /// <summary>
        /// The well arriving. Bottom rows first, so it reads as a well filling rather than as a
        /// grid switching on.
        /// </summary>
        void Enter()
        {
            for (int i = 0; i < _at.Length; i++)
            {
                if (_at[i] == null) continue;

                int row = i / _layout.Width;
                Tween.Pop(_at[i].Rt, 0f, .40f, FallTempo.EntranceDelay(row, _layout.Height));
            }
        }

        // ------------------------------------------------------------------ the ghost
        void ShowGhost(int column)
        {
            _hovered = column;

            if (!Playable || !Run.CanDrop(column)) { HideGhost(); return; }

            int colour = Run.Next;
            int row = Run.Board.Landing(colour, column);
            if (row < 0) { HideGhost(); return; }

            // Everything the ghost says, in one integer. Update asks every frame — the board
            // moves under a held finger — and rebuilding it unconditionally would restart the
            // ring's pulse on every one of them, which draws as a ring that never moves.
            int key = ((column * 16 + row) * 8 + colour) * 2 + (Run.Supply.Spent & 1);
            if (key == _ghostKey) return;
            _ghostKey = key;

            var at = Where(Run.Board.Index(column, row));
            var tint = Pal.EnergyColour(colour);

            ((RectTransform)_ghost.transform).anchoredPosition = at;
            ((RectTransform)_ghostRing.transform).anchoredPosition = at;

            bool enriches = Run.Board.Enriches(colour, column);
            bool charges = Run.Board.Charges(colour, column);
            bool bursts = Run.Board.Bursts(colour, column);
            bool brim = Run.Board.AtBrim(colour, column);

            // Four readings, one glance. Red is the only one that is a warning rather than a
            // description, so it wins over the rest: a drop that comes to rest on the brim is
            // very probably the end of the run, and it is the one thing the player must not do
            // by accident. Glass is next, because "this fills the lens" is the one outcome a
            // player has no other way to predict — it is why they can never be stranded, and a
            // valve nobody can see is a valve nobody uses.
            var ring = brim ? Pal.Rose : charges ? Pal.Glass : enriches ? Pal.Cream : Pal.Amber;

            _ghost.color = Pal.A(bursts ? Pal.Radiance : charges ? Pal.Glass : tint,
                                 bursts ? .55f : charges ? .5f : enriches ? .34f : .44f);
            _ghostRing.color = Pal.A(ring, .9f);

            for (int x = 0; x < _stripGlow.Length; x++)
                if (_stripGlow[x]) _stripGlow[x].color = x == column ? Pal.A(tint, .10f)
                                                                     : new Color(1, 1, 1, 0f);

            Tween.KillChannel(_ghostRing, "ghost");
            if (!bursts) return;

            // A drop that lights a mote all the way to white is worth saying out loud. How far
            // the chain then runs is not shown, and must not be — that is the thinking.
            var pulse = _ghostRing;
            Tween.Run(.42f, Ease.InOutSine, t =>
            {
                if (!pulse) return;
                pulse.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.16f, t);
            }, _ghostRing, "ghost").Loop(-1);
        }

        void HideGhost()
        {
            _hovered = -1;
            _ghostKey = int.MinValue;

            if (_ghost) _ghost.color = new Color(1, 1, 1, 0f);
            if (_ghostRing)
            {
                Tween.KillChannel(_ghostRing, "ghost");
                _ghostRing.color = new Color(1, 1, 1, 0f);
                _ghostRing.transform.localScale = Vector3.one;
            }

            for (int x = 0; x < _stripGlow.Length; x++)
                if (_stripGlow[x]) _stripGlow[x].color = new Color(1, 1, 1, 0f);
        }

        // ------------------------------------------------------------------ dropping
        void Drop(int column)
        {
            if (!Playable || !Run.CanDrop(column)) return;

            int colour = Run.Next;
            int row = Run.Board.Landing(colour, column);

            // Whatever is on top takes the drop when it lacks the colour - a mote is enriched
            // and a lens is charged - and either way the stack does not grow, so the falling
            // widget is handed back and the thing already standing there changes. One question
            // with one answer: `FallBoard.Takes` says why that is not `Enriches` and what asking
            // `Enriches` instead cost.
            bool taken = Run.Board.Takes(colour, column);

            var result = Run.Drop(column);
            if (result == null) return;

            _busy = true;
            HideGhost();

            if (!_committed)
            {
                _committed = true;
                Committed?.Invoke();
            }

            // The tray moves the instant the mote leaves it, so the procession is honest about
            // what is coming while the drop is still in the air — and the board re-reads itself
            // against the new one, because what a halo means has just changed.
            PaintTray();
            PaintHalos();
            Changed?.Invoke();

            StartCoroutine(PlayDrop(column, row, colour, taken, result));
        }

        IEnumerator PlayDrop(int column, int row, int colour, bool taken, FallResolution result)
        {
            int index = Run.Board.Index(column, row);

            // ---- the fall. Short, because it sits between every decision and its consequence.
            var falling = Take();
            falling.Rt.SetAsLastSibling();
            falling.Body.color = Pal.EnergyColour(colour);
            falling.Halo.color = new Color(1, 1, 1, 0f);
            falling.Halo.gameObject.SetActive(false);

            var from = new Vector2(_origin.x + column * _cell, _origin.y + _cell * 1.5f);
            var to = Where(index);
            falling.Rt.anchoredPosition = from;

            float fall = FallTempo.Fall(row + 2);

            var rt = falling.Rt;
            // No sound on the way down. It landing is the event, and it has one - a drop
            // that spoke twice was a sweep pitched up by a third over a wooden knock, on
            // the action this mode repeats more than any other.
            Tween.Run(fall, Ease.InQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = Vector2.Lerp(from, to, t);

                // Stretched along the way and squashed on arrival. It is a few pixels and it is
                // most of what makes a falling object read as having weight.
                float stretch = Mathf.Sin(t * Mathf.PI) * .16f;
                rt.localScale = new Vector3(1f - stretch, 1f + stretch, 1f);
            }, falling.Body);

            yield return new WaitForSecondsRealtime(fall);
            if (!this) yield break;

            // ---- the landing
            if (taken)
            {
                // Whatever it landed on takes the light: the falling widget is handed back and
                // the thing already standing there changes, which is what actually happened. The
                // hand-back is the half that matters — a widget nothing hands back is a widget
                // nothing owns, and `_at` is the only thing that can ever move it again.
                Give(falling);

                var host = _at[index];
                if (host != null)
                {
                    _shown[index] |= colour;

                    // Glass is drawn by the one drawing of "a lens took a channel" rather than a
                    // second copy of it here (invariant 9a, at the smallest scale it appears at).
                    // It is also the only correct one: a lens two channels short *trembles*, on a
                    // looping tween that writes `localScale`, so a punch beside it would be two
                    // tweens on one value — `ChargeGlass` kills the tremble before it punches,
                    // pops every pip the arrival lit, and climbs the note one-of-three,
                    // two-of-three, which is what the player is actually being told.
                    if (FallCell.IsLens(_shown[index]))
                    {
                        ChargeGlass(host, to, _shown[index], colour, index, FallTempo.Enrich);
                    }
                    else
                    {
                        Paint(host, _shown[index], Run.Next, index);
                        Tween.Punch(host.Rt, .38f, FallTempo.Enrich);
                        Ripple(to, Pal.EnergyColour(_shown[index]), _size * 2.1f, .42f);

                        // A bloop, never a bell. This is the commonest good thing that happens in
                        // the mode — every other drop enriches — and `chime` put a metal dong
                        // under it, which is the one material a well of light is not made of.
                        // `free` is `menu`'s block of wood struck a fifth up, and it is the
                        // *upper* note of a pair: the mote that only stacked (below) plays the
                        // same block at the bottom of that fifth, so the two outcomes are two
                        // notes of one instrument rather than two instruments. Glass has its own
                        // note and `ChargeGlass` plays it, so it is not doubled here.
                        Audio.Sfx("free", .46f);
                    }
                }

            }
            else
            {
                // It came to rest above the stack, so this cell was bare a moment ago and the
                // falling widget is what stands in it now. Anything already here would be a
                // widget the view had lost track of - it cannot happen, because `Takes` is the
                // exact complement of this branch and `Sync` re-reads the board after every drop
                // - and handing it back costs nothing where leaking it is invisible for the rest
                // of the run.
                if (_at[index] != null) Give(_at[index]);

                _at[index] = falling;
                _shown[index] = colour;
                falling.Rt.anchoredPosition = to;
                Paint(falling, colour, Run.Next, index);

                // A squash, not a punch: this one landed on something rather than lighting it.
                var landed = falling.Rt;
                Tween.Run(FallTempo.Land, Ease.OutQuad, t =>
                {
                    if (!landed) return;
                    float squash = (1f - t) * .22f;
                    landed.localScale = new Vector3(1f + squash, 1f - squash, 1f);
                }, falling.Body).OnAbandon(() => { if (landed) landed.localScale = Vector3.one; });

                // The lower note of that pair, and quieter: a mote that only stacked has done
                // nothing worth announcing, and it is what the well is full of. `pop` at .82 is
                // a wooden clunk, which reads as something heavy hitting a floor rather than as
                // light coming to rest.
                Audio.Sfx("rotate_a", .34f, .94f);
            }

            PaintBrim();

            // ---- the chain
            if (result.Waves > 0)
            {
                // Read before the first wave plays, so nothing can end the run underneath a
                // cascade that is about to win it.
                if (Run.Verdict.IsWon) Finishing?.Invoke();

                yield return Cascade(result);
                if (!this) yield break;
            }

            // Everything the model settled must now be on screen. Cheap insurance rather than
            // ceremony: the wave walk above is the one place this view can drift from the board,
            // and a drifted well is one a player cannot reason about at all.
            Sync();

            _busy = false;
            Changed?.Invoke();

            Settle();
        }

        /// <summary>
        /// Plays the waves a beat apart.
        ///
        /// <para>
        /// <b>Bounded, and the rate gives way.</b> <c>FallTempo.Cascade</c> caps the whole chain,
        /// so a nine-wave run plays faster rather than longer — the reward for a big chain has to
        /// be the chain rather than the waiting, and the board is latched for exactly as long as
        /// this takes.
        /// </para>
        /// </summary>
        IEnumerator Cascade(FallResolution result)
        {
            int waves = result.Waves;
            float flash = FallTempo.Flash(waves);
            float burst = FallTempo.Burst(waves);
            float settle = FallTempo.Settle(waves);

            // A wave with glass going off in it is given a beat of its own, and the whole cascade
            // is bounded in how much of that it may spend (FallTempo.ShotCeiling). Counted up
            // front so four lenses across four waves share one allowance rather than each taking
            // the full one.
            int firing = 0;
            for (int w = 0; w < waves; w++) if (result.Steps[w].Fired.Count > 0) firing++;

            float gather = FallTempo.Gather(firing);
            float throwing = FallTempo.Throw(firing);

            for (int w = 0; w < waves; w++)
            {
                var step = result.Steps[w];

                // ---- the flash, first, so the eye is told what happened before it changes
                for (int i = 0; i < step.Burst.Count; i++)
                {
                    var mote = _at[step.Burst[i]];
                    if (mote == null) continue;

                    Tween.Tint(mote.Body, Pal.Radiance, flash * .8f);
                    Tween.Punch(mote.Rt, .45f, flash * .9f);
                }

                Audio.Sfx("lit", .62f, FallTempo.Pitch(step.Wave));
                if (step.Wave >= FallTempo.ChainFrom) ShakeBoard(FallTempo.Shake(step.Wave));

                yield return new WaitForSecondsRealtime(flash);
                if (!this) yield break;

                // ---- the burst, and the light going into whatever it touches
                for (int i = 0; i < step.Burst.Count; i++)
                {
                    int at = step.Burst[i];
                    var mote = _at[at];
                    _at[at] = null;
                    _shown[at] = Energy.None;

                    if (mote == null) continue;

                    var where = Where(at);
                    Burst.Sparks(_fx, where, Pal.Radiance, 9, 190f, 17f, burst * 2.2f);
                    Shockwave(where, Pal.Radiance, _size * 3.4f, burst * 2.4f);

                    var going = mote;
                    Tween.Run(burst * .9f, Ease.OutQuad, t =>
                    {
                        if (going.Body == null) return;
                        going.Rt.localScale = Vector3.one * (1f + t * .7f);
                        var c = going.Body.color; c.a = 1f - t; going.Body.color = c;
                    }, mote.Body).OnDone(() => Give(going));
                }

                // ---- glass taking a channel. Two thirds of what the player is doing lives
                //      here: a lens is three drops apart from going off, so the two drops that
                //      pay for it have to land on the board as something. Dealt a little apart
                //      so two lenses charging in one wave read as two events.
                for (int i = 0; i < step.Charged.Count; i++)
                {
                    int at = step.Charged[i];
                    var glass = _at[at];
                    if (glass == null) continue;

                    // What this lens was actually handed, which is no longer the wave's colour:
                    // a burst beside it gives the drop's, and another lens's beam gives white and
                    // fills it in one step. Drawing both as the drop's colour would say a lens is
                    // a channel nearer when it is in fact about to go off.
                    int took = step.ChargeGain(i, result.Colour);
                    _shown[at] |= took;

                    var target = glass;
                    int now = _shown[at];
                    int cell = at;

                    Tween.After(burst * .3f + i * burst * .14f, () =>
                    {
                        if (target.Body == null) return;
                        ChargeGlass(target, Where(cell), now, took, cell, burst);
                    }, glass.Body);
                }

                // Each washed mote is reached by a streak from whichever burst was nearest, so
                // the rule is drawn rather than described: this colour came from that burst.
                for (int i = 0; i < step.Washed.Count; i++)
                {
                    int at = step.Washed[i];
                    var mote = _at[at];
                    if (mote == null) continue;

                    // A cell a beam delivered to has already been shown where its light came
                    // from, at rather more length than a streak would — and the shot is a whole
                    // beat later than the burst, so its colour must not land early. Everything
                    // else keeps the ordinary wash's own beat. That is "nothing is drawn before
                    // its cause" for this mode.
                    bool byBeam = Beamed(step.Beams, at);
                    float arrives = byBeam ? burst + gather + throwing * .4f : burst * .45f;

                    // What this mote was actually handed. A burst washes the drop's one colour; a
                    // beam hands over white, so the mote is completed and pops on the next wave.
                    // Painted as the drop's colour instead, a mote about to go off would be drawn
                    // as one that had merely improved, which is the most misleading thing this
                    // board could say.
                    int took = step.WashGain(i, result.Colour);

                    // The streak is for the ordinary wash alone: it says "this colour came from
                    // that burst", which a beam says for itself at four times the length.
                    if (!byBeam && step.Burst.Count > 0)
                        Streak(Nearest(step.Burst, at), at, Pal.EnergyColour(took), burst);

                    int was = _shown[at];
                    _shown[at] = was | took;

                    var target = mote;
                    int now = _shown[at];
                    int coming = Run.Next;
                    int cell = at;
                    Tween.After(arrives, () =>
                    {
                        if (target.Body == null) return;
                        Paint(target, now, coming, cell);
                        Tween.Punch(target.Rt, .30f, burst);
                    }, mote.Body);
                }

                // The count climbs as the chain runs, one number per wave, so the player
                // watches it grow rather than being told afterwards how big it was. A single
                // burst is not a chain and says nothing at all — see FallChain.
                if (FallChain.Counts(waves)) ShowCount(step.Wave, waves);

                yield return new WaitForSecondsRealtime(burst);
                if (!this) yield break;

                // ---- the shot. Its own beat between the burst and the collapse, because it is
                //      the one thing in this mode worth stopping the board for and because the
                //      well must not fall through the light while it is still crossing.
                if (step.Fired.Count > 0)
                {
                    for (int i = 0; i < step.Fired.Count; i++)
                    {
                        int at = step.Fired[i];
                        var glass = _at[at];
                        _at[at] = null;
                        _shown[at] = Energy.None;

                        FireGlass(glass, Where(at), gather, throwing);
                    }

                    // Every beam of every lens that fired this wave, thrown once the gather is
                    // done. Staggered by a frame or two apiece so the pair or the four leaving one
                    // lens read as a star opening rather than as one cross drawn at once.
                    //
                    // **Drawn white, because it is white.** A lens holds all three channels by the
                    // time it goes off, so what it throws is all three and whatever it lands on is
                    // completed rather than improved. Painting the shot in the drop's colour was
                    // right while a beam carried one channel and is now a lie about the one thing
                    // that separates a shot from a wash.
                    for (int i = 0; i < step.Beams.Count; i++)
                        Ray(step.Beams[i], Pal.Radiance, throwing,
                            gather + i * throwing * .05f);

                    yield return new WaitForSecondsRealtime(gather + throwing);
                    if (!this) yield break;
                }

                // ---- the collapse
                Slide(step.Moved, settle);

                yield return new WaitForSecondsRealtime(settle);
                if (!this) yield break;

                PaintBrim();
            }

            // And the word, once, over a board that has stopped moving. Everything up to here
            // is inside FallTempo.Ceiling because the board is latched while it plays; this is
            // the pay-off and is the one beat allowed to sit outside it.
            yield return Fanfare(waves);
        }

        /// <summary>
        /// The running chain count: one number per wave, each landing bigger and brighter than
        /// the last.
        ///
        /// <para>
        /// <b>It appears while the chain is still running, which is the whole point.</b> A total
        /// printed at the end is a report; a number climbing under your thumb is the thing
        /// actually happening, and nobody watching x3 land knows yet whether there is an x4. How
        /// far up the ladder each one sits — its size and its colour — is <c>FallChain</c>'s, in
        /// Domain, because a switch on a wave count inside a <c>MonoBehaviour</c> is the one
        /// place here nothing can be proved.
        /// </para>
        /// </summary>
        void ShowCount(int wave, int waves)
        {
            Retire(_count, .14f);

            var tint = ChainTint(FallChain.Tier(wave));
            float pop = FallTempo.CountPop(waves);

            var label = UIKit.Titled("Count", _fx, Loc.Format("mode.fall.multiplier", wave),
                                     FallChain.PointsFor(wave), tint, TextAnchor.MiddleCenter,
                                     new Vector2(_layout.Width * _cell, 200f),
                                     new Vector2(.5f, .5f), Vector2.zero, 9f, 9f);
            label.raycastTarget = false;
            _count = label;

            var rt = (RectTransform)label.transform;

            // Alternating tilt, so a long chain reads as a drum roll rather than as one number
            // being redrawn. Landed on nought either way, so nothing is left leaning.
            float lean = (wave & 1) == 0 ? 10f : -10f;

            Tween.Run(pop, Ease.OutBack, t =>
            {
                if (!label) return;
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(.30f, 1f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(lean, 0f, t));
            }, label).OnAbandon(() =>
            {
                if (!label) return;
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            });

            Shockwave(Vector2.zero, tint, _size * (5f + wave), pop * 2.4f);
            Audio.Sfx("chime", .5f, FallTempo.Pitch(wave));
        }

        /// <summary>
        /// The word at the end of a named chain: it slams in over a board that has stopped
        /// moving, holds, and lifts away.
        ///
        /// <para>
        /// <b>It replaced confetti, and that is a better trade than it sounds.</b> Confetti says
        /// "something good happened" and says it identically for a two-chain and a six. A word
        /// that climbs — and a number that climbed to reach it — says <em>how</em> good, which is
        /// the only part worth watching twice.
        /// </para>
        /// </summary>
        IEnumerator Fanfare(int waves)
        {
            string key = FallChain.WordKey(waves);
            if (key == null) { Retire(_count, .22f); _count = null; yield break; }

            var tint = ChainTint(FallChain.Tier(waves));

            // The count steps aside for it rather than sitting underneath.
            Retire(_count, .16f);
            _count = null;

            var rays = UIKit.Img("Rays", _fx, Art.Rays(256, 16), Pal.A(tint, 0f),
                                 Vector2.one * _size * 13f, new Vector2(.5f, .5f), Vector2.zero);
            rays.raycastTarget = false;

            Tween.Run(FallTempo.Fanfare, Ease.OutQuad, t =>
            {
                if (!rays) return;
                rays.transform.localScale = Vector3.one * Mathf.Lerp(.45f, 1.35f, t);
                rays.transform.localRotation = Quaternion.Euler(0f, 0f, t * 34f);
                rays.color = Pal.A(tint, (t < .18f ? t / .18f : 1f - (t - .18f) / .82f) * .55f);
            }, rays).OnDone(() => { if (rays) Destroy(rays.gameObject); });

            var word = UIKit.Titled("Word", _fx, Loc.Get(key), FallChain.WordPointsFor(waves),
                                    tint, TextAnchor.MiddleCenter,
                                    new Vector2(_layout.Width * _cell, 220f),
                                    new Vector2(.5f, .5f), Vector2.zero, 11f, 10f);
            UIKit.Shrinkable(word, 44);
            word.raycastTarget = false;

            var rt = (RectTransform)word.transform;

            // Crashing in from oversized rather than growing into place: the impact is the
            // deceleration, which is why it is OutQuint from well above one rather than a pop.
            Tween.Run(.24f, Ease.OutQuint, t =>
            {
                if (!word) return;
                rt.localScale = Vector3.one * Mathf.Lerp(2.6f, 1f, t);
            }, word);

            Flow.Flash(Pal.A(tint, .46f), .34f, .40f);
            ShakeBoard(FallTempo.Shake(waves) * 1.5f);
            Burst.Sparks(_fx, Vector2.zero, tint, 20, 420f, 26f, .8f);
            Audio.Sfx("win", .8f, 1f + FallChain.Tier(waves) * .05f);

            yield return new WaitForSecondsRealtime(FallTempo.Fanfare * .62f);
            if (!this) yield break;

            Tween.Run(FallTempo.Fanfare * .38f, Ease.OutQuad, t =>
            {
                if (!word) return;
                rt.anchoredPosition = new Vector2(0f, t * 120f);
                word.color = Pal.A(tint, 1f - t);
            }, word).OnDone(() => { if (word) Destroy(word.gameObject); });

            yield return new WaitForSecondsRealtime(FallTempo.Fanfare * .38f);
        }

        /// <summary>Fades a spent chain label out and lets it go. Null-safe; the common case.</summary>
        void Retire(Text label, float seconds)
        {
            if (!label) return;

            var rt = (RectTransform)label.transform;
            var from = label.color;

            Tween.KillAll(label);
            Tween.Run(seconds, Ease.OutQuad, t =>
            {
                if (!label) return;
                rt.localScale = Vector3.one * Mathf.Lerp(1f, .72f, t);
                label.color = Pal.A(from, (1f - t) * from.a);
            }, label).OnDone(() => { if (label) Destroy(label.gameObject); });
        }

        /// <summary>
        /// How a chain is coloured as it climbs. Cream, gold, amber, rose, and white-hot at the
        /// top — a ramp that reads as heat rather than as five arbitrary colours.
        /// </summary>
        static Color ChainTint(int tier)
        {
            switch (tier)
            {
                case 0: return Pal.Cream;
                case 1: return Pal.Gold;
                case 2: return Pal.Amber;
                case 3: return Pal.Rose;
                default: return Pal.Radiance;
            }
        }

        /// <summary>Whether any beam of this wave delivered its light to this cell.</summary>
        static bool Beamed(IReadOnlyList<FallBeam> beams, int cell)
        {
            for (int i = 0; i < beams.Count; i++) if (beams[i].Hit == cell) return true;
            return false;
        }

        /// <summary>The cell of the burst nearest a washed mote, for the streak to come from.</summary>
        int Nearest(IReadOnlyList<int> burst, int at)
        {
            int best = at, near = int.MaxValue;
            int ax = at % _layout.Width, ay = at / _layout.Width;

            for (int i = 0; i < burst.Count; i++)
            {
                int b = burst[i];
                int span = Mathf.Abs(b % _layout.Width - ax) + Mathf.Abs(b / _layout.Width - ay);
                if (span >= near) continue;
                best = b;
                near = span;
            }

            return best;
        }

        /// <summary>
        /// Moves the widgets the model says moved, and reassigns which cell each one draws.
        ///
        /// <para>
        /// The reassignment is the important half. A widget is not tied to a cell — it is what
        /// is currently drawing one — so a mote that slides two rows keeps its own object,
        /// keeps whatever tween is on it and simply belongs to a different index afterwards.
        /// That is what makes the collapse read as things falling rather than as a board being
        /// redrawn.
        /// </para>
        /// </summary>
        void Slide(IReadOnlyList<FallMove> moved, float seconds)
        {
            if (moved == null || moved.Count == 0) return;

            // Read whole before anything is written: a chain of moves inside one column has
            // every `To` sitting on somebody else's `From`, so walking it in place would clear
            // a cell the next move was about to read.
            var carried = new int[moved.Count];
            var carriers = new MoteView[moved.Count];
            for (int i = 0; i < moved.Count; i++)
            {
                carried[i] = _shown[moved[i].From];
                carriers[i] = _at[moved[i].From];
            }

            for (int i = 0; i < moved.Count; i++)
            {
                _at[moved[i].From] = null;
                _shown[moved[i].From] = Energy.None;
            }

            for (int i = 0; i < moved.Count; i++)
            {
                var move = moved[i];
                var mote = carriers[i];

                _shown[move.To] = carried[i];
                _at[move.To] = mote;

                if (mote == null) continue;

                // Staggered a little by column, so the well collapses rather than dropping as
                // one slab.
                float delay = (move.To % _layout.Width) * seconds * .06f;
                var end = Where(move.To);
                var rt = mote.Rt;

                Tween.Move(rt, end, seconds, Ease.OutCubic).Delay(delay)
                     .OnAbandon(() => { if (rt) rt.anchoredPosition = end; });
            }
        }

        // ------------------------------------------------------------------ the flourishes
        void Ripple(Vector2 at, Color colour, float size, float seconds)
        {
            var img = UIKit.Img("Ripple", _fx, Art.Ring(128, 8f), Pal.A(colour, .85f),
                                Vector2.one * size, new Vector2(.5f, .5f), at);
            img.raycastTarget = false;

            Tween.Run(seconds, Ease.OutQuint, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(.32f, 1.25f, t);
                img.color = Pal.A(colour, .85f * (1f - t));
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>A burst's own ring: brighter, faster and larger than an enrich's ripple.</summary>
        void Shockwave(Vector2 at, Color colour, float size, float seconds)
        {
            var img = UIKit.Img("Wave", _fx, Art.Ring(128, 11f), Pal.A(colour, .95f),
                                Vector2.one * size, new Vector2(.5f, .5f), at);
            img.raycastTarget = false;

            Tween.Run(seconds, Ease.OutQuint, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(.18f, 1.35f, t);
                img.color = Pal.A(colour, .95f * (1f - t) * (1f - t));
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// The light travelling from a burst into the mote it washes. Short, and drawn in the
        /// colour that is actually being handed over, because that is the rule it is showing.
        /// </summary>
        void Streak(int fromCell, int toCell, Color colour, float seconds)
        {
            var a = Where(fromCell);
            var b = Where(toCell);

            var img = UIKit.Img("Streak", _fx, Art.SoftCapsule(28, 96), Pal.A(colour, .9f),
                                new Vector2(_size * .42f, _cell), new Vector2(.5f, .5f), a);
            img.raycastTarget = false;

            var rt = (RectTransform)img.transform;
            rt.localRotation = Quaternion.Euler(0, 0,
                Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg - 90f);

            Tween.Run(seconds * .55f, Ease.OutCubic, t =>
            {
                if (!img) return;
                rt.anchoredPosition = Vector2.Lerp(a, b, t);
                img.color = Pal.A(colour, .9f * (1f - t * t));
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        // ------------------------------------------------------------------ the glass
        /// <summary>
        /// A lens: a rim, a four-point glint, and three pips saying how full it is.
        ///
        /// <para>
        /// <b>The silhouette carries the fact that it is not a mote, and the pips carry the
        /// puzzle.</b> Every other cell here is a bright saturated circle, so glass is hollow and
        /// pale and cold (<c>Pal.Glass</c>) and wears the one mark on this board that points four
        /// ways — which is the shape of what it does when it goes off. What the rim alone cannot
        /// say is <em>which colour it is still waiting for</em>, and that is the whole of the
        /// decision: three pips in R, G and B, lit for what it holds and dark for what it wants.
        /// A player reads "needs blue" off the board at a glance, exactly as the halo on a mote
        /// says "this drop would finish me".
        /// </para>
        /// <para>
        /// <b>And a lens one channel short trembles.</b> That is not decoration: charging is
        /// three drops apart and the payoff is the biggest thing in the mode, so the board owes
        /// the player a beat of "the next one does it". Nothing else in this well moves while
        /// nobody is touching it.
        /// </para>
        /// </summary>
        void PaintGlass(MoteView glass, int cell, int index)
        {
            int charge = FallCell.Charge(cell);
            int wants = FallCell.Wants(cell);
            bool nearly = charge != Energy.None && CountChannels(charge) == 2;

            glass.Body.sprite = Art.Ring(128, nearly ? 11f : 9f);
            glass.Body.color = charge == Energy.None
                             ? Pal.A(Pal.Glass, .92f)
                             : Pal.A(Color.Lerp(Pal.Glass, Pal.EnergyColour(charge), .72f), .96f);

            glass.Sheen.color = charge == Energy.None
                              ? Pal.A(Pal.Glass, .12f)
                              : Pal.A(Pal.EnergyColour(charge), .20f);

            // Never ripe. A halo says "the next drop finishes this", and no drop ever finishes a
            // lens — only light that has already travelled does.
            glass.Halo.gameObject.SetActive(false);
            glass.Halo.color = new Color(1, 1, 1, 0f);

            PaintPips(glass, charge);

            if (!glass.Facet) return;

            if (!glass.Facet.gameObject.activeSelf)
            {
                glass.Facet.gameObject.SetActive(true);

                // Phased off the cell index rather than rolled: a random phase differs between a
                // board being built and the same board restarted, and two runs of one well that
                // shimmer differently is a difference nobody can name and everybody notices.
                var facet = glass.Facet;
                var frt = (RectTransform)facet.transform;
                float phase = (index * .37f) % 1f;

                Tween.Run(GlintTurn, Ease.Linear, t =>
                {
                    if (!facet) return;

                    float a = (t + phase) % 1f;
                    frt.localRotation = Quaternion.Euler(0f, 0f, a * 90f);
                    facet.color = Pal.A(Pal.Glass, .48f + Mathf.Abs(Mathf.Sin(a * Mathf.PI)) * .38f);
                }, facet, "glint").Loop(-1);
            }

            Tween.KillChannel(glass.Rt, "tremble");
            if (!nearly) { glass.Rt.localScale = Vector3.one; return; }

            // One channel short. It is about to be the loudest thing on the board and it says so.
            var body = glass.Body;
            var rt = glass.Rt;

            Tween.Run(TrembleTurn, Ease.InOutSine, t =>
            {
                if (!body) return;

                float swell = Mathf.Sin(t * Mathf.PI * 2f);
                rt.localScale = Vector3.one * (1f + swell * .07f);
                body.color = Pal.A(Color.Lerp(Pal.Glass, Pal.EnergyColour(FallCell.Charge(cell)), .72f),
                                   .82f + Mathf.Abs(swell) * .18f);
            }, glass.Rt, "tremble").Loop(-1)
              .OnAbandon(() => { if (rt) rt.localScale = Vector3.one; });
        }

        /// <summary>How long a lens takes to turn a quarter, which is one period of a four-point glint.</summary>
        const float GlintTurn = 3.4f;

        /// <summary>How long one breath of a lens that is one channel short takes.</summary>
        const float TrembleTurn = .78f;

        static int CountChannels(int mask)
        {
            int n = 0;
            if ((mask & Energy.R) != 0) n++;
            if ((mask & Energy.G) != 0) n++;
            if ((mask & Energy.B) != 0) n++;
            return n;
        }

        /// <summary>
        /// The three pips: lit for a channel the glass holds, dark for one it still wants.
        ///
        /// Built once and rebound, for <c>GridView</c>'s reason — a well is up to a hundred cells
        /// and a cascade recharges several of them, so three objects per lens that are recoloured
        /// beats three destroyed and remade in the middle of the one animation this chapter is
        /// for.
        /// </summary>
        void PaintPips(MoteView glass, int charge)
        {
            if (glass.Pips == null)
            {
                glass.Pips = new Image[3];
                for (int i = 0; i < 3; i++)
                {
                    // A triangle inside the rim, which reads as a gauge without needing a track
                    // drawn round it: three of anything is counted at a glance.
                    float angle = (90f + i * 120f) * Mathf.Deg2Rad;
                    var at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _size * .22f;

                    glass.Pips[i] = UIKit.Img("Pip" + i, glass.Body.transform, Art.Disc(64),
                                              new Color(1, 1, 1, 0f), Vector2.one * _size * .17f,
                                              new Vector2(.5f, .5f), at);
                    glass.Pips[i].raycastTarget = false;
                }
            }

            int[] channels = { Energy.R, Energy.G, Energy.B };
            for (int i = 0; i < 3; i++)
            {
                if (!glass.Pips[i]) continue;

                bool held = (charge & channels[i]) != 0;
                glass.Pips[i].color = held ? Pal.A(Pal.EnergyColour(channels[i]), 1f)
                                           : Pal.A(Pal.Glass, .18f);
                glass.Pips[i].transform.localScale = Vector3.one * (held ? 1f : .72f);
            }
        }

        /// <summary>
        /// A lens taking light: it arrives, the pips it lit spring in, the glass rings.
        ///
        /// <para>
        /// <b>This is two thirds of what the player actually does, and it used to be drawn as
        /// nothing.</b> Filling a lens is three drops apart; if only the shot were animated, the
        /// two drops that paid for it would land on the board as silence. So a charge is a small
        /// version of the big moment — a ring closing inward, the pip springing in, the rim
        /// taking the colour — and the note climbs, so the player hears one-of-three and
        /// two-of-three without counting.
        /// </para>
        /// <para>
        /// <b><paramref name="taken"/> is a mask rather than a channel, and on one arrival it is
        /// all three.</b> A burst beside the glass hands over the drop's one colour; another
        /// lens's beam hands over white and fills it outright. So every pip in the mask is popped
        /// rather than one worked out from a channel — the version that did the latter answered
        /// "blue" for white, lit one pip of three and pitched the note as though a lens two drops
        /// away had just gone off.
        /// </para>
        /// </summary>
        void ChargeGlass(MoteView glass, Vector2 where, int cell, int taken, int index, float run)
        {
            if (glass == null) return;

            int charge = FallCell.Charge(cell);
            int filled = CountChannels(charge);

            // A ring closing onto the glass rather than a streak into it. Where the light came
            // from is already drawn — by the burst beside it, or by the beam that carried it —
            // and a second line saying the same thing is the clutter Budburst's bolt was.
            Circle(where, Pal.EnergyColour(taken), _size * 2.3f, run * 1.1f);

            PaintGlass(glass, cell, index);

            // Every pip that just lit springs in. Named on its own channel so a second charge in
            // the same cascade supersedes cleanly rather than compounding.
            if (glass.Pips != null)
            {
                int[] channels = { Energy.R, Energy.G, Energy.B };
                for (int i = 0; i < 3; i++)
                {
                    if ((taken & channels[i]) == 0 || !glass.Pips[i]) continue;
                    Tween.Pop(glass.Pips[i].transform, .1f, run * 1.2f);
                }
            }

            Tween.KillChannel(glass.Rt, "tremble");
            Tween.Punch(glass.Rt, .30f, run);

            Burst.Sparks(_fx, where, Pal.EnergyColour(taken), 5, 120f, 9f, run * 1.6f);

            // One of three, two of three — and the top of the run when a beam filled it outright,
            // which is the loudest a charge is allowed to be before the shot itself.
            Audio.Sfx("lit", .42f, .92f + filled * .16f);
        }

        /// <summary>
        /// <b>The shot.</b> A lens that has taken all three fires along every axis at once, and
        /// this is the one moment in Lightfall the board is allowed to stop for.
        ///
        /// <para>
        /// <b>Four gestures, not eight.</b> Budburst's burst was rebuilt from petals, rays,
        /// embers, a backlight and a prism ring, and came back as "a meshed up random animation"
        /// — the lesson being that a premium moment is a few things done properly, all of them
        /// round and soft-edged, rather than a pile of kinds. So: it <b>gathers</b> (the glass
        /// draws in and goes white while the well dims and a ring closes onto it), it
        /// <b>strikes</b> (a white core, a flash, a shake), it <b>throws</b> (its beams, drawn
        /// by <see cref="Ray"/>), and it <b>comes apart</b> (a shockwave and prismatic shards).
        /// </para>
        /// <para>
        /// <b>The dim is what makes it read as an event rather than a bigger burst.</b> Nothing
        /// else in this mode darkens the well, so the first frame of a gather is already unlike
        /// every other frame the player has seen — which is worth more than any amount added on
        /// top of the explosion itself.
        /// </para>
        /// </summary>
        void FireGlass(MoteView glass, Vector2 where, float gather, float run)
        {
            // The whole shot, not half of it: the beams set off after the gather and run for
            // the rest of the beat, so a dim that lifted at the midpoint would brighten the well
            // underneath light that was still crossing it.
            Dim(gather + run);

            var closing = Circle(where, Pal.Radiance, _size * 4.2f, gather);
            if (closing) closing.transform.SetAsLastSibling();

            Audio.Sfx("whoosh", .5f, 1.35f);

            if (glass != null)
            {
                var rt = glass.Rt;
                var body = glass.Body;

                Tween.KillChannel(glass.Rt, "tremble");
                if (glass.Facet) Tween.KillAll(glass.Facet);

                // Drawn in rather than swelling: a thing that gathers is about to do something,
                // where a thing that grows is only getting bigger. The white arrives with it.
                Tween.Run(gather, Ease.InQuad, t =>
                {
                    if (!body) return;

                    rt.localScale = Vector3.one * Mathf.Lerp(1f, .46f, t);
                    body.color = Color.Lerp(body.color, Pal.A(Pal.Radiance, 1f), t);

                    if (glass.Pips == null) return;
                    for (int i = 0; i < glass.Pips.Length; i++)
                        if (glass.Pips[i]) glass.Pips[i].color = Pal.A(Pal.Radiance, 1f - t);
                }, glass.Body);
            }

            var going = glass;
            Tween.After(gather, () =>
            {
                // ---- the strike
                Flash(where, run);
                ShakeBoard(22f);
                Flow.Flash(Pal.A(Pal.Radiance, .30f), .10f, .28f);
                Audio.Sfx("burst", .85f, .78f);
                Audio.Sfx("chime2", .5f, 1.55f);

                Shockwave(where, Pal.Radiance, _size * 7.5f, run * 2.2f);
                Shards(where, run);

                if (going == null) return;

                var rt = going.Rt;
                var body = going.Body;

                Tween.Run(run * .55f, Ease.OutQuad, t =>
                {
                    if (!body) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.46f, 2.1f, t);
                    body.color = Pal.A(Pal.Radiance, 1f - t);
                }, going.Body).OnDone(() => Give(going));
            }, this);
        }

        /// <summary>
        /// The well darkening for the length of a shot, so the light in it is the only thing on
        /// screen. Drawn over the board and under the effects, and it is the whole reason a shot
        /// reads as a different kind of event.
        /// </summary>
        void Dim(float seconds)
        {
            if (_fx == null) return;

            var shade = UIKit.Img("Shade", _fx, Art.Pixel, new Color(0f, 0f, 0f, 0f),
                                  new Vector2(_layout.Width * _cell + 64f,
                                              _layout.Height * _cell + 64f),
                                  new Vector2(.5f, .5f), Vector2.zero);
            shade.raycastTarget = false;
            shade.transform.SetAsFirstSibling();

            Tween.Run(seconds, Ease.OutQuad, t =>
            {
                if (!shade) return;
                // Up quickly, held, and away — the hold is what the beams are drawn against.
                float a = t < .22f ? t / .22f : t > .74f ? (1f - t) / .26f : 1f;
                shade.color = new Color(0f, 0f, 0f, a * .46f);
            }, shade).OnDone(() => { if (shade) Destroy(shade.gameObject); });
        }

        /// <summary>A hot round core over a wide soft bloom, which is the two-layer light every
        /// game of this shape draws. Round on purpose: a spiky star reads as lighting equipment
        /// rather than as light, which is the fault Budburst shipped and took back.</summary>
        void Flash(Vector2 at, float seconds)
        {
            var bloom = UIKit.Img("Bloom", _fx, Art.Glow(256, 1.6f), Pal.A(Pal.Radiance, .0f),
                                  Vector2.one * _size * 6.5f, new Vector2(.5f, .5f), at);
            bloom.raycastTarget = false;

            var core = UIKit.Img("Core", _fx, Art.Glow(128, 3.4f), Pal.A(Color.white, .0f),
                                 Vector2.one * _size * 2.4f, new Vector2(.5f, .5f), at);
            core.raycastTarget = false;

            Tween.Run(seconds * 1.5f, Ease.OutQuad, t =>
            {
                if (!core) return;

                float up = t < .12f ? t / .12f : 1f - (t - .12f) / .88f;
                core.color = Pal.A(Color.white, up);
                core.transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1.5f, t);

                if (!bloom) return;
                bloom.color = Pal.A(Pal.Radiance, up * .72f);
                bloom.transform.localScale = Vector3.one * Mathf.Lerp(.3f, 1.25f, t);
            }, core).OnDone(() =>
            {
                if (core) Destroy(core.gameObject);
                if (bloom) Destroy(bloom.gameObject);
            });
        }

        /// <summary>
        /// What is left of the glass: shards thrown outward, each in one of the three channels.
        ///
        /// Prismatic rather than white, because that is the one thing a lens is: the light it was
        /// holding comes apart into the colours it was made of, which nothing else on this board
        /// does.
        /// </summary>
        void Shards(Vector2 at, float seconds)
        {
            int[] channels = { Energy.R, Energy.G, Energy.B };

            for (int i = 0; i < 9; i++)
            {
                var tint = Color.Lerp(Pal.EnergyColour(channels[i % 3]), Pal.Glass, .35f);

                var shard = UIKit.Img("Shard", _fx, Art.SoftCapsule(20, 72), Pal.A(tint, .95f),
                                      new Vector2(_size * .12f, _size * .34f),
                                      new Vector2(.5f, .5f), at);
                shard.raycastTarget = false;

                // Spread evenly rather than rolled, for the phase rule: a restart of one well must
                // not throw its glass differently from the first run of it.
                float angle = i * (360f / 9f) + 18f;
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                float reach = _cell * (1.5f + (i % 3) * .45f);

                var rt = (RectTransform)shard.transform;
                rt.localRotation = Quaternion.Euler(0f, 0f, -angle);

                Tween.Run(seconds * 1.9f, Ease.OutQuint, t =>
                {
                    if (!shard) return;
                    rt.anchoredPosition = at + dir * reach * t;
                    rt.localScale = new Vector3(1f - t * .5f, 1f + t * .35f, 1f);
                    shard.color = Pal.A(tint, .95f * (1f - t) * (1f - t));
                }, shard).OnDone(() => { if (shard) Destroy(shard.gameObject); });
            }
        }

        /// <summary>
        /// One of a shot's beams: a white core inside a coloured glow, with a red and a blue
        /// fringe either side of it.
        ///
        /// <para>
        /// <b>The fringe is the one idea here that is about a lens rather than about an
        /// explosion.</b> Light through glass comes apart, so the beam is drawn as three strands
        /// that do not quite agree — red one side, blue the other, white down the middle. It
        /// costs two extra capsules and it is the difference between "a bright line" and "light
        /// being refracted", which is the whole of what the object is.
        /// </para>
        /// <para>
        /// <b>It grows from its source rather than appearing whole.</b> The pivot is moved to the
        /// base of the capsule so length is the only thing animated — growing a centre-pivoted
        /// bar would have it reaching backwards out of the lens at the same rate it reaches
        /// forwards. And a beam that reached nothing is drawn exactly as far as it went, one cell
        /// outside the wall: three drops of charge spent on nothing is a decision that went
        /// wrong, and the player is entitled to watch it happen.
        /// </para>
        /// </summary>
        void Ray(FallBeam beam, Color colour, float run, float delay)
        {
            if (_fx == null) return;

            // Cell space counts rows downward and the canvas counts them up, so the step is
            // negated in y. Getting this wrong draws every vertical shot upside down.
            var step = new Vector2(beam.Dx * _cell, -beam.Dy * _cell);
            var from = Where(beam.From);
            var to = from + step * beam.Steps;

            float length = step.magnitude * beam.Steps;
            float angle = Mathf.Atan2(step.y, step.x) * Mathf.Rad2Deg - 90f;
            float width = _size * .26f;

            // The fringes *diverge*. They leave the glass together and fan apart by a couple
            // of degrees as they go, so the further the shot travels the wider the split — which
            // is what light through a prism actually does, and is the difference between a beam
            // with coloured edges and a beam that is visibly being refracted. Two degrees is
            // enough to read across five cells and small enough that a one-cell shot still looks
            // like one beam.
            var glow = Lance("Beam", Art.SoftCapsule(44, 128), Pal.A(colour, .5f),
                             width * 3.1f, from, angle);
            var red = Lance("Fringe", Art.SoftCapsule(20, 128), Pal.A(Pal.Ember, .55f),
                            width * .8f, from, angle - Split);
            var blue = Lance("Fringe", Art.SoftCapsule(20, 128), Pal.A(Pal.Azure, .55f),
                             width * .8f, from, angle + Split);
            var core = Lance("Core", Art.SoftCapsule(24, 128), Pal.A(Color.white, .0f),
                             width, from, angle);

            var gr = (RectTransform)glow.transform;
            var rr = (RectTransform)red.transform;
            var br = (RectTransform)blue.transform;
            var cr = (RectTransform)core.transform;

            Tween.Run(run, Ease.Linear, t =>
            {
                if (!core) return;

                // Out to full length over the first stretch, then held while it fades: a beam
                // still growing as it faded would never be seen at its own length.
                float reach = length * Ease.OutQuint(Mathf.Clamp01(t / .34f));
                float fade = (1f - t) * (1f - t);

                cr.sizeDelta = new Vector2(width, reach);
                core.color = Pal.A(Color.white, fade);

                if (gr) { gr.sizeDelta = new Vector2(width * 3.1f, reach); glow.color = Pal.A(colour, .5f * (1f - t)); }
                if (rr) { rr.sizeDelta = new Vector2(width * .8f, reach); red.color = Pal.A(Pal.Ember, .5f * fade); }
                if (br) { br.sizeDelta = new Vector2(width * .8f, reach); blue.color = Pal.A(Pal.Azure, .5f * fade); }
            }, core).Delay(delay).OnDone(() =>
            {
                if (core) Destroy(core.gameObject);
                if (glow) Destroy(glow.gameObject);
                if (red) Destroy(red.gameObject);
                if (blue) Destroy(blue.gameObject);
            });

            // The arrival, and only where there was one. A ring thrown at the wall would say
            // something landed there.
            if (beam.Landed)
                Tween.After(delay + run * .34f,
                            () => Shockwave(to, colour, _size * 2.4f, run * 1.4f), this);
        }

        /// <summary>
        /// How far the two fringes fan away from the beam they left with, in degrees.
        ///
        /// The whole of what makes a shot read as glass rather than as a bright line. Kept small
        /// on purpose: at two degrees a shot across five cells splits by about a fifth of a cell,
        /// which is legible, and a one-cell shot still reads as a single beam.
        /// </summary>
        const float Split = 2.2f;

        /// <summary>A capsule pivoted at its base and turned along the way it travels.</summary>
        Image Lance(string name, Sprite sprite, Color colour, float width, Vector2 at, float angle)
        {
            var img = UIKit.Img(name, _fx, sprite, colour, new Vector2(width, 0f),
                                new Vector2(.5f, .5f), at);
            img.raycastTarget = false;

            var rt = (RectTransform)img.transform;

            // UIKit.Box always pivots at centre, so this is set after the fact — and the position
            // after that, because moving the pivot moves the rect under it.
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = at;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);

            return img;
        }

        /// <summary>
        /// A ring closing <em>inward</em> onto a cell, which is this game's idiom for "this one".
        ///
        /// Every other ring in Lightfall expands, and an expanding ring says <em>something went
        /// off here</em>. Closing says <em>watch this</em>, which is what both a charge and the
        /// gather before a shot need and what neither could say with a shockwave.
        /// </summary>
        Image Circle(Vector2 at, Color colour, float size, float seconds)
        {
            var img = UIKit.Img("Closing", _fx, Art.Ring(128, 7f), Pal.A(colour, 0f),
                                Vector2.one * size, new Vector2(.5f, .5f), at);
            img.raycastTarget = false;

            Tween.Run(seconds, Ease.OutQuad, t =>
            {
                if (!img) return;
                img.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, .28f, t);
                img.color = Pal.A(colour, Mathf.Sin(t * Mathf.PI) * .9f);
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });

            return img;
        }

        void ShakeBoard(float amount)
        {
            if (amount <= 0f || !_plate) return;

            var rt = (RectTransform)_grid;
            Tween.Shake(rt, amount, .26f);
        }

        // ------------------------------------------------------------------ endings
        /// <summary>
        /// Reads the run and reports it, once. Called on the edges that can end one and never
        /// from a poll — this is the same argument <c>RippleScreen.OnChanged</c> makes, and it
        /// matters more here because the verdict walks the board.
        /// </summary>
        void Settle()
        {
            if (_over || Run == null) return;

            var verdict = Run.Verdict;
            if (!verdict.IsOver) return;

            _over = true;
            Locked = true;
            HideGhost();

            if (verdict.IsWon) { StartCoroutine(Triumph()); return; }

            if (verdict.Ending == FallEnding.Flooded) StartCoroutine(Overflow());
            else Lost?.Invoke();
        }

        /// <summary>
        /// The well is empty. The brim line — the thing the whole run was about not touching —
        /// comes apart, which is the one flourish this mode has that no other could.
        /// </summary>
        IEnumerator Triumph()
        {
            Audio.Sfx("win", .9f);

            if (_brimLine)
            {
                Tween.KillChannel(_brimLine, "brim");
                var line = _brimLine;
                Tween.Run(.5f, Ease.OutQuint, t =>
                {
                    if (!line) return;
                    line.transform.localScale = new Vector3(1f - t, 1f + t * 5f, 1f);
                    line.color = Pal.A(Pal.Radiance, (1f - t) * .9f);
                }, _brimLine);
            }

            if (_plate) Tween.Punch(_plate.transform, .07f, .5f);

            yield return new WaitForSecondsRealtime(.34f);
            if (!this) yield break;

            Solved?.Invoke();
        }

        /// <summary>
        /// A mote came to rest above the brim. Said on the board, in the place the rule lives,
        /// before the panel arrives — so a player knows what they did rather than being told.
        /// </summary>
        IEnumerator Overflow()
        {
            // A low thud rather than breaking glass. The shatter was the same sample the
            // offer panel was playing a beat later, so a flood arrived as two crashes — and one
            // crash for "you stacked a mote too high" is already more punishment noise than the
            // mistake deserves. The shake and the red line are the report; this is its weight.
            Audio.Sfx("pop", .55f, .55f);
            ShakeBoard(20f);

            if (_brimLine)
            {
                Tween.KillChannel(_brimLine, "brim");
                _brimLine.color = Pal.A(Pal.Rose, 1f);
            }

            for (int x = 0; x < _layout.Width; x++)
            {
                var mote = _at[Run.Board.Index(x, FallLayout.Brim)];
                if (mote != null) Tween.Punch(mote.Rt, .5f, .45f);
            }

            Flow.Flash(Pal.A(Pal.Rose, .34f), .3f, .4f);

            yield return new WaitForSecondsRealtime(.42f);
            if (!this) yield break;

            Lost?.Invoke();
        }

        // ------------------------------------------------------------------ housekeeping
        /// <summary>
        /// Keeps the ghost honest while a finger is held still.
        ///
        /// The board moves underneath a held finger — a cascade lands, the procession advances —
        /// so a ghost drawn once at the moment of touch would go on promising a landing that is
        /// no longer where it says.
        /// </summary>
        void Update()
        {
            if (_hovered < 0) return;

            if (!Playable) { HideGhost(); return; }

            ShowGhost(_hovered);
        }

        void OnDestroy()
        {
            Tween.KillAll(this);
        }

        /// <summary>Where a lesson should point when it names the supply. Null before the tray exists.</summary>
        public RectTransform SupplyAnchor => _supply ? (RectTransform)_supply.transform : null;

        /// <summary>Where a lesson should point when it names the brim.</summary>
        public RectTransform BrimAnchor => _brimBand ? (RectTransform)_brimBand.transform : null;

        /// <summary>
        /// A mote the player can be shown, for a lesson that has to ring one — the ripest thing
        /// on the board, which is what the lesson about cooking is actually about.
        /// </summary>
        public RectTransform RipeAnchor
        {
            get
            {
                if (_at == null) return null;

                int next = Run != null ? Run.Next : Energy.None;

                for (int i = 0; i < _at.Length; i++)
                {
                    if (_at[i] == null) continue;
                    if (next != Energy.None && (_shown[i] | next) == Energy.All) return _at[i].Rt;
                }

                for (int i = 0; i < _at.Length; i++)
                    if (_at[i] != null) return _at[i].Rt;

                return null;
            }
        }

        /// <summary>
        /// A lens to point a lesson at, or null on a well that stands none — which is every well
        /// of the first chapter, and is exactly why the lesson is conditional on this.
        /// </summary>
        public RectTransform LensAnchor
        {
            get
            {
                if (_at == null) return null;

                // The fullest one, because a lesson about glass wants to point at the piece
                // that is closest to showing the player what it is for.
                RectTransform best = null;
                int most = -1;

                for (int i = 0; i < _at.Length; i++)
                {
                    if (_at[i] == null || !FallCell.IsLens(_shown[i])) continue;

                    int held = CountChannels(FallCell.Charge(_shown[i]));
                    if (held <= most) continue;

                    most = held;
                    best = _at[i].Rt;
                }

                return best;
            }
        }

        /// <summary>Hands the run more motes, because a continue was paid for.</summary>
        public void Grant(int motes)
        {
            if (Run == null) return;

            Run.Grant(motes);
            _over = false;
            Locked = false;

            PaintTray();
            PaintHalos();
            Changed?.Invoke();

            // Asked again rather than assumed: if a grant somehow left the well lost, the run
            // reaches its fail state again and the player is asked again rather than being
            // silently left on a dead board.
            Settle();
        }
    }

    /// <summary>
    /// Pointer enter and exit, which <c>Btn</c> does not report.
    ///
    /// It is what lets a column show its ghost while a finger is held over it and take it away
    /// when the finger leaves — on a touch screen that is a drag across the well, which is
    /// exactly how somebody chooses a column.
    /// </summary>
    public sealed class Hover : MonoBehaviour,
        UnityEngine.EventSystems.IPointerEnterHandler,
        UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.IPointerDownHandler
    {
        public Action Enter, Exit;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Exit?.Invoke();
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Enter?.Invoke();
    }
}
