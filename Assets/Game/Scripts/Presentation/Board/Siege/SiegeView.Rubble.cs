using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the fifth chapter's two bosses leave on the line: a thunderer's drain leaving a post,
    /// and a colossus's rubble standing on one until the player digs it off - or until it
    /// weathers off on its own, which is the same picture drawn from the same place.
    ///
    /// <para>
    /// <b>Its own file because the rubble is the one thing on the line the player touches</b>,
    /// which is the bomb's argument (<c>SiegeView.Bombs</c>) arriving at the posts: a pile with a
    /// tap target under it, drawn from the model every frame (<see cref="Heaped"/>) so the frame
    /// the last piece comes off is the frame the ward is drawn firing again.
    /// </para>
    /// <para>
    /// <b>The stone is a cut sprite, not a primitive</b> (invariant 47i): one rock out of the gem
    /// pack, graded to stone, drawn three times at three sizes (<c>make_siege_art.RUBBLE_GEM</c>).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the pile
        /// <summary>How big each of the three stones is drawn, in cells, largest first.</summary>
        static readonly float[] StoneSize = { .60f, .48f, .40f };

        /// <summary>Where each stone sits on the post, in cells from the chassis glyph.</summary>
        static readonly Vector2[] StoneSeat =
        {
            new Vector2(-.22f, -.04f),
            new Vector2(.21f, -.08f),
            new Vector2(.02f, .20f),
        };

        /// <summary>How each stone is turned, so three copies of one rock read as three rocks.</summary>
        static readonly float[] StoneTurn = { -14f, 23f, 168f };

        /// <summary>
        /// Builds a post's pile: three stones over the chassis and a tap target under them,
        /// hidden until a boulder lands.
        ///
        /// <b>Built with the post rather than minted on the landing</b>, for the guard ring's
        /// reason: a pile that has to be built before it can be seen arriving arrives a frame
        /// late, and the arrival is the whole of what <see cref="Buried"/> draws.
        /// </summary>
        void Rubble(Post post, int seat)
        {
            if (post == null || post.Node == null) return;

            post.Heap = UIKit.Node("Heap", post.Node);
            post.Heap.anchoredPosition = new Vector2(0f, ChargeY - Cell * .04f);
            post.Heap.sizeDelta = new Vector2(Cell * 1.3f, Cell * 1.15f);

            // The tap target: the whole pile, invisible, and only ever raycast while a pile is
            // standing - `Heaped` switches the node off with it, so a clear post cannot be tapped.
            var pad = UIKit.Img("Dig", post.Heap, Art.Disc(64), new Color(1f, 1f, 1f, 0f),
                                post.Heap.sizeDelta);
            pad.raycastTarget = true;

            var dig = pad.gameObject.AddComponent<Btn>();
            dig.PressScale = .94f;
            dig.Setup(() => Digging(seat), silent: true);

            int stones = SiegeTuning.RubbleTaps;
            post.Stones = new Image[stones];

            for (int i = 0; i < stones; i++)
            {
                int look = i < StoneSize.Length ? i : StoneSize.Length - 1;
                float side = Cell * StoneSize[look];

                var stone = UIKit.Img("Stone", post.Heap, Piece("rubble"), Color.white,
                                      new Vector2(side, side));
                stone.raycastTarget = false;
                stone.preserveAspect = true;
                stone.rectTransform.anchoredPosition = StoneSeat[look] * Cell;
                stone.rectTransform.localRotation = Quaternion.Euler(0f, 0f, StoneTurn[look]);
                stone.enabled = false;

                post.Stones[i] = stone;
            }

            post.Piled = 0;
            post.Heap.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draws the pile the model says is standing on this post - and nothing when none is.
        ///
        /// <para>
        /// <b>An edge, not a redraw</b>: the widgets are touched only when the count moves, so
        /// this costs nothing on the ordinary frame and cannot fight the arrival or the dig
        /// animations for the same transforms.
        /// </para>
        /// <para>
        /// <b>And the edge is where a piece leaving is drawn, whichever end took it.</b> A tap
        /// takes one (<c>SiegeBoard.Dig</c>) and the clock takes one every
        /// <c>SiegeTuning.RubblePiece</c> seconds (<c>SiegeWard.Weather</c>), and to the player
        /// those are one event - so there is one place that says a stone came off and one that
        /// says the post is answering again. Drawn per piece lost rather than per call, because
        /// a coarse step of the clock can take two.
        /// </para>
        /// </summary>
        void Heaped(Post post, SiegeWard ward, int seat)
        {
            if (post == null || post.Heap == null || post.Stones == null || ward == null) return;

            int piled = ward.Buried ? ward.Rubble : 0;
            if (piled == post.Piled) return;

            // **A post that fell takes its pile down in silence**, because `Buried` answers false
            // the moment a ward stops standing: a stone thrown clear of a wreck, and a light
            // saying it is answering again, would both be lies.
            //
            // A landing (nought to a full pile) draws none of this either: `Buried` draws the
            // arrival, and the loop below has nothing to walk when the count went up.
            if (ward.Alive)
                for (int gone = post.Piled - 1; gone >= piled; gone--) Chip(seat, gone);

            bool freed = ward.Alive && piled == 0 && post.Piled > 0;

            post.Piled = piled;
            post.Heap.gameObject.SetActive(piled > 0);

            for (int i = 0; i < post.Stones.Length; i++)
                if (post.Stones[i] != null) post.Stones[i].enabled = i < piled;

            if (freed) Freed(post, seat);
        }

        /// <summary>
        /// A tap on a pile. The board decides whether a piece came off, and answers on the call
        /// (<c>SiegeBoard.Dig</c>) - so the view draws the piece leaving off that answer, exactly
        /// as an overcharge is drawn off <c>SiegeBoard.Overcharge</c>'s, and never off the tap.
        /// </summary>
        void Digging(int seat)
        {
            if (!Tappable || _board == null) return;

            HideCoach();
            Stir();

            if (!_board.Dig(seat))
            {
                var post = seat >= 0 && seat < _posts.Length ? _posts[seat] : null;
                if (post != null && post.Heap != null) Refuse(post.Heap);
                Rejected?.Invoke();
                return;
            }

            Dug(seat);
            Changed?.Invoke();
        }

        /// <summary>
        /// A pile standing on the line, for a lesson to ring, or null when every post is clear.
        ///
        /// Asked when the tip goes up rather than remembered, for <see cref="LiveBomb"/>'s
        /// reason: the pile can be dug clear between the landing and the panel opening, and a
        /// null here is a tip that teaches without pointing rather than a ring round bare post.
        /// </summary>
        public RectTransform BuriedWard()
        {
            if (_board == null || _posts == null) return null;

            var wards = _board.Wards;

            for (int i = 0; i < _posts.Length && i < wards.Count; i++)
            {
                if (!wards[i].Buried) continue;

                var post = _posts[i];
                if (post != null && post.Heap != null && post.Heap.gameObject.activeSelf)
                    return post.Heap;
            }

            return null;
        }

        // ------------------------------------------------------------------ the colossus
        /// <summary>
        /// A boulder landing: the pile arrives, stone by stone, in dust.
        ///
        /// <b>The stones are drawn from the model first and then popped</b>, so what the eye
        /// sees arriving is exactly the pile the post is about to keep - three because the rule
        /// says three (<c>SiegeTuning.RubbleTaps</c>), never a picture of three over a rule that
        /// says four.
        /// </summary>
        void Buried(int ward, Color fire)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length || _board == null) return;

            var post = _posts[ward];
            if (post == null || post.Node == null) return;

            Heaped(post, _board.Wards[ward], ward);

            var at = new Vector2(PostX(ward), _lineY + Cell * .4f);

            if (post.Stones != null)
                for (int i = 0; i < post.Stones.Length; i++)
                {
                    var stone = post.Stones[i];
                    if (stone == null || !stone.enabled) continue;

                    var rt = stone.rectTransform;
                    rt.localScale = Vector3.zero;
                    Tween.Scale(rt, 1f, .30f + i * .05f, Ease.OutBack).Delay(i * .06f);
                }

            Burst.Sparks(_fx, at, Pal.Rope, 16, Cell * 2.6f, Cell * .24f, .55f);
            Burst.Sparks(_fx, at, fire, 8, Cell * 1.6f, Cell * .18f, .4f);
            Tween.Shake(post.Node, Cell * .22f, .34f);
            Audio.Sfx("boom", .62f, .5f);
        }

        /// <summary>
        /// A tap that freed a piece: the post takes the knock and the stone leaves through
        /// <see cref="Heaped"/>, which is the one place a piece is drawn coming off.
        ///
        /// <b>The tap's own feedback and nothing else</b> - the stone and the beat the ward comes
        /// back on belong to the pile falling rather than to the finger, or a piece the clock
        /// weathered off would leave in silence.
        /// </summary>
        void Dug(int ward)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length || _board == null) return;

            var post = _posts[ward];
            if (post == null || post.Node == null || post.Stones == null) return;

            Tween.Punch(post.Node, .08f, .2f);
            Audio.SfxVaried("poke", .5f, .08f);

            Heaped(post, _board.Wards[ward], ward);
        }

        /// <summary>
        /// A piece of rubble coming off a post: the stone flies clear and the pile is one
        /// smaller. <paramref name="gone"/> is the piece's own index, which is the count the pile
        /// has fallen to.
        ///
        /// <b>A throwaway copy flies, never the widget</b>, because <see cref="Heaped"/> owns the
        /// widget's visibility and will switch it off on the same frame - a tween on it would be
        /// a stone vanishing mid-air.
        /// </summary>
        void Chip(int ward, int gone)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var post = _posts[ward];
            if (post == null || post.Node == null) return;

            int look = gone < StoneSize.Length ? gone : StoneSize.Length - 1;
            if (look < 0) return;

            var origin = new Vector2(PostX(ward), _lineY + ChargeY - Cell * .04f)
                       + StoneSeat[look] * Cell;

            float side = Cell * StoneSize[look];
            var chip = UIKit.Img("Chip", _fx, Piece("rubble"), Color.white, new Vector2(side, side));
            chip.raycastTarget = false;
            chip.preserveAspect = true;

            var rt = chip.rectTransform;
            rt.anchoredPosition = origin;
            rt.localRotation = Quaternion.Euler(0f, 0f, StoneTurn[look]);

            float away = (gone % 2 == 0 ? -1f : 1f) * Cell * Random.Range(1.2f, 1.8f);
            var land = origin + new Vector2(away, -Cell * .5f);
            float spin = StoneTurn[look] + away * 1.4f;

            Tween.Run(.40f, Ease.Linear, t =>
            {
                if (!rt) return;
                var q = Vector2.Lerp(origin, land, t);
                q.y += Mathf.Sin(t * Mathf.PI) * Cell * .9f;
                rt.anchoredPosition = q;
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(StoneTurn[look], spin, t));
                chip.color = Pal.A(Color.white, t < .7f ? 1f : 1f - (t - .7f) / .3f);
            }, chip).OnDone(() => { if (chip) Destroy(chip.gameObject); });

            Burst.Sparks(_fx, origin, Pal.Rope, 7, Cell * 1.4f, Cell * .16f, .4f);
        }

        /// <summary>
        /// The last piece leaving: the post is answering again, and it says so.
        ///
        /// <b>Hung off the pile reaching nought rather than off the tap</b> - a burial that
        /// weathered off on its own (<c>SiegeWard.Weather</c>) is the same beat to the player as
        /// one that was dug clear, and a turret coming back with nothing said is a turret the
        /// player does not know is back.
        /// </summary>
        void Freed(Post post, int ward)
        {
            if (post == null || post.Node == null) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);
            Pop(at, Pal.Cream, 2.2f, .3f);
            Shockwave(at, Pal.Lift(TintOf(post.Colour), .45f), 2.8f, .36f);
            Tween.Punch(post.Node, .16f, .3f);
            Audio.Sfx("collect", .5f, 1.05f);
        }

        // ------------------------------------------------------------------ the thunderer
        /// <summary>
        /// A drain landing with something to take: the charges leave the post and run back up
        /// the hill to the boss, one bolt each.
        ///
        /// <b>Drawn upward, which is the whole reading.</b> Every other spell in this mode is
        /// something arriving at a post; a drain is something <em>leaving</em> one, and the only
        /// way to say that is the direction the light goes. The chassis glyph the player has
        /// been watching goes out under it, because that is where the charges were.
        /// </summary>
        void Drained(int ward, int taken, Color fire, Mob caster)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var post = _posts[ward];
            if (post == null || post.Node == null) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);
            var to = caster != null && caster.Node != null
                   ? caster.Node.anchoredPosition + new Vector2(0f, caster.Height * .1f)
                   : at + new Vector2(0f, Cell * 3f);

            for (int i = 0; i < taken; i++)
            {
                float when = i * .10f;
                Arc(at, to, fire, Cell * .07f, .36f, 2, .42f, when);
                Tween.After(when + .3f, () =>
                {
                    if (_fx == null) return;
                    Pop(to, fire, 2.2f, .26f);
                    Burst.Sparks(_fx, to, fire, 8, Cell * 1.8f, Cell * .18f, .4f);
                }, _fx);
            }

            if (post.Dump != null)
            {
                Tween.Punch(post.Dump.transform, .5f, .3f);
                Pop(at, fire, 1.9f, .28f);
            }

            Burst.Sparks(_fx, at, fire, 12, Cell * 2.2f, Cell * .2f, .45f);
            Flow.Flash(Pal.A(fire, .45f), .2f, .25f);
            Audio.Sfx("shatter", .55f, .9f);
        }
    }
}
