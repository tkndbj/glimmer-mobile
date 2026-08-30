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

        /// <summary>One mote on screen: the body, the sheen over it and the halo round it.</summary>
        sealed class MoteView
        {
            public Image Body, Sheen, Halo;
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

            return new MoteView { Body = body, Halo = halo, Sheen = sheen, Rt = (RectTransform)body.transform };
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
            mote.Rt.gameObject.SetActive(false);
            mote.Rt.localScale = Vector3.one;
            mote.Body.color = Color.white;
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
                Paint(_at[i], colour, Run.Next);
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
        static void Paint(MoteView mote, int colour, int next)
        {
            mote.Body.color = Pal.EnergyColour(colour);

            // A mote the next drop would finish, and only that. Buried ones are ringed too:
            // no drop can land on them, but a chain can reach them, and that is worth seeing.
            bool ripe = next != Energy.None && colour != Energy.All &&
                        (colour | next) == Energy.All;

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
                if (_at[i] != null) Paint(_at[i], _shown[i], next);
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
            bool bursts = Run.Board.Bursts(colour, column);
            bool brim = Run.Board.AtBrim(colour, column);

            // Three readings, one glance. Red is the only one that is a warning rather than a
            // description, so it wins over the other two: a drop that comes to rest on the brim
            // is very probably the end of the run, and it is the one thing the player must not
            // do by accident.
            var ring = brim ? Pal.Rose : enriches ? Pal.Cream : Pal.Amber;

            _ghost.color = Pal.A(bursts ? Pal.Radiance : tint, bursts ? .55f : enriches ? .34f : .44f);
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
            bool enriches = Run.Board.Enriches(colour, column);

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

            StartCoroutine(PlayDrop(column, row, colour, enriches, result));
        }

        IEnumerator PlayDrop(int column, int row, int colour, bool enriches, FallResolution result)
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
            if (enriches)
            {
                // The mote it landed on takes the light: the falling widget is handed back and
                // the one already standing there changes colour, which is what actually happened.
                Give(falling);

                var host = _at[index];
                if (host != null)
                {
                    _shown[index] |= colour;
                    Paint(host, _shown[index], Run.Next);
                    Tween.Punch(host.Rt, .38f, FallTempo.Enrich);
                    Ripple(to, Pal.EnergyColour(_shown[index]), _size * 2.1f, .42f);
                }

                Audio.Sfx("chime", .58f, 1.22f);
            }
            else
            {
                _at[index] = falling;
                _shown[index] = colour;
                falling.Rt.anchoredPosition = to;
                Paint(falling, colour, Run.Next);

                // A squash, not a punch: this one landed on something rather than lighting it.
                var landed = falling.Rt;
                Tween.Run(FallTempo.Land, Ease.OutQuad, t =>
                {
                    if (!landed) return;
                    float squash = (1f - t) * .22f;
                    landed.localScale = new Vector3(1f + squash, 1f - squash, 1f);
                }, falling.Body).OnAbandon(() => { if (landed) landed.localScale = Vector3.one; });

                Audio.Sfx("pop", .5f, .82f);
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

                // Each washed mote is reached by a streak from whichever burst was nearest, so
                // the rule is drawn rather than described: this colour came from that burst.
                for (int i = 0; i < step.Washed.Count; i++)
                {
                    int at = step.Washed[i];
                    var mote = _at[at];
                    if (mote == null) continue;

                    Streak(Nearest(step.Burst, at), at, Pal.EnergyColour(result.Colour), burst);

                    int was = _shown[at];
                    _shown[at] = was | result.Colour;

                    var target = mote;
                    int now = _shown[at];
                    int coming = Run.Next;
                    Tween.After(burst * .45f, () =>
                    {
                        if (target.Body == null) return;
                        Paint(target, now, coming);
                        Tween.Punch(target.Rt, .30f, burst);
                    }, mote.Body);
                }

                // The count climbs as the chain runs, one number per wave, so the player
                // watches it grow rather than being told afterwards how big it was. A single
                // burst is not a chain and says nothing at all — see FallChain.
                if (FallChain.Counts(waves)) ShowCount(step.Wave, waves);

                yield return new WaitForSecondsRealtime(burst);
                if (!this) yield break;

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
