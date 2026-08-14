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
        Text _moves, _lamps, _hintCount;
        StarRow _pips;
        Btn _undo, _hint;
        bool _finished;
        bool _hasColourKey;
        float _startedAt;

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
            UIKit.IconButton("Restart", bar, "sq_green", "ic_restart", new Vector2(150f, 150f),
                             new Vector2(.5f, .5f), new Vector2(215f, 10f), () => _board.Restart());

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
            _board.Restart();
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

            int moves = _puzzle.Moves;
            int stars = _puzzle.StarsFor(Mathf.Max(1, moves));

            var before = PlayerProgress.Record(_def.Id);
            int previousBest = before.BestMoves;
            bool firstClear = !before.IsCleared;

            PlayerProgress.RecordRun(_def.Id, stars, moves);

            // Counted here and in Defeat, which are the two places a run actually ends.
            // PlayerProgress hears about wins only — a defeat is not a worse clear, it
            // simply did not happen — so there is no single Domain hook to hang this on,
            // and pretending otherwise would silently stop counting losses.
            DailyChests.RecordRun();

            // The reward is the difference between the record before and after, not a
            // payout for the run. A replay that does not beat the old result is worth
            // nothing, and that falls out of the subtraction rather than needing a rule.
            var reward = PlayerProgression.RewardFor(before, PlayerProgress.Record(_def.Id));

            LevelAnalytics.TrackCompleted(_def, moves, stars, _def.Tuning.HintAllowance - _board.HintsLeft,
                                          Time.unscaledTime - _startedAt, firstClear);

            Flow.Modal<WinOverlay>(v =>
            {
                v.LevelId = _def.Id;
                v.Stars = stars;
                v.Moves = moves;
                v.Par = _puzzle.Gold;
                v.PreviousBest = previousBest;
                v.FirstClear = firstClear;
                v.XpGained = reward.Xp;
                v.CreditsGained = reward.EarnedCredits;
            });
        }

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

            bool charged = Wallet.TrySpendHeart();
            int left = Profile.Hearts;

            // A loss is a run. It cost a heart, which is the same price a win pays, and a
            // daily loop that only rewards winning takes hearts from exactly the players
            // who most need what the chests hold.
            DailyChests.RecordRun();

            LevelAnalytics.TrackDefeated(_def, _puzzle.Moves, Time.unscaledTime - _startedAt,
                                         left, reason.ToString());

            Flow.Modal<DefeatOverlay>(v =>
            {
                v.Screen = this;
                v.Reason = reason;
                v.HeartsLeft = left;
                v.HeartWasCharged = charged;

                // How close they were, which only means anything when turns ran out.
                v.LampsLit = _puzzle.LampsLit;
                v.LampCount = _puzzle.LampCount;
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
