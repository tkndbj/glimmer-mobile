using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>The phoenix's mantle</b> (<c>SiegeBoard.Mantle</c>): the line rising in a burst of
    /// blue light that drives the hill back, a shield standing over every turret for as long as
    /// the model says it does, every hit it turns away rippling across it, and the shield
    /// breaking when its time runs out.
    ///
    /// <para>
    /// <b>Four beats, each drawn off the model and never timed beside it.</b> The rise and the
    /// push are drawn on the frame the report says the mantle went up
    /// (<see cref="SiegeReport.Mantled"/>). The shield is put up, kept and taken down off
    /// <see cref="SiegeBoard.CoverLeft"/> every frame, per post. A hit is drawn off
    /// <see cref="SiegeReport.Turned"/>. The break is drawn on the frame a post's clock reads
    /// nought. None of it can show a covered post the model has stopped covering, or the reverse.
    /// </para>
    /// <para>
    /// <b>One shell for two covers.</b> A revive (<c>SiegeWard.Barrier</c>) is the mantle on one
    /// post, so it is drawn by the same shell off the same reading - which is why this runs on
    /// every board rather than only on a Shuffle build, at the cost of one comparison per post a
    /// frame while nothing is covered.
    /// </para>
    /// <para>
    /// <b>Blue on purpose.</b> Every hurt on this line is warm (a blow's rose, a fall's ember)
    /// and the golden dome is gold, so a cold, bright blue says "protected" at a glance and
    /// cannot be mistaken for either.
    /// </para>
    /// <para>
    /// <b>Generated and additive, never a bought sprite</b>: the shell and the honeycomb are cut
    /// by <see cref="Art.MantleShell"/> and <see cref="Art.MantleComb"/>, so nothing here waits
    /// on an address and nothing can come up as a white rectangle (invariant 7b). Every image is
    /// built once when the shield goes up and moved every frame. Hits draw with the pooled
    /// pieces the rest of the board already uses. While the shield is down this costs one float
    /// comparison a frame.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the look
        /// <summary>The mantle's blue: the board's own azure, lifted toward ice so it glows on a dark hill.</summary>
        static readonly Color MantleHue = new Color(.36f, .78f, 1f);

        /// <summary>The white-blue of the mantle's brightest parts: its rim, a struck cell, the motes.</summary>
        static readonly Color MantleIce = new Color(.80f, .95f, 1f);

        /// <summary>Seconds the shell takes to form round a post, and to fade once broken.</summary>
        const float MantleForm = .45f, MantleFade = .22f;

        /// <summary>Seconds before the end at which the shell starts to flicker, so the player is warned.</summary>
        const float MantleWarn = 2f;

        /// <summary>How tall the shell stands over a post, in cells; its width follows the art's aspect.</summary>
        const float MantleTall = 2.55f;

        /// <summary>Real seconds between two hit drawings on one post, so a crowd at the line is a shimmer, not a strobe.</summary>
        const float MantleHitEvery = .12f;

        /// <summary>Motes circling each shell, and sparks rising through it.</summary>
        const int MantleMotes = 3, MantleRisers = 4;

        /// <summary>The slow motion while the hill is driven back: how slow, and for how long in real time.</summary>
        const float MantlePace = .35f, MantleHold = .55f;

        /// <summary>One post's shield, built once and moved every frame.</summary>
        sealed class MantleLook
        {
            public RectTransform Root, Spin;
            public Image Floor, Fill, Comb, Shell;
            public Image[] Motes, Risers;

            /// <summary>How bright the last hit left it, falling back to nought.</summary>
            public float Struck;

            /// <summary>When it was last drawn being hit, in real seconds.</summary>
            public float HitAt = -1f;

            /// <summary>How fully this post's shell stands, eased, from nought (gone) to one (formed).</summary>
            public float Shown;

            /// <summary>Whether this post was covered last frame: the edge its break is drawn on.</summary>
            public bool WasUp;
        }

        RectTransform _mantleHost;
        MantleLook[] _mantles;

        float _mantleAge;

        /// <summary>
        /// The board the shields were drawn for. A run dealt again hands the view a new board
        /// whose clock reads nought, and that is a new run, not the old mantle running out, so
        /// the old shields are taken down without being drawn breaking.
        /// </summary>
        SiegeBoard _mantleBoard;

        float MantleHigh => Cell * MantleTall;

        /// <summary>
        /// The shell's width: the art's own aspect, kept inside a post's share of the line plus a
        /// tenth, so neighbouring shells just touch and read as one linked wall.
        /// </summary>
        float MantleWide
        {
            get
            {
                float own = MantleHigh * MantleAspect;
                int n = _posts != null ? _posts.Length : 1;
                float share = Span.x / (n + .6f) * 1.1f;
                return Mathf.Min(own, share);
            }
        }

        /// <summary>The shell art's width over its height (<see cref="Art.MantleShell"/>'s default size).</summary>
        const float MantleAspect = 184f / 256f;

        /// <summary>
        /// Where a post's shell is centred: a little above the turret's middle, so the shell
        /// takes in the health bar above it and stops at the socket instead of spilling onto the
        /// field's plate below (measured on the render mirror).
        /// </summary>
        Vector2 MantleAt(int post) => new Vector2(PostX(post), _lineY + Cell * .18f);

        // ------------------------------------------------------------------ every frame
        /// <summary>
        /// Keeps the shields drawn true to <see cref="SiegeBoard.CoverLeft"/>, post by post: forms
        /// a shell as its clock starts, flickers it through its last <see cref="MantleWarn"/>
        /// seconds, breaks it on the frame the clock reaches nought and takes it down once faded.
        /// Called on every board (<c>SiegeView.Clock</c>); while nothing is covered and nothing is
        /// fading it reads one number per post and returns.
        /// </summary>
        void Mantling(float dt)
        {
            if (_board == null || _fx == null || _posts == null) return;

            if (_mantleBoard != _board)
            {
                if (_mantleHost) ScrapMantle();
                _mantleBoard = _board;
            }

            bool any = false;
            for (int i = 0; i < _posts.Length; i++)
                if (_board.CoverLeft(i) > 0f) { any = true; break; }

            if (!any && !_mantleHost) return;

            if (any && (!_mantleHost || _mantles == null || _mantles.Length != _posts.Length)) BuildMantle();
            if (!_mantleHost) return;

            _mantleAge += dt;

            float breathe = .88f + .12f * Mathf.Sin(_mantleAge * 2.6f);
            float wide = MantleWide, high = MantleHigh;
            bool showing = false, broke = false;

            for (int i = 0; i < _mantles.Length; i++)
            {
                var look = _mantles[i];
                if (look == null || !look.Root) continue;

                float left = _board.CoverLeft(i);
                bool up = left > 0f;

                // The break, on the edge, while the shell is still there to break.
                if (look.WasUp && !up) { MantleBreak(i); broke = true; }
                look.WasUp = up;

                look.Shown = Mathf.MoveTowards(look.Shown, up ? 1f : 0f, dt / (up ? MantleForm : MantleFade));

                // A fallen post wears no shell, and one that has finished fading is put away.
                bool standing = i < _board.Wards.Count && _board.Wards[i].Alive;
                bool drawn = standing && (up || look.Shown > 0f);
                look.Root.gameObject.SetActive(drawn);
                if (!drawn) continue;

                showing = true;

                // Formed with an overshoot, so the shell arrives rather than fades in; faded out plain.
                float form = up ? Ease.OutBack(look.Shown) : look.Shown;

                // **The warning: a flicker that quickens toward the end**, so the player sees the
                // shield is about to fail before it does. Off until the last `MantleWarn` seconds.
                float flicker = 1f;
                if (up && left < MantleWarn)
                {
                    float k = 1f - left / MantleWarn;
                    float beat = Mathf.Abs(Mathf.Sin(_mantleAge * Mathf.Lerp(7f, 24f, k)));
                    flicker = Mathf.Lerp(1f, .30f + .70f * beat, k);
                }

                look.Struck = Mathf.MoveTowards(look.Struck, 0f, dt * 3.2f);
                float hit = look.Struck;
                float lit = look.Shown * flicker;

                look.Root.anchoredPosition = MantleAt(i);
                look.Root.localScale = new Vector3(form, form, 1f);

                // A struck shell flares toward white and swells a touch, then settles.
                float swell = 1f + hit * .06f;
                var rim = Color.Lerp(MantleHue, MantleIce, .35f + hit * .65f);

                Paint(look.Shell, rim, (.70f * breathe + hit * .25f) * lit, swell);
                Paint(look.Comb, Color.Lerp(MantleHue, MantleIce, hit * .7f),
                      (.22f + .12f * Mathf.Sin(_mantleAge * 3.3f + i) + hit * .5f) * lit, swell);
                Paint(look.Fill, MantleHue, (.10f * breathe + hit * .12f) * lit, 1f);

                // The floor turns under the post, a hexagon spinning on the ground.
                if (look.Spin) look.Spin.localRotation = Quaternion.Euler(0f, 0f, _mantleAge * 38f + i * 15f);
                Paint(look.Floor, MantleHue, (.55f * breathe + hit * .3f) * lit, 1f);

                // Motes circling the shell on a tilted ring, bright in front and dim behind.
                for (int m = 0; m < look.Motes.Length; m++)
                {
                    var mote = look.Motes[m];
                    if (!mote) continue;

                    float a = _mantleAge * 2.1f + m * Mathf.PI * 2f / look.Motes.Length + i * .7f;
                    float front = (Mathf.Sin(a) + 1f) * .5f;
                    var rt = mote.rectTransform;
                    rt.anchoredPosition = new Vector2(Mathf.Cos(a) * wide * .47f,
                                                      Mathf.Sin(a) * high * .07f + Mathf.Cos(a) * high * .06f);
                    rt.localScale = Vector3.one * Mathf.Lerp(.55f, 1.1f, front);
                    rt.localRotation = Quaternion.Euler(0f, 0f, _mantleAge * 120f + m * 30f);
                    mote.color = Pal.A(MantleIce, Mathf.Lerp(.25f, .95f, front) * lit);
                }

                // Sparks rising through the shell and thinning out as they climb, each on its own beat.
                for (int s = 0; s < look.Risers.Length; s++)
                {
                    var riser = look.Risers[s];
                    if (!riser) continue;

                    float run = Mathf.Repeat(_mantleAge * .55f + s / (float)look.Risers.Length + i * .37f, 1f);
                    float sway = Mathf.Sin((run * 2.3f + s) * Mathf.PI) * wide * .08f;
                    float x = (((s * 7 + i * 3) % 5) / 4f - .5f) * wide * .55f + sway;

                    var rt = riser.rectTransform;
                    rt.anchoredPosition = new Vector2(x, Mathf.Lerp(-high * .38f, high * .40f, run));
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, .4f, run);
                    riser.color = Pal.A(MantleIce, Mathf.Sin(run * Mathf.PI) * .8f * lit);
                }
            }

            // One sound for every shell that broke this frame: four at once is a flam (37q).
            if (broke) Audio.Sfx("shatter", .42f, 1.3f);

            if (!any && !showing) ScrapMantle();
        }

        static void Paint(Image image, Color hue, float alpha, float scale)
        {
            if (!image) return;
            image.color = Pal.A(hue, Mathf.Clamp01(alpha));
            image.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Builds one shield per post, all clear: <see cref="Mantling"/> paints them in.</summary>
        void BuildMantle()
        {
            ScrapMantle();
            if (_posts == null) return;

            _mantleHost = UIKit.Node("Mantle", _fx);
            _mantleHost.SetAsFirstSibling();
            _mantles = new MantleLook[_posts.Length];

            float wide = MantleWide, high = MantleHigh;
            var shell = Art.MantleShell();
            var comb = Art.MantleComb();
            var glint = Kit(StrikeFx.Glint) ?? Art.Glint(96, 4);
            var spark = Kit(StrikeFx.Star) ?? Art.Spark(64);

            for (int i = 0; i < _posts.Length; i++)
            {
                var look = new MantleLook();
                look.Root = UIKit.Box("Post mantle", _mantleHost, new Vector2(wide, high),
                                      new Vector2(.5f, .5f), MantleAt(i));

                // The floor first, so everything else is drawn over it: a hexagon lying on the
                // ground at the post's footing, squashed to the hill's perspective, turning.
                var tilt = UIKit.Box("Floor", look.Root, Vector2.zero, new Vector2(.5f, .5f),
                                     new Vector2(0f, -high * .41f));
                tilt.localScale = new Vector3(1f, GroundSquash, 1f);
                look.Spin = UIKit.Box("Spin", tilt, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
                look.Floor = Lit("Hex", look.Spin, Art.HexRing(128, 7f), Color.clear, Vector2.one * wide * 1.05f);

                look.Fill = Lit("Air", look.Root, Art.Glow(128, 1.3f), Color.clear, new Vector2(wide, high) * .96f);
                look.Comb = Lit("Comb", look.Root, comb, Color.clear, new Vector2(wide, high));
                look.Shell = Lit("Shell", look.Root, shell, Color.clear, new Vector2(wide, high));

                look.Motes = new Image[MantleMotes];
                for (int m = 0; m < MantleMotes; m++)
                    look.Motes[m] = Lit("Mote", look.Root, glint, Color.clear, Vector2.one * Cell * .42f);

                look.Risers = new Image[MantleRisers];
                for (int s = 0; s < MantleRisers; s++)
                    look.Risers[s] = Lit("Riser", look.Root, spark, Color.clear, Vector2.one * Cell * .22f);

                _mantles[i] = look;
            }

            _mantleAge = 0f;
        }

        void ScrapMantle()
        {
            if (_mantleHost) Destroy(_mantleHost.gameObject);
            _mantleHost = null;
            _mantles = null;
        }

        // ------------------------------------------------------------------ the rise
        /// <summary>
        /// The phoenix's rise, on the frame the model put the mantle on: a blue burst at every
        /// post, a front of light driving up the hill to the crest, every body it pushed lit
        /// where the front reaches it, and a beat of slow motion so the push is seen.
        /// The posts stand back up a beat later (<see cref="Revived"/>), inside shells that are
        /// already forming.
        ///
        /// <b>The front is drawn even when nothing was pushed</b> (a hill holding only a boss),
        /// for the anvil's reason: a payoff that silently did nothing would read as a broken card.
        /// It dies against the boss instead.
        /// </summary>
        void Mantled(int repelled)
        {
            if (_fx == null || _posts == null) return;

            Lightup(MantleHue, .22f, .55f);
            ShakeBoard(Cell * .18f);

            for (int i = 0; i < _posts.Length; i++)
            {
                var at = MantleAt(i);
                Flash(at, MantleHue, 3.2f);
                Ripple(new Vector2(at.x, _lineY - Cell * .7f), MantleHue, Cell * 4.4f, .7f, i * .04f);
                Ripple(at, MantleIce, Cell * 3f, .5f, .08f + i * .04f, 1f);
                Embers(at, MantleHue, 10, Cell * .9f, Cell * .32f);
            }

            float from = _lineY + Cell * .6f;
            float over = MantleSweep;

            Front(from, _hillTop, over, Cell * 1.1f, MantleIce, .85f);
            Front(from, _hillTop, over * 1.2f, Cell * 2.4f, MantleHue, .45f);

            if (repelled > 0) Dilate(MantlePace, MantleHold);

            // Each body lit as the front reaches it: a blue burst where it is pushed from, and a
            // brace on a boss, which does not move (`SiegeRaider.Shove`).
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null || mob.Falling) continue;

                var node = mob.Node;
                bool boss = mob.Boss;
                float when = Mathf.Clamp01(Mathf.InverseLerp(from, _hillTop, node.anchoredPosition.y)) * over;

                Tween.After(when, () =>
                {
                    if (_fx == null || !node) return;

                    var stood = node.anchoredPosition;
                    if (boss)
                    {
                        FxWave(stood, MantleHue, 2.4f, .32f);
                        return;
                    }

                    FxPop(stood, Pal.A(MantleHue, .9f), 1.3f, .30f);
                    FxSparks(_fx, stood, MantleIce, 8, Cell * 1.6f, Cell * .16f, .45f);
                }, this);
            }

            Audio.Sfx("charge", .65f, 1.35f);
            Audio.SfxVaried("whoosh", .6f, .05f);
            Audio.Sfx("boom", .5f, 1.25f);
        }

        /// <summary>Seconds the rise's front takes to climb from the line to the crest.</summary>
        const float MantleSweep = .62f;

        /// <summary>
        /// A band of light climbing the hill from <paramref name="from"/> to <paramref name="to"/>,
        /// full board width, thinning as it goes: what the push looks like.
        /// </summary>
        void Front(float from, float to, float over, float deep, Color tint, float alpha)
        {
            var band = Lit("Mantle front", _fx, Art.SoftCapsule(40, 120), Pal.A(tint, 0f),
                           new Vector2(deep, Span.x * 1.04f));
            var rt = band.rectTransform;
            rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rt.anchoredPosition = new Vector2(0f, from);

            Tween.Run(over, Ease.OutCubic, t =>
            {
                if (!band) return;
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, t));
                rt.localScale = new Vector3(Mathf.Lerp(1f, .45f, t), 1f, 1f);
                float a = t < .1f ? t / .1f : 1f - (t - .1f) / .9f;
                band.color = Pal.A(Color.Lerp(Color.white, tint, Mathf.Clamp01(t * 2.5f)), alpha * a);
            }, band).OnDone(() => { if (band) Destroy(band.gameObject); });
        }

        // ------------------------------------------------------------------ a hit turned away
        /// <summary>
        /// A hit the mantle turned away from post <paramref name="post"/>: a hexagon of light
        /// where it struck the top of the shell, the shell flaring, a few sparks thrown back up
        /// the hill. At most one drawing per post every <see cref="MantleHitEvery"/> seconds, so
        /// a crowd at the line reads as a shield under strain rather than a strobe.
        /// </summary>
        void Turned(int post)
        {
            if (_fx == null || _mantles == null || post < 0 || post >= _mantles.Length) return;

            var look = _mantles[post];
            if (look == null || !look.Root) return;

            look.Struck = 1f;

            float now = Time.unscaledTime;
            if (look.HitAt >= 0f && now - look.HitAt < MantleHitEvery) return;
            look.HitAt = now;

            // Where the hill meets the shell: its upper arc, a little to one side.
            float wide = MantleWide, high = MantleHigh;
            float side = Random.Range(-.55f, .55f);
            var at = MantleAt(post) + new Vector2(side * wide * .5f,
                                                  Mathf.Sqrt(1f - side * side) * high * .46f);

            var hex = Lit("Mantle hit", _fx, Art.HexRing(128, 9f), Pal.A(MantleIce, 1f), Vector2.one * Cell * 1.1f);
            var rt = hex.rectTransform;
            rt.anchoredPosition = at;
            float spin = Random.Range(-40f, 40f);
            Tween.Run(.32f, Ease.OutQuad, t =>
            {
                if (!hex) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.45f, 1.35f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                hex.color = Pal.A(Color.Lerp(Color.white, MantleHue, t), 1f - t);
            }, hex).OnDone(() => { if (hex) Destroy(hex.gameObject); });

            Ripple(at, MantleHue, Cell * 1.8f, .3f, 0f, 1f);
            FxSparks(_fx, at, MantleIce, 5, Cell * 1.2f, Cell * .12f, .3f);
            Audio.SfxVaried("blocked", .22f, .10f);
        }

        // ------------------------------------------------------------------ the break
        /// <summary>
        /// One post's cover running out: its shell shatters into shards of light thrown outward
        /// and falling, with a ring. The shell then fades over <see cref="MantleFade"/>, and
        /// <see cref="Mantling"/> takes it down; the sound is <see cref="Mantling"/>'s, once for
        /// however many broke on the same frame.
        /// </summary>
        void MantleBreak(int post)
        {
            if (_fx == null || _mantles == null || post < 0 || post >= _mantles.Length) return;

            var look = _mantles[post];
            if (look == null || !look.Root || !look.Root.gameObject.activeSelf) return;

            float wide = MantleWide, high = MantleHigh;
            var at = MantleAt(post);

            const int Shards = 10;
            for (int s = 0; s < Shards; s++)
            {
                float a = Mathf.PI * 2f * (s + Random.Range(-.3f, .3f)) / Shards;
                var spot = at + new Vector2(Mathf.Cos(a) * wide * .48f, Mathf.Sin(a) * high * .48f);
                var drift = new Vector2(Mathf.Cos(a) * Cell * 1.2f, Mathf.Sin(a) * Cell * .6f - Cell * 1.6f);
                Cinder(spot, s % 3 == 0 ? MantleIce : MantleHue, Cell * Random.Range(.30f, .55f), .55f, drift);
            }

            Ripple(at, MantleHue, Cell * 3.4f, .4f, 0f, 1f);
            look.Struck = 1f;
        }
    }
}
