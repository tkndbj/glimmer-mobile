using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>Lays out the grove, routes taps and choreographs the light.</summary>
    public sealed class BoardView : MonoBehaviour
    {
        public Puzzle P { get; private set; }
        public bool Locked { get; set; }
        public Action OnChanged;
        public Action OnSolved;

        /// <summary>
        /// Raised once the losing animation has played out. Separate from
        /// <see cref="OnSolved"/> rather than a result code, because losing costs the
        /// player a heart and the screen has to do something quite different with it.
        /// </summary>
        public Action<DefeatReason> OnDefeated;

        readonly List<TileView> _tiles = new List<TileView>();
        readonly List<int> _history = new List<int>();
        readonly Dictionary<int, TileView> _byIndex = new Dictionary<int, TileView>();
        int[] _start;
        RectTransform _grid;
        Image _floor;
        float _pitch;
        bool _celebrating;
        bool _lost;
        Pal.BoardTheme _theme;

        // a warm pentatonic ladder: consecutive critters waking sound like a melody
        static readonly int[] Ladder = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26 };

        public int Moves => P.Moves;
        public bool CanUndo => _history.Count > 0 && !Locked;

        /// <summary>One tile's transform, for anything that needs to point at the board.</summary>
        public RectTransform TileAt(int index)
            => _byIndex.TryGetValue(index, out var tile) ? (RectTransform)tile.transform : null;
        public int HintsLeft { get; private set; }

        public void Build(RectTransform host, Puzzle puzzle, Pal.BoardTheme theme, int hints = 3)
        {
            P = puzzle;
            _theme = theme;
            HintsLeft = hints;
            _start = puzzle.Snapshot();

            var rect = host.rect;
            float pad = 34f;
            float pitch = Mathf.Min((rect.width - pad * 2f) / P.W_, (rect.height - pad * 2f) / P.H_);
            _pitch = Mathf.Clamp(pitch, 64f, 190f);

            float boardW = _pitch * P.W_, boardH = _pitch * P.H_;

            _floor = UIKit.Img("Floor", host, Art.Round(40), _theme.Floor,
                               new Vector2(boardW + 44f, boardH + 44f), new Vector2(.5f, .5f), Vector2.zero);
            var edge = UIKit.Img("FloorEdge", _floor.transform, Art.RoundOutline(40, 4f), new Color(1, 1, 1, .19f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);
            var inner = UIKit.Img("FloorGlow", _floor.transform, Art.Glow(128, 1.6f), Pal.A(_theme.Glow, .16f));
            UIKit.StretchTo((RectTransform)inner.transform, -40, -40, -40, -40);
            inner.transform.SetAsFirstSibling();

            _grid = UIKit.Box("Grid", host, new Vector2(boardW, boardH), new Vector2(.5f, .5f), Vector2.zero);

            float tile = _pitch * .965f;
            for (int y = 0; y < P.H_; y++)
                for (int x = 0; x < P.W_; x++)
                {
                    int i = P.Idx(x, y);
                    if (!P.Used(i)) continue;
                    var rt = UIKit.Box($"T{x}_{y}", _grid, Vector2.one * tile, new Vector2(.5f, .5f),
                        new Vector2((x - (P.W_ - 1) * .5f) * _pitch, -(y - (P.H_ - 1) * .5f) * _pitch));
                    var tv = rt.gameObject.AddComponent<TileView>();
                    tv.Build(this, P, i, tile, _theme);
                    _tiles.Add(tv);
                    _byIndex[i] = tv;
                }

            IntroSweep();
        }

        void IntroSweep()
        {
            Locked = true;
            var centre = new Vector2((P.W_ - 1) * .5f, (P.H_ - 1) * .5f);
            foreach (var t in _tiles)
            {
                float dx = P.X(t.Index) - centre.x, dy = P.Y(t.Index) - centre.y;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                var tr = t.transform;
                tr.localScale = Vector3.zero;
                Tween.Pop(tr, 0f, .5f, .05f + dist * .045f);
            }
            Tween.After(.55f + P.W_ * .05f, () => { Locked = false; }, this);
        }

        // ----------------------------------------------------------------- input
        public void OnTileTapped(TileView tile)
        {
            if (Locked || _celebrating || _lost) return;
            int i = tile.Index;

            if (P.C[i].locked)
            {
                tile.Refuse();
                Audio.SfxVaried("nope", .45f, .05f);
                return;
            }
            if (P.Inert(i))
            {
                Tween.Punch(tile.transform, .09f, .28f);
                Audio.SfxVaried("tick", .3f, .1f);
                return;
            }

            ApplyTurn(i, 1, countMove: true);
        }

        void ApplyTurn(int i, int dir, bool countMove)
        {
            var before = CaptureLit();
            if (!P.Turn(i, dir)) return;
            if (countMove) { P.Moves++; _history.Add(i); }

            _byIndex[i].Spin(dir);
            Audio.SfxVaried(UnityEngine.Random.value < .5f ? "rotate_a" : "rotate_b", .42f, .08f);

            CollectDebris();

            P.Evaluate();
            Refresh(before);
            OnChanged?.Invoke();

            // Checked after Refresh, which owns the win: a last turn that solves the
            // board is a win, so only an unfinished run can run out of turns.
            if (P.OutOfMoves) Exhaust();
        }

        /// <summary>
        /// Deals with a conduit that just crumbled.
        ///
        /// Clearing the undo history is the important half. A shattered conduit is not
        /// recoverable, so an undo that stepped back past it would rewind the rotations
        /// and leave the hole — a board the player could not have reached by playing.
        /// Better to say plainly that this was a point of no return.
        /// </summary>
        void CollectDebris()
        {
            if (P.ShatteredAt < 0) return;

            int at = P.ShatteredAt;
            P.ShatteredAt = -1;

            if (_byIndex.TryGetValue(at, out var tile)) tile.Crumble();

            Audio.Sfx("shatter", .55f, 1.25f);
            Haptic.Tap();
            Tween.Punch(_floor.transform, .04f, .4f);

            _history.Clear();
        }

        bool[] CaptureLit()
        {
            var b = new bool[P.C.Length];
            Array.Copy(P.Lit, b, b.Length);
            return b;
        }

        void Refresh(bool[] before)
        {
            foreach (var t in _tiles) t.ApplyEnergy(true);

            var woken = new List<TileView>();
            foreach (var t in _tiles)
            {
                if (!t.IsLamp) continue;
                if (P.Lit[t.Index] && !before[t.Index]) woken.Add(t);
                else if (!P.Lit[t.Index] && before[t.Index])
                    Audio.Sfx("pop2", .3f, .78f, Mathf.Max(0, P.Depth[t.Index]) * .028f);
            }

            woken.Sort((a, b) => Mathf.Max(0, P.Depth[a.Index]).CompareTo(Mathf.Max(0, P.Depth[b.Index])));
            for (int k = 0; k < woken.Count; k++)
            {
                float delay = .04f + Mathf.Max(0, P.Depth[woken[k].Index]) * .028f + k * .07f;
                int step = Ladder[Mathf.Min(k, Ladder.Length - 1)];
                Audio.Sfx("lit", .6f, Mathf.Pow(2f, step / 12f) * .92f, delay);
            }

            if (P.Won) Celebrate();
        }


        /// <summary>
        /// The turns ran out with the glade still dark.
        ///
        /// Quieter than a detonation on purpose. There is nothing to point at — the
        /// player did not do a wrong thing, they did too many nearly-right ones — so
        /// the light simply gutters rather than exploding, and the overlay does the
        /// explaining.
        /// </summary>
        void Exhaust()
        {
            if (_lost) return;
            _lost = true;
            Locked = true;

            Haptic.Tap();
            Audio.Duck(.3f, 1.4f);
            Audio.Sfx("nope", .65f, .78f);
            Audio.Sfx("pop2", .4f, .6f, .12f);

            // every lit arm fades back to dormant, slowest first, so the grove visibly
            // goes to sleep instead of the screen just freezing
            foreach (var t in _tiles) t.Gutter();

            Flow.Flash(new Color(.24f, .31f, .46f), .40f, .7f);
            Tween.Shake((RectTransform)_floor.transform, 7f, .45f);

            Tween.After(.95f, () => OnDefeated?.Invoke(DefeatReason.OutOfMoves), this);
        }

        void Celebrate()
        {
            if (_celebrating) return;
            _celebrating = true;
            Locked = true;
            Haptic.Tap();
            Audio.Duck(.3f, 2.2f);

            int maxDepth = 0;
            foreach (var t in _tiles) maxDepth = Mathf.Max(maxDepth, Mathf.Max(0, P.Depth[t.Index]));
            foreach (var t in _tiles) t.Flare(.18f + Mathf.Max(0, P.Depth[t.Index]) * .045f);

            float sweep = .18f + maxDepth * .045f;
            Tween.After(sweep * .55f, () =>
            {
                Flow.Flash(new Color(1f, .96f, .82f), .55f, .7f);
                Audio.Sfx("win", .85f);
                Burst.Confetti(Flow.Effects, 80);
            }, this);

            Tween.Punch(_floor.transform, .045f, .8f);
            Tween.After(sweep + 1.15f, () => OnSolved?.Invoke(), this);
        }

        /// <summary>Re-read the model and snap every tile to it, after a bulk change.</summary>
        public void SyncViews()
        {
            P.Evaluate();
            foreach (var t in _tiles)
            {
                t.ResetTo(P.C[t.Index].rot);
                t.ApplyEnergy(false);
            }
            OnChanged?.Invoke();
            if (P.Won) Celebrate();
        }

        // ------------------------------------------------------------- controls
        public void Undo()
        {
            if (!CanUndo || _celebrating || _lost) return;
            int i = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);

            var before = CaptureLit();

            // wear: false — undo rewinds the rotation but never mends a conduit. The
            // cost of having explored is the whole point of a fragile board.
            P.Turn(i, -1, wear: false);
            P.Moves = Mathf.Max(0, P.Moves - 1);
            _byIndex[i].Spin(-1);
            Audio.SfxVaried("back", .5f, .05f);
            P.Evaluate();
            Refresh(before);
            OnChanged?.Invoke();

            // No budget check here on purpose: undo gives a turn back, so it can only
            // ever move the count away from the limit.
        }

        public bool Hint()
        {
            if (Locked || _celebrating || HintsLeft <= 0) return false;
            int i = P.NextHint();
            if (i < 0) return false;

            HintsLeft--;
            Locked = true;
            var tile = _byIndex[i];
            tile.Beckon(Pal.Gold);
            Audio.Sfx("shatter", .45f, 1.25f);

            int turns = P.TurnsOwed(i);
            P.Moves += 2;                       // hints cost a little polish
            OnChanged?.Invoke();

            for (int k = 0; k < turns; k++)
            {
                float delay = .5f + k * .2f;
                Tween.After(delay, () =>
                {
                    if (this == null) return;
                    var before = CaptureLit();
                    P.Turn(i, 1);
                    _history.Add(i);
                    _byIndex[i].Spin(1);
                    Audio.SfxVaried("rotate_a", .42f, .06f);
                    P.Evaluate();
                    Refresh(before);
                    OnChanged?.Invoke();
                }, this);
            }
            Tween.After(.55f + turns * .2f, () => { if (this) Locked = _celebrating; }, this);
            return true;
        }

        public void Restart()
        {
            if (_celebrating) return;

            // A lost board is exactly the one a player most wants to restart, and
            // P.Reset puts the move count back to zero.
            _lost = false;

            _history.Clear();
            P.Reset(_start);
            Locked = true;
            Audio.SfxVaried("whoosh", .5f);

            var centre = new Vector2((P.W_ - 1) * .5f, (P.H_ - 1) * .5f);
            foreach (var t in _tiles)
            {
                float dx = P.X(t.Index) - centre.x, dy = P.Y(t.Index) - centre.y;
                float delay = Mathf.Sqrt(dx * dx + dy * dy) * .035f;
                var tv = t;
                Tween.After(delay, () =>
                {
                    if (tv == null) return;
                    tv.ResetTo(P.C[tv.Index].rot);
                    tv.ApplyEnergy(false);
                }, this);
            }
            Tween.After(.45f, () =>
            {
                if (this == null) return;
                foreach (var t in _tiles) t.ApplyEnergy(true);
                Locked = false;
                OnChanged?.Invoke();
            }, this);
            OnChanged?.Invoke();
        }
    }
}
