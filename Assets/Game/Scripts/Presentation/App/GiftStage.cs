using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The room a first step's gift is delivered into, and the one gesture both gifts are made of:
    /// a heavy thing <em>slammed</em> onto the ground (<c>WardGiftOverlay</c>, <c>ChestGiftOverlay</c>).
    ///
    /// <para>
    /// <b>Its own verb, on purpose.</b> The turret ceremonies pull light in and go nova; the rank
    /// ceremony climbs; a chest thumps and bursts. A gift <em>arrives</em>: it drops out of the dark
    /// into a pool of light and hits the floor - squash, a shake of the whole room, a shockwave
    /// lying flat on the ground, dust rolling out both ways, a plume hanging behind, rubble and
    /// sparks thrown up, cracks left in the floor - and then the dust settles round it while it
    /// glows. Two ceremonies of one verb share it, so a turret and a chest are delivered alike and
    /// read as one kind of moment (invariant 37z's rule, asked of a celebration).
    /// </para>
    /// <para>
    /// <b>Matter and light are separate layers</b> (<see cref="CeremonyFx.AttachSoft"/> under
    /// <see cref="CeremonyFx.Attach(RectTransform, string, (Sprite, int)[])"/>): dust can be darker
    /// than the room and is lit by the flash rather than drawn over it. <b>Every sprite is
    /// generated</b> (<see cref="Art.Smoke"/> and the rest), so this room is the same over a run's
    /// board and over the hub, whatever scope is loaded (invariant 7b).
    /// </para>
    /// </summary>
    public sealed class GiftStage
    {
        /// <summary>How far the sky overhangs the canvas on every side, so a shake never shows an edge.</summary>
        const float Overhang = 160f;

        /// <summary>The dark the room settles to. Deep enough that dust and light both read on it.</summary>
        const float SkyAlpha = .9f;

        static readonly Color SkyTop = Pal.Hex("#0B1830");
        static readonly Color SkyMid = Pal.Hex("#14284A");
        static readonly Color SkyLow = Pal.Hex("#0A0F1C");

        /// <summary>The spotlight's warm white.</summary>
        static readonly Color Spot = Pal.Hex("#FFE9B8");

        /// <summary>Dust kicked off the floor: a warm stone grey, lit from above by the puff's own shading.</summary>
        static readonly Color DustTint = Pal.Hex("#C9B79C");

        static readonly Color[] Rubble = { Pal.Hex("#6E5A49"), Pal.Hex("#8A7562"), Pal.Hex("#4E4038") };
        static readonly Color[] Hot = { Pal.Hex("#FFF4CE"), Pal.Hex("#FFC93C"), Pal.Hex("#FF8A2B") };

        static Sprite RingArt => Art.Ring(256, 6f);
        static Sprite GlowArt => Art.Glow(64, 1.6f);
        static Sprite SparkArt => Art.Spark(64);
        static Sprite RockArt => Art.Crystal(64);
        static Sprite CrackArt => Art.Capsule(12, 96);

        static Sprite[] _smoke;

        /// <summary>Four silhouettes of one puff, dealt round so a cloud is not one stamp repeated.</summary>
        public static Sprite[] SmokeArt
            => _smoke ??= new[] { Art.Smoke(128, 1), Art.Smoke(128, 2), Art.Smoke(128, 3), Art.Smoke(128, 4) };

        public RectTransform Root { get; private set; }

        /// <summary>The y the delivered thing stands on, in the room's own units (centre origin).</summary>
        public float GroundY { get; private set; }

        /// <summary>Smoke, dust and rubble: drawn with the ordinary blend, under the light.</summary>
        public CeremonyFx Matter { get; private set; }

        /// <summary>Flashes, rings and sparks: additive, over the matter.</summary>
        public CeremonyFx Light { get; private set; }

        /// <summary>Where the gift itself is parented: over the matter, under the light.</summary>
        public RectTransform Props { get; private set; }

        Image _sky, _spot, _pool, _vignette;
        RectTransform _shakes;
        readonly List<Image> _cracks = new List<Image>();

        /// <summary>
        /// Builds the room over <paramref name="content"/>: dark, with a pool of light on the floor at
        /// <paramref name="groundY"/>. It fades in over <paramref name="hush"/> seconds.
        /// </summary>
        public static GiftStage Build(RectTransform content, float groundY, float hush)
        {
            var stage = new GiftStage { GroundY = groundY };

            // Everything that shakes hangs off one node, so the shake is one tween on one value
            // (CRAFT.md: two tweens on one value are a bug) and the buttons, which do not shake,
            // stay where the player's thumb is.
            var shakes = UIKit.Node("Room", content);
            shakes.anchorMin = Vector2.zero;
            shakes.anchorMax = Vector2.one;
            shakes.offsetMin = shakes.offsetMax = Vector2.zero;
            stage._shakes = shakes;
            stage.Root = shakes;

            stage._sky = UIKit.Img("Sky", shakes, Art.Gradient(SkyLow, SkyMid, SkyTop), Color.white);
            Stretch(stage._sky.rectTransform, Overhang);
            stage._sky.raycastTarget = true;   // the room swallows taps aimed at the board behind it
            UIKit.Group(stage._sky.rectTransform).alpha = 0f;
            Tween.Fade(UIKit.Group(stage._sky.rectTransform), SkyAlpha, hush, Ease.OutQuad);

            // A shaft of light down onto the floor - the stage the gift will land on, lit before
            // anything falls, so the eye is already where the slam will be.
            float shaftH = 2600f;
            stage._spot = Additive.Lit(UIKit.Img("Spot", shakes, Art.Shaft(128, 256, 1.4f, .02f), Pal.A(Spot, 0f),
                                                 new Vector2(820f, shaftH), new Vector2(.5f, .5f),
                                                 new Vector2(0f, groundY + shaftH * .5f - 40f)));
            stage._spot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);   // full at the floor
            stage._spot.raycastTarget = false;
            Tween.Fade(stage._spot, .16f, hush * 1.4f, Ease.OutQuad);

            stage._pool = Additive.Lit(UIKit.Img("Pool", shakes, Art.Glow(128, 1.8f), Pal.A(Spot, 0f),
                                                 new Vector2(1000f, 260f), new Vector2(.5f, .5f),
                                                 new Vector2(0f, groundY)));
            stage._pool.raycastTarget = false;
            Tween.Fade(stage._pool, .30f, hush * 1.4f, Ease.OutQuad);

            stage._vignette = UIKit.Img("Vignette", shakes, Art.Vignette(256), new Color(0f, 0f, 0f, .55f));
            Stretch(stage._vignette.rectTransform, Overhang);
            stage._vignette.raycastTarget = false;

            stage.Matter = CeremonyFx.AttachSoft(shakes, "Matter",
                                                 (SmokeArt[0], 24), (SmokeArt[1], 24), (SmokeArt[2], 24),
                                                 (SmokeArt[3], 24), (RockArt, 40));

            stage.Props = UIKit.Node("Props", shakes);
            stage.Props.anchorMin = stage.Props.anchorMax = new Vector2(.5f, .5f);
            stage.Props.sizeDelta = Vector2.zero;

            stage.Light = CeremonyFx.Attach(shakes, "Light", (RingArt, 6), (GlowArt, 40), (SparkArt, 120));

            return stage;
        }

        static void Stretch(RectTransform rt, float overhang)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-overhang, -overhang);
            rt.offsetMax = new Vector2(overhang, overhang);
        }

        // ------------------------------------------------------------- the fall
        /// <summary>
        /// Drops <paramref name="body"/> - pivoted at its foot - from above the screen onto
        /// <paramref name="foot"/>, landing at <paramref name="delay"/> + <paramref name="fall"/>
        /// seconds. Stretched along its fall, squashed into the floor, sprung back to its own scale.
        /// The impact itself is <see cref="Impact"/>'s, scheduled by the caller on the same beat.
        ///
        /// <para>
        /// <b>The pivot is the foot</b>, so the squash presses down into the ground rather than
        /// shrinking toward the thing's middle - the difference between landing and deflating.
        /// The body writes its position on one channel and its scale on another, and nothing else
        /// may tween either while this runs.
        /// </para>
        /// </summary>
        public static void Drop(RectTransform body, Vector2 foot, float delay, float fall, float settle,
                                Object owner)
        {
            if (!body) return;

            var from = foot + new Vector2(0f, 1500f);
            body.pivot = new Vector2(.5f, 0f);
            body.anchoredPosition = from;
            body.localScale = Vector3.one;
            UIKit.Group(body).alpha = 0f;

            Tween.Run(fall, Ease.InQuad, t =>
            {
                if (!body) return;
                body.anchoredPosition = Vector2.LerpUnclamped(from, foot, t);
                UIKit.Group(body).alpha = 1f;

                // Long and thin as it gathers speed - the read of weight moving fast.
                float s = Mathf.Lerp(1f, 1.24f, t);
                body.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f);
            }, owner, "fall").Delay(delay).OnDone(() =>
            {
                if (!body) return;

                // The squash into the floor, then a spring back up past rest and down again.
                Tween.Run(settle, Ease.Linear, t =>
                {
                    if (!body) return;
                    float wobble = Mathf.Exp(-6.5f * t) * Mathf.Cos(t * 15f);
                    body.localScale = new Vector3(1f + .26f * wobble, 1f - .30f * wobble, 1f);
                }, owner, "settle").OnDone(() => { if (body) body.localScale = Vector3.one; });
            });
        }

        // ------------------------------------------------------------- the slam
        /// <summary>
        /// A heavy thing has just landed at <paramref name="at"/> (its foot, on the floor): the flash,
        /// two shockwaves lying flat, dust rolling both ways, a plume hanging behind, rubble and
        /// sparks thrown up, cracks in the floor, the room shaking and the sound. <paramref name="weight"/>
        /// scales the lot - a second chest landing beside the first is a smaller thump.
        /// </summary>
        public void Impact(Vector2 at, Color accent, float weight = 1f)
        {
            if (Root == null) return;
            weight = Mathf.Clamp(weight, .3f, 1.4f);

            // A white-out of the room, gone before the dust has finished leaving.
            var flash = Additive.Lit(UIKit.Img("Flash", Root, Art.Glow(128, .9f), Pal.A(Spot, .85f * weight),
                                               new Vector2(1900f, 1300f), new Vector2(.5f, .5f), at + new Vector2(0f, 120f)));
            flash.raycastTarget = false;
            Tween.Fade(flash, 0f, .42f, Ease.OutQuad).OnDone(() => { if (flash) Object.Destroy(flash.gameObject); });

            // The floor rings: two waves lying flat, the warm one first and wider.
            Light.Shock(RingArt, at, Pal.A(Spot, .85f), 80f, 1250f * weight, .62f, 0f, .30f);
            Light.Shock(RingArt, at, Pal.A(accent, .75f), 60f, 820f * weight, .74f, .07f, .30f);

            // The dust: rolled out along the floor both ways, and a column hanging behind.
            Matter.Billow(SmokeArt, at + new Vector2(0f, 20f), Pal.A(DustTint, .92f),
                          Mathf.RoundToInt(30 * weight), 380f, 1250f * weight, 150f * weight, 1.7f);
            Matter.Plume(SmokeArt, at + new Vector2(0f, 40f), Pal.A(DustTint, .55f),
                         Mathf.RoundToInt(9 * weight), 120f * weight, 210f * weight, 2.1f, 170f);

            // Rubble, solid and tumbling, and sparks, hot and streaking.
            Debris(at + new Vector2(0f, 20f), Mathf.RoundToInt(16 * weight), 1150f * weight);
            Light.Spray(SparkArt, at + new Vector2(0f, 30f), Hot, Mathf.RoundToInt(46 * weight),
                        420f, 1350f * weight, 26f, .85f, 1500f, 2.2f, .05f, 380f);
            Light.Spray(GlowArt, at + new Vector2(0f, 40f), new[] { Pal.A(accent, .8f) },
                        Mathf.RoundToInt(14 * weight), 200f, 600f, 70f, .7f, 600f, 3f, 0f, 160f);

            Cracks(at, weight);

            if (_shakes) Tween.Shake(_shakes, 30f * weight, .5f);

            Audio.Sfx("boom", .85f * weight);
            Audio.SfxVaried("land", .9f);
        }

        /// <summary>
        /// Chips of floor thrown up and falling back: solid, tumbling, steady - never twinkling, which
        /// is <see cref="CeremonyFx.Shards"/>' light and would read as glitter rather than stone.
        /// </summary>
        void Debris(Vector2 at, int count, float fast)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = Mathf.Lerp(20f, 160f, (i + Random.value) / count) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                Matter.Emit(new CeremonyFx.Mote
                {
                    Sprite = RockArt,
                    Colour = Rubble[i % Rubble.Length],
                    At = at + new Vector2(Random.Range(-50f, 50f), 0f),
                    Velocity = dir * Random.Range(fast * .4f, fast),
                    Life = Random.Range(.9f, 1.3f),
                    Size = Random.Range(18f, 40f),
                    SizeEnd = Random.Range(14f, 30f),
                    Gravity = 2600f,
                    Drag = .6f,
                    Rotation = Random.Range(0f, 360f),
                    Spin = Random.Range(-620f, 620f),
                    FadePower = 3.2f,
                });
            }
        }

        /// <summary>
        /// The floor broken where it was hit: a few dark seams radiating from the impact, lying in
        /// the floor's own perspective. They stay, faded to a scar, which is what tells a player
        /// looking a second later that something heavy landed here.
        /// </summary>
        void Cracks(Vector2 at, float weight)
        {
            int n = 7;
            for (int i = 0; i < n; i++)
            {
                float angle = (i / (float)n) * 360f + Random.Range(-14f, 14f);
                float length = Random.Range(150f, 300f) * weight;
                float rad = angle * Mathf.Deg2Rad;

                // Laid out in the floor plane and squashed into the floor's perspective by hand, so
                // a seam heading away from the eye is short and one heading sideways is long.
                var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad) * .30f);
                var mid = at + dir * (length * .55f);
                float screenAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float screenLength = length * dir.magnitude;

                var crack = UIKit.Img("Crack", Root, CrackArt, new Color(.03f, .02f, .02f, 0f),
                                      new Vector2(screenLength, Random.Range(6f, 11f)), new Vector2(.5f, .5f), mid);
                crack.raycastTarget = false;
                crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, screenAngle);
                crack.rectTransform.SetSiblingIndex(_pool ? _pool.rectTransform.GetSiblingIndex() + 1 : 0);
                _cracks.Add(crack);

                Tween.Fade(crack, .75f, .08f).OnDone(() => { if (crack) Tween.Fade(crack, .38f, 1.6f, Ease.OutQuad); });
            }
        }

        // ------------------------------------------------------------- after the dust
        /// <summary>
        /// The gift standing in its light once the dust has gone: a slow glow behind it and embers
        /// lifting off the floor. Runs until the room goes.
        /// </summary>
        public void Glow(Vector2 centre, Color accent, float size)
        {
            if (Root == null) return;

            var rays = Additive.Lit(UIKit.Img("Rays", Props, Art.Rays(256, 14), Pal.A(accent, 0f),
                                              Vector2.one * size * 2.2f, new Vector2(.5f, .5f), centre));
            rays.raycastTarget = false;
            rays.rectTransform.SetAsFirstSibling();
            Tween.Fade(rays, .26f, .6f, Ease.OutQuad);
            rays.gameObject.AddComponent<GiftSpin>().DegreesPerSecond = -9f;

            var halo = Additive.Lit(UIKit.Img("Halo", Props, Art.Glow(128, 2f), Pal.A(accent, 0f),
                                              Vector2.one * size * 1.5f, new Vector2(.5f, .5f), centre));
            halo.raycastTarget = false;
            halo.rectTransform.SetSiblingIndex(1);
            Tween.Fade(halo, .42f, .5f, Ease.OutQuad).OnDone(() => { if (halo) Tween.Breathe(halo.transform, .07f, 2.4f); });

            Light.Rise(SparkArt, new Vector2(0f, GroundY + 20f), 300f, new[] { Pal.A(Spot, .9f), Pal.A(accent, .9f) },
                       7f, -1f, 18f, 120f);
        }

        /// <summary>Brightens the spotlight a touch for the reveal of what landed.</summary>
        public void Lift()
        {
            if (_spot) Tween.Fade(_spot, .24f, .5f, Ease.OutQuad);
            if (_pool) Tween.Fade(_pool, .42f, .5f, Ease.OutQuad);
        }
    }
}

namespace GlimmerGrove
{
    /// <summary>
    /// Turns its object steadily, on unscaled time, for as long as it lives. One value, one writer:
    /// nothing else may tween the rotation of what carries it (CRAFT.md).
    /// </summary>
    public sealed class GiftSpin : MonoBehaviour
    {
        public float DegreesPerSecond;

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            transform.localRotation = Quaternion.Euler(0f, 0f, transform.localEulerAngles.z + DegreesPerSecond * dt);
        }
    }
}
