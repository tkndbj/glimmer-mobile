using System.Collections;
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

            UIKit.IconButton("Back", bar, "sq_dark", "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -6f), LeaveToMap);
            UIKit.IconButton("Pause", bar, "sq_dark", "ic_pause", new Vector2(118f, 118f),
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
            // Quiet on purpose. It is a record the player may care about afterwards, not a
            // countdown they have to race, and nothing in this game is scored on speed.
            _timer = UIKit.Titled("Timer", bar, RunClock.Format(0), 30,
                                  new Color(1f, .96f, .86f, .55f), TextAnchor.MiddleCenter,
                                  new Vector2(320f, 40f), new Vector2(.5f, .5f),
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
        /// Drives the run stopwatch.
        ///
        /// <para>
        /// The start edge is found by <em>polling the move count</em> rather than by hooking
        /// the turn. <see cref="BoardView"/> raises <c>OnChanged</c> for undos and refreshes
        /// as well as turns, so a subscription would have to re-derive "was that the first
        /// turn" anyway — and it would be one more thing to unsubscribe. A poll cannot miss
        /// the edge, cannot fire twice (<see cref="RunClock.Start"/> is idempotent), and
        /// leaves nothing behind when the screen is destroyed.
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

            if (!_clock.HasStarted && _puzzle.Moves > 0) _clock.Start();

            if (_clock.HasStarted && !_finished && _board != null && !_board.Locked)
                _clock.Advance(Time.unscaledDeltaTime);

            PaintClock();
        }

        void PaintClock()
        {
            if (!_timer) return;

            int seconds = _clock.Millis / 1000;
            if (seconds == _paintedSeconds) return;

            _paintedSeconds = seconds;
            _timer.text = RunClock.Format(_clock.Millis);
        }

        /// <summary>
        /// Puts the stopwatch back to zero. Every path that hands the player a fresh board
        /// goes through here — see <see cref="RunClock.Reset"/> for why missing one would
        /// stick permanently rather than merely look wrong once.
        /// </summary>
        void ResetClock()
        {
            _clock.Reset();
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
                Audio.Sfx("nope", .45f);
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

            _board.Restart();
            ResetClock();
            Refresh();
        }

        /// <summary>Leaving without solving is a data point, not just a navigation.</summary>
        void LeaveToMap()
        {
            if (!_finished && _def != null)
                LevelAnalytics.TrackAbandoned(_def, _puzzle.Moves, Time.unscaledTime - _startedAt, "back");
            Flow.Go<LevelsScreen>();
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;

            // Frozen before anything else reads it. A celebration runs for seconds after
            // this, and the value is about to be written to a permanent record.
            _clock.Stop();

            int moves = _puzzle.Moves;
            int stars = _puzzle.StarsFor(Mathf.Max(1, moves));

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

            _clock.Stop();

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
            // still carrying the stopped reading of the one that just failed.
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
