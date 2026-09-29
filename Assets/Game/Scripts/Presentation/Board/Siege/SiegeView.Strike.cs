using GlimmerGrove.AssetPipeline;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The strike: a stormcall's bolt out of the sky, and the light every big impact on this
    /// board is stacked out of.
    ///
    /// <para>
    /// <b>Drawn, stacked and additive - never baked</b> (invariant 37eu). The stormcall was one
    /// reel photographed off the bought prefab, alpha-blended, five cells tall: a thread with a
    /// star on the end of it, and nothing like the pack it was bought from. What the pack
    /// actually is, is a <em>stack</em> - three strands over a flare over a streak over two rings
    /// over a crack over sparks, every one added to the last so the overlaps go white - and a
    /// photograph of a stack is one flat picture. So the pieces are cut out of the pack as masks
    /// (<c>StrikeFx</c>, <c>Tools/make_strike_fx.py</c>) and stacked here with the additive
    /// material (<c>Additive</c>) round a bolt the board draws for itself (<c>Lightning</c>):
    /// unique per strike, cut at the top of the board by <c>_sky</c>'s mask, and landing on the
    /// raider because its last joint <em>is</em> the raider.
    /// </para>
    /// <para>
    /// <b>A strike is three events a few hundredths apart, and that is what makes it read as
    /// lightning rather than as a stamp.</b> A dim leader creeps down the channel
    /// (<see cref="LeaderFor"/>); the return stroke lights it white with the strand, the flash
    /// and the ground all at once; and two re-strikes follow down a <em>different</em> channel
    /// (<see cref="Restrikes"/>) - the geometry is rebuilt from a new seed each time, because
    /// what says electricity is that the next frame is unrelated to the last. Then it decays
    /// on a flicker rather than a fade.
    /// </para>
    /// <para>
    /// <b>The ground is the other half.</b> A bolt alone is a picture of lightning; a ring
    /// spreading round the raider's feet, a crack under it and sparks thrown up are what say it
    /// arrived somewhere. Every piece on the ground is squashed to the hill's perspective
    /// (<see cref="GroundSquash"/>), which is the only thing that tells a ring on the floor from
    /// a ring in the air.
    /// </para>
    /// <para>
    /// <b>Nothing here is drawn from a sprite that has not arrived.</b> Every piece goes
    /// through <see cref="Lit"/>, which answers null for a mask the hold has not delivered
    /// (invariant 7b): a strike short of its flare is a strike, a white square is a bug.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the kit
        /// <summary>One piece of the strike kit, or null when it is not loaded.</summary>
        static Sprite Kit(string key) => AssetLibrary.Sprite(AssetManifest.StrikeFx(key));

        /// <summary>
        /// One piece of the kit as an additive image, or null when the piece is not there.
        /// Every caller tolerates the null: the piece is simply not drawn.
        /// </summary>
        Image Lit(string name, RectTransform parent, string key, Color colour, Vector2 size)
        {
            var sprite = Kit(key);
            if (sprite == null) return null;

            var img = UIKit.Img(name, parent, sprite, colour, size);
            img.raycastTarget = false;
            return Additive.Lit(img);
        }

        /// <summary>A generated sprite as an additive image.</summary>
        Image Lit(string name, RectTransform parent, Sprite sprite, Color colour, Vector2 size)
        {
            var img = UIKit.Img(name, parent, sprite, colour, size);
            img.raycastTarget = false;
            return Additive.Lit(img);
        }

        // ------------------------------------------------------------------ the bolt
        /// <summary>How far above the board's top edge a bolt starts, in cells - off the picture, so the mask cuts it.</summary>
        const float StrikeOver = 1.2f;

        /// <summary>The core's width in cells; the sheath and halo are multiples (`Lightning.Grow`).</summary>
        const float StrikeCore = .085f;

        /// <summary>How far a joint of the channel may wander sideways, in cells.</summary>
        const float StrikeJag = .40f;

        /// <summary>How many branches the return stroke throws.</summary>
        const int StrikeForks = 3;

        /// <summary>Seconds the leader takes to creep down the channel before the return stroke.</summary>
        const float LeaderFor = .07f;

        /// <summary>When the re-strikes land, in seconds after the return stroke.</summary>
        static readonly float[] Restrikes = { .075f, .165f };

        /// <summary>Seconds the lit channel takes to die after the return stroke.</summary>
        const float StrikeLife = .62f;

        /// <summary>The painted strand's alpha - under the drawn bolt, which carries the core.</summary>
        const float StrandAlpha = .85f;

        /// <summary>How a piece lying on the ground is squashed to the hill's perspective.</summary>
        const float GroundSquash = .45f;

        /// <summary>
        /// A stormcall's bolt falling on <paramref name="at"/>, seeded so two devices - and the
        /// render mirror - draw the same channel for the same strike.
        /// </summary>
        void Thunderbolt(Vector2 at, int seed)
        {
            if (_sky == null || _fx == null) return;

            var rng = new System.Random(seed);
            var hue = Pal.Sun;

            float top = Span.y * .5f + Cell * StrikeOver;
            var from = new Vector2(at.x + Cell * Between(rng, -.9f, .9f), top);

            var bolt = Lightning.Grow("Strike", _sky, hue, Cell * StrikeCore);
            bolt.Group.alpha = 0f;
            bolt.Strike(from, at, Cell, StrikeJag, 0, seed);

            // **The leader.** Dim, thin and creeping - the channel finding its way down. Nothing
            // else is drawn yet: the flash is the return stroke's.
            Tween.Run(LeaderFor, Ease.Linear, t =>
            {
                if (bolt.Host == null) return;
                bolt.Reveal = t;
                bolt.Group.alpha = .38f + .18f * Mathf.Abs(Mathf.Sin(t * 60f));
            }, bolt.Host).OnDone(() => Stroke(bolt, from, at, hue, seed));
        }

        /// <summary>The return stroke: the channel lit white, the strand, the flash, the ground.</summary>
        void Stroke(Lightning.Layered bolt, Vector2 from, Vector2 at, Color hue, int seed)
        {
            if (bolt.Host == null) return;

            bolt.Reveal = 1f;
            bolt.Strike(from, at, Cell, StrikeJag, StrikeForks, seed + 1);
            bolt.Group.alpha = 1f;

            var strand = Strand(_sky, from, at, hue, seed);

            Flash(at, hue, 3.4f);
            Ripple(at, hue, Cell * 3.8f, .44f, 0f);
            Ripple(at, hue, Cell * 2.7f, .38f, .06f);
            Answer(at, hue, 2.9f, StrikeFx.Crack, 2.3f);
            Embers(at, hue, 18, Cell * 2.3f, Cell * .34f);
            Twinkles(at, hue, 7, Cell * 1.5f);

            ShakeBoard(9f);
            Audio.SfxVaried("arc", .72f, .09f);

            // **The re-strikes: a new channel each, and the strand turned over.** Built by
            // rebuilding rather than by animating, because a bolt that moves is a snake and a
            // bolt that is replaced is lightning.
            for (int i = 0; i < Restrikes.Length; i++)
            {
                int n = i;
                Tween.After(Restrikes[i], () =>
                {
                    if (bolt.Host == null) return;
                    bolt.Strike(from, at, Cell, StrikeJag, StrikeForks - 1, seed + 2 + n);
                    bolt.Group.alpha = 1f;
                    if (strand != null)
                    {
                        var s = strand.rectTransform.localScale;
                        strand.rectTransform.localScale = new Vector3(-s.x, s.y, 1f);
                        strand.color = Pal.A(hue, StrandAlpha);
                    }
                }, bolt.Host);
            }

            // **And then it dies on a flicker.** Held bright past the last re-strike, then let
            // go against a stutter - a bolt that ramps smoothly to nothing reads as a beam being
            // switched off.
            float held = Restrikes[Restrikes.Length - 1] + .04f;
            float phase = (float)new System.Random(seed).NextDouble() * 40f;

            Tween.Run(StrikeLife, Ease.Linear, t =>
            {
                if (bolt.Host == null) return;

                float s = t * StrikeLife;
                float envelope = s < held ? 1f : Mathf.Pow(1f - (s - held) / (StrikeLife - held), 1.4f);
                float flicker = .55f + .45f * Mathf.Abs(Mathf.Sin(s * 38f + phase));

                float a = s < held ? Mathf.Max(bolt.Group.alpha * .92f, .7f) : envelope * flicker;
                bolt.Group.alpha = a;
                if (strand != null) strand.color = Pal.A(hue, StrandAlpha * a);
            }, bolt.Host).OnDone(() =>
            {
                bolt.Destroy();
                if (strand) Destroy(strand.gameObject);
            });
        }

        /// <summary>
        /// The pack's painted strand, stood along the channel: forks and tendrils the drawn
        /// bolt does not have, turned over on every re-strike so it is never the same picture
        /// twice. Its foot sits on the target and its head on where the bolt came from - off
        /// the top of the mask for a stormcall (<c>_sky</c>), the muzzle for an overcharge.
        /// </summary>
        Image Strand(RectTransform parent, Vector2 from, Vector2 at, Color hue, int seed)
        {
            var span = from - at;
            float tall = span.magnitude + Cell * .5f;
            float wide = tall * .25f;

            var strand = Lit("Strand", parent, StrikeFx.Bolt, Pal.A(hue, StrandAlpha), new Vector2(wide, tall));
            if (strand == null) return null;

            var rng = new System.Random(seed ^ 0x5bd1e995);
            float flip = rng.NextDouble() < .5 ? -1f : 1f;
            float lean = Between(rng, .75f, 1.1f);

            var rt = strand.rectTransform;
            rt.anchoredPosition = at + span.normalized * (tall * .5f - Cell * .3f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f);
            rt.localScale = new Vector3(flip * lean, 1f, 1f);
            rt.SetAsFirstSibling();
            return strand;
        }

        // ------------------------------------------------------------------ the light
        /// <summary>The flash where something landed: a flare, a streak across, a glint turning.</summary>
        void Flash(Vector2 at, Color tint, float cells)
        {
            var flare = Lit("Flare", _fx, StrikeFx.Flare, Pal.A(tint, 1f), Vector2.one * Cell * cells);
            if (flare != null)
            {
                var rt = flare.rectTransform;
                rt.anchoredPosition = at;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-14f, 14f));
                Tween.Run(.42f, Ease.Linear, t =>
                {
                    if (!flare) return;
                    float grow = Ease.OutQuint(Mathf.Clamp01(t / .3f));
                    rt.localScale = Vector3.one * Mathf.Lerp(.3f, 1f, grow);
                    flare.color = Pal.A(Color.Lerp(Color.Lerp(tint, Color.white, .6f), tint, Mathf.Clamp01(t * 2f)), 1f - t * t);
                }, flare).OnDone(() => { if (flare) Destroy(flare.gameObject); });
            }

            var streakSprite = Kit(StrikeFx.Streak);
            if (streakSprite != null)
            {
                float wide = Cell * cells * 1.6f;
                float tall = wide * streakSprite.rect.height / streakSprite.rect.width;
                var streak = Lit("Streak", _fx, streakSprite, Pal.A(Color.Lerp(tint, Color.white, .5f), 1f),
                                 new Vector2(wide, tall));
                var rt = streak.rectTransform;
                rt.anchoredPosition = at;
                Tween.Run(.24f, Ease.Linear, t =>
                {
                    if (!streak) return;
                    rt.localScale = new Vector3(Mathf.Lerp(.45f, 1.1f, Ease.OutQuint(t)), 1f - t * .5f, 1f);
                    streak.color = Pal.A(Color.Lerp(tint, Color.white, .5f), 1f - t);
                }, streak).OnDone(() => { if (streak) Destroy(streak.gameObject); });
            }

            var glint = Lit("Glint", _fx, StrikeFx.Glint, Pal.A(Color.white, .95f), Vector2.one * Cell * cells * .55f);
            if (glint != null)
            {
                var rt = glint.rectTransform;
                rt.anchoredPosition = at;
                float spin = Random.Range(-1f, 1f) < 0f ? -50f : 50f;
                Tween.Run(.32f, Ease.Linear, t =>
                {
                    if (!glint) return;
                    rt.localRotation = Quaternion.Euler(0f, 0f, 25f + spin * t);
                    rt.localScale = Vector3.one * Mathf.Lerp(.5f, 1.25f, Ease.OutQuad(t));
                    glint.color = Pal.A(Color.white, .95f * (1f - t));
                }, glint).OnDone(() => { if (glint) Destroy(glint.gameObject); });
            }
        }

        /// <summary>
        /// A shockwave ring, spreading and going out: on the ground, squashed to the hill, unless
        /// <paramref name="squash"/> says otherwise. A round one (1) is a shock in the air,
        /// which is what an overcharge throws - it may not draw on the floor (invariant 37ev).
        /// </summary>
        void Ripple(Vector2 at, Color tint, float reach, float life, float delay,
                    float squash = GroundSquash)
        {
            var ring = Lit("Ripple", _fx, StrikeFx.Ring, Pal.A(tint, 0f), new Vector2(reach, reach * squash));
            if (ring == null) return;

            var rt = ring.rectTransform;
            rt.anchoredPosition = at;
            rt.localScale = Vector3.one * .12f;

            Tween.Run(life, Ease.OutQuint, t =>
            {
                if (!ring) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.12f, 1f, t);
                float a = t < .12f ? t / .12f : 1f - (t - .12f) / .88f;
                ring.color = Pal.A(Color.Lerp(Color.white, tint, Mathf.Clamp01(t * 3f)), a * a);
            }, ring).Delay(delay).OnDone(() => { if (ring) Destroy(ring.gameObject); });
        }

        /// <summary>
        /// The ground answering a bolt out of the sky: a radial burst, the crack left in it
        /// that lingers, and the warm rung under everything. All squashed to the hill, all behind
        /// whatever else is in the layer. <b>The stormcall's alone</b> - an overcharge leaves
        /// nothing on the ground (<c>SiegeView.Arcburst</c>).
        /// </summary>
        void Answer(Vector2 at, Color tint, float cells, string mark, float markCells,
                    float markAlpha = .95f)
        {
            var warm = Lit("Warm", _fx, Art.Glow(128, 2.2f), Pal.A(Pal.Ember, .7f),
                           new Vector2(Cell * cells * .9f, Cell * cells * .9f * GroundSquash));
            warm.rectTransform.anchoredPosition = at;
            warm.transform.SetAsFirstSibling();
            Tween.Run(.5f, Ease.OutQuad, t =>
            {
                if (!warm) return;
                warm.transform.localScale = Vector3.one * Mathf.Lerp(.5f, 1.2f, t);
                warm.color = Pal.A(Pal.Ember, .7f * (1f - t));
            }, warm).OnDone(() => { if (warm) Destroy(warm.gameObject); });

            var left = Lit("Mark", _fx, mark, Pal.A(Color.Lerp(tint, Color.white, .35f), 0f),
                           new Vector2(Cell * markCells, Cell * markCells * GroundSquash));
            if (left != null)
            {
                var rt = left.rectTransform;
                rt.anchoredPosition = at;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                rt.SetSiblingIndex(1);
                Tween.Run(1.1f, Ease.Linear, t =>
                {
                    if (!left) return;
                    float grow = Ease.OutQuint(Mathf.Clamp01(t / .16f));
                    rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1f, grow);
                    float a = t < .25f ? Mathf.Clamp01(t / .05f) : 1f - (t - .25f) / .75f;
                    left.color = Pal.A(Color.Lerp(tint, Color.white, .35f), markAlpha * a);
                }, left).OnDone(() => { if (left) Destroy(left.gameObject); });
            }

            var wave = Lit("Wave", _fx, StrikeFx.Wave, Pal.A(tint, .9f),
                           new Vector2(Cell * cells, Cell * cells * (GroundSquash + .1f)));
            if (wave != null)
            {
                var rt = wave.rectTransform;
                rt.anchoredPosition = at;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                rt.SetSiblingIndex(2);
                Tween.Run(.3f, Ease.OutQuad, t =>
                {
                    if (!wave) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.6f, 1.25f, t);
                    wave.color = Pal.A(tint, .9f * (1f - t) * (1f - t));
                }, wave).OnDone(() => { if (wave) Destroy(wave.gameObject); });
            }
        }

        /// <summary>
        /// Sparks thrown up and falling back: stars stretched along their flight, kicked upward
        /// and pulled down, so they arc rather than radiate. What <c>Burst.Sparks</c> is not - a
        /// burst in a ring reads as an explosion, and a strike throws its debris <em>up</em>.
        /// </summary>
        void Embers(Vector2 at, Color tint, int count, float reach, float size)
        {
            var sprite = Kit(StrikeFx.Star) ?? Art.Spark(64);
            var host = UIKit.Node("Embers", _fx);
            float gravity = Cell * 9f;

            for (int i = 0; i < count; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float speed = reach * Random.Range(1.4f, 3.2f);
                var vel = new Vector2(Mathf.Cos(ang) * speed * .8f, Mathf.Sin(ang) * speed * .5f + reach * 2.2f);
                float life = Random.Range(.42f, .78f);
                float sz = size * Random.Range(.6f, 1.4f);

                var spark = Lit("s", host, sprite, Pal.A(Color.Lerp(tint, Color.white, .45f), 1f),
                                new Vector2(sz, sz));
                var rt = spark.rectTransform;
                rt.anchoredPosition = at;

                Tween.Run(life, Ease.Linear, t =>
                {
                    if (!spark) return;
                    float s = t * life;
                    var v = new Vector2(vel.x, vel.y - gravity * s);
                    rt.anchoredPosition = at + new Vector2(vel.x * s, vel.y * s - .5f * gravity * s * s);

                    // Stretched along its own flight, more the faster it moves.
                    float stretch = 1f + v.magnitude / (Cell * 6f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
                    rt.localScale = new Vector3(Mathf.Lerp(1f, .3f, t), stretch * Mathf.Lerp(1f, .5f, t), 1f);
                    spark.color = Pal.A(Color.Lerp(Color.white, tint, t), 1f - t * t);
                }, spark);
            }

            Tween.After(.9f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        /// <summary>Stars popping in and out round an impact, each on its own beat.</summary>
        void Twinkles(Vector2 at, Color tint, int count, float spread)
        {
            var host = UIKit.Node("Twinkles", _fx);

            for (int i = 0; i < count; i++)
            {
                var where = at + new Vector2(Random.Range(-spread, spread), Random.Range(-spread, spread) * GroundSquash);
                float size = Cell * Random.Range(.35f, .8f);
                float delay = Random.Range(0f, .28f);
                float spin = Random.Range(-40f, 40f);

                var star = Lit("t", host, StrikeFx.Glint, Pal.A(Color.Lerp(tint, Color.white, .6f), 0f),
                               new Vector2(size, size));
                if (star == null) break;

                var rt = star.rectTransform;
                rt.anchoredPosition = where;
                rt.localScale = Vector3.zero;

                Tween.Run(.24f, Ease.Linear, t =>
                {
                    if (!star) return;
                    float k = Mathf.Sin(t * Mathf.PI);
                    rt.localScale = Vector3.one * k;
                    rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                    star.color = Pal.A(Color.Lerp(tint, Color.white, .6f), k);
                }, star).Delay(delay);
            }

            Tween.After(.65f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        /// <summary>The whole board lit for a beat in <paramref name="tint"/>: what a strike does to a sky.</summary>
        void Lightup(Color tint, float alpha, float life)
        {
            if (_fx == null) return;

            var flash = Lit("Lightup", _fx, Art.Round(4), Pal.A(tint, alpha), new Vector2(Span.x, Span.y));
            flash.rectTransform.anchoredPosition = Vector2.zero;
            flash.transform.SetAsLastSibling();

            Tween.Run(life, Ease.OutQuad, t =>
            {
                if (!flash) return;
                flash.color = Pal.A(tint, alpha * (1f - t));
            }, flash).OnDone(() => { if (flash) Destroy(flash.gameObject); });
        }

        static float Between(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}
