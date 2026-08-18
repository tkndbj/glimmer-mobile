using System;
using System.Collections;
using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the streak did on the run that just ended: how long it now is, and whether
    /// this run is what extended it.
    ///
    /// Both are needed and neither implies the other. A second run of the evening leaves a
    /// six-day streak at six and extends nothing, and a panel that congratulated the player
    /// every time would be congratulating them for a thing they did an hour ago.
    /// </summary>
    public readonly struct StreakNote
    {
        public readonly int Days;
        public readonly bool Advanced;

        public StreakNote(int days, bool advanced)
        {
            Days = days < 0 ? 0 : days;
            Advanced = advanced;
        }

        /// <summary>Worth a line only when this run moved it. See the type summary.</summary>
        public bool WorthSaying => Advanced && Days > 0;
    }

    public sealed class PlayScreen : View
    {
        /// <summary>Which level to play. Set by whoever opened the screen.</summary>
        public LevelId LevelId;

        public override string Track => "mus_play";

        BoardView _board;
        RectTransform _boardHost;
        Puzzle _puzzle;
        LevelDefinition _def;
        ChapterDefinition _chapter;
        Text _moves, _lamps, _hintCount, _timer;
        StarRow _pips;
        Btn _undo, _hint;
        bool _finished;

        /// <summary>
        /// True once this run has been paid for — that is, once abandoning it costs a heart.
        ///
        /// <para>
        /// Set on the <em>first turn</em>, or after a few seconds of clock, whichever comes
        /// first. Both halves are needed. Waiting only for a turn lets a player study the board
        /// for the whole countdown, back out for free and re-enter knowing the answer, which is
        /// exactly the free planning the countdown's start edge was moved to prevent. Committing
        /// the instant the board appears would charge somebody who opened the wrong glade and
        /// left within a second. Three seconds is longer than a misplaced tap and far shorter
        /// than reading a 6x7 board.
        /// </para>
        /// </summary>
        bool _committed;

        /// <summary>How long the board may be looked at before the run is owed for.</summary>
        const float CommitGraceSeconds = 3f;
        bool _hasColourKey;
        float _startedAt;

        /// <summary>
        /// This run's stopwatch. An instance, never a static, so a second glade cannot
        /// inherit the first one's time — and an accumulator ticked from
        /// <see cref="Update"/> rather than a coroutine or a subscription, so there is
        /// nothing to unwind when the screen dies. See <see cref="RunClock"/>.
        /// </summary>
        readonly RunClock _clock = new RunClock();

        /// <summary>
        /// The authored route, measured on the untouched board.
        ///
        /// <para>
        /// Taken once, here, because <see cref="Puzzle.TurnsToSolution"/> is a live reading
        /// of the board in front of it — asking at the end of a run returns zero, since the
        /// board is by then solved. It survives a restart without being retaken:
        /// <see cref="Puzzle.Reset"/> restores the same start rotations, so the route is a
        /// fact about the glade rather than about the attempt.
        /// </para>
        /// </summary>
        int _route;

        /// <summary>
        /// Whole seconds already painted, so the readout builds one string a second instead
        /// of one a frame. -1 forces the next paint.
        /// </summary>
        int _paintedSeconds = -1;

        public BoardView Board => _board;
        public LevelDefinition Level => _def;

        protected override void Build()
        {
            // The chapter is normally already in hand - the map loaded it to draw the
            // node that was just tapped - so this completes without yielding and the
            // board appears in the same frame it always did. It only ever waits when
            // the player arrived some other way: a deep link, or a "next" that stepped
            // over a chapter boundary.
            StartCoroutine(ResolveThenBuild());
        }

        IEnumerator ResolveThenBuild()
        {
            var chapterId = GameContent.ChapterOf(LevelId);
            if (!chapterId.IsValid)
            {
                Debug.LogError($"[Play] unknown level '{LevelId}', returning to the map");
                yield return BailOut();
                yield break;
            }

            var task = GameContent.ChapterAsync(chapterId);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) Debug.LogException(task.Exception);

            var body = task.Result;
            _def = body?.Find(LevelId);
            _chapter = body?.Definition;

            if (_def == null || _chapter == null)
            {
                Debug.LogError($"[Play] level '{LevelId}' could not be read, returning to the map");
                yield return BailOut();
                yield break;
            }

            if (!PuzzleFactory.TryCreate(_def, out _puzzle, out var errors))
            {
                Debug.LogError($"[Play] level '{LevelId}' is unplayable: {string.Join("; ", errors)}");
                yield return BailOut();
                yield break;
            }

            // Must happen before anything asks for a sprite: it registers which
            // addresses belong to this chapter, and an asset fetched before that
            // registration would be filed as global and never released.
            _ = AssetLibrary.EnsureChapterAsync(body);

            if (!this) yield break;
            BuildResolved();
        }

        void BuildResolved()
        {
            Scenery.Cover(Content, "Bg/" + _def.Presentation.ResolveBackdrop(_chapter), 0f, .22f);
            Fireflies.Spawn(Content, 18, new Color(1f, .97f, .86f), 5f, 18f);

            BuildTopBar();
            BuildStatus();
            BuildBottomBar();

            _boardHost = UIKit.Node("BoardHost", Content);
            _boardHost.offsetMin = new Vector2(26f, 300f);
            _boardHost.offsetMax = new Vector2(-26f, _hasColourKey ? -424f : -350f);

            _board = _boardHost.gameObject.AddComponent<BoardView>();
            _board.OnChanged = Refresh;
            _board.OnSolved = Finish;
            _board.OnDefeated = Defeat;

            // Before a single turn is possible, and before the board view exists to allow one.
            _route = _puzzle.TurnsToSolution;

            // The first run of a glade goes through here rather than through RestartLevel, so
            // it needs arming too — a clock built with the screen has no limit yet, and one
            // that never learned this glade's would leave the opening attempt untimed while
            // every retry after it was not.
            ResetClock();

            _startedAt = Time.unscaledTime;
            PlayerProgress.NoteOpened(_def.Id);
            LevelAnalytics.TrackStarted(_def, PlayerProgress.Record(_def.Id).Clears + 1);

            StartCoroutine(RaiseBoard());
        }

        /// <summary>
        /// Retreats to the map once the incoming transition has finished. Flow ignores
        /// navigation while it is mid-transition, so leaving immediately would strand
        /// the player on an empty screen.
        /// </summary>
        IEnumerator BailOut()
        {
            while (Flow.Busy) yield return null;
            Flow.Go<LevelsScreen>();
        }

        IEnumerator RaiseBoard()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            int guard = 0;
            while (_boardHost.rect.width < 40f && guard++ < 60) yield return null;

            _board.Build(_boardHost, _puzzle,
                         Pal.BoardTheme.From(_def.Presentation.ResolveSlate(_chapter)),
                         _def.Tuning.HintAllowance);
            Refresh();
        }

        // -------------------------------------------------------------- chrome
        void BuildTopBar()
        {
            var bar = UIKit.Box("TopBar", Content, new Vector2(0f, 230f), new Vector2(.5f, 1f), new Vector2(0f, -115f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 230f);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .05f, .08f, .55f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, -40, 0, 0);
            ((RectTransform)shade.transform).localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", bar, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -6f), LeaveToMap);
            UIKit.IconButton("Pause", bar, Skins.Nav, "ic_pause", new Vector2(118f, 118f),
                             new Vector2(1f, .5f), new Vector2(-102f, -6f), Pause);

            UIKit.Titled("Name", bar, Loc.Get(_def.NameKey).ToUpperInvariant(), 52, Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(620f, 62f), new Vector2(.5f, .5f),
                         new Vector2(0f, 12f), 4f, 4f);
            UIKit.Titled("Tag", bar, Loc.Get(_def.TaglineKey), 28, new Color(1f, .94f, .80f, .82f),
                         TextAnchor.MiddleCenter, new Vector2(760f, 44f), new Vector2(.5f, .5f),
                         new Vector2(0f, -38f), 3f, 3f);

            // Under the tagline rather than in the counter row, which has no free width that
            // survives a narrow screen — the two pills are anchored to opposite edges and the
            // star pips hold the middle, so anything wedged between them collides on some
            // aspect ratio nobody tested. This slot is centred, fixed, and clear of both the
            // colour key and the board.
            //
            // It is a countdown now, so it is no longer quiet — it is the second thing on the
            // screen that can end the run, and it colours and punches exactly like the move
            // counter does for the same reason: once it is low the number itself is the
            // tension. An untimed glade still shows the stopwatch, dimmed, as a record the
            // player may care about afterwards.
            _timer = UIKit.Titled("Timer", bar, RunClock.Format(0), 34,
                                  TimerCalm, TextAnchor.MiddleCenter,
                                  new Vector2(320f, 44f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -80f), 3f, 2f);
        }

        void BuildStatus()
        {
            var row = UIKit.Box("Status", Content, new Vector2(0f, 96f), new Vector2(.5f, 1f), new Vector2(0f, -288f));
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
            row.sizeDelta = new Vector2(0f, 96f);

            _moves = Scenery.Pill(row, "0", 40, new Vector2(230f, 84f), new Vector2(0f, .5f),
                                  new Vector2(160f, 0f), null, "ic_restart");
            _lamps = Scenery.Pill(row, "0/0", 40, new Vector2(230f, 84f), new Vector2(1f, .5f),
                                  new Vector2(-160f, 0f), null, "ic_check");
            _pips = StarRow.Create(row, new Vector2(.5f, .5f), Vector2.zero, 62f, 66f, 3);

            BuildColourKey();
        }

        /// <summary>
        /// The blending chart, sitting under the counters.
        ///
        /// Permanent rather than a tip, because it is a lookup and not a lesson: the
        /// rule takes five seconds to explain and a while to internalise, and a player
        /// mid-puzzle wants to check "what does red and blue make" without being taught
        /// anything. A modal cannot answer that; a chart on the wall can.
        ///
        /// Only drawn where it applies. On a single-colour glade it would be three rows
        /// of noise above the board, so a board with one heart colour gets nothing and
        /// keeps the space.
        /// </summary>
        void BuildColourKey()
        {
            if (!NeedsColourKey()) return;

            var strip = UIKit.Box("ColourKey", Content, new Vector2(0f, 64f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -372f));
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.sizeDelta = new Vector2(0f, 64f);

            // the three pairs; every other blend is these repeated
            Recipe(strip, -300f, Energy.R, Energy.G);
            Recipe(strip, 0f, Energy.R, Energy.B);
            Recipe(strip, 300f, Energy.G, Energy.B);

            // the board starts lower when the chart is there, so it is never covered.
            // Recorded rather than applied: BuildStatus runs before the host exists.
            _hasColourKey = true;
        }

        bool NeedsColourKey()
        {
            int first = -1;

            foreach (var cell in _puzzle.C)
            {
                if (cell.kind != Kind.Source) continue;
                if (first < 0) first = cell.colour;
                else if (cell.colour != first) return true;
            }

            return false;
        }

        /// <summary>One "a + b = c" of coloured dots. No words, so nothing to translate.</summary>
        static void Recipe(Transform parent, float x, int a, int b)
        {
            Dot(parent, x - 58f, a);
            Sign(parent, x - 29f, "+");
            Dot(parent, x, b);
            Sign(parent, x + 29f, "=");
            Dot(parent, x + 58f, a | b);
        }

        static void Dot(Transform parent, float x, int energy)
        {
            var colour = Pal.EnergyColour(energy);

            UIKit.Img("Glow", parent, Art.Glow(64, 1.9f), Pal.A(colour, .45f),
                      Vector2.one * 46f, new Vector2(.5f, .5f), new Vector2(x, 0f));
            UIKit.Img("Dot", parent, Art.Disc(64), Pal.Lift(colour, .25f),
                      Vector2.one * 26f, new Vector2(.5f, .5f), new Vector2(x, 0f));
        }

        static void Sign(Transform parent, float x, string glyph)
            => UIKit.Titled("S" + x, parent, glyph, 26, new Color(1f, .96f, .86f, .55f),
                            TextAnchor.MiddleCenter, new Vector2(30f, 30f),
                            new Vector2(.5f, .5f), new Vector2(x, 1f), 0f, 2f);

        void BuildBottomBar()
        {
            var bar = UIKit.Box("BottomBar", Content, new Vector2(0f, 250f), new Vector2(.5f, 0f), new Vector2(0f, 125f));
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f);
            bar.sizeDelta = new Vector2(0f, 250f);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .05f, .08f, .5f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, 0, 0, -40);

            _undo = UIKit.IconButton("Undo", bar, "sq_blue", "ic_undo", new Vector2(150f, 150f),
                                     new Vector2(.5f, .5f), new Vector2(-215f, 10f), () => _board.Undo());
            _hint = UIKit.IconButton("Hint", bar, "sq_orange", "ic_hint", new Vector2(168f, 168f),
                                     new Vector2(.5f, .5f), new Vector2(0f, 10f), UseHint);
            // Routed through RestartLevel rather than straight at the board, so this button
            // and the pause menu's cannot disagree about what a restart resets — the clock
            // was the first thing they would have.
            UIKit.IconButton("Restart", bar, "sq_green", "ic_restart", new Vector2(150f, 150f),
                             new Vector2(.5f, .5f), new Vector2(215f, 10f), RestartLevel);

            var badge = UIKit.Img("Badge", _hint.transform, Art.Disc(64), Pal.Rose,
                                  new Vector2(58f, 58f), new Vector2(1f, 1f), new Vector2(-16f, -16f));
            _hintCount = UIKit.Titled("N", badge.transform, _def.Tuning.HintAllowance.ToString(), 34,
                                      Pal.Cream, TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);

            Caption(bar, "undo", -215f);
            Caption(bar, "hint", 0f);
            Caption(bar, "reset", 215f);

            foreach (Transform c in bar)
            {
                if (c.GetComponent<Btn>() == null) continue;
                c.localScale = Vector3.zero;
                Tween.Pop(c, 0f, .5f, .35f + Mathf.Abs(((RectTransform)c).anchoredPosition.x) * .0006f)
                     .OnDone(() => { var b = c.GetComponent<Btn>(); if (b) b.Rehome(); });
            }
        }

        static void Caption(Transform parent, string text, float x)
            => UIKit.Titled("Cap_" + text, parent, text, 26, new Color(1f, .95f, .84f, .62f),
                            TextAnchor.MiddleCenter, new Vector2(220f, 36f), new Vector2(.5f, .5f),
                            new Vector2(x, -84f), 3f, 0f);

        // --------------------------------------------------------------- the clock
        /// <summary>
        /// Drives the countdown, and ends the run when it is spent.
        ///
        /// <para>
        /// The start edge is found by <em>polling the board's lock</em> rather than by hooking
        /// anything. A poll cannot miss the edge, cannot fire twice
        /// (<see cref="RunClock.Start"/> is idempotent), and leaves nothing behind when the
        /// screen is destroyed — which a subscription would.
        /// </para>
        /// <para>
        /// It used to start on the first conduit turned, and that was right while the clock
        /// was only a record: a player who studies a glade is not doing worse than one who
        /// spins tiles at random. It is wrong for a limit. A countdown a player can hold at
        /// full simply by not touching anything lets them plan the whole solution for free
        /// and then execute it, which removes exactly the pressure the limit exists to apply.
        /// </para>
        /// <para>
        /// Time only accrues while the board can actually be acted on. <c>Locked</c> covers
        /// the pause overlay, the win and defeat sequences and the brief animation locks, so
        /// a player who pauses to answer the door does not lose their record — and one who
        /// backgrounds the app contributes nothing at all, because no frames run.
        /// </para>
        /// </summary>
        void Update()
        {
            if (_puzzle == null) return;

            // Started when the glade becomes playable, not on the first turn. A countdown a
            // player can hold at full by not touching anything is not a countdown — they can
            // plan the entire solution for free and then execute it, which is precisely the
            // pressure the limit exists to apply. Locked covers the raise animation, so the
            // clock still does not burn while the board is flying in.
            if (!_clock.HasStarted && _board != null && !_board.Locked && !_finished) _clock.Start();

            if (_clock.HasStarted && !_finished && _board != null && !_board.Locked)
                _clock.Advance(Time.unscaledDeltaTime);

            PaintClock();

            // Polled for the same reasons the clock's own start edge is: it cannot miss the
            // edge, cannot fire twice, and leaves nothing to unsubscribe.
            if (!_committed && !_finished && (_puzzle.Moves > 0 || _clock.Elapsed > CommitGraceSeconds))
                Commit();

            // Checked after the paint, so the player sees 0:00 on the frame the run ends
            // rather than being taken off a board still reading 0:01. Defeat guards itself
            // against a second call, but _finished is tested here too so a locked board mid
            // defeat sequence does not keep asking.
            if (!_finished && !_offeringTime && _clock.Expired) TimeUp();
        }

        // ------------------------------------------------------- the last thirty seconds
        /// <summary>
        /// True while the continue offer is up. The run is frozen behind it and is neither
        /// won, lost, nor running.
        ///
        /// <para>
        /// A field rather than a check on whether the modal exists, because <c>Update</c> runs
        /// every frame and <see cref="RunClock.Expired"/> stays true for every one of them —
        /// without a latch the first frame of the offer would open a second offer, and the
        /// hundredth would open a hundredth.
        /// </para>
        /// </summary>
        bool _offeringTime;

        /// <summary>
        /// The clock ran out. Sell the player thirty seconds, or lose the run.
        ///
        /// <para>
        /// The one moment in the game with a natural, high-intent offer: the whole run is
        /// already invested, the loss is one frame away, and what is for sale is the only
        /// thing that undoes it. It is also the only offer here that pays no currency —
        /// see <see cref="AdPlacement.RunContinue"/> — so it needs no account and no network
        /// beyond the video itself, and it works on a first launch that has never signed in.
        /// </para>
        /// <para>
        /// <c>ShouldOffer</c> rather than <c>CanOffer</c>, matching the defeat panel: a
        /// cooldown or a spent allowance still draws the panel, which then says which of them
        /// it was. Only the refusals that cannot resolve by waiting — no provider at all, or a
        /// content table that does not carry the placement — skip straight to the defeat, and
        /// they are the ones where a panel would be a dead end rather than an explanation.
        /// </para>
        /// </summary>
        void TimeUp()
        {
            if (!RewardedAds.ShouldOffer(AdPlacement.RunContinue))
            {
                Defeat(DefeatReason.OutOfTime);
                return;
            }

            _offeringTime = true;

            // Locked before the modal rather than by it. The clock stops accruing on a locked
            // board, so this is also what stops the frozen run from being charged for however
            // long the player spends reading the panel or watching the video.
            if (_board != null) _board.Locked = true;

            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.RunContinue;
                v.Rewarded = ContinueRun;
                v.Dismissed = () => { if (this) { _offeringTime = false; Defeat(DefeatReason.OutOfTime); } };
            });
        }

        /// <summary>
        /// The video paid. Put the seconds on the clock and hand the board back.
        ///
        /// <para>
        /// The amount is read from the live table at the moment it is applied rather than
        /// captured when the panel opened, for the reason <c>RewardedAds.Redeem</c> re-checks
        /// the cap: a published table can change while a thirty-second video is playing, and
        /// what the player is owed is what the placement pays now.
        /// </para>
        /// <para>
        /// If the extension is refused — an untimed glade, a stopped clock, a run that
        /// resolved underneath the video — the run is lost rather than left frozen. That
        /// branch should be unreachable (nothing can resolve a run whose board is locked
        /// behind a modal) and it is written anyway, because the alternative to a wrong
        /// ending here is no ending at all: a player sitting on a dead board with a spent
        /// clock and no way forward.
        /// </para>
        /// </summary>
        void ContinueRun()
        {
            if (this == null) return;

            _offeringTime = false;

            if (_finished) return;

            int seconds = RewardedAds.Table.Offer(AdPlacement.RunContinue).Amount;

            if (!_clock.Extend(seconds * 1000))
            {
                Defeat(DefeatReason.OutOfTime);
                return;
            }

            // Repainted before the board is handed back, so the first frame the player can
            // act on already shows the time they bought. Straight to PaintClock rather than
            // waiting for Update, because _paintedSeconds is still holding 0 and the label
            // would otherwise read 0:00 for one frame on the board it just rescued.
            _paintedSeconds = -1;
            PaintClock();

            if (_board != null) _board.Locked = false;

            Audio.Sfx("unlock", .7f);
            Scenery.Toast(Content, Loc.Format("ui.time.granted", seconds), Pal.Radiance, 2.2f);
        }

        /// <summary>
        /// The countdown's three states, and the thresholds between them.
        ///
        /// Seconds rather than a fraction of the limit: ten seconds is ten seconds of dread
        /// whether the glade allowed sixty or a hundred, while a tenth of the clock is four
        /// seconds on one board and ten on another. The move counter's amber and red are
        /// chosen the same way.
        /// </summary>
        static readonly Color TimerCalm = new Color(1f, .96f, .86f, .55f);
        const int TimerWarnSeconds = 15, TimerUrgentSeconds = 5;

        /// <summary>
        /// Repaints the clock, on the second and only when it changed.
        ///
        /// <para>
        /// A countdown rounds <em>up</em>, which is not a detail: floored, the label shows
        /// "0:00" for a whole second before the run actually ends, and a player who solves the
        /// glade in that second is certain the game cheated them. Ceiling means 0:00 appears
        /// exactly when the clock is spent.
        /// </para>
        /// </summary>
        void PaintClock()
        {
            if (!_timer) return;

            bool timed = _clock.HasLimit;
            int millis = timed ? _clock.RemainingMillis : _clock.Millis;
            int seconds = timed ? (millis + 999) / 1000 : millis / 1000;

            if (seconds == _paintedSeconds) return;
            _paintedSeconds = seconds;

            _timer.text = RunClock.Format(timed ? seconds * 1000 : millis);

            if (!timed) { _timer.color = TimerCalm; return; }

            _timer.color = seconds <= TimerUrgentSeconds ? Pal.Ember
                         : seconds <= TimerWarnSeconds ? Pal.Gold
                         : TimerCalm;

            // Only once the clock is genuinely short, and only while it is running: a punch
            // on every second would be a metronome, and one on a board that has not been
            // touched yet would advertise a countdown that is not counting.
            if (_clock.HasStarted && !_finished && seconds <= TimerUrgentSeconds && seconds > 0)
            {
                Tween.Punch(_timer.transform, .3f, .3f);
                Audio.Sfx("tock", .34f, 1f + (TimerUrgentSeconds - seconds) * .06f);
            }
        }

        /// <summary>
        /// Puts the stopwatch back to zero. Every path that hands the player a fresh board
        /// goes through here — see <see cref="RunClock.Reset"/> for why missing one would
        /// stick permanently rather than merely look wrong once.
        /// </summary>
        void ResetClock()
        {
            _clock.Reset(_puzzle != null && _puzzle.HasTimeLimit ? _puzzle.TimeLimitMillis : 0);

            // Cleared here rather than beside each caller, for the reason the summary gives
            // about the clock itself: this is the one funnel every fresh board goes through,
            // and a latch left set on a new run would swallow that run's first timeout in
            // silence — the board would simply stop, with no panel and no defeat.
            _offeringTime = false;

            _paintedSeconds = -1;
            PaintClock();
        }

        // --------------------------------------------------------------- state
        void Refresh()
        {
            if (_puzzle == null) return;
            if (_moves)
            {
                // Turns remaining, not turns spent. A budget the player has to subtract
                // in their head is not a budget they can plan against — and once it is
                // low the number itself is the tension, so it turns amber then red.
                string text = _puzzle.HasBudget
                    ? _puzzle.MovesLeft.ToString()
                    : _puzzle.Moves.ToString();

                bool changed = _moves.text != text;
                _moves.text = text;

                if (_puzzle.HasBudget)
                {
                    int left = _puzzle.MovesLeft;
                    _moves.color = left <= 3 ? Pal.Ember
                                 : left <= 8 ? Pal.Gold
                                 : Pal.Cream;

                    if (changed && left <= 3) Tween.Punch(_moves.transform, .34f, .34f);
                }

                if (changed) Tween.Punch(_moves.transform, .22f, .3f);
            }
            if (_lamps)
            {
                string s = $"{_puzzle.LampsLit}/{_puzzle.LampCount}";
                if (_lamps.text != s) { _lamps.text = s; Tween.Punch(_lamps.transform, .25f, .34f); }
            }
            // The stars already earned here, not what this run is currently on track
            // for. A fresh board is nought moves in, which projects to three stars and
            // reads as "already perfect" before the player has touched anything.
            if (_pips) _pips.SetInstant(PlayerProgress.Stars(_def.Id));
            if (_undo) _undo.Interactable = _board != null && _board.CanUndo;
            if (_hint)
            {
                bool can = _board != null && _board.HintsLeft > 0;
                _hint.Interactable = can;
                if (_hintCount) _hintCount.text = (_board == null ? 0 : _board.HintsLeft).ToString();
            }
        }

        void UseHint()
        {
            if (_board == null) return;
            if (!_board.Hint())
            {
                Scenery.Toast(Content, Loc.Get("ui.play.no_hints"), Pal.Parchment, 1.6f);
                return;
            }
            LevelAnalytics.TrackHintUsed(_def, _board.HintsLeft, _puzzle.Moves);
            Scenery.Toast(Content, Loc.Get("ui.play.hint_used"), Pal.Gold, 1.6f);
            Refresh();
        }

        void Pause()
        {
            if (_finished) return;
            if (_board != null) _board.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        public void Resume()
        {
            if (_board != null && !_finished) _board.Locked = false;
        }

        public void RestartLevel()
        {
            if (_board == null) return;

            // BoardView.Restart refuses while a celebration is playing, and the clock must
            // not be zeroed in that case either or the two would part company.
            if (_board.Locked && _finished) return;

            // A restart abandons the run in progress and begins another, so it is priced like
            // any other abandonment and asked about the same way. The board is rewound only
            // once the player has agreed.
            ConfirmForfeit(ForfeitOverlay.Kind.Restart, "restart", () =>
            {
                if (_board == null) return;

                _board.Restart();
                ResetClock();
                Refresh();
                Resume();
            });
        }

        // --------------------------------------------------------------- the stake
        /// <summary>
        /// Notes that the run is now owed for, on disk, so that the process dying does not make
        /// it free. See <see cref="RunGuard"/>.
        /// </summary>
        void Commit()
        {
            // Guarded rather than assumed. Everything downstream of this takes a heart off a
            // player, so the one path where the level never resolved is worth a line of
            // insurance even though Update already refuses to run without a board.
            if (_def == null) return;

            _committed = true;
            RunGuard.Begin(_def.Id);
        }

        /// <summary>
        /// The run reached an ending and has been accounted for. Every path that finishes one
        /// calls this — a win, a defeat, or an abandonment the player agreed to.
        ///
        /// Missing one costs a player a heart they did not owe on their next launch, so it is
        /// deliberately cheap and idempotent rather than conditional.
        /// </summary>
        void Resolve()
        {
            _committed = false;
            RunGuard.Resolve();
        }

        /// <summary>
        /// The player walked away from a run that had begun. It costs exactly what losing it
        /// costs, because that is what it is.
        ///
        /// <para>
        /// Note what it does <em>not</em> do. A defeat also counts a run towards the daily
        /// chests and feeds the streak; a forfeit counts towards neither. Those are for runs
        /// that were <em>finished</em>, won or lost, and a withdrawn run was not — the line is
        /// easy to explain, and it keeps the restart button from being the fastest way to bank
        /// three chests.
        /// </para>
        /// </summary>
        void Forfeit(string reason)
        {
            if (!_committed) return;

            if (_def != null)
                LevelAnalytics.TrackAbandoned(_def, _puzzle.Moves,
                                              Time.unscaledTime - _startedAt, reason);

            Wallet.TrySpendHeart();
            Resolve();
        }

        /// <summary>
        /// Asks before charging, then does the thing. On an uncommitted run there is nothing to
        /// charge, so it does the thing immediately — a confirmation over a free action is
        /// friction that teaches players to dismiss the one that is not free.
        /// </summary>
        void ConfirmForfeit(ForfeitOverlay.Kind kind, string reason, Action then)
        {
            if (!_committed || _finished) { then(); return; }

            if (_board != null) _board.Locked = true;

            Flow.Modal<ForfeitOverlay>(v =>
            {
                v.Choice = kind;
                v.OnConfirm = () => { Forfeit(reason); then(); };
                v.OnCancel = Resume;
            });
        }

        /// <summary>Leaving without solving is a data point, not just a navigation.</summary>
        public void LeaveToMap()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "back", () => Flow.Go<LevelsScreen>());

        /// <summary>The pause menu's way out to the hub, guarded like every other.</summary>
        public void LeaveToHome()
            => ConfirmForfeit(ForfeitOverlay.Kind.Leave, "home", () => Flow.Go<HomeScreen>());

        void Finish()
        {
            if (_finished) return;
            _finished = true;

            // Paid for by winning it. Cleared before anything else, so a crash during the
            // celebration cannot charge for a run the player actually solved.
            Resolve();

            // Frozen before anything else reads it. A celebration runs for seconds after
            // this, and the value is about to be written to a permanent record.
            _clock.Stop();

            int moves = _puzzle.Moves;
            int stars = _puzzle.StarsFor(Mathf.Max(1, moves), _clock.Millis);

            var before = PlayerProgress.Record(_def.Id);
            int previousBest = before.BestMoves;
            bool firstClear = !before.IsCleared;

            // Decided once, here, and handed to everything downstream. Built *before*
            // the record is updated, because half of what it describes — the previous
            // best, whether this was a first clear — stops being true the moment
            // RecordRun folds this run in. See RunOutcome.
            var run = RunOutcome.Win(_puzzle, stars, previousBest, firstClear,
                                     before.Clears + 1, HintsSpent,
                                     Time.unscaledTime - _startedAt, _clock.Millis, _route);

            PlayerProgress.RecordRun(_def.Id, stars, moves, run.Millis);

            // Counted here and in Defeat, which are the two places a run actually ends.
            // PlayerProgress hears about wins only — a defeat is not a worse clear, it
            // simply did not happen — so there is no single Domain hook to hang this on,
            // and pretending otherwise would silently stop counting losses.
            DailyChests.RecordRun();
            var streak = RecordStreak();

            // The reward is the difference between the record before and after, not a
            // payout for the run. A replay that does not beat the old result is worth
            // nothing, and that falls out of the subtraction rather than needing a rule.
            var reward = PlayerProgression.RewardFor(before, PlayerProgress.Record(_def.Id));

            LevelAnalytics.TrackCompleted(_def, moves, stars, run.HintsUsed,
                                          run.Seconds, firstClear);

            Flow.Modal<WinOverlay>(v =>
            {
                v.Run = run;
                v.Streak = streak;
                v.XpGained = reward.Xp;
                v.CreditsGained = reward.EarnedCredits;

                // Already inside CreditsGained. Passed separately only so the panel can
                // say *why* the number is larger than the glade's usual, which is the
                // whole point of the bonus existing.
                v.GoldenPercent = PlayerProgression.GoldenPercentFor(_def.Id);
            });
        }

        /// <summary>
        /// Feeds the streak and reports what happened, so the panel that follows can say
        /// so.
        ///
        /// <para>
        /// Measured either side of the call rather than read from an event, because the
        /// panel needs the answer synchronously — it is built on the next line — and an
        /// event handler would have to stash the result somewhere for it to be found
        /// again. Two reads of a derived number is the cheapest correct version.
        /// </para>
        /// </summary>
        StreakNote RecordStreak()
        {
            int before = DailyStreak.Days;
            DailyStreak.Record();

            return new StreakNote(DailyStreak.Days, DailyStreak.Days > before);
        }

        /// <summary>
        /// Hints spent on the run so far.
        ///
        /// Derived from the allowance rather than counted, so it cannot fall out of step
        /// with the badge the player is looking at. Clamped because a board that has not
        /// finished building yet reports no hints left, and a negative count would travel
        /// into analytics.
        /// </summary>
        int HintsSpent
            => _board == null ? 0 : Mathf.Max(0, _def.Tuning.HintAllowance - _board.HintsLeft);

        /// <summary>
        /// The run was lost.
        ///
        /// The heart is charged here rather than inside the board, because the board
        /// knows about turns and the screen knows about the player. Note there is no
        /// star, no move record and no reward: a defeat is not a worse clear, it simply
        /// did not happen, and <c>PlayerProgress</c> never hears about it.
        /// </summary>
        void Defeat(DefeatReason reason)
        {
            if (_finished) return;
            _finished = true;

            // The heart is charged below, so the marker's work is done — and clearing it here
            // rather than after the charge means a crash mid-defeat cannot charge twice.
            Resolve();

            _clock.Stop();

            // The board's own two endings lock it themselves on their way here; the clock's
            // does not, because it is raised from Update rather than from the board. Locking
            // for every reason keeps the board dead behind the panel whichever way the run
            // ended, rather than depending on the modal's scrim to swallow taps.
            if (_board != null) _board.Locked = true;

            var record = PlayerProgress.Record(_def.Id);

            // Read off the board before anything touches it. The panel this feeds offers
            // a retry, which restarts the very board being measured — so a screen that
            // asked afterwards would be describing a run that no longer exists.
            var run = RunOutcome.Loss(_puzzle, reason, record.BestMoves,
                                      record.Clears + 1, HintsSpent,
                                      Time.unscaledTime - _startedAt, _clock.Millis);

            bool charged = Wallet.TrySpendHeart();
            int left = Profile.Hearts;

            // A loss is a run. It cost a heart, which is the same price a win pays, and a
            // daily loop that only rewards winning takes hearts from exactly the players
            // who most need what the chests hold.
            DailyChests.RecordRun();
            var streak = RecordStreak();

            LevelAnalytics.TrackDefeated(_def, run.Moves, run.Seconds, left, reason.ToString());

            Flow.Modal<DefeatOverlay>(v =>
            {
                v.Screen = this;
                v.Run = run;
                v.Streak = streak;
                v.HeartsLeft = left;
                v.HeartWasCharged = charged;
            });
        }

        /// <summary>
        /// Another go after a defeat. Distinct from <see cref="RestartLevel"/> because
        /// the run had already been declared over — the finished latch has to come back
        /// off, and the board has to be rebuilt rather than merely rewound.
        /// </summary>
        public void RetryAfterDefeat()
        {
            _finished = false;
            _startedAt = Time.unscaledTime;

            _board.Restart();

            // After the latch comes off, so the clock is armed for the new run rather than
            // still carrying the stopped reading of the one that just failed. The stake is
            // armed with it: this is a fresh run and it has not been paid for yet.
            _committed = false;
            ResetClock();
            Refresh();
        }

        public override void OnPresented()
        {
            if (_def == null) return;

            // A brand new idea outranks the glade's flavour line. Both at once is two
            // things to read before the first tap, and the tip is the one that is only
            // ever offered once.
            if (TryTeach()) return;

            string lesson = Loc.Get(_def.LessonKey);
            Tween.After(.35f, () => { if (this) Scenery.Toast(Content, lesson, Pal.Cream, 3.4f); }, this);
        }

        /// <summary>
        /// Shows the one mechanic on this board the player has never met.
        ///
        /// "Never met" is per player, not per level — so the lesson lands on whichever
        /// glade they happen to meet the idea in, however they got there, and a chapter
        /// shipped next year that uses a known mechanic teaches it with no authoring.
        /// Returns false when there is nothing new, which is almost always.
        /// </summary>
        bool TryTeach()
        {
            if (_puzzle == null || _board == null) return false;

            var queue = MechanicScan.Unseen(_puzzle, TipLedger.HasSeen);
            if (queue.Count == 0) return false;

            _board.Locked = true;

            // After the intro sweep, so the tile is actually on screen to be ringed.
            Tween.After(.75f, () => ShowTip(queue, 0), this);
            return true;
        }

        /// <summary>
        /// Shows one tip and, when it is dismissed, the next.
        ///
        /// Chained on dismissal rather than shown together: a glade that introduces two
        /// ideas would otherwise stack two modals, and the player would meet the second
        /// before reading the first. The board stays locked until the last one closes.
        /// </summary>
        void ShowTip(System.Collections.Generic.List<MechanicSighting> queue, int index)
        {
            if (!this) return;

            if (index >= queue.Count)
            {
                if (!_finished) _board.Locked = false;
                return;
            }

            var sighting = queue[index];

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = sighting.Mechanic;
                v.Target = sighting.HasCell
                    ? _board.TileAt(sighting.CellIndex)
                    : HudTargetFor(sighting.Mechanic);

                // A short beat between them, so the second does not appear to be the
                // first flickering.
                v.Dismissed = () => Tween.After(.18f, () => ShowTip(queue, index + 1), this);
            });
        }

        /// <summary>
        /// Where to point for a rule that lives in the HUD rather than on the board.
        ///
        /// The move budget is the case that matters: a bubble floating in the middle of
        /// the screen saying "the counter at the top" makes the player hunt for the
        /// thing being described. Ringing the actual pill removes the hunt.
        ///
        /// Resolved here rather than in the scan because the scan is Domain and knows
        /// nothing about pills — it reports the mechanic, the screen knows where it is
        /// drawn.
        /// </summary>
        RectTransform HudTargetFor(Mechanic mechanic)
        {
            // The pill background, not the label inside it, so the ring frames the whole
            // readout rather than a few digits.
            if (mechanic.Equals(Mechanic.MoveBudget) && _moves)
                return _moves.transform.parent as RectTransform;

            return null;
        }

        public override bool OnBack()
        {
            if (_finished) return false;
            Pause();
            return true;
        }
    }
}
