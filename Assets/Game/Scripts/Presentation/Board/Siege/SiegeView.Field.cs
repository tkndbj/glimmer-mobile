using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>The gem field: dealing it, dragging on it, and animating a turn.</summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the field
        void Poke(int cell)
        {
            if (!Playable) return;

            // A tap is not this mode's verb. The gem leans and comes back, which is the genre's
            // own answer to a tap on a jewel - and the screen says the sentence, rate-limited, so
            // a player poking about is answered once rather than shouted at.
            HideCoach();
            Stir();

            var gem = _gems[cell];
            if (gem != null && gem.Img != null) Refuse(gem.Img.rectTransform);

            Refused?.Invoke();
        }

        void Drag(int cell, Vector2Int dir)
        {
            if (!Playable) return;

            HideCoach();
            Stir();

            int x = cell % Width + dir.x;
            int y = cell / Width - dir.y;      // screen up is a lower row

            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                Refuse(_gems[cell].Img.rectTransform);
                return;
            }

            int other = y * Width + x;

            if (!_board.Lines(cell, other))
            {
                StartCoroutine(Rebuff(cell, other));
                return;
            }

            var turn = _board.Swap(cell, other);
            if (turn == null) return;

            Took(turn.Worth);

            Busy = true;
            StartCoroutine(Resolve(turn));
        }

        /// <summary>A swap that lines nothing up: the two lean into each other and come back.</summary>
        IEnumerator Rebuff(int a, int b)
        {
            Busy = true;

            var ga = _gems[a].Img.rectTransform;
            var gb = _gems[b].Img.rectTransform;
            Vector2 pa = CentreOf(a), pb = CentreOf(b);

            Audio.Sfx("blocked", .3f, 1.2f);

            Tween.Move(ga, Vector2.Lerp(pa, pb, .38f), .11f, Ease.OutQuad);
            Tween.Move(gb, Vector2.Lerp(pb, pa, .38f), .11f, Ease.OutQuad);

            yield return new WaitForSecondsRealtime(.12f);

            Tween.Move(ga, pa, .13f, Ease.OutBack);
            Tween.Move(gb, pb, .13f, Ease.OutBack);

            yield return new WaitForSecondsRealtime(.14f);

            Busy = false;
        }

        IEnumerator Resolve(SiegeTurn turn)
        {
            var ga = _gems[turn.A];
            var gb = _gems[turn.B];

            Tween.Move(ga.Img.rectTransform, CentreOf(turn.B), SiegeTuning.SwapFor * .94f,
                       Ease.OutQuad);
            Tween.Move(gb.Img.rectTransform, CentreOf(turn.A), SiegeTuning.SwapFor * .94f,
                       Ease.OutQuad);
            Audio.SfxVaried("rotate_a", .3f);

            yield return new WaitForSecondsRealtime(SiegeTuning.SwapFor);

            var keep = _gems[turn.A];
            _gems[turn.A] = _gems[turn.B];
            _gems[turn.B] = keep;
            Place(turn.A);
            Place(turn.B);

            for (int i = 0; i < turn.Beats.Count; i++)
                yield return Beat(turn.Beats[i]);

            Busy = false;
            Repaint();
            Changed?.Invoke();
            Judge();
        }

        IEnumerator Beat(SiegeBeat beat)
        {
            // What went, and where its fuel is going. The motes are the point of the whole
            // animation: a match is only ever worth the colour it was, so the colour has to be
            // seen leaving the field and arriving at a ward.
            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int cell = beat.Cleared[i];
                var gem = _gems[cell];
                if (gem == null || gem.Img == null) continue;

                var at = CentreOf(cell);
                var tint = TintOf(gem.Colour);

                // **A gem comes apart rather than switching off**, which is what came back from
                // play as "gems only disappear when they are matched". Three things at once and
                // none of them is the gem: a ring of debris in the gem's own colour, a flash under
                // it, and shards thrown outward. The gem itself does the smallest part - one
                // frame of swelling and then it is behind all of that.
                Shatter(at, tint, i * .012f);

                int ward = _layout.WardOf(SiegeLayout.Letters[gem.Colour]);
                if (ward >= 0) Mote(at, ward, tint, i * .012f);

                var img = gem.Img;
                Tween.Scale(img.transform, 1.45f, .07f, Ease.OutQuad).OnDone(() =>
                {
                    if (img) Tween.Scale(img.transform, 0f, .10f, Ease.InBack);
                });
                Tween.RotateBy(img.rectTransform, Random.Range(-70f, 70f), .17f, Ease.OutQuad);

                _gems[cell] = null;
                Tween.After(.23f, () => { if (img) Destroy(img.gameObject); });
            }

            // The cogs this beat took, drawn as a journey rather than as a disappearance: what
            // the player decided was *which colour to line up beside that cog*, and it is paid on
            // the line, so the drawing has to say those are one thing.
            for (int i = 0; i < beat.Rises.Count; i++) Forge(beat.Rises[i]);

            // **`.68f` rather than `pop`'s `.42f`, and that is a measurement not a taste.**
            // `gem` is a very peaky transient, so the loudness match is caught by the -1 dBFS
            // ceiling about 4 dB under the rest of the set (`make_sfx.py --report` names it) -
            // the volume here is what buys that back, so a match lands where the old wooden pop
            // landed. The ramp with depth is unchanged in spirit: a deeper beat is louder.
            Audio.SfxVaried("gem", .68f + Mathf.Min(.24f, beat.Depth * .07f));

            if (beat.Depth > 1) Chain(beat.Depth);

            // Halves of `BeatFor`, because the board books this beat's fuel to land a whole
            // `BeatFor` after the last one - see `SiegeTuning.FuelLands`. Typed here they would
            // drift, and a mote that arrives after its fuel does is the bug this schedule exists
            // to stop.
            yield return new WaitForSecondsRealtime(SiegeTuning.BeatFor * .5f);

            // The fall. New gems come in from above the field so a refill reads as a refill and
            // not as a board being redrawn.
            for (int i = 0; i < beat.Drops.Count; i++)
            {
                var drop = beat.Drops[i];
                int to = drop.To * Width + drop.Column;

                Gem gem;

                if (drop.IsNew)
                {
                    gem = Mint(drop.Colour);
                    gem.Img.rectTransform.anchoredPosition =
                        CentreOf(drop.Column) + new Vector2(0f, Cell * (1.1f - drop.From));
                }
                else
                {
                    gem = _gems[drop.From * Width + drop.Column];
                    if (gem == null) continue;
                    _gems[drop.From * Width + drop.Column] = null;
                }

                _gems[to] = gem;

                float far = Mathf.Abs(gem.Img.rectTransform.anchoredPosition.y - CentreOf(to).y);
                float fall = Mathf.Clamp(.09f + far / (Cell * 22f), .12f, .34f);

                Tween.Move(gem.Img.rectTransform, CentreOf(to), fall, Ease.OutBounce);
            }

            // **The refill is audible, once.** A voice per falling gem would be twenty of them on
            // the one moment the ten-voice pool is already carrying the match, the motes and a
            // cascade banner - and `Audio.PlayOne` stops a voice it reuses, so what that buys is
            // not a fuller sound but the match being cut off mid-tail. One sound for the fall says
            // the same thing: the board filled back in. It is `gem`'s own clip a minor third below
            // and trimmed under it, so the pair reads as burst-and-answer rather than as two events.
            if (beat.Drops.Count > 0) Audio.SfxVaried("settle", .52f);

            yield return new WaitForSecondsRealtime(SiegeTuning.BeatFor * .5f);
        }

        /// <summary>
        /// What a matched gem comes apart into.
        ///
        /// <para>
        /// One white reel tinted to the gem's colour rather than four painted ones — eighty
        /// textures against twenty, and four chances for one of them to stop matching <c>Pal</c>
        /// against none. It is the same reel every colour uses and the same <see cref="TintOf"/>
        /// the ward it feeds is painted from, which is what makes a match, its fuel and the bolt
        /// that fuel becomes visibly one colour all the way through.
        /// </para>
        /// </summary>
        void Shatter(Vector2 at, Color tint, float delay)
        {
            var frames = Blast("pop");

            if (frames != null && frames.Length > 0)
            {
                var img = UIKit.Img("Pop", _fx, frames[0], Pal.A(Pal.Lift(tint, .3f), 1f),
                                    new Vector2(Cell * 1.9f, Cell * 1.9f));
                img.raycastTarget = false;
                img.rectTransform.anchoredPosition = at;
                img.rectTransform.localRotation =
                    Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                var book = Flipbook.Attach(img, frames, 30f, false);
                if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
                else Tween.After(.5f, () => { if (img) Destroy(img.gameObject); });
            }

            Pop(at, tint, 1.7f, .3f);
            Burst.Sparks(_fx, at, tint, 8, Cell * 3.2f, Cell * .22f, .42f);
        }

        /// <summary>
        /// A cog going: it comes apart where it stood and its light crosses to the ward that took
        /// it.
        ///
        /// <para>
        /// <b>A cog that bought nothing goes nowhere</b>, and that is the whole reason this is
        /// drawn rather than folded into the ordinary burst. A cog taken by a colour whose ward has
        /// fallen — or is already at the top of the ladder — is spent for nothing, and the player
        /// has to be able to see that it was: something coming apart and *not* travelling is the
        /// only way a wrong answer here reads as a wrong answer (invariant 26h).
        /// </para>
        /// </summary>
        void Forge(SiegeRise rise)
        {
            var gem = rise.Cell >= 0 && rise.Cell < _gems.Count ? _gems[rise.Cell] : null;
            var at = CentreOf(rise.Cell);
            var tint = rise.Colour >= 0 ? TintOf(rise.Colour) : Pal.Cream;

            if (gem != null && gem.Img != null)
            {
                var img = gem.Img;
                _gems[rise.Cell] = null;

                Tween.RotateBy(img.rectTransform, 260f, .26f, Ease.OutQuad);
                Tween.Scale(img.transform, 1.5f, .1f, Ease.OutQuad).OnDone(() =>
                {
                    if (img) Tween.Scale(img.transform, 0f, .14f, Ease.InBack);
                });
                Tween.After(.3f, () => { if (img) Destroy(img.gameObject); });
            }

            Shatter(at, Pal.Cream, 0f);
            Burst.Sparks(_fx, at, tint, 12, Cell * 3.6f, Cell * .26f, .5f);

            if (!rise.Rose)
            {
                // Nothing to send it to. It still came apart, which is the news.
                Audio.Sfx("blocked", .34f, 1.15f);
                return;
            }

            Audio.Sfx("collect", .5f, 1.1f);

            // A bigger, slower mote than a gem's, so a cog crossing the field is visibly not one
            // more unit of fuel. What it *does* when it lands is `Rose`, raised off the model.
            var to = new Vector2(PostX(rise.Ward), _lineY + Cell * .5f);

            var spark = UIKit.Img("Cog", _fx, Piece("gem_cog"), Color.white,
                                  new Vector2(Cell * .6f, Cell * .6f));
            spark.raycastTarget = false;
            spark.preserveAspect = true;

            var rt = spark.rectTransform;
            rt.anchoredPosition = at;

            float bow = Cell * 1.6f * (rise.Ward < _posts.Length / 2 ? -1f : 1f);

            Tween.Run(SiegeTuning.FuelFlight * 1.15f, Ease.InOutSine, t =>
            {
                if (!rt) return;
                var q = Vector2.Lerp(at, to, t);
                q.x += Mathf.Sin(t * Mathf.PI) * bow;
                rt.anchoredPosition = q;
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 420f);
                rt.localScale = Vector3.one * Mathf.Lerp(.8f, 1.4f, t);
            }, spark).OnDone(() =>
            {
                if (spark) Destroy(spark.gameObject);
                Pop(to, Pal.Cream, 1.6f, .3f);
            });
        }

        /// <summary>A gem's worth of fuel, flying from the field to the ward it feeds.</summary>
        void Mote(Vector2 from, int ward, Color tint, float delay)
        {
            // **The tube, wherever the tube is** - see `TubeY`. It was a typed offset from the
            // ward, and the day the tube moved that would have left every mote in this mode flying
            // to a point with nothing at it. A mote that lands somewhere other than the meter it is
            // filling is the same class of fault as one that lands out of step with its own fuel.
            var to = new Vector2(PostX(ward), TubeY);

            var img = UIKit.Img("Mote", _fx, Art.Spark(64), Pal.A(Pal.Lift(tint, .4f), 1f),
                                new Vector2(Cell * .3f, Cell * .3f));
            img.raycastTarget = false;

            var rt = img.rectTransform;
            rt.anchoredPosition = from;

            // Arced sideways, so a stream of them from one match reads as several things
            // travelling rather than as one line being drawn.
            float bow = Random.Range(-Cell * 1.1f, Cell * 1.1f);

            Tween.Run(SiegeTuning.FuelFlight, Ease.InOutSine, t =>
            {
                if (!rt) return;
                var p = Vector2.Lerp(from, to, t);
                p.x += Mathf.Sin(t * Mathf.PI) * bow;
                rt.anchoredPosition = p;
                rt.localScale = Vector3.one * Mathf.Lerp(.7f, 1.25f, t);
            }, img).Delay(delay).OnDone(() =>
            {
                if (img) Destroy(img.gameObject);
                if (ward < _posts.Length) Fed(ward, tint);
            });
        }

        void Fed(int ward, Color tint)
        {
            var post = _posts[ward];
            if (post == null || post.Node == null) return;

            Pop(new Vector2(PostX(ward), TubeY), tint, .7f, .2f);
            Tween.Punch(post.Node, .1f, .2f);
            Audio.SfxVaried("lit", .18f);
        }

        /// <summary>
        /// How hot a chain reads. Yellow, gold, ember, rose — a heat ladder rather than one
        /// colour at four sizes, because the thing being said is <em>how big</em>.
        /// </summary>
        /// <summary>The dark behind the banner, which a heavy outline alone cannot replace here.</summary>
        static Color Under(float alpha) => new Color(.04f, .06f, .09f, .62f * alpha);

        static Color ChainHeat(int depth)
        {
            switch (depth)
            {
                case 2: return Pal.Sun;
                case 3: return Pal.Gold;
                case 4: return Pal.Ember;
                default: return Pal.Rose;
            }
        }

        Text _chain;
        Image _chainAura;

        /// <summary>
        /// A cascade, announced over the ward line.
        ///
        /// <para>
        /// <b>Over the turrets rather than over the field, which is where it was and where nobody
        /// saw it.</b> It sat just above the gems in a plain label at half the size it is now — on
        /// top of the one part of the board the player is already staring at, in the same band as
        /// forty gems, with no outline to separate it from any of them. A chain is the loudest
        /// thing that can happen on this board and it read as a caption.
        /// </para>
        /// <para>
        /// It is drawn on the empty run of hill just above the line: nothing else lives there, the
        /// eye is already going that way to see what the wards are shooting, and it is far enough
        /// from the field that it never covers the move that earned it. A soft dark aura sits under
        /// it, because a heavy outline alone is not enough over a lit hill.
        /// </para>
        /// <para>
        /// <b>One banner, reused.</b> A cascade raises this once per wave of it, so two arriving in
        /// a quarter of a second would otherwise be two labels in one place — the second one
        /// re-punches the first instead, which is also what makes a long chain read as one thing
        /// getting louder.
        /// </para>
        /// </summary>
        void Chain(int depth)
        {
            var heat = ChainHeat(depth);
            float y = _lineY + Cell * 2.35f;

            if (_chain == null)
            {
                var host = UIKit.Node("Chain", _fx);
                host.anchoredPosition = new Vector2(0f, y);

                _chainAura = UIKit.Img("Aura", host, Art.Glow(128, 1.7f), Under(1f),
                                       new Vector2(Cell * 7f, Cell * 2.6f));
                _chainAura.raycastTarget = false;

                _chain = UIKit.Titled("Text", host, "", 24, heat, TextAnchor.MiddleCenter,
                                      new Vector2(Span.x, Cell * 1.6f), default, default,
                                      Cell * .07f, Cell * .07f);
            }

            var rt = (RectTransform)_chain.transform.parent;
            rt.anchoredPosition = new Vector2(0f, y);

            // Bigger with depth as well as hotter, so a five reads as more than a two across the
            // room rather than only up close.
            _chain.fontSize = Mathf.RoundToInt(Cell * (.72f + Mathf.Min(depth, 6) * .05f));
            _chain.text = Loc.Format("mode.siege.chain", depth);
            _chain.color = heat;

            var solid = _chain.color;
            solid.a = 1f;
            _chain.color = solid;

            if (_chainAura) _chainAura.color = Under(1f);

            Tween.KillAll(rt);
            Tween.KillAll(_chain);

            Tween.Run(.22f, Ease.OutBack, k =>
            {
                if (rt) rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1f, k);
            }, rt, "pop");

            var banner = _chain;
            var group = rt;
            var aura = _chainAura;

            // Held solid for most of its life and then let go quickly: a banner that starts fading
            // as it arrives is one nobody reads, and this one has a number in it.
            Tween.Run(.95f, Ease.Linear, k =>
            {
                if (!banner || !group) return;

                float fade = k < .62f ? 1f : 1f - (k - .62f) / .38f;

                var lit = banner.color;
                lit.a = fade;
                banner.color = lit;

                if (aura) aura.color = Under(fade);

                group.anchoredPosition = new Vector2(0f, y + Cell * .3f * k);
            }, banner, "fade").OnDone(() =>
            {
                if (group) Destroy(group.gameObject);
                if (_chain == banner) { _chain = null; _chainAura = null; }
            });

            Audio.Sfx("chime", .35f, Mathf.Min(1.6f, .9f + depth * .12f));
        }

        /// <summary>
        /// What is standing in this cell, as the number <see cref="GemArt"/> is keyed on.
        /// </summary>
        int Face(int cell) => _board.ColourAt(cell);

        /// <summary>
        /// Shows or hides the lock over a gem, minting it the first time one is needed.
        ///
        /// <b>Minted lazily and then kept</b>: most fields never see one, and a field that does
        /// locks and unlocks all run.
        /// </summary>
        void Lock(Gem gem, bool webbed)
        {
            if (gem == null || gem.Img == null) return;

            if (gem.Web == null)
            {
                if (!webbed) return;

                gem.Web = UIKit.Img("Web", (RectTransform)gem.Img.transform, Piece("web"),
                                    new Color(1f, 1f, 1f, .92f),
                                    new Vector2(Cell * GemInset, Cell * GemInset));
                gem.Web.raycastTarget = false;
                gem.Web.preserveAspect = true;
            }

            if (gem.Web.enabled == webbed) return;

            gem.Web.enabled = webbed;

            // The arrival is drawn and the release is not, deliberately: a web landing is the
            // hill doing something *to* the player and wants a beat; forty of them coming off at
            // once when the weaver dies is the payoff, and forty pops would be a mess where one
            // board going clean is the picture (invariant 20m).
            if (webbed) Tween.Pop(gem.Web.transform, .0f, .34f);
        }

        Gem Mint(int colour)
        {
            // A cog is drawn a shade smaller than a jewel, so the socket shows around it: it is
            // the one thing on this field that is not a gem, and the gap is the cheapest way of
            // saying so that survives being forty pixels wide.
            float side = Cell * (colour == CogColour ? GemInset * .88f : GemInset);

            var img = UIKit.Img("Gem", _field, GemArt(colour), Color.white,
                                new Vector2(side, side));
            img.preserveAspect = true;

            return new Gem { Img = img, Colour = colour };
        }

        /// <summary>Repaints one cell's face and its lock. The one door both readings go through.</summary>
        void Dress(int cell)
        {
            if (cell < 0 || cell >= _gems.Count) return;

            var gem = _gems[cell];
            if (gem == null) return;

            int face = Face(cell);

            if (gem.Colour != face)
            {
                gem.Colour = face;
                gem.Img.sprite = GemArt(face);

                float side = Cell * (face == CogColour ? GemInset * .88f : GemInset);
                gem.Img.rectTransform.sizeDelta = new Vector2(side, side);
            }
        }

        void Place(int cell)
        {
            var gem = _gems[cell];
            if (gem != null && gem.Img != null)
                gem.Img.rectTransform.anchoredPosition = CentreOf(cell);
        }
    }
}
