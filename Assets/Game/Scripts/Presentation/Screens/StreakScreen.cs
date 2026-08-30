using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The streak, as a board of nights under a moon.
    ///
    /// <para>
    /// <b>Why a board and not a scene.</b> This page was a campfire in a night clearing:
    /// seven numbered isometric blocks in a ring around a fire. The picture was the whole
    /// design, and the picture did not survive contact with the content. A ring has room
    /// for exactly the number of stones it was drawn for, so a ladder retuned from seven
    /// nights to ten — which <see cref="StreakRules.MaxRungs"/> explicitly allows — had
    /// nowhere to put the extra ones. The rewards had to hover over the stones because the
    /// stones had no faces to print them on, and half the ring was drawn behind the fire,
    /// so the same tile meant two different things depending on where the arithmetic put
    /// it. What was left read as coloured boxes on a teal cube.
    /// </para>
    /// <para>
    /// A grid fixes all three at once. Every night is the same object at the same size,
    /// carrying its own day, its own reward and its own state, so nothing is inferred from
    /// position and nothing is hidden behind anything. It grows: rungs beyond the fourth
    /// wrap to a new row and the board scrolls, which is the difference between retuning
    /// the ladder in <c>progression.json</c> and rewriting this file. And it is legible
    /// while it is being used — a player checking "what do I get tonight" reads one tile
    /// rather than matching a floating icon to a stone.
    /// </para>
    /// <para>
    /// <b>Why the grove is dark here.</b> The page used to stand on the hub's daylight
    /// backdrop, on the argument that the streak is part of the game rather than a side
    /// room. The argument was right and the conclusion was wrong: sharing a backdrop with
    /// the two screens either side of it made this one read as a variant of them rather
    /// than as a place. It is the same islands — literally the hub's art — at night, which
    /// keeps the continuity the old choice was protecting while giving the one screen
    /// about nights kept a sky with a moon in it. See <c>Bg/streak_*</c>.
    /// </para>
    /// <para>
    /// <b>Why rewards are collected by hand.</b> A rung used to be applied the moment a run
    /// ended, which meant the player's reward for a six-day streak arrived as a number
    /// changing behind a defeat screen. Nothing about that is a reward. Now the night sits
    /// on the board glowing until it is tapped — see <see cref="DailyStreak.Collect"/> —
    /// which costs a save field and buys the only moment this feature ever had.
    /// </para>
    /// </summary>
    public sealed class StreakScreen : View
    {
        public override string Track => "mus_menu";

        // ------------------------------------------------------------ geometry
        /// <summary>
        /// A night tile, and how many sit in a row.
        ///
        /// Four across is what fits at a size a thumb can read on a phone: the tile has to
        /// carry a caption, a reward glyph and an amount, and five across takes the glyph
        /// below the size where a heart and a boost are told apart at a glance.
        /// </summary>
        const float TileW = 240f;
        const float TileH = 272f;
        const float Gap = 24f;
        const int PerRow = 4;

        /// <summary>Width of the run of nights, which is also the count-up bar's width.</summary>
        const float BarW = 460f;
        const float BarH = 36f;

        /// <summary>Where the board's band begins, below the count and any prompt.</summary>
        float BoardTop => _pending > 0 ? 616f : 566f;

        // --------------------------------------------------------------- state
        StreakTable _ladder;
        int _rungs;
        int _days;
        int _pending;
        bool _atRisk;
        bool _playedToday;

        /// <summary>
        /// The absolute night the board's first tile stands for.
        ///
        /// One for a first week, eight for a second, and so on — the board is a window onto
        /// a run of nights that has no end. Everything on a tile is derived from the
        /// absolute night rather than from its position, which is what lets the window move
        /// without a single reward or caption meaning something different.
        /// </summary>
        int _first;

        /// <summary>Which lap of the ladder the board is showing. One for the first.</summary>
        int _cycle;

        Text _state;
        RectTransform _fill;
        RectTransform _countHost;
        Text _number;
        float _tick;

        /// <summary>
        /// True while a collection is playing out. The page is mid-animation and describes
        /// state it is in the middle of changing, so a rebuild underneath it would destroy
        /// the tiles the sequence is still animating. See <see cref="OnChanged"/>.
        /// </summary>
        bool _collecting;

        readonly List<NightTile> _tiles = new List<NightTile>();

        /// <summary>How a single night reads. Derived on every tile from the same facts.</summary>
        enum Night { Kept, Ready, Today, Tonight, Ahead }

        /// <summary>
        /// The pieces of one night that a collection has to reach back into.
        ///
        /// Held rather than re-found because the tap animation retints the skin, stamps a
        /// seal and throws the glyph across the screen, and hunting for those by name
        /// afterwards is how an animation ends up drawing the wrong tile.
        /// </summary>
        sealed class NightTile
        {
            public int Rung;
            public RectTransform Root;
            public Image Skin;
            public Image Icon;
            public RectTransform Aura;
            public Btn Tap;
            public ChestDrop Drop;
        }

        protected override void Build()
        {
            _ladder = DailyStreak.Ladder;
            _rungs = Mathf.Max(1, _ladder.Length);
            _days = DailyStreak.Days;
            _pending = DailyStreak.Pending;
            _atRisk = DailyStreak.AtRisk;
            _playedToday = DailyStreak.PlayedToday;
            _first = DailyStreak.BoardFirstNight;
            _cycle = DailyStreak.CycleOf(_first, _rungs);
            _tiles.Clear();

            // The grove after dark: the hub's own islands under a moon. The dim is low
            // because the art is already night — the shared shade exists to sit UI on a
            // bright backdrop, and stacking it on this one would only make it muddy.
            Scenery.Layered(Content, "streak", .10f);
            Fireflies.Spawn(Content, 26, new Color(1f, .90f, .62f), 5f, 22f);

            BuildHeader();
            BuildCount();
            BuildPrompt();
            BuildBoard();
            BuildFooter();

            NavBar.Build(Content, NavBar.Tab.Home);
        }

        void OnEnable() { DailyStreak.Changed += OnChanged; }
        void OnDisable() { DailyStreak.Changed -= OnChanged; }

        /// <summary>
        /// Redraws on any change the page did not make itself.
        ///
        /// A collection raises this too, and must not be allowed to act on it: the page is
        /// halfway through an animation that is already showing the new state, and tearing
        /// the board down under it would leave the sequence running against destroyed
        /// tiles. The chrome that a collection does change is refreshed by hand when the
        /// sequence lands — see <see cref="RefreshChrome"/>.
        /// </summary>
        void OnChanged()
        {
            if (_collecting) return;
            Rebuild();
        }

        /// <summary>
        /// Ticks the countdown, and rebuilds the page on the day it runs out.
        ///
        /// This is the one screen a player might sit on late at night watching the clock. A
        /// stale number reads as the game having already taken the streak away, and a page
        /// still showing "lost in 0h 00m" ten minutes after midnight is worse than that —
        /// it is wrong. When the day turns over the whole board is rebuilt from the same
        /// facts it was built from, which is the only version of this that cannot disagree
        /// with itself.
        ///
        /// Polled every second whatever the state, not only while the streak is at risk.
        /// Midnight passes just as often for a player who has already played today, and
        /// after it their board is a day stale — the night they kept is now the night
        /// before, and the reward waiting on it is drawn on the wrong tile.
        /// </summary>
        void Update()
        {
            if (_collecting) return;

            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;

            if (DailyStreak.Days != _days || DailyStreak.PlayedToday != _playedToday)
            {
                Rebuild();
                return;
            }

            if (_atRisk && _state) _state.text = StateLine();
        }

        /// <summary>
        /// Draws the page again from scratch.
        ///
        /// The old children are hidden before they are destroyed: <c>Destroy</c> is deferred
        /// to the end of the frame, so without this the outgoing board draws over the
        /// incoming one for one frame.
        /// </summary>
        void Rebuild()
        {
            if (Content == null) return;

            for (int i = Content.childCount - 1; i >= 0; i--)
            {
                var child = Content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            _state = null;
            _fill = null;
            _countHost = null;
            _number = null;
            Build();
        }

        // --------------------------------------------------------------- count
        /// <summary>
        /// The count: a flame with the number in it, and how far along the ladder that is.
        ///
        /// <para>
        /// Floating rather than sitting on a card. A plate here would be the bottom third of
        /// the reason this page used to look like furniture, and the pieces do not need one:
        /// the number carries an outline and a drop shadow, the flame carries its own light,
        /// and the bar has a moulded track. See <c>NavBar</c> for the same argument at
        /// length.
        /// </para>
        /// <para>
        /// It is also where a collected reward flies to, which is why the host is kept.
        /// A prize has to land somewhere the eye is already going, and on this page the
        /// only thing the player came to look at is the number.
        /// </para>
        /// </summary>
        void BuildCount()
        {
            var host = UIKit.Box("Count", Content, new Vector2(1000f, 300f),
                                 new Vector2(.5f, 1f), new Vector2(0f, -390f));
            _countHost = host;

            // --- the flame, with the number standing in it
            var seat = UIKit.Img("Seat", host, Art.Glow(128, 1.7f), new Color(.02f, .05f, .12f, .46f),
                                 new Vector2(320f, 130f), new Vector2(.5f, .5f), new Vector2(-268f, -108f));
            seat.transform.SetAsFirstSibling();

            UIKit.Halo(host, new Color(1f, .62f, .22f), 520f, _days > 0 ? .38f : .12f,
                       new Vector2(-268f, -20f));

            var flame = UIKit.Img("Fire", host, null, Color.white,
                                  new Vector2(280f, 280f), new Vector2(.5f, .5f), new Vector2(-268f, -14f));
            flame.preserveAspect = true;

            // Animated only while the streak is alive, exactly as the hub's chip does it. A
            // flame that flickers for a player with no streak is decoration claiming to be
            // a state.
            if (_days > 0)
            {
                Flipbook.Attach(flame, "Ui/Flame", 9f);
            }
            else
            {
                var frames = Art.Frames("Ui/Flame");
                flame.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
                flame.color = new Color(1f, 1f, 1f, .45f);
            }

            // Sat down into the flame rather than on top of it. The sprite's body fills the
            // lower two thirds of its box, so a number centred on the box floats above the
            // fire instead of standing in it.
            // Shrinkable because this number has no ceiling. The ladder is seven nights long
            // and the streak it counts is not — a player three months in reads "94" here,
            // and the one the whole feature is built for reads more than that.
            _number = UIKit.Shrinkable(
                UIKit.Titled("N", host, _days > 0 ? "0" : "—", 140,
                             _days > 0 ? Pal.Cream : new Color(1f, .96f, .86f, .45f),
                             TextAnchor.MiddleCenter, new Vector2(420f, 170f),
                             new Vector2(.5f, .5f), new Vector2(-268f, -22f), 8f, 8f), 74);

            // Counted up rather than printed. It is the one number on the page a player came
            // to see, and a number that arrives is worth a third of a second.
            var number = _number;
            if (_days > 0)
                Tween.Value(0f, _days, .7f, v => { if (number) number.text = Mathf.RoundToInt(v).ToString(); },
                            Ease.OutCubic, number).Delay(.18f);

            // --- what it is, and how far along the ladder
            UIKit.Shrinkable(
                UIKit.Titled("Cap", host, Loc.Get(_days == 1 ? "ui.streak.day" : "ui.streak.days")
                                              .ToUpperInvariant(), 50, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(600f, 70f),
                             new Vector2(.5f, .5f), new Vector2(110f, 58f), 6f, 6f), 32);

            // Tinted down rather than shipped dark: the track and the fill are the same
            // shape from the same sheet, and a track that reads as "not yet" has to be
            // darker than the bar running along it.
            var track = UIKit.Img("Track", host, Art.S("Ui/bar_track"), new Color(.22f, .27f, .38f, .95f),
                                  new Vector2(BarW, BarH), new Vector2(.5f, .5f), new Vector2(110f, -18f));

            _fill = (RectTransform)UIKit.Img("Fill", track.transform, Art.S("Ui/bar_fill"), Color.white)
                                        .transform;
            // Left-pivoted so it grows from the start of the track rather than from its
            // middle, and inset so the track's own rim still shows at a full bar.
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, .5f);
            _fill.sizeDelta = new Vector2(0f, -8f);
            _fill.anchoredPosition = new Vector2(4f, 0f);

            // The bar measures the lap on the board, never the whole streak. A streak has no
            // end — the flame above counts on for ever — so a bar against it would be a bar
            // that can never fill, which is the opposite of what a bar is for.
            int done = Mathf.Clamp(_days - _first + 1, 0, _rungs);

            float target = (BarW - 8f) * Mathf.Clamp01(done / (float)_rungs);
            Tween.Value(0f, target, .8f, w => { if (_fill) _fill.sizeDelta = new Vector2(w, -8f); },
                        Ease.OutCubic, this).Delay(.28f);

            string lap = _cycle > 1
                ? Loc.Format("ui.streak.progress_week", done, _rungs, _cycle)
                : Loc.Format("ui.streak.progress", done, _rungs);

            UIKit.Shrinkable(
                UIKit.Titled("Of", host, lap.ToUpperInvariant(), 28, new Color(1f, .92f, .78f, .90f),
                             TextAnchor.MiddleCenter, new Vector2(520f, 34f),
                             new Vector2(.5f, .5f), new Vector2(110f, -76f), 4f, 3f), 18);

            host.localScale = Vector3.zero;
            Tween.Pop(host, 0f, .55f, .08f);
        }

        // -------------------------------------------------------------- prompt
        /// <summary>
        /// The line that says something is waiting, directly above the tiles that are.
        ///
        /// It exists rather than being folded into the footer because the two say different
        /// kinds of thing. The footer describes the streak; this asks for a tap, and an
        /// instruction belongs next to the thing it is about — a player who has to look at
        /// the bottom of the screen to learn that the tiles at the top are tappable has
        /// already been failed by the layout.
        /// </summary>
        void BuildPrompt()
        {
            if (_pending <= 0) return;

            var pill = UIKit.Img("Prompt", Content, Art.Round(26), new Color(.20f, .13f, .03f, .80f),
                                 new Vector2(740f, 78f), new Vector2(.5f, 1f), new Vector2(0f, -552f));

            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(26, 3f), Pal.A(Pal.Gold, .70f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            UIKit.Halo(pill.transform, Pal.Gold, 820f, .16f);

            UIKit.Shrinkable(
                UIKit.Titled("Text", pill.transform,
                             _pending == 1
                                 ? Loc.Get("ui.streak.waiting_one")
                                 : Loc.Format("ui.streak.waiting_many", _pending),
                             30, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(690f, 62f), new Vector2(.5f, .5f), Vector2.zero, 4f, 3f, true), 18);

            // Breathing rather than static: it is the one element on the page asking for
            // something, and it has to still be asking after the entrance has settled.
            pill.transform.localScale = Vector3.zero;
            Tween.Pop(pill.transform, 0f, .5f, .30f)
                 .OnDone(() => { if (pill) Tween.Breathe(pill.transform, .022f, 2.2f); });
        }

        // --------------------------------------------------------------- board
        /// <summary>
        /// Every rung of the ladder, four to a row.
        ///
        /// <para>
        /// It scrolls only when it has to. A ladder is content — <see cref="StreakRules.MaxRungs"/>
        /// allows thirty — so a board sized to the shipped seven is a code change waiting
        /// on a content change, which is the failure invariant 4 exists to prevent. But a
        /// <c>ScrollRect</c> that is always there is not free: it clamps its content to the
        /// top of the viewport, so the seven that do fit would sit hard against the count
        /// instead of centred in the space they have.
        /// </para>
        /// </summary>
        void BuildBoard()
        {
            var band = UIKit.Node("Board", Content);
            UIKit.StretchTo(band, 0f, FooterTop, 0f, BoardTop);
            band.gameObject.AddComponent<RectMask2D>();

            int rows = Mathf.CeilToInt(_rungs / (float)PerRow);
            float boardH = rows * (TileH + Gap) - Gap;

            // A ladder that fits sits in the middle of its band rather than hard against the
            // top of it. The band is whatever is left between the count and the footer, and
            // that is a different number on a 16:9 phone and a 20:9 one — measured from the
            // canvas rather than from a reference height, or the shipped seven-night ladder
            // is centred on exactly one device.
            float slack = Mathf.Max(0f, (Flow.Size.y - BoardTop - FooterTop) - boardH);

            var nights = UIKit.Node("Nights", band);
            nights.anchorMin = new Vector2(0f, 1f);
            nights.anchorMax = new Vector2(1f, 1f);
            nights.pivot = new Vector2(.5f, 1f);
            nights.sizeDelta = new Vector2(0f, boardH);
            nights.anchoredPosition = new Vector2(0f, -slack * .5f);

            for (int i = 0; i < _rungs; i++)
            {
                int row = i / PerRow;
                int col = i % PerRow;
                int inRow = Mathf.Min(PerRow, _rungs - row * PerRow);

                float x = (col - (inRow - 1) * .5f) * (TileW + Gap);
                float y = -(row * (TileH + Gap) + TileH * .5f);

                Tile(nights, _first + i, new Vector2(x, y), i);
            }

            if (slack > 0f) return;

            // Invisible, but drags have to land on something: every Image this UI builds is
            // raycast-transparent, so without a catcher a long ladder could not be scrolled.
            var catcher = band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var scroll = band.gameObject.AddComponent<ScrollRect>();
            scroll.content = nights;
            scroll.viewport = band;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;
        }

        /// <summary>
        /// One night: what day it is, what it pays, and whether it has been taken.
        ///
        /// <para>
        /// The skin carries the state rather than a badge doing it — green for a night
        /// banked, gold for one with a reward still on it, teal for the night on offer,
        /// dark for a night still ahead — because a player scanning the board is reading
        /// colour, not glyphs. The seal is confirmation on top of that, not the signal.
        /// </para>
        /// <para>
        /// Every tile carries the moon at that night's phase, faintly, behind the reward.
        /// It is derived from the rung's place on the ladder rather than drawn, so a ladder
        /// retuned to ten nights gets ten phases and the last one is still full — the
        /// alternative is ten sprites nobody remembers to add.
        /// </para>
        /// </summary>
        void Tile(Transform parent, int day, Vector2 at, int index)
        {
            var state = StateOf(day);
            bool ready = state == Night.Ready;
            bool banked = state == Night.Kept || state == Night.Today;

            // The last night of the lap on the board, not the seventh night ever. On a
            // second week the crest belongs on night fourteen.
            bool last = day == _first + _rungs - 1;
            int place = day - _first + 1;              // where this night sits in its lap

            var tile = UIKit.Box("N" + day, parent, new Vector2(TileW, TileH), new Vector2(.5f, 1f), at);

            var entry = new NightTile { Rung = day, Root = tile, Drop = _ladder.Rung(day).AsDrop() };
            _tiles.Add(entry);

            // What a plate used to do, scoped to the element: the grove behind runs from
            // moonlit rock to open night sky, so a flat tile would vanish over half of it.
            var seat = UIKit.Img("Seat", tile, Art.Glow(128, 1.7f), new Color(.01f, .04f, .10f, .48f),
                                 new Vector2(TileW * 1.10f, TileH * .40f), new Vector2(.5f, .5f),
                                 new Vector2(0f, -TileH * .44f));
            seat.transform.SetAsFirstSibling();

            if (ready) entry.Aura = Aura(tile);
            else if (state == Night.Tonight) UIKit.Halo(tile, Pal.Aqua, TileW * 1.9f, .34f);
            else if (last && !banked) UIKit.Halo(tile, Pal.Gold, TileW * 1.8f, .28f);

            entry.Skin = UIKit.Img("Skin", tile, Art.S(Skin(state, last)), Color.white);
            UIKit.StretchTo((RectTransform)entry.Skin.transform, 0, 0, 0, 0);

            // The jelly art's lit face sits above the sprite's middle, so everything printed
            // on a tile rides up by the same fraction the rest of this UI uses.
            float lift = TileH * UIKit.SquareFaceLift;

            // In the corner rather than behind the glyph, where it was invisible: a
            // watermark under a bright jelly face at any alpha a jelly face will tolerate
            // is not a detail, it is a wasted draw call.
            UIKit.Img("Moon", tile, Art.Moon(64, place / (float)_rungs),
                      new Color(1f, .97f, .86f, ready ? .70f : banked ? .46f : .34f),
                      new Vector2(40f, 40f), new Vector2(.5f, .5f),
                      new Vector2(-TileW * .36f, -TileH * .30f + lift));

            UIKit.Shrinkable(
                UIKit.Titled("Cap", tile, Caption(state, day), 26,
                             banked || ready || state == Night.Tonight
                                 ? Pal.Cream : new Color(1f, .97f, .90f, .82f),
                             TextAnchor.MiddleCenter, new Vector2(TileW - 44f, 34f),
                             new Vector2(.5f, .5f), new Vector2(0f, TileH * .30f + lift), 4f, 3f), 16);

            Reward(entry, tile, day, state, lift);

            // The last rung wears a crest, in the corner rather than beside the caption: the
            // caption is centred and short, so the corner is the one place on a tile that is
            // free whatever the ladder is tuned to and whatever language it is read in.
            if (last)
            {
                var crest = UIKit.Img("Crest", tile, Art.S("Ui/crest_gold"), Color.white,
                                      new Vector2(54f, 54f), new Vector2(.5f, .5f),
                                      new Vector2(-TileW * .38f, TileH * .38f));
                crest.preserveAspect = true;
            }

            if (banked) Seal(tile, false);

            if (ready)
            {
                // The whole tile is the button. A small "collect" chip inside it would be a
                // smaller target for the same action, and there is nothing else on a night
                // to tap by mistake.
                entry.Tap = UIKit.Button("Tap", tile, Art.Pixel, new Vector2(TileW, TileH),
                                         new Vector2(.5f, .5f), Vector2.zero, () => Take(entry));
                entry.Tap.GetComponent<Image>().color = new Color(1, 1, 1, 0);
                entry.Tap.ClickSfx = null;
                entry.Tap.PressScale = .94f;
            }

            tile.localScale = Vector3.zero;
            var pop = Tween.Pop(tile, 0f, .48f, .22f + index * .05f);

            if (ready)
                pop.OnDone(() =>
                {
                    if (!tile) return;
                    Tween.Breathe(tile, .035f, 1.5f);
                    Sheen.Attach(tile, 2.6f);
                });
            else if (state == Night.Tonight)
                pop.OnDone(() => { if (tile) Tween.Breathe(tile, .028f, 1.9f); });
            else if (last && !banked)
                pop.OnDone(() => { if (tile) Sheen.Attach(tile, 3.6f); });
        }

        /// <summary>
        /// What a night with a reward still on it wears: a turning fan of light and a ring
        /// that keeps opening out of it.
        ///
        /// <para>
        /// Both are generated shapes rather than art — see <see cref="Art.Rays"/> — so the
        /// effect costs no addresses and tints to whatever the palette says. They live on
        /// their own node so that collecting can throw the lot away in one call without
        /// touching the tile underneath.
        /// </para>
        /// </summary>
        RectTransform Aura(RectTransform tile)
        {
            // Built between the seat shadow and the skin, so it reads as light coming out
            // from behind the tile rather than as a decal stuck on the front of it.
            var host = UIKit.Box("Aura", tile, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);

            UIKit.Halo(host, Pal.Gold, TileW * 2.1f, .30f);

            var rays = UIKit.Img("Rays", host, Art.Rays(256, 14), Pal.A(Pal.Sun, .26f),
                                 Vector2.one * TileW * 1.95f, new Vector2(.5f, .5f), Vector2.zero);
            var rrt = (RectTransform)rays.transform;
            Tween.Run(14f, Ease.Linear,
                      t => { if (rrt) rrt.localRotation = Quaternion.Euler(0f, 0f, t * 360f); },
                      rays).Loop(-1, false);

            var ring = UIKit.Img("Ring", host, Art.Ring(160, 9f), Pal.A(Pal.Gold, 0f),
                                 Vector2.one * TileW, new Vector2(.5f, .5f), Vector2.zero);
            var rt = (RectTransform)ring.transform;
            Tween.Run(1.6f, Ease.OutCubic, t =>
            {
                if (!ring) return;
                float k = Mathf.Repeat(t, 1f);
                rt.localScale = Vector3.one * Mathf.Lerp(.82f, 1.55f, k);
                ring.color = Pal.A(Pal.Gold, .62f * (1f - k) * Mathf.Clamp01(k * 6f));
            }, ring).Loop(-1, false);

            return host;
        }

        /// <summary>The wax seal a banked night carries.</summary>
        void Seal(RectTransform tile, bool animated)
        {
            var seal = UIKit.Img("Seal", tile, Art.S("Ui/seal_gold"), Color.white,
                                 new Vector2(84f, 84f), new Vector2(.5f, .5f),
                                 new Vector2(TileW * .34f, TileH * .34f));
            seal.preserveAspect = true;

            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Pal.Cream,
                                 new Vector2(42f, 42f), new Vector2(.5f, .5f), Vector2.zero);
            tick.preserveAspect = true;

            if (!animated) return;

            // Stamped rather than faded in. A seal that arrives from above and overshoots
            // reads as something being pressed onto the tile; one that appears reads as a
            // sprite being switched on.
            seal.transform.localScale = Vector3.one * 2.6f;
            Tween.Scale(seal.transform, 1f, .34f, Ease.OutBack)
                 .OnDone(() => { if (seal) Tween.Punch(tile, .10f, .26f); });
        }

        /// <summary>
        /// What the night pays, printed on its own face.
        ///
        /// A rung that pays nothing is not left blank: day one is the night the flame
        /// lights, and saying so is the difference between "this day is worth nothing" and
        /// "this day is where it starts".
        /// </summary>
        void Reward(NightTile entry, Transform tile, int day, Night state, float lift)
        {
            bool bright = state != Night.Ahead;
            var drop = entry.Drop;

            if (!drop.IsValid)
            {
                var spark = UIKit.Img("I", tile, Art.S("Ui/ic_star"), Pal.A(Pal.Cream, bright ? 1f : .82f),
                                      new Vector2(76f, 76f), new Vector2(.5f, .5f), new Vector2(0f, lift - 6f));
                spark.preserveAspect = true;
                entry.Icon = spark;

                UIKit.Shrinkable(
                    UIKit.Titled("A", tile, Loc.Get("ui.streak.rung_none"), 24, Pal.Cream,
                                 TextAnchor.MiddleCenter, new Vector2(TileW - 34f, 56f),
                                 new Vector2(.5f, .5f), new Vector2(0f, -TileH * .28f + lift), 4f, 3f, true), 15);
                return;
            }

            // Never tinted: every reward glyph carries its own colour now that the boost has
            // a real one of its own. See RewardArt.
            var icon = UIKit.Img("I", tile, RewardArt.Icon(drop.Kind),
                                 new Color(1f, 1f, 1f, bright ? 1f : .82f),
                                 new Vector2(92f, 92f), new Vector2(.5f, .5f), new Vector2(0f, lift - 4f));
            icon.preserveAspect = true;
            entry.Icon = icon;

            // Credits have no sprite — they are the spinning coin — so the glyph is finished
            // here rather than by Icon. Without this the ladder's credit night draws as a
            // white square, which is what an Image with no sprite actually is.
            RewardArt.Glyph(icon, drop.Kind, 10f);

            // A reward still on the tile bobs. It is the difference between a picture of a
            // prize and a prize sitting there waiting to be picked up.
            if (state == Night.Ready)
                Tween.Bob((RectTransform)icon.transform, 7f, 1.5f, day * .6f);

            UIKit.Titled("A", tile, RewardArt.Amount(drop), 34,
                         bright ? Pal.Cream : new Color(1f, .97f, .90f, .88f),
                         TextAnchor.MiddleCenter, new Vector2(TileW - 34f, 42f),
                         new Vector2(.5f, .5f), new Vector2(0f, -TileH * .28f + lift), 4f, 3f);
        }

        // ------------------------------------------------------------ collecting
        /// <summary>
        /// Takes a night, and plays what that is worth.
        ///
        /// <para>
        /// The grant happens first and the animation reports it, exactly as the chest
        /// overlay does: a player who kills the app mid-burst has still collected the
        /// night, and a reward that depended on an animation finishing would be a reward a
        /// slow phone could lose.
        /// </para>
        /// <para>
        /// Tapping a night can sweep earlier ones with it — see <see cref="DailyStreak.Collect"/>
        /// — so the tiles that were waiting are gathered <em>before</em> the call and each
        /// one is then played a beat after the last. The alternative, animating only the
        /// tile that was tapped, would pay two nights and show one.
        /// </para>
        /// </summary>
        void Take(NightTile tapped)
        {
            if (_collecting || tapped == null || tapped.Root == null) return;
            if (!DailyStreak.IsCollectable(tapped.Rung)) return;

            var taking = new List<NightTile>();
            foreach (var t in _tiles)
                if (t.Rung <= tapped.Rung && DailyStreak.IsCollectable(t.Rung)) taking.Add(t);

            if (taking.Count == 0) return;

            _collecting = true;

            // Switched off rather than marked non-interactable: Btn paints a disabled
            // element grey, and these are transparent hit boxes over the tile art.
            foreach (var t in taking) if (t.Tap) t.Tap.gameObject.SetActive(false);

            DailyStreak.Collect(tapped.Rung);


            var cue = new Cue(this);
            for (int i = 0; i < taking.Count; i++)
            {
                var t = taking[i];
                cue.Then(i == 0 ? 0f : .30f, () => Payout(t, taking.Count));
            }

            cue.Then(.70f, () =>
            {
                _collecting = false;
                _pending = DailyStreak.Pending;

                // Taking the last night of a lap moves the board on to the next one, which
                // is a different set of nights rather than different words on the same
                // ones — the only case here that a redraw is the honest answer to.
                if (DailyStreak.BoardFirstNight != _first) { Rebuild(); return; }

                RefreshChrome();
            });
        }

        /// <summary>One night's worth of noise: the pop, the throw and the seal.</summary>
        void Payout(NightTile t, int total)
        {
            if (t == null || t.Root == null) return;

            // On the tap, not on the landing. The reward flies for the better part of a
            // second before it arrives, so a sound on the arrival leaves the tap itself
            // silent - and `Throw` returns early when a reward has no glyph to fly, so
            // some collects made no sound at all. Feedback belongs on the action.
            Audio.Sfx("collect", .6f);
            Tween.KillChannel(t.Root, "breathe");
            Tween.Punch(t.Root, .22f, .38f);

            // The fan goes out with a flare rather than a fade: it was the invitation, and
            // the invitation has been accepted.
            if (t.Aura)
            {
                var aura = t.Aura;
                var fade = UIKit.Group(aura);
                Tween.Run(.34f, Ease.OutQuint, k =>
                {
                    if (!aura) return;
                    aura.localScale = Vector3.one * Mathf.Lerp(1f, 1.7f, k);
                    fade.alpha = 1f - k;
                }, aura).OnDone(() => { if (aura) Destroy(aura.gameObject); });
                t.Aura = null;
            }

            var tint = t.Drop.IsValid ? RewardArt.Tint(t.Drop.Kind) : Pal.Gold;
            Burst.Sparks(t.Root, Vector2.zero, tint, 22, 380f, 30f, .72f);
            Flow.Flash(Pal.A(tint, 1f), .10f, .34f);

            if (t.Skin) t.Skin.sprite = Art.S("Ui/sq_green");

            Throw(t, tint);
            Seal(t.Root, true);

            if (t.Tap) Destroy(t.Tap.gameObject);
        }

        /// <summary>
        /// Throws the reward's glyph across the page to the count, and lands a line above it.
        ///
        /// <para>
        /// The arc is what makes it read as a thing moving rather than a sprite being
        /// tweened: a straight line between two points on a phone screen is short enough
        /// that the eye reads it as a jump cut. The lift is a fraction of the distance, so
        /// a tile in the top row and one three rows down both arc by the same amount
        /// relative to how far they have to travel.
        /// </para>
        /// </summary>
        void Throw(NightTile t, Color tint)
        {
            if (t.Icon == null || _countHost == null || Content == null) return;

            Vector2 from = Content.InverseTransformPoint(t.Icon.transform.position);
            Vector2 count = Content.InverseTransformPoint(_countHost.position);
            Vector2 to = count + new Vector2(-268f, -14f);   // the flame, inside the count block

            var flyer = UIKit.Img("Flyer", Content, t.Icon.sprite, Color.white,
                                  new Vector2(92f, 92f), new Vector2(.5f, .5f), from);
            flyer.preserveAspect = true;
            var trail = UIKit.Img("Trail", flyer.transform, Art.Glow(128, 1.9f), Pal.A(tint, .55f),
                                  new Vector2(190f, 190f), new Vector2(.5f, .5f), Vector2.zero);
            trail.transform.SetAsFirstSibling();

            var frt = (RectTransform)flyer.transform;
            float lift = Mathf.Min(280f, Vector2.Distance(from, to) * .34f);

            Tween.Run(.62f, Ease.InOutCubic, k =>
            {
                if (!frt) return;
                var p = Vector2.LerpUnclamped(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * lift;
                frt.anchoredPosition = p;
                frt.localScale = Vector3.one * Mathf.Lerp(1.25f, .55f, k);
                frt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, -24f, k));
            }, flyer).Delay(.10f).OnDone(() =>
            {
                if (flyer) Destroy(flyer.gameObject);
                Land(t, to, tint);
            });

            // Hidden rather than destroyed: the tile keeps its layout, and a reward that
            // vanishes from the face it was printed on is the point.
            if (t.Icon) t.Icon.enabled = false;
        }

        /// <summary>The arrival: a bang on the count and the amount rising off it.</summary>
        void Land(NightTile t, Vector2 at, Color tint)
        {
            if (Content == null) return;

            // Silent: the tap already spoke. See the note where it does.
            Burst.Sparks(Content, at, tint, 16, 260f, 26f, .55f);

            if (_countHost) Tween.Punch(_countHost, .12f, .34f);
            if (_number) Tween.Punch(_number.transform, .22f, .40f);

            if (!t.Drop.IsValid) return;

            var line = UIKit.Titled("Won", Content,
                                    RewardArt.Amount(t.Drop) + " " + RewardArt.Name(t.Drop.Kind),
                                    46, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(560f, 64f), new Vector2(.5f, .5f), at + new Vector2(0f, 40f),
                                    5f, 5f);
            UIKit.Shrinkable(line, 26);

            var lrt = (RectTransform)line.transform;
            var start = lrt.anchoredPosition;
            Tween.Run(1.25f, Ease.OutCubic, k =>
            {
                if (!line) return;
                lrt.anchoredPosition = start + new Vector2(0f, 110f * k);
                line.color = Pal.A(Pal.Cream, k < .18f ? k / .18f : 1f - (k - .18f) / .82f);
            }, line).OnDone(() => { if (line) Destroy(line.gameObject); });
        }

        /// <summary>
        /// Re-texts the parts a collection changes, without rebuilding the board.
        ///
        /// A full redraw here would pop every tile in again half a second after the player
        /// tapped one, which reads as the page having been reset rather than as a reward
        /// having been taken. Nothing structural has moved — the same nights are on the
        /// same ladder — so only the words need to catch up.
        /// </summary>
        void RefreshChrome()
        {
            if (_state) _state.text = StateLine();

            var prompt = Content ? Content.Find("Prompt") : null;
            if (prompt == null) return;

            if (_pending <= 0)
            {
                var go = prompt.gameObject;
                Tween.Scale(prompt, 0f, .28f, Ease.InBack).OnDone(() => { if (go) Destroy(go); });
                return;
            }

            var text = prompt.Find("Text") ? prompt.Find("Text").GetComponent<Text>() : null;
            if (text) text.text = _pending == 1
                ? Loc.Get("ui.streak.waiting_one")
                : Loc.Format("ui.streak.waiting_many", _pending);
        }

        // ------------------------------------------------------------- reading
        /// <summary>
        /// How a night reads, from the stored dates and nothing else.
        ///
        /// Past the end of the ladder every rung is kept and none is on offer, which is
        /// correct: the ladder repeats, and the footer is where that is said.
        /// </summary>
        Night StateOf(int day)
        {
            if (DailyStreak.IsCollectable(day)) return Night.Ready;
            if (_playedToday && day == _days) return Night.Today;
            if (!_playedToday && day == _days + 1) return Night.Tonight;
            return day <= _days ? Night.Kept : Night.Ahead;
        }

        /// <summary>
        /// Written out per state, never composed: the build gate scans the source for
        /// key-shaped literals and a concatenated key is invisible to it.
        /// </summary>
        string Caption(Night state, int day)
        {
            switch (state)
            {
                case Night.Ready: return Loc.Get("ui.streak.collect").ToUpperInvariant();
                case Night.Today: return Loc.Get("ui.streak.done_today").ToUpperInvariant();
                case Night.Tonight: return Loc.Get("ui.streak.tonight").ToUpperInvariant();
                default: return Loc.Format("ui.streak.day_n", day).ToUpperInvariant();
            }
        }

        /// <summary>
        /// Which jelly a night wears.
        ///
        /// <b>Gold means "there is something here to take", and nothing else.</b> The last
        /// rung used to wear gold too, as the milestone — which was harmless while nothing
        /// was tappable and became a trap the moment something was: a player looking at a
        /// gold day seven and a gold collectable day four cannot tell which one is asking
        /// for a tap. The milestone keeps its crest, its halo and its sheen, which say
        /// "this is the big one" without borrowing the one colour that now means "act".
        /// </summary>
        static string Skin(Night state, bool last)
        {
            switch (state)
            {
                case Night.Ready: return "Ui/sq_orange";
                case Night.Kept:
                case Night.Today: return "Ui/sq_green";
                case Night.Tonight: return "Ui/sq_aqua";
                default: return "Ui/" + Skins.Resting;
            }
        }

        // --------------------------------------------------------------- chrome
        void BuildHeader()
        {
            UIKit.IconButton("Back", Content, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<HomeScreen>());

            // The rules the board cannot draw: how a night is earned, what a boost does,
            // and what happens at the end of the ladder. See StreakInfoOverlay.
            UIKit.IconButton("Info", Content, Skins.Aside, "ic_info", new Vector2(118f, 118f),
                             new Vector2(1f, 1f), new Vector2(-96f, -132f),
                             () => { if (!Flow.HasModal) Flow.Modal<StreakInfoOverlay>(); });

            UIKit.Titled("Title", Content, Loc.Get("ui.streak.title").ToUpperInvariant(), 34,
                         new Color(1f, .88f, .62f, .82f), TextAnchor.MiddleCenter,
                         new Vector2(700f, 46f), new Vector2(.5f, 1f), new Vector2(0f, -128f), 3f, 3f);

            UIKit.Shrinkable(
                UIKit.Titled("Sub", Content, Loc.Get("ui.streak.subtitle"), 24,
                             new Color(.78f, .84f, 1f, .72f), TextAnchor.MiddleCenter,
                             new Vector2(700f, 32f), new Vector2(.5f, 1f), new Vector2(0f, -172f), 3f, 3f), 16);
        }

        /// <summary>
        /// How much room the footer needs above the nav bar, which is what the board's band
        /// is measured against. Derived rather than written twice, so the board cannot
        /// overlap the button on the one state that has one.
        /// </summary>
        float FooterTop => NavBar.Height + (Asking ? 400f : 250f);

        void BuildFooter()
        {
            // The one line that changes while the page is open, so it is a pill rather than
            // loose type: a countdown ticking on bare backdrop reads as a glitch.
            //
            // Wide and two lines tall on purpose. It carries a whole sentence, and the
            // pill it used to live in was sized for a short one — best-fit could not save
            // it, because best-fit does not shrink text that is allowed to overflow
            // sideways. See UIKit.Shrinkable, which is where that was actually wrong.
            _state = UIKit.Shrinkable(
                Scenery.Pill(Content, StateLine(), 30, new Vector2(880f, 116f), new Vector2(.5f, 0f),
                             new Vector2(0f, NavBar.Height + (Asking ? 268f : 122f)),
                             _atRisk ? new Color(.22f, .07f, .08f, .82f) : new Color(.05f, .09f, .18f, .78f)),
                18);
            _state.color = _atRisk ? new Color(1f, .62f, .50f) : Pal.Cream;

            string beyond = _ladder.Rung(_rungs + 1).AsDrop() is var repeat && repeat.IsValid
                ? Loc.Format("ui.streak.beyond", RewardArt.Amount(repeat) + " " + RewardArt.Name(repeat.Kind))
                : Loc.Get("ui.streak.beyond_plain");

            UIKit.Shrinkable(
                UIKit.Titled("Beyond", Content, beyond, 24, new Color(1f, .95f, .84f, .88f),
                             TextAnchor.MiddleCenter, new Vector2(860f, 32f), new Vector2(.5f, 0f),
                             new Vector2(0f, NavBar.Height + (Asking ? 358f : 212f)), 3f, 3f), 16);

            if (!Asking) return;

            var play = UIKit.TextButton("Play", Content, "btn_green", Loc.Get("ui.streak.cta"), 46,
                                        new Vector2(560f, 140f), new Vector2(.5f, 0f),
                                        new Vector2(0f, NavBar.Height + 104f),
                                        () => Flow.Go<LevelsScreen>());
            UIKit.Halo(play.transform, Pal.Mint, 640f, .26f);

            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .6f, .85f).OnDone(() =>
            {
                if (!play) return;
                play.Rehome();
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            });
        }

        string StateLine()
        {
            if (_days <= 0) return Loc.Get("ui.streak.explain_none");

            return DailyStreak.AtRisk
                ? Loc.Format("ui.streak.explain_risk_clock", Profile.Countdown(DailyStreak.SecondsUntilLost))
                : Loc.Format("ui.streak.explain_held", _days);
        }

        /// <summary>
        /// Whether the page has something to ask for. A night already banked does not — an
        /// instruction to do a thing that has been done is worse than no button.
        /// </summary>
        bool Asking => _atRisk || _days <= 0;

        public override bool OnBack() { Flow.Go<HomeScreen>(); return true; }
    }
}
