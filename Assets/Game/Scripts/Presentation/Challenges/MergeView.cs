using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Merge: a gem per cell in the colour of its rank, with the rank's value written on it,
    /// growing a little with each rank so a 32 reads as heavier than a 2. A swipe anywhere on
    /// the board slides it.
    ///
    /// <para>
    /// <b>A move is drawn as a score, in the order the rules resolved it</b> (the rule every
    /// moving board here keeps, <c>CRAFT.md</c>): every gem the trace names slides from the
    /// cell it left to the cell it packed against; the two halves of a merge arrive together
    /// and the cell they met in pops into the new rank; the dealt gem lands <em>after</em> the
    /// slide has settled, so it reads as a thing that arrived rather than a thing that was
    /// always there; a rung of the ladder lights when a new size is made; and a mote flies from
    /// each merge to the turret it fed, which is the whole fusion drawn. The first cut
    /// repainted the settled board in one frame with a punch on top, and the owner's reading
    /// was "everything happens too sudden, and what is happening is not clear" (2026-09-24).
    /// </para>
    /// <para>
    /// <b>A thing that is leaving and a thing that is arriving never share a transform.</b>
    /// The residents (one piece per cell) draw the settled state and nothing else; travellers
    /// in a layer above them stand in for every gem the trace names while it moves, and the
    /// resident at each end is dark until they land. A repaint lands the travellers, so a
    /// board put right mid-flight can never keep a copy of a gem in the air.
    /// </para>
    /// <para>
    /// <b>The ladder under the plate is the colour map and the progress in one row</b>: every
    /// rank up to the target as the gem it draws, lit once that size has been made, the goal
    /// ringed in gold. It is what lets a first-timer read which turret a merge will feed
    /// without a caption, and it is counted into the band the board asks for
    /// (<see cref="PuzzleView.EdgeRows"/>), never drawn into the foot of the screen.
    /// </para>
    /// <para>
    /// <b>Three lessons, once in a life, through the game's own tip machinery</b>: the swipe
    /// (a hand slides across the real board), the goal (ringing the readout that carries the
    /// figure) and the feed (ringing the post the first merge reached, at that moment).
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

        /// <summary>The score's beats: the slide, the meeting, the deal, the flight, a rung.</summary>
        const float SlideFor = .13f, MeetFor = .18f, DealFor = .22f, FeedFor = .30f, RungFor = .24f;

        /// <summary>The ladder's height under the plate, in cells. Mirrored by <c>render_challenges.py</c>.</summary>
        public const float LadderRows = .58f;

        /// <summary>An unlit rung: the gem dimmed toward the plate, not recoloured (44g).</summary>
        static readonly Color Dim = new Color(.46f, .52f, .62f, .55f);

        MergePuzzle _merge;
        Piece[] _cell;
        RectTransform _flight;
        readonly List<Piece> _travellers = new List<Piece>(24);
        readonly List<Piece> _pool = new List<Piece>(24);

        RectTransform _ladder;
        Image[] _rung;
        Text[] _rungValue;
        Image _goalRing;

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

            Sockets();

            _cell = new Piece[n];
            for (int i = 0; i < n; i++)
            {
                _cell[i] = MakePiece(Field, "Piece");
                _cell[i].Node.anchoredPosition = CentreOf(i);
            }

            // Above every resident, so a gem in flight is never drawn under the cell it is
            // leaving. Sized to the field, positioned by the field's own centre.
            _flight = UIKit.Node("Flight", Field);
            _flight.anchorMin = _flight.anchorMax = new Vector2(.5f, .5f);
            _flight.sizeDelta = Field.sizeDelta;
            _flight.anchoredPosition = Vector2.zero;

            Ladder();
            _lit = _merge.Best;
            PaintLadder();

            Swipes(dir => Send(ChallengeInput.Swipe(dir.x, dir.y)));
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

        void Dress(Piece piece, int rank)
        {
            bool shown = rank > 0;
            piece.Gem.sprite = shown ? ChallengeArt.Gem(MergePuzzle.ColourOf(rank)) : null;
            piece.Gem.enabled = shown && piece.Gem.sprite != null;
            piece.Gem.rectTransform.sizeDelta = Vector2.one * Cell * SizeOf(rank);
            piece.Value.enabled = shown;
            piece.Value.text = shown ? (1 << rank).ToString() : string.Empty;
        }

        // ------------------------------------------------------------------ the ladder
        void Ladder()
        {
            int rungs = Mathf.Max(1, _merge.Target);
            float tall = Cell * LadderRows;
            float y = -(Rows * Cell * .5f + PlateRim * Cell * .5f + LadderGap + tall * .5f);

            _ladder = UIKit.Box("Ladder", Field, new Vector2(Columns * Cell, tall), new Vector2(.5f, .5f),
                                new Vector2(0f, y));

            float pitch = Mathf.Min(Cell * .62f, Columns * Cell / rungs);
            float gem = Mathf.Min(tall * .80f, pitch * .86f);

            var ground = UIKit.Img("Ground", _ladder, Art.Round(20), new Color(0f, 0f, 0f, .26f),
                                   new Vector2(rungs * pitch + gem * .6f, tall * .92f));
            ground.raycastTarget = false;
            ground.type = Image.Type.Sliced;

            _rung = new Image[rungs];
            _rungValue = new Text[rungs];

            for (int r = 1; r <= rungs; r++)
            {
                float x = (r - 1 - (rungs - 1) * .5f) * pitch;

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
            float goalX = ((rungs - 1) - (rungs - 1) * .5f) * pitch;
            _goalRing = UIKit.Img("Goal", _ladder, Art.Ring(128, 8f), Pal.A(Pal.Gold, .95f),
                                  Vector2.one * gem * 1.34f, new Vector2(.5f, .5f), new Vector2(goalX, 0f));
            _goalRing.raycastTarget = false;
            Tween.Breathe(_goalRing.transform, .08f, 1.6f);
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

            // The goal made: the ring stops breathing before it is punched, or two tweens
            // write one scale (the house rule about channels, CRAFT.md).
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
            _lit = _merge.Best;
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
            Dress(piece, _merge.RankAt(i));
        }

        Piece Traveller()
        {
            Piece piece;
            if (_pool.Count > 0)
            {
                piece = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                piece = MakePiece(_flight, "Traveller");
            }

            piece.Node.localScale = Vector3.one;
            piece.Node.gameObject.SetActive(true);
            _travellers.Add(piece);
            return piece;
        }

        /// <summary>Every traveller back in the pool, with every tween it owns killed.</summary>
        void Land()
        {
            for (int i = 0; i < _travellers.Count; i++)
            {
                var piece = _travellers[i];
                Tween.KillAll(piece.Node);
                piece.Node.gameObject.SetActive(false);
                _pool.Add(piece);
            }
            _travellers.Clear();
        }

        // ------------------------------------------------------------------ the move
        public override IEnumerator Animate(ChallengeMove move)
        {
            var slides = _merge.LastSlides;
            var merged = _merge.LastMerged;
            int dealt = _merge.LastDealt;

            if (slides.Count == 0)
            {
                Repaint();
                yield break;
            }

            // 1. The slide. A traveller stands in for every gem the trace names, dressed in the
            //    rank it carried, and the resident at each end goes dark until it lands.
            for (int s = 0; s < slides.Count; s++)
            {
                var slide = slides[s];
                _cell[slide.From].Node.gameObject.SetActive(false);
                _cell[slide.To].Node.gameObject.SetActive(false);

                var traveller = Traveller();
                Dress(traveller, slide.Rank);
                traveller.Node.anchoredPosition = CentreOf(slide.From);
                if (slide.Moved) Tween.Move(traveller.Node, CentreOf(slide.To), SlideFor, Ease.OutCubic);
            }

            Audio.SfxVaried("whoosh", .32f);
            yield return new WaitForSecondsRealtime(SlideFor);
            if (!this) yield break;

            // 2. Landed. The residents come back in their new ranks; a cell two gems met in
            //    pops into the size it became, in the colour it will feed. The dealt cell is
            //    left dark for one more beat, whether or not a gem just left it.
            Land();
            for (int s = 0; s < slides.Count; s++)
            {
                if (slides[s].From != dealt) Paint(slides[s].From);
                if (slides[s].To != dealt) Paint(slides[s].To);
            }

            for (int i = 0; i < merged.Count; i++) Meet(merged[i]);
            if (merged.Count > 0) Audio.SfxVaried("chime", .55f);

            yield return new WaitForSecondsRealtime(merged.Count > 0 ? MeetFor * .5f : .04f);
            if (!this) yield break;

            // 3. The deal: one gem arriving, sprung up from nothing with a ring off it.
            if (dealt >= 0)
            {
                Paint(dealt);
                var node = _cell[dealt].Node;
                node.localScale = Vector3.zero;
                Tween.Pop(node, 0f, DealFor);

                var ring = UIKit.Img("Dealt", _flight, Art.Ring(128, 7f), Pal.A(Pal.Cream, .7f),
                                     Vector2.one * Cell * .5f, new Vector2(.5f, .5f), CentreOf(dealt));
                ring.raycastTarget = false;
                var rt = ring.rectTransform;
                Tween.Scale(rt, 1.9f, .3f, Ease.OutCubic);
                Tween.Fade(ring, 0f, .3f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });

                Audio.Sfx("pop2", .26f);
            }

            yield return new WaitForSecondsRealtime(merged.Count > 0 ? MeetFor * .5f : DealFor * .6f);
            if (!this) yield break;

            // 4. The ladder, when a size was made for the first time this run.
            if (_merge.Best > _lit)
            {
                yield return Climb();
                if (!this) yield break;
            }

            // 5. The feed: a mote from every merge to the post of its colour. Waited on, so the
            //    hill's replay - the bolt the feed bought - starts after the feed has arrived.
            float longest = 0f;
            for (int i = 0; i < merged.Count; i++)
            {
                int colour = MergePuzzle.ColourOf(_merge.RankAt(merged[i]));
                float flight = FlyFeed(merged[i], colour, FeedFor);
                if (flight > longest) longest = flight;
            }

            if (longest > 0f) yield return new WaitForSecondsRealtime(longest + .06f);
        }

        /// <summary>Two gems became one here: a squash, a ring in the new colour, sparks.</summary>
        void Meet(int cell)
        {
            var node = _cell[cell].Node;
            var tint = ChallengeArt.Tint(MergePuzzle.ColourOf(_merge.RankAt(cell)));

            Tween.Punch(node, .30f, MeetFor + .12f);

            var ring = UIKit.Img("Meet", _flight, Art.Ring(128, 10f), Pal.A(tint, .95f),
                                 Vector2.one * Cell * .7f, new Vector2(.5f, .5f), CentreOf(cell));
            ring.raycastTarget = false;
            var rt = ring.rectTransform;
            Tween.Scale(rt, 1.9f, .32f, Ease.OutCubic);
            Tween.Fade(ring, 0f, .32f, Ease.InQuad).OnDone(() => { if (rt) Destroy(rt.gameObject); });

            Burst.Sparks(Field, CentreOf(cell), tint, 8, Cell * 1.3f, Cell * .1f, .38f);
        }

        // ------------------------------------------------------------------ the lessons
        public override void Lessons(List<ScreenLesson> into)
        {
            // The verb, shown: a hand slides along a row from a dealt gem toward the wall it
            // would pack against. A straight line the input can produce, on the real board,
            // and it demonstrates a slide rather than solving anything - a slide is the only
            // move this board has.
            if (Route(out var from, out var to, out int cells))
                ScreenLessons.OfferGesture(into, Mechanic.MergeSwipe, Field, new[] { from, to }, Pal.Cream, cells);

            ScreenLessons.Offer(into, Mechanic.MergeGoal, GoalReadout, 1 << _merge.Target);
        }

        public override void LessonsAfter(ChallengeMove move, List<ScreenLesson> into)
        {
            if (move == null || move.Feeds.Count == 0) return;

            // Rings the post the first merge just fed - the mote has landed on it by now.
            var post = PostOf?.Invoke(move.Feeds[0].Colour);
            ScreenLessons.Offer(into, Mechanic.MergeFeed, post);
        }

        /// <summary>
        /// The demonstration's two ends: a gem on the board and the cell at the far end of
        /// its row, on whichever side is farther. False on a board with no gem.
        /// </summary>
        bool Route(out RectTransform from, out RectTransform to, out int cells)
        {
            from = to = null;
            cells = 0;

            for (int i = 0; i < _cell.Length; i++)
            {
                if (_merge.RankAt(i) <= 0) continue;

                int x = i % Columns, y = i / Columns;
                int wall = x >= Columns - 1 - x ? 0 : Columns - 1;
                if (wall == x) continue;

                from = _cell[i].Node;
                to = _cell[y * Columns + wall].Node;
                cells = Mathf.Abs(wall - x);
                return true;
            }

            return false;
        }
    }
}
