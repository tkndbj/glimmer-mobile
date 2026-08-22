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

        bool _locked;

        /// <summary>
        /// Whether the board is refusing input — the raise animation, a hint's reveal, the
        /// pause menu, a panel raised over the run, the win and defeat sequences.
        ///
        /// <para>
        /// <b>It raises <see cref="OnChanged"/>, and that is not a convenience.</b> Every
        /// control that depends on it — the undo button, the hint button, the clock's own
        /// start edge — is recomputed from that event, so a latch that moved silently was a
        /// control left in whatever state it happened to be in when the last turn was taken.
        /// The hint shipped exactly that bug: the reveal locks the board, every tween along
        /// the way raises <see cref="OnChanged"/> while it is still locked, and the unlatch
        /// at the end raised nothing — so the hint and undo buttons stayed dead for the rest
        /// of the run unless the player happened to turn a tile. The same hole sat under the
        /// entry animation and under every panel that latches the board.
        /// </para>
        /// <para>
        /// Fixing it at the eight call sites was the wrong shape: a repaint somebody has to
        /// remember beside every assignment is one the ninth caller forgets, which is this
        /// project's oldest lesson (<c>AdOfferOverlay.Dismissed</c>, the pause menu's
        /// unlatch, <c>CompanionLedger.Changed</c>). Here the safe outcome is what
        /// <em>every</em> exit does, because there is only one exit.
        /// </para>
        /// <para>
        /// Only a real move raises, so this is safe to assign from anywhere, including from
        /// inside a handler of the event itself.
        /// </para>
        /// </summary>
        public bool Locked
        {
            get => _locked;
            set
            {
                if (_locked == value) return;
                _locked = value;
                OnChanged?.Invoke();
            }
        }

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
        readonly List<int> _owed = new List<int>();
        readonly Dictionary<int, TileView> _byIndex = new Dictionary<int, TileView>();
        int[] _start;
        RectTransform _grid;
        Image _floor;
        float _pitch;
        bool _celebrating;
        bool _lost;
        Pal.BoardTheme _theme;

        /// <summary>
        /// How many scoring turns in a row have woken something. Reset by a turn that
        /// wakes nothing. See <see cref="Refresh"/> for what it is worth.
        /// </summary>
        int _chain;

        // a warm pentatonic ladder: consecutive critters waking sound like a melody
        static readonly int[] Ladder = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24, 26 };

        /// <summary>
        /// The most the chain may raise the ladder, in semitones. A fifth.
        ///
        /// Capped because the rise is the reward and the ceiling is what keeps it one: an
        /// uncapped chain walks a five-lamp glade off the top of the register, at which
        /// point every later turn sounds the same as the last and the escalation stops
        /// being audible at exactly the moment the player is doing best.
        /// </summary>
        const int ChainLiftMax = 7;

        public int Moves => P.Moves;
        public bool CanUndo => _history.Count > 0 && !Locked;

        /// <summary>One tile's transform, for anything that needs to point at the board.</summary>
        public RectTransform TileAt(int index)
            => _byIndex.TryGetValue(index, out var tile) ? (RectTransform)tile.transform : null;
        /// <summary>
        /// Whether the board has a hint to give right now.
        ///
        /// <para>
        /// Asked separately from <see cref="Hint"/> because the two refusals are different
        /// questions with different answers, and only one of them costs anything. This one
        /// is "there is nothing left to point at" — every turnable tile is already where the
        /// solution wants it, which happens on a board finished but for its rooted stubs.
        /// Whether the <em>player</em> can afford a hint is not a fact about a board and is
        /// decided by <c>PlayScreen</c> against the account pool.
        /// </para>
        /// </summary>
        public bool CanHint => Accepting && P.NextHint() >= 0;

        /// <summary>
        /// Whether the board is taking input at all — not latched, not in the middle of its
        /// own celebration, and not already lost.
        ///
        /// <para>
        /// Separate from <see cref="CanHint"/> because the bottom bar's hint button is drawn
        /// live on a board that has nothing left to point at: the refusal is a sentence
        /// worth reading rather than a greyed control, and the same button is the way to the
        /// offer when the account's pool is empty.
        /// </para>
        /// </summary>
        public bool Accepting => !Locked && !_celebrating && !_lost;

        public void Build(RectTransform host, Puzzle puzzle, Pal.BoardTheme theme)
        {
            P = puzzle;
            _theme = theme;
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

            SpinRoot(i, dir);
            Audio.SfxVaried(UnityEngine.Random.value < .5f ? "rotate_a" : "rotate_b", .42f, .08f);

            CollectDebris();

            P.Evaluate();
            Refresh(before, chains: countMove);
            OnChanged?.Invoke();

            // Checked after Refresh, which owns the win: a last turn that solves the
            // board is a win, so only an unfinished run can run out of turns.
            if (P.OutOfMoves) Exhaust();
        }

        /// <summary>
        /// Turns the tapped tile, and every conduit sharing its taproot.
        ///
        /// <para>
        /// The partners get a pulse on top of the spin, and only the partners. That is the
        /// whole discovery mechanism: nothing else on this board ever answers a tap
        /// somewhere else, so a player who has not read the tip still learns the rule from
        /// their first tap rather than from losing a run to it. The model has already
        /// turned them by the time this runs — <see cref="Puzzle.Turn"/> owns the taproot —
        /// so this is strictly the view catching up, and a tile the model refused to turn
        /// is skipped here on the same test.
        /// </para>
        /// </summary>
        void SpinRoot(int i, int dir)
        {
            var root = P.Bound(i);
            if (root == null) { _byIndex[i].Spin(dir); return; }

            for (int k = 0; k < root.Count; k++)
            {
                int j = root[k];
                if (!P.Used(j) || P.C[j].locked) continue;
                if (!_byIndex.TryGetValue(j, out var tile) || !tile) continue;

                tile.Spin(dir);
                if (j != i) tile.RootPulse();
            }
        }

        /// <summary>
        /// A conduit was turned once too often and gave way, which ends the run.
        ///
        /// Losing the glade rather than merely breaking it is what gives the mechanic
        /// teeth. While a crumble only damaged the board, the answer was always to press
        /// restart — free, instant, and no reason ever to count turns. Now the count is
        /// the whole point, and validation guarantees the solution never needs the turn
        /// that breaks it.
        /// </summary>
        void CollectDebris()
        {
            if (P.ShatteredAt < 0 || _lost) return;

            int at = P.ShatteredAt;
            P.ShatteredAt = -1;

            _lost = true;
            Locked = true;
            _history.Clear();

            if (_byIndex.TryGetValue(at, out var tile)) tile.Crumble();

            Audio.Sfx("shatter", .8f, 1.1f);
            Audio.Duck(.3f, 1.4f);
            Haptic.Tap();
            Flow.Flash(new Color(.78f, .62f, .40f), .38f, .6f);
            Tween.Punch(_floor.transform, .07f, .5f);

            // long enough to watch the conduit go, short enough not to feel like a wait
            Tween.After(1.0f, () => OnDefeated?.Invoke(DefeatReason.ConduitLost), this);
        }

        bool[] CaptureLit()
        {
            var b = new bool[P.C.Length];
            Array.Copy(P.Lit, b, b.Length);
            return b;
        }

        /// <summary>
        /// Repaints after a change and sounds whatever just woke.
        ///
        /// <para>
        /// <paramref name="chains"/> is true only for a turn the player spent, which is
        /// what separates progress from bookkeeping: an undo, a hint's automated turns and
        /// a bulk resync all repaint the board but none of them is an achievement, and
        /// letting them advance the chain would let a player climb the ladder by pressing
        /// undo.
        /// </para>
        /// <para>
        /// <b>The escalation.</b> Two of them, and they compose. Within a single turn the
        /// lamps that wake climb a pentatonic ladder, so lighting four at once is a phrase
        /// rather than four copies of one sound. Across turns, each consecutive turn that
        /// wakes anything lifts that whole ladder — so a player who is solving a section
        /// cleanly hears themselves getting somewhere, and one who is flailing hears the
        /// pitch drop back. Neither costs the player anything or is worth anything: this
        /// is entirely feedback, which is the only honest place for escalation of this
        /// kind to live. Nothing about the chain reaches the score, the reward or the save
        /// file, so there is no incentive to farm it and no state to merge.
        /// </para>
        /// </summary>
        void Refresh(bool[] before, bool chains = false)
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

            SoundDuskcaps(before);

            if (chains) _chain = woken.Count > 0 ? _chain + 1 : 0;

            // Whole tones per link, so the lift stays inside the same scale the ladder is
            // built from and a long chain never sounds out of key against a short one.
            int lift = Mathf.Min(Mathf.Max(0, _chain - 1) * 2, ChainLiftMax);

            woken.Sort((a, b) => Mathf.Max(0, P.Depth[a.Index]).CompareTo(Mathf.Max(0, P.Depth[b.Index])));
            for (int k = 0; k < woken.Count; k++)
            {
                float delay = .04f + Mathf.Max(0, P.Depth[woken[k].Index]) * .028f + k * .07f;
                int step = Ladder[Mathf.Min(k, Ladder.Length - 1)] + lift;

                // Louder as the phrase goes on as well as higher. Pitch alone reads as a
                // different sound; pitch and weight together read as the same sound
                // arriving harder.
                Audio.Sfx("lit", .6f + Mathf.Min(k, 4) * .045f,
                          Mathf.Pow(2f, step / 12f) * .92f, delay);
            }

            // The board itself answers a real cascade. Scaled by how many woke rather than
            // fired flat, so the difference between one lamp and four is felt and not only
            // heard — and skipped entirely on a win, where Celebrate has a much larger
            // version of the same gesture a moment later.
            if (woken.Count >= 2 && !P.Won)
            {
                Tween.Punch(_floor.transform, .010f * Mathf.Min(woken.Count, 5), .45f);
                if (woken.Count >= 3) Haptic.Tap();
            }

            if (P.Won) Celebrate();
        }


        /// <summary>
        /// Says out loud that the light has spilled somewhere it was not wanted, and that
        /// it has been taken back.
        ///
        /// <para>
        /// Deliberately not a defeat. Waking a duskcap costs nothing but the turn it took,
        /// and the board says so by making the sound recoverable: a low refusal on the way
        /// in, a soft chime on the way out. A flash or a heart here would teach players to
        /// stop exploring, when exploring is how this mechanic is meant to be learned — the
        /// price is already paid by the clock and the move budget.
        /// </para>
        /// </summary>
        void SoundDuskcaps(bool[] before)
        {
            if (P.DuskcapCount == 0) return;

            bool disturbed = false, settled = false;
            foreach (var t in _tiles)
            {
                if (!t.IsDuskcap) continue;
                if (P.Lit[t.Index] && !before[t.Index]) disturbed = true;
                else if (!P.Lit[t.Index] && before[t.Index]) settled = true;
            }

            if (disturbed)
            {
                Audio.Sfx("blocked", .55f, .72f);
                Tween.Shake((RectTransform)_floor.transform, 4f, .28f);
            }
            else if (settled)
            {
                Audio.Sfx("chime2", .42f, .82f);
            }
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
            Audio.Sfx("pop2", .4f, .6f, .12f);

            // every lit arm fades back to dormant, slowest first, so the grove visibly
            // goes to sleep instead of the screen just freezing
            foreach (var t in _tiles) t.Gutter();

            Flow.Flash(new Color(.24f, .31f, .46f), .40f, .7f);
            Tween.Shake((RectTransform)_floor.transform, 7f, .45f);

            var cue = new Cue(this);
            bool close = RevealNearMiss(cue);

            // Slightly sooner after a near miss, because the pulse has already given the
            // player something to look at and the panel is now the thing they are waiting
            // for rather than an interruption.
            cue.Then(close ? .70f : .95f, () => OnDefeated?.Invoke(DefeatReason.OutOfMoves));
        }

        /// <summary>
        /// Pulses the conduits that would have finished the glade, and says whether it
        /// did. Advances <paramref name="cue"/> over whatever it schedules.
        ///
        /// <para>
        /// This is the whole near-miss moment, and it is placed here rather than on the
        /// defeat panel for a reason that is not aesthetic: the panel draws a scrim over
        /// the board, so by the time it exists there is nothing left to point at. The only
        /// window where the player can be shown the answer is the second the lights are
        /// going out — which is also, conveniently, the second it lands hardest.
        /// </para>
        /// <para>
        /// It fires only when <see cref="Puzzle.TurnsToSolution"/> is a small number it can
        /// stand behind — an upper bound, so a pulse on one tile is a promise that one turn
        /// would genuinely have done it. That honesty is load-bearing. The effect works
        /// because a loss that registers as nearly a win drives another attempt far harder
        /// than a plain loss does, and it keeps working only while the player cannot catch
        /// it being generous. Restarting and counting the turns has to agree.
        /// </para>
        /// </summary>
        bool RevealNearMiss(Cue cue)
        {
            int turns = P.TurnsToSolution;
            if (turns < 1 || turns > RunOutcome.NearMissTurns) return false;

            P.Owed(_owed);
            if (_owed.Count == 0) return false;

            // After the guttering has begun, so the pulse arrives into a board that has
            // already gone quiet rather than competing with it.
            cue.Wait(.42f);

            for (int k = 0; k < _owed.Count; k++)
            {
                // Both copied out of the loop: a `for` variable is one variable shared by
                // every closure, so reading k inside the beat would give all of them the
                // count rather than their own place in it.
                int index = _owed[k];
                int step = k;

                cue.Then(k == 0 ? 0f : .24f, () =>
                {
                    if (_byIndex.TryGetValue(index, out var tile) && tile) tile.Beckon(Pal.Gold);
                    Audio.Sfx("star", .55f, 1.18f + step * .12f);
                });
            }

            return true;
        }

        /// <summary>
        /// The board's own solve: every conduit flares, the screen washes, the fanfare plays.
        ///
        /// <para>
        /// No confetti and no haptic, by request. Both used to fire here and again on the
        /// victory panel a second later, which a player reads as one celebration stuttering
        /// rather than as two — so removing them from one place only would have left the effect
        /// looking half-deleted. The light and the fanfare carry the moment on their own.
        /// </para>
        /// </summary>
        void Celebrate()
        {
            if (_celebrating) return;
            _celebrating = true;
            Locked = true;
            Audio.Duck(.3f, 2.2f);

            int maxDepth = 0;
            foreach (var t in _tiles) maxDepth = Mathf.Max(maxDepth, Mathf.Max(0, P.Depth[t.Index]));
            foreach (var t in _tiles) t.Flare(.18f + Mathf.Max(0, P.Depth[t.Index]) * .045f);

            float sweep = .18f + maxDepth * .045f;
            Tween.After(sweep * .55f, () =>
            {
                Flow.Flash(new Color(1f, .96f, .82f), .55f, .7f);
                Audio.Sfx("win", .85f);
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
            SpinRoot(i, -1);
            Audio.SfxVaried("back", .5f, .05f);
            P.Evaluate();
            Refresh(before);
            OnChanged?.Invoke();

            // No budget check here on purpose: undo gives a turn back, so it can only
            // ever move the count away from the limit.
        }

        /// <summary>
        /// Turns the tile nearest the crystal that is still wrong, all the way to where the
        /// solution wants it. Returns false when the board has nothing to point at.
        ///
        /// <para>
        /// <b>It charges no moves.</b> It used to add two, back when a hint was three per
        /// glade handed back at every board — the move cost was the only price a hint had,
        /// because the allowance itself cost nothing. A hint is now spent from an
        /// account-wide pool that refills on a clock, so the hint <em>is</em> the price, and
        /// charging moves as well is two punishments for one decision — the second of them
        /// invisible until the victory panel counts a star the player did not know they had
        /// lost.
        /// </para>
        /// <para>
        /// The pool is not touched here. <c>PlayScreen</c> checks it before calling and
        /// spends after this returns true, so a board with nothing to give — see
        /// <see cref="CanHint"/> — cannot cost anybody a hint.
        /// </para>
        /// <para>
        /// <paramref name="revealed"/> fires on the beat the reveal finishes and the board
        /// is handed back, and only then — anything raised while the tiles are still turning
        /// would land on a latched board with a stopped clock.
        /// </para>
        /// </summary>
        public bool Hint(Action revealed = null)
        {
            if (!Accepting) return false;
            int i = P.NextHint();
            if (i < 0) return false;

            Locked = true;
            var tile = _byIndex[i];
            tile.Beckon(Pal.Gold);
            Audio.Sfx("shatter", .45f, 1.25f);

            int turns = P.TurnsOwed(i);
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
                    SpinRoot(i, 1);
                    Audio.SfxVaried("rotate_a", .42f, .06f);
                    P.Evaluate();
                    Refresh(before);
                    OnChanged?.Invoke();
                }, this);
            }
            // The board comes back and the caller is told, in that order, on the one beat
            // the whole reveal ends on. `Locked` raises OnChanged for us, so the bar
            // repaints itself; `revealed` is for what the board cannot know — that this was
            // the player's last hint, which is worth an offer while their hand is still on
            // the button. It runs owned by this component, so a screen torn down mid-reveal
            // simply never hears.
            Tween.After(.55f + turns * .2f, () =>
            {
                if (this == null) return;
                Locked = _celebrating || _lost;
                revealed?.Invoke();
            }, this);
            return true;
        }

        public void Restart()
        {
            if (_celebrating) return;

            // A lost board is exactly the one a player most wants to restart, and
            // P.Reset puts the move count back to zero.
            _lost = false;
            _chain = 0;

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
