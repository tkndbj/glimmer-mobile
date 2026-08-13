using System.Collections;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
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
        float _startedAt;

        public BoardView Board => _board;
        public LevelDefinition Level => _def;

        protected override void Build()
        {
            if (!Resolve()) return;

            Scenery.Cover(Content, "Bg/" + _def.Presentation.ResolveBackdrop(_chapter), 0f, .22f);
            Fireflies.Spawn(Content, 18, new Color(1f, .97f, .86f), 5f, 18f);

            BuildTopBar();
            BuildStatus();
            BuildBottomBar();

            _boardHost = UIKit.Node("BoardHost", Content);
            _boardHost.offsetMin = new Vector2(26f, 300f);
            _boardHost.offsetMax = new Vector2(-26f, -350f);

            _board = _boardHost.gameObject.AddComponent<BoardView>();
            _board.OnChanged = Refresh;
            _board.OnSolved = Finish;

            _startedAt = Time.unscaledTime;
            PlayerProgress.NoteOpened(_def.Id);
            LevelAnalytics.TrackStarted(_def, PlayerProgress.Record(_def.Id).Clears + 1);

            StartCoroutine(RaiseBoard());
        }

        /// <summary>
        /// Finds the level and builds its board, bailing out to the map rather than
        /// throwing. A level can legitimately be missing — content removed from the
        /// catalog, a stale deep link — and that must never be a crash.
        /// </summary>
        bool Resolve()
        {
            _def = GameContent.Find(LevelId);
            if (_def == null)
            {
                Debug.LogError($"[Play] unknown level '{LevelId}', returning to the map");
                StartCoroutine(BailOut());
                return false;
            }

            _chapter = GameContent.ChapterOf(_def);

            // Must happen before anything asks for a sprite: it registers which
            // addresses belong to this chapter, and an asset fetched before that
            // registration would be filed as global and never released.
            _ = AssetLibrary.EnsureChapterAsync(_chapter, GameContent.Catalog);

            if (!PuzzleFactory.TryCreate(_def, out _puzzle, out var errors))
            {
                Debug.LogError($"[Play] level '{LevelId}' is unplayable: {string.Join("; ", errors)}");
                StartCoroutine(BailOut());
                return false;
            }

            return true;
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
        }

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
                bool changed = _moves.text != _puzzle.Moves.ToString();
                _moves.text = _puzzle.Moves.ToString();
                if (changed) Tween.Punch(_moves.transform, .22f, .3f);
            }
            if (_lamps)
            {
                string s = $"{_puzzle.LampsLit}/{_puzzle.LampCount}";
                if (_lamps.text != s) { _lamps.text = s; Tween.Punch(_lamps.transform, .25f, .34f); }
            }
            if (_pips) _pips.SetInstant(_puzzle.StarsFor(Mathf.Max(1, _puzzle.Moves)));
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
            });
        }

        public override void OnPresented()
        {
            if (_def == null) return;
            string lesson = Loc.Get(_def.LessonKey);
            Tween.After(.35f, () => { if (this) Scenery.Toast(Content, lesson, Pal.Cream, 3.4f); }, this);
        }

        public override bool OnBack()
        {
            if (_finished) return false;
            Pause();
            return true;
        }
    }
}
