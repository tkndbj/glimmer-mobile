using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Push: walls are dark stone, pads are wells lit in their colour, the gems are gems, and
    /// the keeper is a little cannon (<c>Tools/make_push_art.py</c>) that turns to face the way
    /// it walks and shoves a gem along with its barrel - the owner's own picture of this genre
    /// (2026-09-27, replacing a gold disc with the profile mark on it). A swipe anywhere on the
    /// board is one step.
    ///
    /// <para>
    /// <b>The board is as big as the band lets it be</b> (the owner's "looks a little small,
    /// hard to see", 2026-09-27): a row carries no ring of wall round itself any more, because
    /// the board's edge already is one, and the cell is capped at the shared 200 rather than
    /// 150 - so a 7x6 room draws at about 136 units a cell on a 16:9 phone where the ringed
    /// 10x6 drew at 97.
    /// </para>
    /// <para>
    /// <b>A move is drawn as a score</b> (<c>CRAFT.md</c>'s moving-board rule): the keeper turns
    /// if it must, then glides a cell; a pushed gem glides ahead of it and the barrel kicks. A
    /// gem that lands on its own pad lights its well and its tray chip, and a mote flies to the
    /// turret it has just armed - the fusion drawn, as Merge draws it. A gem lifted off its pad
    /// dims them again. An undo is the same step run backwards, the keeper still facing the gem
    /// it pulls home.
    /// </para>
    /// <para>
    /// <b>The tray under the plate is the progress and the way out in one row</b>: a chip per
    /// pad, lit while its gem stands on it, and the UNDO key at the right end - Merge's ladder
    /// row, because it is the same fact about the same kind of board.
    /// </para>
    /// </summary>
    public sealed class SokobanView : PuzzleView
    {
        /// <summary>The tray's height under the plate, in cells. Mirrored by <c>render_challenges.py</c>.</summary>
        public const float TrayRows = .58f;

        /// <summary>The UNDO key's width at the tray's right end, in cells.</summary>
        public const float UndoCells = 1.25f;

        /// <summary>
        /// The keeper's canvas, in cells. <c>make_push_art.py</c> pivots the cannon on its hull
        /// with the hull 0.47 of the canvas across and the barrel's tip 0.46 of it from the
        /// middle, so at 1.5 cells the hull fills 0.7 of its cell and the barrel just reaches
        /// into the cell it faces - which is the gem it is about to push.
        /// </summary>
        public const float KeeperCells = 1.5f;

        /// <summary>The score's beats: a turn, a step, the barrel's kick, a seat's flight.</summary>
        const float TurnFor = .09f, StepFor = .15f, KickFor = .16f, FeedFor = .30f;

        /// <summary>The air between the plate's foot and the tray.</summary>
        const float TrayGap = 8f;

        /// <summary>An unlit chip: the gem dimmed toward the plate, not recoloured (44g).</summary>
        static readonly Color Dim = new Color(.46f, .52f, .62f, .55f);

        SokobanPuzzle _sokoban;
        Image[] _gem;
        Image[] _well, _ring;
        bool[] _seated;

        RectTransform _keeper, _body;
        Image _keeperArt;

        RectTransform _tray;
        readonly List<int> _padCells = new List<int>();
        Image[] _chip;
        Btn _undo;

        protected override float EdgeRows => TrayRows;
        protected override float EdgeBelow => TrayRows;

        // ------------------------------------------------------------------ building
        protected override void Build()
        {
            _sokoban = (SokobanPuzzle)Run.Puzzle;
            int n = Columns * Rows;

            _well = new Image[n];
            _ring = new Image[n];
            _seated = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (_sokoban.IsWall(i)) { WallAt(i); continue; }

                var slot = UIKit.Img("Slot", Field, Art.Round(16), Pal.Slot, Vector2.one * Cell * .92f);
                slot.raycastTarget = false;
                slot.type = Image.Type.Sliced;
                slot.rectTransform.anchoredPosition = CentreOf(i);

                int pad = _sokoban.PadAt(i);
                if (pad < 0) continue;

                _padCells.Add(i);
                var tint = ChallengeArt.Tint(pad);

                // The well: the pad's colour poured into the slot, so a pad reads as a place
                // from across the board rather than as a thin circle.
                _well[i] = UIKit.Img("Well", Field, Art.Glow(128, 1.4f), Pal.A(tint, .40f), Vector2.one * Cell * .86f);
                _well[i].raycastTarget = false;
                _well[i].rectTransform.anchoredPosition = CentreOf(i);

                _ring[i] = UIKit.Img("Pad", Field, Art.Ring(128, 12f), Pal.A(tint, .9f), Vector2.one * Cell * .74f);
                _ring[i].raycastTarget = false;
                _ring[i].rectTransform.anchoredPosition = CentreOf(i);
                Tween.Breathe(_ring[i].transform, .05f, 2.4f, i * .2f);
            }

            _gem = new Image[n];
            for (int i = 0; i < n; i++)
            {
                _gem[i] = GemAt(i, 0, .78f);
                _gem[i].enabled = false;
            }

            BuildKeeper();
            Tray();

            Swipes(dir => Send(ChallengeInput.Swipe(dir.x, dir.y)));

            for (int i = 0; i < n; i++) _seated[i] = _sokoban.Seated(i);
        }

        /// <summary>A wall: dark stone in the cell, the Merge board's rocks (they are the same fact).</summary>
        void WallAt(int i)
        {
            var wall = UIKit.Img("Wall", Field, Art.Round(14), new Color(.20f, .17f, .16f, 1f), Vector2.one * Cell * .98f);
            wall.raycastTarget = false;
            wall.type = Image.Type.Sliced;
            wall.rectTransform.anchoredPosition = CentreOf(i);

            var face = UIKit.Img("Face", wall.transform, Art.Round(12), new Color(.36f, .30f, .27f, 1f),
                                 Vector2.one * Cell * .82f);
            face.raycastTarget = false;
            face.type = Image.Type.Sliced;
        }

        /// <summary>
        /// The keeper: a shadow that stays put and a body that turns. A build whose keeper has
        /// not arrived draws a plain gold disc rather than a white rectangle (7b).
        /// </summary>
        void BuildKeeper()
        {
            _keeper = UIKit.Box("Keeper", Field, Vector2.one * Cell, new Vector2(.5f, .5f), CentreOf(_sokoban.Keeper));

            var shadow = UIKit.Img("Shadow", _keeper, Art.Disc(96), new Color(0f, 0f, 0f, .32f),
                                   new Vector2(Cell * .74f, Cell * .60f), new Vector2(.5f, .5f),
                                   new Vector2(0f, -Cell * .06f));
            shadow.raycastTarget = false;

            var sprite = ChallengeArt.Keeper();
            _keeperArt = sprite != null
                ? UIKit.Img("Body", _keeper, sprite, Color.white, Vector2.one * Cell * KeeperCells)
                : UIKit.Img("Body", _keeper, Art.Disc(96), Pal.Gold, Vector2.one * Cell * .70f);
            _keeperArt.raycastTarget = false;
            _keeperArt.preserveAspect = true;
            _body = _keeperArt.rectTransform;
            _body.localRotation = Quaternion.Euler(0f, 0f, Facing());
        }

        /// <summary>
        /// The body's rotation for the way the keeper faces. The cannon is cut barrel up, and
        /// the model's facing is in board terms, where y runs <em>down</em> the rows.
        /// </summary>
        float Facing()
        {
            if (_sokoban.FacingX > 0) return -90f;
            if (_sokoban.FacingX < 0) return 90f;
            return _sokoban.FacingY > 0 ? 180f : 0f;
        }

        // ------------------------------------------------------------------ the tray
        void Tray()
        {
            float tall = Cell * TrayRows;
            float y = -(Rows * Cell * .5f + PlateRim * Cell * .5f + TrayGap + tall * .5f);
            float wide = Columns * Cell;

            _tray = UIKit.Box("Tray", Field, new Vector2(wide, tall), new Vector2(.5f, .5f), new Vector2(0f, y));

            float keyW = Cell * UndoCells;
            float room = wide - keyW - Cell * .15f;
            float left = -wide * .5f + room * .5f;

            int chips = Mathf.Max(1, _padCells.Count);
            float pitch = Mathf.Min(Cell * .72f, room / (chips + .6f));
            float gem = Mathf.Min(tall * .80f, pitch * .86f);

            var ground = UIKit.Img("Ground", _tray, Art.Round(20), new Color(0f, 0f, 0f, .26f),
                                   new Vector2(chips * pitch + gem * .6f, tall * .92f), new Vector2(.5f, .5f),
                                   new Vector2(left, 0f));
            ground.raycastTarget = false;
            ground.type = Image.Type.Sliced;

            // One chip per pad, in lane order, so the tray reads left to right as the line does.
            _padCells.Sort((a, b) => _sokoban.PadAt(a) != _sokoban.PadAt(b)
                                         ? _sokoban.PadAt(a).CompareTo(_sokoban.PadAt(b))
                                         : a.CompareTo(b));
            _chip = new Image[_padCells.Count];
            for (int c = 0; c < _padCells.Count; c++)
            {
                float x = left + (c - (_padCells.Count - 1) * .5f) * pitch;
                var img = UIKit.Img("Chip", _tray, ChallengeArt.Gem(_sokoban.PadAt(_padCells[c])), Color.white,
                                    Vector2.one * gem, new Vector2(.5f, .5f), new Vector2(x, 0f));
                img.raycastTarget = false;
                img.preserveAspect = true;
                img.enabled = img.sprite != null;
                _chip[c] = img;
            }

            _undo = UIKit.TextButton("Undo", _tray, Skins.Alternate, Loc.Get("ui.challenges.undo").ToUpperInvariant(),
                                     Mathf.RoundToInt(Mathf.Min(tall * .34f, 30f)), new Vector2(keyW, tall * .92f),
                                     new Vector2(.5f, .5f), new Vector2(wide * .5f - keyW * .5f, 0f),
                                     () => Send(ChallengeInput.Undo()));
        }

        void PaintTray()
        {
            for (int c = 0; c < _chip.Length; c++)
            {
                bool lit = _sokoban.Seated(_padCells[c]);
                Tween.KillChannel(_chip[c].transform, "scale");
                _chip[c].transform.localScale = Vector3.one;
                _chip[c].color = lit ? Color.white : Dim;
            }

            if (_undo != null) _undo.Interactable = _sokoban.CanUndo;
        }

        int ChipOf(int cell) => _padCells.IndexOf(cell);

        // ------------------------------------------------------------------ lessons
        /// <summary>Rings the keeper, because the sentence is about moving it (invariant 6b).</summary>
        public override void Lessons(List<ScreenLesson> into)
            => ScreenLessons.Offer(into, Mechanic.SokobanPush, _keeper);

        public override void Review(List<ScreenLesson> into)
            => ScreenLessons.Add(into, Mechanic.SokobanPush, _keeper);

        // ------------------------------------------------------------------ painting
        public override void Repaint()
        {
            for (int i = 0; i < _gem.Length; i++)
            {
                int colour = _sokoban.GemAt(i);
                bool seated = _sokoban.Seated(i);

                Tween.KillChannel(_gem[i].rectTransform, "move");
                _gem[i].sprite = colour >= 0 ? ChallengeArt.Gem(colour) : null;
                _gem[i].enabled = colour >= 0 && _gem[i].sprite != null;
                _gem[i].rectTransform.anchoredPosition = CentreOf(i);
                _gem[i].transform.localScale = Vector3.one * (seated ? 1.08f : 1f);

                if (_well[i] != null)
                {
                    var tint = ChallengeArt.Tint(_sokoban.PadAt(i));
                    _well[i].color = Pal.A(tint, seated ? .75f : .40f);
                    _ring[i].color = Pal.A(seated ? Color.Lerp(tint, Color.white, .35f) : tint, seated ? 1f : .9f);
                }
            }

            Tween.KillChannel(_keeper, "move");
            _keeper.anchoredPosition = CentreOf(_sokoban.Keeper);
            Tween.KillChannel(_body, "rot");
            _body.localRotation = Quaternion.Euler(0f, 0f, Facing());

            PaintTray();
        }

        public override IEnumerator Animate(ChallengeMove move)
        {
            if (!move.Turn) { Repaint(); yield break; }

            bool undone = _sokoban.LastUndone;
            int from = _sokoban.LastPushFrom, to = _sokoban.LastPushTo;

            // 1. Turn to face the step, if the keeper is not already facing it.
            float face = Facing();
            if (!undone && Mathf.Abs(Mathf.DeltaAngle(_body.localEulerAngles.z, face)) > 1f)
            {
                Tween.Rotate(_body, face, TurnFor, Ease.OutCubic);
                yield return new WaitForSecondsRealtime(TurnFor);
            }

            // 2. The step: the keeper glides a cell, and whatever it pushed glides with it.
            if (_sokoban.LastFrom >= 0) _keeper.anchoredPosition = CentreOf(_sokoban.LastFrom);
            Tween.Move(_keeper, CentreOf(_sokoban.Keeper), StepFor, undone ? Ease.InOutQuad : Ease.OutQuad);

            if (from >= 0 && to >= 0)
            {
                // The gem's widget for its old cell slides to the new one, then the repaint
                // re-homes every widget.
                var rt = _gem[from].rectTransform;
                _gem[from].sprite = ChallengeArt.Gem(_sokoban.GemAt(to));
                _gem[from].enabled = _gem[from].sprite != null;
                _gem[from].transform.localScale = Vector3.one;
                Tween.Move(rt, CentreOf(to), StepFor, undone ? Ease.InOutQuad : Ease.OutQuad);
                if (!undone) Kick();
                Audio.SfxVaried("whoosh", undone ? .22f : .40f);
            }
            else
            {
                Audio.Sfx("tick", undone ? .15f : .25f);
            }

            yield return new WaitForSecondsRealtime(StepFor);
            Repaint();

            // 3. What the step did to the pads: a seat lights and arms its turret, a lift dims.
            float flight = 0f;
            for (int i = 0; i < _seated.Length; i++)
            {
                bool now = _sokoban.Seated(i);
                if (now == _seated[i]) continue;
                _seated[i] = now;

                int chip = ChipOf(i);
                if (now)
                {
                    int colour = _sokoban.GemAt(i);
                    Tween.Punch(_gem[i].transform, .2f, .24f);
                    Tween.Punch(_ring[i].transform, .25f, .30f);
                    Burst.Sparks(Field, CentreOf(i), ChallengeArt.Tint(colour), 10, Cell * 1.3f, Cell * .1f, .4f);
                    if (chip >= 0) Tween.Pop(_chip[chip].transform, .4f, .24f);
                    Audio.Sfx("chime", .6f);
                    flight = Mathf.Max(flight, FlyFeed(i, colour, FeedFor));
                }
                else if (chip >= 0)
                {
                    Tween.Punch(_chip[chip].transform, .12f, .2f);
                }
            }

            if (flight > 0f) yield return new WaitForSecondsRealtime(flight);
        }

        /// <summary>The barrel's kick: the body lunges a little toward the gem it shoved and settles.</summary>
        void Kick()
        {
            var body = _body;
            float z = Facing() * Mathf.Deg2Rad;
            var dir = new Vector2(-Mathf.Sin(z), Mathf.Cos(z));
            float reach = Cell * .10f;

            Tween.Run(KickFor, Ease.Linear, t =>
            {
                if (!body) return;
                body.anchoredPosition = dir * reach * Mathf.Sin(t * Mathf.PI);
            }, body, "kick").OnDone(() => { if (body) body.anchoredPosition = Vector2.zero; })
                             .OnAbandon(() => { if (body) body.anchoredPosition = Vector2.zero; });
        }

        /// <summary>A step into a wall or a stuck gem: the keeper rattles in place rather than the board.</summary>
        public override void Refuse()
        {
            if (_keeper) Tween.Shake(_keeper, Cell * .08f, .22f);
            Audio.Sfx("blocked", .5f);
        }
    }
}
