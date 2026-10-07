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
    /// Merge: an authored board of gems and rocks, a gem per cell in the colour of its rank
    /// with the rank's value written on it. <b>A drag on a gem slides that gem and nothing
    /// else</b> (<see cref="MergePuzzle"/>), so every move is one gem travelling and the rest
    /// of the board standing still - the owner's objection to the 2048 board it replaced was
    /// exactly that everything moved at once (2026-09-27).
    ///
    /// <para>
    /// <b>A move is drawn as a score</b> (<c>CRAFT.md</c>'s moving-board rule): the gem lifts
    /// off its cell, travels at a pace per cell so a long slide reads as long, and lands. A
    /// gem that met nothing lands with a thud against whatever stopped it; one that met its
    /// own size becomes the next size with a ring and sparks in the new colour, a rung of
    /// the ladder lights when a size is made for the first time, and a mote flies to the
    /// turret the merge fed. An undo is the same flight run home, dimmed, so it never reads
    /// as a move that earned anything.
    /// </para>
    /// <para>
    /// <b>The ladder under the plate is the colour map and the progress in one row</b>, with
    /// the UNDO key at its right end.
    /// </para>
    /// </summary>
    public sealed class MergeView : PuzzleView
    {
        sealed class Piece
        {
            public RectTransform Node;
            public Image Gem;
            public Text Value;
        }

        /// <summary>The score's beats: a cell of travel, the lift, a meeting, the flight, a rung.</summary>
        const float PerCell = .075f, LeastTravel = .14f, MostTravel = .40f, MeetFor = .20f, FeedFor = .30f, RungFor = .24f;

        /// <summary>The ladder's height under the plate, in cells. Mirrored by <c>render_challenges.py</c>.</summary>
        public const float LadderRows = .58f;

        /// <summary>The UNDO key's width at the ladder's right end, in cells.</summary>
        public const float UndoCells = 1.25f;

        /// <summary>An unlit rung: the gem dimmed toward the plate, not recoloured (44g).</summary>
        static readonly Color Dim = new Color(.46f, .52f, .62f, .55f);

        MergePuzzle _merge;
        Piece[] _cell;
        RectTransform _flight;
        Piece _traveller;

        RectTransform _ladder;
        Image[] _rung;
        Text[] _rungValue;
        Image _goalRing;
        Btn _undo;

        /// <summary>The cell the last drag started on, so a refusal shakes that gem rather than the board.</summary>
        int _asked = -1;

        /// <summary>The highest rank the ladder shows lit. Trails the model by one beat while a climb plays.</summary>
        int _lit;

        /// <summary>The air between the plate's foot and the ladder.</summary>
        const float LadderGap = 8f;

        protected override float EdgeRows => LadderRows;
        protected override float EdgeBelow => LadderRows;

        // ------------------------------------------------------------------ building
        protected override void Build()
        {
            _merge = (MergePuzzle)Run.Puzzle;
            int n = Columns * Rows;

            for (int i = 0; i < n; i++)
            {
                if (_merge.IsRock(i)) RockAt(i);
                else
                {
                    var slot = UIKit.Img("Slot", Field, Art.Round(16), Pal.Slot, Vector2.one * Cell * .92f);
                    slot.raycastTarget = false;
                    slot.type = Image.Type.Sliced;
                    slot.rectTransform.anchoredPosition = CentreOf(i);
                }
            }

            _cell = new Piece[n];
            for (int i = 0; i < n; i++)
            {
                _cell[i] = MakePiece(Field, "Piece");
                _cell[i].Node.anchoredPosition = CentreOf(i);
            }

            // Above every resident, so the gem in flight is never drawn under a cell it crosses.
            _flight = UIKit.Node("Flight", Field);
            _flight.anchorMin = _flight.anchorMax = new Vector2(.5f, .5f);
            _flight.sizeDelta = Field.sizeDelta;
            _flight.anchoredPosition = Vector2.zero;
            _traveller = MakePiece(_flight, "Traveller");
            _traveller.Node.gameObject.SetActive(false);

            Grips();

            Ladder();
            _lit = _merge.Best;
            PaintLadder();
        }

        /// <summary>A rock: dark stone in the cell, the Push board's walls (they are the same fact).</summary>
        void RockAt(int i)
        {
            var rock = UIKit.Img("Rock", Field, Art.Round(14), new Color(.20f, .17f, .16f, 1f), Vector2.one * Cell * .94f);
            rock.raycastTarget = false;
            rock.type = Image.Type.Sliced;
            rock.rectTransform.anchoredPosition = CentreOf(i);

            var face = UIKit.Img("Face", rock.transform, Art.Round(12), new Color(.36f, .30f, .27f, 1f),
                                 Vector2.one * Cell * .78f);
            face.raycastTarget = false;
            face.type = Image.Type.Sliced;
        }

        /// <summary>
        /// One drag target per cell: the gem under the finger is the gem that moves. A press
        /// lifts it, so the player sees which one they have hold of before it goes anywhere.
        /// </summary>
        void Grips()
        {
            for (int cell = 0; cell < Columns * Rows; cell++)
            {
                if (_merge.IsRock(cell)) continue;

                int at = cell;
                var hit = UIKit.Img("Grip", Field, Art.Pixel, new Color(0f, 0f, 0f, 0f), Vector2.one * Cell);
                hit.raycastTarget = true;
                hit.rectTransform.anchoredPosition = CentreOf(at);

                var drag = hit.gameObject.AddComponent<CellDrag>();
                drag.Threshold = Cell * .30f;
                drag.Began = () => Lift(at, true);
                drag.Ended = () => Lift(at, false);
                drag.Dragged = dir =>
                {
                    if (_merge.RankAt(at) <= 0) return;
                    _asked = at;
                    Send(ChallengeInput.Slide(at, dir.x, dir.y));
                };
            }
        }

        void Lift(int cell, bool up)
        {
            if (_cell == null || _merge.RankAt(cell) <= 0) return;
            Tween.Scale(_cell[cell].Node, up ? 1.10f : 1f, .10f, Ease.OutQuad);
        }

        Piece MakePiece(Transform parent, string name)
        {
            var piece = new Piece();
            piece.Node = UIKit.Box(name, parent, Vector2.one * Cell, new Vector2(.5f, .5f), Vector2.zero);

            piece.Gem = UIKit.Img("Gem", piece.Node, null, Color.white, Vector2.one * Cell * .8f);
            piece.Gem.raycastTarget = false;
            piece.Gem.preserveAspect = true;
            piece.Gem.enabled = false;

            piece.Value = UIKit.Titled("Value", piece.Node, string.Empty, Mathf.RoundToInt(Cell * .30f), Pal.Cream,
                                       TextAnchor.MiddleCenter, Vector2.one * Cell, default, default, 2f, 2f);
            piece.Value.raycastTarget = false;
            piece.Value.enabled = false;

            return piece;
        }

        /// <summary>How big a rank's gem draws, as a share of the cell: a 32 heavier than a 2.</summary>
        static float SizeOf(int rank) => .66f + Mathf.Min(rank, 8) * .03f;

        void Dress(Piece piece, int rank, float alpha = 1f)
        {
            bool shown = rank > 0;
            piece.Gem.sprite = shown ? ChallengeArt.Gem(MergePuzzle.ColourOf(rank)) : null;
            piece.Gem.enabled = shown && piece.Gem.sprite != null;
            piece.Gem.color = new Color(1f, 1f, 1f, alpha);
            piece.Gem.rectTransform.sizeDelta = Vector2.one * Cell * SizeOf(rank);
            piece.Value.enabled = shown;
            piece.Value.text = shown ? (1 << rank).ToString() : string.Empty;
            piece.Value.color = Pal.A(Pal.Cream, alpha);
        }

        // ------------------------------------------------------------------ the ladder
        void Ladder()
        {
            int rungs = Mathf.Max(1, _merge.Target);
            float tall = Cell * LadderRows;
            float y = -(Rows * Cell * .5f + PlateRim * Cell * .5f + LadderGap + tall * .5f);
            float wide = Columns * Cell;

            _ladder = UIKit.Box("Ladder", Field, new Vector2(wide, tall), new Vector2(.5f, .5f), new Vector2(0f, y));

            // The UNDO key takes the right end; the rungs are centred in what is left.
            float keyW = Cell * UndoCells;
            float room = wide - keyW - Cell * .15f;
            float left = -wide * .5f + room * .5f;

            float pitch = Mathf.Min(Cell * .62f, room / (rungs + .6f));
            float gem = Mathf.Min(tall * .80f, pitch * .86f);

            var ground = UIKit.Img("Ground", _ladder, Art.Round(20), new Color(0f, 0f, 0f, .26f),
                                   new Vector2(rungs * pitch + gem * .6f, tall * .92f), new Vector2(.5f, .5f),
                                   new Vector2(left, 0f));
            ground.raycastTarget = false;
            ground.type = Image.Type.Sliced;

            _rung = new Image[rungs];
            _rungValue = new Text[rungs];

            for (int r = 1; r <= rungs; r++)
            {
                float x = left + (r - 1 - (rungs - 1) * .5f) * pitch;

                var img = UIKit.Img("Rung", _ladder, ChallengeArt.Gem(MergePuzzle.ColourOf(r)), Color.white,
                                    Vector2.one * gem, new Vector2(.5f, .5f), new Vector2(x, 0f));
                img.raycastTarget = false;
                img.preserveAspect = true;
                img.enabled = img.sprite != null;

                var value = UIKit.Titled("Value", _ladder, (1 << r).ToString(), Mathf.RoundToInt(gem * .36f), Pal.Cream,
                                         TextAnchor.MiddleCenter, new Vector2(pitch, gem), new Vector2(.5f, .5f),
                                         new Vector2(x, 0f), 1.5f, 1.5f);
                value.raycastTarget = false;

                _rung[r - 1] = img;
                _rungValue[r - 1] = value;
            }

            // The goal: a gold ring round the last rung, breathing so the eye finds the finish.
            float goalX = left + ((rungs - 1) - (rungs - 1) * .5f) * pitch;
            _goalRing = UIKit.Img("Goal", _ladder, Art.Ring(128, 8f), Pal.A(Pal.Gold, .95f),
                                  Vector2.one * gem * 1.34f, new Vector2(.5f, .5f), new Vector2(goalX, 0f));
            _goalRing.raycastTarget = false;
            Tween.Breathe(_goalRing.transform, .08f, 1.6f);

            _undo = UIKit.TextButton("Undo", _ladder, Skins.Alternate, Loc.Get("ui.challenges.undo").Upper(),
                                     Mathf.RoundToInt(Mathf.Min(tall * .34f, 30f)), new Vector2(keyW, tall * .92f),
                                     new Vector2(.5f, .5f), new Vector2(wide * .5f - keyW * .5f, 0f),
                                     () => { _asked = -1; Send(ChallengeInput.Undo()); });
        }

        void PaintLadder()
        {
            for (int r = 0; r < _rung.Length; r++)
            {
                bool lit = r + 1 <= _lit;
                Tween.KillChannel(_rung[r].transform, "scale");
                _rung[r].transform.localScale = Vector3.one;
                _rung[r].color = lit ? Color.white : Dim;
                _rungValue[r].color = lit ? Pal.Cream : Pal.A(Pal.Cream, .45f);
            }

            PaintUndo();
        }

        /// <summary>The key reads as dark when there is nothing to take back.</summary>
        void PaintUndo()
        {
            if (_undo != null) _undo.Interactable = _merge.CanUndo;
        }

        /// <summary>The rungs a new best just reached light one after another, bottom up.</summary>
        IEnumerator Climb()
        {
            int from = _lit;
            _lit = _merge.Best;

            for (int r = from; r < _lit && r < _rung.Length; r++)
            {
                _rung[r].color = Color.white;
                _rungValue[r].color = Pal.Cream;
                Tween.Pop(_rung[r].transform, .4f, RungFor);
                Burst.Sparks(_ladder, _rung[r].rectTransform.anchoredPosition,
                             ChallengeArt.Tint(MergePuzzle.ColourOf(r + 1)), 6, Cell * .7f, Cell * .06f, .3f);
                Audio.SfxVaried("lit", .42f);
                yield return new WaitForSecondsRealtime(.07f);
            }

            if (_lit >= _rung.Length && _goalRing)
            {
                Tween.KillChannel(_goalRing.transform, "breathe");
                Tween.Punch(_goalRing.transform, .3f, .4f);
            }
        }

        // ------------------------------------------------------------------ painting
        public override void Repaint()
        {
            Land();
            for (int i = 0; i < _cell.Length; i++) Paint(i);

            // The ladder never goes dark again: a size made once was made (an undo is a step
            // back on the board, not a rung taken off the climb).
            if (_merge.Best > _lit) _lit = _merge.Best;
            PaintLadder();
        }

        /// <summary>A resident put back exactly where the model says, with nothing borrowed.</summary>
        void Paint(int i)
        {
            var piece = _cell[i];
            Tween.KillAll(piece.Node);
            piece.Node.localScale = Vector3.one;
            piece.Node.anchoredPosition = CentreOf(i);
            piece.Node.gameObject.SetActive(true);
            Dress(piece, Mathf.Max(0, _merge.RankAt(i)));
        }

        void Land()
        {
            if (_traveller == null) return;
            Tween.KillAll(_traveller.Node);
            _traveller.Node.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ the move
        public override IEnumerator Animate(ChallengeMove move)
        {
            int from = _merge.LastFrom, to = _merge.LastTo;
            if (from < 0 || to < 0 || _merge.LastRank <= 0)
            {
                Repaint();
                yield break;
            }

            bool undone = _merge.LastUndone;
            int made = _merge.LastMade;

            // 1. The flight. The resident it left goes dark; the resident it lands on keeps
            //    showing what stood there (the partner of a merge) until the gem arrives.
            _cell[from].Node.gameObject.SetActive(false);
            if (undone) _cell[to].Node.gameObject.SetActive(false);

            var a = CentreOf(from);
            var b = CentreOf(to);
            int cells = Mathf.RoundToInt((b - a).magnitude / Cell);
            float travel = Mathf.Clamp(cells * PerCell, LeastTravel, MostTravel);

            Land();
            _traveller.Node.gameObject.SetActive(true);
            Dress(_traveller, _merge.LastRank, undone ? .6f : 1f);
            _traveller.Node.anchoredPosition = a;
            _traveller.Node.localScale = Vector3.one * (undone ? 1f : 1.10f);
            Tween.Move(_traveller.Node, b, travel, undone ? Ease.InOutQuad : Ease.InQuad);

            Audio.SfxVaried("whoosh", undone ? .18f : .30f);
            yield return new WaitForSecondsRealtime(travel);
            if (!this) yield break;

            // 2. Arrived. The cells come back as the model has them.
            Land();
            Paint(from);
            Paint(to);
            PaintUndo();

            if (undone)
            {
                Tween.Punch(_cell[to].Node, .10f, .18f);
                // A take-back that split a merge shows both halves come apart.
                if (_merge.RankAt(from) > 0) Tween.Punch(_cell[from].Node, .10f, .18f);
                Audio.Sfx("pop2", .18f);
                yield return new WaitForSecondsRealtime(.08f);
                yield break;
            }

            if (made <= 0)
            {
                // A thud against whatever stopped it: squashed along the travel, a puff behind.
                Bump(to, (b - a).normalized);
                Audio.Sfx("blocked", .22f);
                yield return new WaitForSecondsRealtime(.10f);
                yield break;
            }

            // 3. The meeting: the next size, in the colour it will feed.
            Meet(to);
            Audio.SfxVaried("chime", .55f);
            yield return new WaitForSecondsRealtime(MeetFor);
            if (!this) yield break;

            // 4. The ladder, when a size was made for the first time this run.
            if (_merge.Best > _lit)
            {
                yield return Climb();
                if (!this) yield break;
            }

            // 5. The feed, when the merge paid one (a re-made merge after an undo does not).
            float longest = 0f;
            for (int i = 0; i < move.Feeds.Count; i++)
            {
                float flight = FlyFeed(to, move.Feeds[i].Colour, FeedFor);
                if (flight > longest) longest = flight;
            }

            if (longest > 0f) yield return new WaitForSecondsRealtime(longest + .06f);
        }

        void Bump(int cell, Vector2 along)
        {
            var node = _cell[cell].Node;
            var squash = new Vector3(1f - Mathf.Abs(along.x) * .16f + Mathf.Abs(along.y) * .08f,
                                     1f - Mathf.Abs(along.y) * .16f + Mathf.Abs(along.x) * .08f, 1f);
            node.localScale = squash;
            Tween.Scale(node, 1f, .20f, Ease.OutBack);

            Burst.Sparks(Field, CentreOf(cell) + along * Cell * .42f, Pal.A(Pal.Cream, .6f), 4, Cell * .45f,
                         Cell * .05f, .22f);
        }

        /// <summary>Two gems became one here: a squash, a ring in the new colour, sparks.</summary>
        void Meet(int cell)
        {
            var node = _cell[cell].Node;
            var tint = ChallengeArt.Tint(MergePuzzle.ColourOf(_merge.RankAt(cell)));

            Tween.Punch(node, .32f, MeetFor + .14f);

            var ring = UIKit.Img("Meet", _flight, Art.Ring(128, 10f), Pal.A(tint, .95f),
                                 Vector2.one * Cell * .7f, new Vector2(.5f, .5f), CentreOf(cell));
            ring.raycastTarget = false;
            var rt = ring.rectTransform;
            Tween.Scale(rt, 1.9f, .32f, Ease.OutCubic);
            Tween.Fade(ring, 0f, .32f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });

            Burst.Sparks(Field, CentreOf(cell), tint, 10, Cell * 1.3f, Cell * .1f, .38f);
        }

        /// <summary>A refused drag shakes the gem that could not go, not the whole board.</summary>
        public override void Refuse()
        {
            if (_asked >= 0 && _asked < _cell.Length && _merge.RankAt(_asked) > 0)
            {
                Tween.Shake(_cell[_asked].Node, Cell * .08f, .22f);
                Audio.Sfx("blocked", .5f);
                return;
            }
            base.Refuse();
        }
    }
}
