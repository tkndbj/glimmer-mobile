using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    public sealed class PlayScreen : View
    {
        public int LevelIndex;
        public override string Track => "mus_play";

        BoardView _board;
        RectTransform _boardHost;
        Puzzle _puzzle;
        Text _moves, _lamps, _hintCount;
        StarRow _pips;
        Btn _undo, _hint;
        bool _finished;

        public BoardView Board => _board;

        protected override void Build()
        {
            var def = Levels.All[Mathf.Clamp(LevelIndex, 0, Levels.Count - 1)];
            Scenery.Cover(Content, "Bg/" + def.Backdrop, 0f, .22f);
            Fireflies.Spawn(Content, 18, new Color(1f, .97f, .86f), 5f, 18f);

            _puzzle = Levels.Build(LevelIndex);

            BuildTopBar(def);
            BuildStatus();
            BuildBottomBar();

            _boardHost = UIKit.Node("BoardHost", Content);
            _boardHost.offsetMin = new Vector2(26f, 300f);
            _boardHost.offsetMax = new Vector2(-26f, -350f);

            _board = _boardHost.gameObject.AddComponent<BoardView>();
            _board.OnChanged = Refresh;
            _board.OnSolved = Finish;

            StartCoroutine(RaiseBoard());
        }

        IEnumerator RaiseBoard()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            int guard = 0;
            while (_boardHost.rect.width < 40f && guard++ < 60) yield return null;
            _board.Build(_boardHost, _puzzle, Pal.BoardTheme.From(Levels.All[LevelIndex].Slate));
            Refresh();
        }

        // -------------------------------------------------------------- chrome
        void BuildTopBar(LevelDef def)
        {
            var bar = UIKit.Box("TopBar", Content, new Vector2(0f, 230f), new Vector2(.5f, 1f), new Vector2(0f, -115f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 230f);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .05f, .08f, .55f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, -40, 0, 0);
            ((RectTransform)shade.transform).localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", bar, "sq_dark", "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -6f), () => Flow.Go<LevelsScreen>());
            UIKit.IconButton("Pause", bar, "sq_dark", "ic_pause", new Vector2(118f, 118f),
                             new Vector2(1f, .5f), new Vector2(-102f, -6f), Pause);

            UIKit.Titled("Name", bar, def.Name.ToUpperInvariant(), 52, Pal.Cream, TextAnchor.MiddleCenter,
                         new Vector2(620f, 62f), new Vector2(.5f, .5f), new Vector2(0f, 12f), 4f, 4f);
            UIKit.Titled("Tag", bar, def.Tagline, 28, new Color(1f, .94f, .80f, .82f), TextAnchor.MiddleCenter,
                         new Vector2(760f, 44f), new Vector2(.5f, .5f), new Vector2(0f, -38f), 3f, 3f);
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
            _hintCount = UIKit.Titled("N", badge.transform, "3", 34, Pal.Cream, TextAnchor.MiddleCenter,
                                      outline: 0f, shadow: 2f);

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
                Scenery.Toast(Content, "no whispers left in this glade", Pal.Parchment, 1.6f);
                return;
            }
            Scenery.Toast(Content, "a whisper turns one conduit  (+2 moves)", Pal.Gold, 1.6f);
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

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            int moves = _puzzle.Moves;
            int stars = _puzzle.StarsFor(Mathf.Max(1, moves));
            int previousBest = Save.BestMoves(LevelIndex);
            bool firstClear = Save.Stars(LevelIndex) == 0;
            Save.Record(LevelIndex, stars, moves);
            Save.TutorialSeen = true;

            Flow.Modal<WinOverlay>(v =>
            {
                v.Level = LevelIndex;
                v.Stars = stars;
                v.Moves = moves;
                v.Par = _puzzle.Gold;
                v.PreviousBest = previousBest;
                v.FirstClear = firstClear;
            });
        }

        public override void OnPresented()
        {
            var def = Levels.All[LevelIndex];
            Tween.After(.35f, () => { if (this) Scenery.Toast(Content, def.Lesson, Pal.Cream, 3.4f); }, this);
        }

        public override bool OnBack()
        {
            if (_finished) return false;
            Pause();
            return true;
        }
    }
}
