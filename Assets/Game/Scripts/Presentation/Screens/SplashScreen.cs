using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Ads;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Launch screen: one floating island, waking up.
    ///
    /// <para>
    /// The bar tracks genuine work — every sprite, clip and generated texture the game
    /// needs is pulled into memory here, so the first tap on PLAY never stutters. What
    /// changed is what the player is shown while it happens.
    /// </para>
    ///
    /// <para>
    /// <b>The progress indicator is the game's own verb.</b> A light leaves a lantern at
    /// the near edge of the grove and walks a conduit up to the cottage door, lighting
    /// each lantern it reaches; how far along that walk it is <em>is</em> how far along
    /// the load is. There is no bar and no percentage, and that is the point rather than
    /// a flourish — a launch screen is the only place a puzzle game gets to say what it
    /// is before anybody has played it, and a percentage says nothing at all. It costs no
    /// delivered art either: a conduit here is the same <see cref="Art.Capsule"/> pair
    /// <c>TileView</c> draws an arm from, so it cannot drift from what a board looks like.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing here waits on the load to exist.</b> The sky is a generated gradient, the
    /// stars are generated discs, the conduit and every lantern are generated shapes. The
    /// three painted layers (<c>splash_isle</c>, <c>splash_far</c>, <c>splash_mist</c>) are
    /// pulled synchronously in <see cref="Build"/>, exactly as the old backdrop was — see
    /// <c>Tools/make_splash_art.py</c>, which composes the island out of the shipped grove
    /// catalog so the first thing a player sees is a picture of the thing they are being
    /// sold, drawn from the same art, at the same angle, by the same numbers.
    /// </para>
    ///
    /// <para>
    /// <b>Dawn is a tint, not a second set of art.</b> The island bakes lit; the screen
    /// walks its colour up from a cold night value to white while the two sky gradients
    /// cross-fade under it. That is one texture instead of two to keep in step, and it is
    /// the only version in which the light arrives <em>continuously</em> rather than in
    /// whatever steps a pair of bakes happened to be cut at.
    /// </para>
    /// </summary>
    public sealed class SplashScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// How long the screen stands, and how fast the light is allowed to walk.
        ///
        /// <para>
        /// The rate is a ceiling rather than a speed: <c>_shown</c> chases the real
        /// <c>_target</c> and can only ever be behind it, so a slow device stretches the
        /// walk honestly while a warm Editor cannot finish it in a blink. Without the
        /// ceiling everything below happens in under a second and is then waited out,
        /// which is a loading screen that lies in the flattering direction.
        /// </para>
        /// </summary>
        const float MinimumShow = 2.5f;
        const float LightRate = .55f;
        const float FinaleHold = .55f;

        // ------------------------------------------------------------- the island
        const float IsleWidth = 1100f;
        /// <summary>
        /// How far below centre the island hangs. Set against the wordmark rather than by
        /// eye: the sprite's top edge is the cottage roof, and it has to clear the bottom of
        /// the subtitle. The pair were 20 points apart at the first size that felt bold
        /// enough, which is a collision on any phone whose font renders a shade larger.
        /// </summary>
        const float IsleDrop = -285f;

        /// <summary>
        /// The route the light takes, in the island picture's own space: <c>x</c> across it
        /// from the left, <c>y</c> down it from the top, both 0..1.
        ///
        /// <para>
        /// Fractions of the sprite rather than reference-canvas points, so re-running the art
        /// tool at a different size moves the route with the island instead of leaving it
        /// hanging in the sky. These are <em>anchors</em>, not stops: the light actually
        /// travels a Catmull-Rom spline through them, because a light that turns corners
        /// reads as a diagram and a light that curves reads as a light. It enters at the near
        /// right of the grass, runs round the front past the flowers and the pond, and climbs
        /// the path to the cottage door.
        /// </para>
        /// </summary>
        static readonly Vector2[] Route =
        {
            new Vector2(.880f, .600f),     // the near right corner of the grass
            new Vector2(.720f, .642f),
            new Vector2(.560f, .652f),
            new Vector2(.400f, .600f),     // the flowers
            new Vector2(.270f, .520f),     // the lily pads
            new Vector2(.170f, .432f),     // the foot of the rune stone
            new Vector2(.300f, .352f),     // the stone path
            new Vector2(.392f, .296f),     // the cottage door
        };

        /// <summary>
        /// Where the two residents stand, same space. Off the route rather than on it, so the
        /// light passes <em>by</em> them and the trail never draws across a face.
        /// </summary>
        static readonly Vector2[] Perches =
        {
            new Vector2(.385f, .585f),
            new Vector2(.700f, .545f),
        };

        /// <summary>The cottage front, which lights last and stays lit.</summary>
        static readonly Vector2 Hearth = new Vector2(.455f, .272f);

        const int PerSegment = 10;         // spline samples between one anchor and the next

        static readonly Color Asleep = new Color(.42f, .53f, .68f, .48f);
        static readonly Color Awake = new Color(1f, .84f, .46f, 1f);

        static readonly Color NightLow = Pal.Hex("#2B3660");
        static readonly Color NightMid = Pal.Hex("#1B2247");
        static readonly Color NightTop = Pal.Hex("#0C1130");
        static readonly Color DawnLow = Pal.Hex("#FFC98A");
        static readonly Color DawnMid = Pal.Hex("#9A8AC0");
        static readonly Color DawnTop = Pal.Hex("#3E7BC4");

        readonly List<Text> _letters = new List<Text>();
        readonly List<Color> _letterTint = new List<Color>();
        readonly List<Image> _dust = new List<Image>();
        readonly List<float> _dustSeed = new List<float>();
        readonly List<Vector2> _path = new List<Vector2>();
        readonly List<Image> _bloom = new List<Image>();
        readonly List<Sleeper> _sleepers = new List<Sleeper>();

        Image _dawn, _sun, _isle, _isleBloom, _hearth;
        Image _wisp, _wispHalo;
        RectTransform _sunRT, _wire;
        CanvasGroup _stars;
        Text _flavour;
        float _shown, _target, _isleHeight;
        int _litBlooms;
        bool _flared;

        static readonly string[] Flavour =
        {
            "waking the fireflies",
            "polishing the heart-crystals",
            "teaching critters to snore",
            "untangling the conduits",
            "brewing the morning dew",
            "letting the light in",
        };

        protected override void Build()
        {
            BuildSky();
            BuildIsland();
            BuildLogo();
            BuildCaption();
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------- sky
        void BuildSky()
        {
            UIKit.Img("Night", Content, Art.Gradient(NightLow, NightMid, NightTop, 256), Color.white);
            _dawn = UIKit.Img("Dawn", Content, Art.Gradient(DawnLow, DawnMid, DawnTop, 256),
                              new Color(1, 1, 1, 0f));

            BuildStars();

            // The sun itself, behind everything, climbing out of the mist as the load runs.
            // A glow rather than a disc: a hard edge would have to be positioned exactly and
            // would read as a moon, and nothing here wants a second celestial body.
            _sun = UIKit.Img("Sun", Content, Art.Glow(128, 1.5f), new Color(1f, .78f, .42f, 0f),
                             new Vector2(1500f, 1500f), new Vector2(.5f, .5f), new Vector2(120f, -700f));
            _sunRT = (RectTransform)_sun.transform;

            var far = Art.S("Bg/splash_far");
            if (far != null)
            {
                var host = UIKit.Node("FarHost", Content);
                var bob = UIKit.Box("FarBob", host, new Vector2(1160f, 1160f * far.rect.height / far.rect.width),
                                    new Vector2(.5f, .5f), new Vector2(0f, 330f));
                UIKit.Img("Far", bob, far, new Color(1, 1, 1, .92f));
                Parallax.Attach(host, 9f);
                Tween.Bob(bob, 9f, 7.4f);
            }

            Mist("MistBack", 520f, -470f, .78f, 24f, 1.18f);
        }

        void BuildStars()
        {
            var host = UIKit.Node("Stars", Content);
            _stars = host.gameObject.AddComponent<CanvasGroup>();
            var rnd = new System.Random(41);
            for (int i = 0; i < 54; i++)
            {
                float size = 4f + (float)rnd.NextDouble() * 8f;
                var img = UIKit.Img("s" + i, host, Art.Glow(64, 1.7f),
                                    new Color(1f, .97f, .88f, .3f + (float)rnd.NextDouble() * .55f),
                                    Vector2.one * size, new Vector2(.5f, .5f),
                                    new Vector2((float)rnd.NextDouble() * 1060f - 530f,
                                                180f + (float)rnd.NextDouble() * 780f));
                Tween.Breathe(img.transform, .35f, 1.4f + (float)rnd.NextDouble() * 2.6f,
                              (float)rnd.NextDouble() * 6f);
            }
        }

        /// <summary>
        /// A band of cloud, scrolled as two copies of one tileable strip.
        ///
        /// <para>
        /// The strip is seamless left to right (see the art tool), so a second copy laid
        /// exactly one width along and moved with it makes an endless bank out of one
        /// texture. Two bands are drawn — one behind the island and one in front of its
        /// root — which is what gives a flat picture its depth for the price of one sprite.
        /// </para>
        /// </summary>
        void Mist(string name, float height, float y, float alpha, float speed, float scale)
        {
            var strip = Art.S("Bg/splash_mist");
            if (strip == null) return;

            var host = UIKit.Box(name, Content, new Vector2(1080f, height), new Vector2(.5f, .5f),
                                 new Vector2(0f, y));
            float w = 1080f * scale;
            var a = UIKit.Img("a", host, strip, new Color(1, 1, 1, alpha),
                              new Vector2(w, height), new Vector2(.5f, .5f), Vector2.zero);
            var b = UIKit.Img("b", host, strip, new Color(1, 1, 1, alpha),
                              new Vector2(w, height), new Vector2(.5f, .5f), new Vector2(w, 0f));
            MistDrift.Attach((RectTransform)a.transform, (RectTransform)b.transform, w, speed);
        }

        // ---------------------------------------------------------------- island
        void BuildIsland()
        {
            var sprite = Art.S("Bg/splash_isle");
            _isleHeight = sprite != null ? IsleWidth * sprite.rect.height / sprite.rect.width : 1150f;

            var host = UIKit.Node("IsleHost", Content);
            var bob = UIKit.Box("IsleBob", host, new Vector2(IsleWidth, _isleHeight),
                                new Vector2(.5f, .5f), new Vector2(0f, IsleDrop));

            // A warm bloom behind the island, so the dawn reads as coming from *behind* it
            // rather than as the picture simply getting brighter.
            _isleBloom = UIKit.Img("Bloom", bob, Art.Glow(128, 2.1f), new Color(1f, .80f, .44f, 0f),
                                   new Vector2(IsleWidth * 1.45f, _isleHeight * 1.15f),
                                   new Vector2(.5f, .5f), new Vector2(0f, 90f));

            if (sprite != null)
                _isle = UIKit.Img("Isle", bob, sprite, Asleep,
                                  new Vector2(IsleWidth, _isleHeight), new Vector2(.5f, .5f), Vector2.zero);

            _wire = UIKit.Box("Wire", bob, new Vector2(IsleWidth, _isleHeight),
                              new Vector2(.5f, .5f), Vector2.zero);
            BuildLight();

            Parallax.Attach(host, 24f);
            Tween.Bob(bob, 13f, 5.2f);

            Fireflies.Spawn(Content, 30, new Color(1f, .94f, .70f), 5f, 20f);
            Mist("MistFront", 460f, -830f, .95f, -32f, 1.30f);

            var vig = UIKit.Img("Vignette", Content, Art.Vignette(256), new Color(.02f, .05f, .12f, .42f));
            vig.type = Image.Type.Simple;
        }

        Vector2 OnIsle(Vector2 uv)
            => new Vector2((uv.x - .5f) * IsleWidth, (.5f - uv.y) * _isleHeight);

        /// <summary>
        /// The route as a curve.
        ///
        /// <para>
        /// Catmull-Rom through the anchors, sampled evenly, with the ends doubled so the
        /// first and last segments bend the same way as the rest. The first version of this
        /// screen joined the anchors with straight capsules and hard discs, and it read as a
        /// network diagram pasted over a painting rather than as light in a grove — which is
        /// the whole difference between the two, and it is curvature and softness rather than
        /// colour.
        /// </para>
        /// </summary>
        void BuildPath()
        {
            for (int i = 0; i < Route.Length - 1; i++)
            {
                var p0 = Route[Mathf.Max(0, i - 1)];
                var p1 = Route[i];
                var p2 = Route[i + 1];
                var p3 = Route[Mathf.Min(Route.Length - 1, i + 2)];

                for (int k = 0; k < PerSegment; k++)
                    _path.Add(OnIsle(CatmullRom(p0, p1, p2, p3, k / (float)PerSegment)));
            }
            _path.Add(OnIsle(Route[Route.Length - 1]));
        }

        static Vector2 CatmullRom(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f * ((2f * b)
                          + (-a + c) * t
                          + (2f * a - 5f * b + 4f * c - d) * t2
                          + (-a + 3f * b - 3f * c + d) * t3);
        }

        /// <summary>
        /// The light itself: a dusting of small glows along the curve that lights behind the
        /// wisp, a soft bloom wherever the route touches something, and the wisp on the front
        /// of it. Every part of it is a generated shape, so none of it waits on the load.
        /// </summary>
        void BuildLight()
        {
            BuildPath();

            var rnd = new System.Random(19);
            for (int i = 0; i < _path.Count; i++)
            {
                float size = 15f + (float)rnd.NextDouble() * 13f;
                // Nudged off the curve, because a perfectly strung line of beads is the map
                // marker this screen is trying not to be.
                var jitter = new Vector2((float)rnd.NextDouble() * 14f - 7f,
                                         (float)rnd.NextDouble() * 12f - 6f);
                var img = UIKit.Img("d" + i, _wire, Art.Glow(64, 1.3f), Pal.A(Awake, 0f),
                                    Vector2.one * size, new Vector2(.5f, .5f), _path[i] + jitter);
                _dust.Add(img);
                _dustSeed.Add((float)rnd.NextDouble() * 8f);
            }

            for (int i = 0; i < Route.Length; i++)
            {
                bool last = i == Route.Length - 1;
                _bloom.Add(UIKit.Img("b" + i, _wire, Art.Glow(128, 2.1f), Pal.A(Awake, 0f),
                                     Vector2.one * (last ? 300f : 190f), new Vector2(.5f, .5f),
                                     OnIsle(Route[i])));
            }

            // The cottage, which is the destination and the only thing that stays properly lit.
            _hearth = UIKit.Img("Hearth", _wire, Art.Glow(128, 1.9f), Pal.A(new Color(1f, .88f, .58f), 0f),
                                Vector2.one * 340f, new Vector2(.5f, .5f), OnIsle(Hearth));

            _wispHalo = UIKit.Img("WispHalo", _wire, Art.Glow(128, 1.7f), Pal.A(Awake, .55f),
                                  Vector2.one * 150f, new Vector2(.5f, .5f), _path[0]);
            _wisp = UIKit.Img("Wisp", _wire, Art.Glow(64, 1.9f), Pal.A(Pal.Radiance, .95f),
                              Vector2.one * 46f, new Vector2(.5f, .5f), _path[0]);
        }

        /// <summary>
        /// The two residents, asleep until the light gets to them.
        ///
        /// <para>
        /// <b>Which two is derived, never typed.</b> The starter is whoever the roster gates
        /// at nothing (<see cref="AvatarCatalog.Starter"/>, invariant 16f) and the second is
        /// the next companion with an animation, so a drop that changes who a new player
        /// begins with changes the launch screen with nobody editing this file.
        /// </para>
        /// <para>
        /// Built from <see cref="Run"/> rather than <see cref="Build"/>, because the roster
        /// arrives with the content and the content is what this screen is loading. That is
        /// safe rather than lucky: the light has moved a fraction of the way by then, and a
        /// sleeper the wisp has already passed simply starts awake.
        /// </para>
        /// </summary>
        void BuildSleepers()
        {
            if (_wire == null || _sleepers.Count > 0) return;

            var picked = new List<AvatarDefinition>();
            var starter = AvatarCatalog.Starter;
            if (starter.HasAnimation) picked.Add(starter);

            foreach (var a in AvatarCatalog.All)
            {
                if (picked.Count >= Perches.Length) break;
                if (!a.HasAnimation || a.Id == starter.Id) continue;
                picked.Add(a);
            }

            for (int i = 0; i < picked.Count && i < Perches.Length; i++)
            {
                var frames = Art.Frames("Critters/" + picked[i].Animated);
                if (frames == null || frames.Length == 0) continue;

                float w = IsleWidth * .150f;
                float h = w * frames[0].rect.height / frames[0].rect.width;
                var foot = OnIsle(Perches[i]);

                UIKit.Img("Shade" + i, _wire, Art.Glow(64, 1.2f), new Color(.05f, .10f, .07f, .30f),
                          new Vector2(w * .74f, w * .30f), new Vector2(.5f, .5f),
                          foot + new Vector2(0f, -h * .04f));

                var img = UIKit.Img("Sleeper" + i, _wire, frames[0], Asleep,
                                    new Vector2(w, h), new Vector2(.5f, .5f),
                                    foot + new Vector2(0f, h * .42f));
                Flipbook.Attach(img, frames, 12f);
                img.transform.localScale = Vector3.one * .86f;

                _sleepers.Add(new Sleeper { Img = img, At = foot, Wake = NearestOnPath(foot) });
            }

            // Drawn under the wisp and its dust, so nothing ever crosses a face.
            foreach (var s in _sleepers) s.Img.transform.SetSiblingIndex(0);
        }

        /// <summary>How far along the path (0..1) the wisp is when it passes a point.</summary>
        float NearestOnPath(Vector2 at)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _path.Count; i++)
            {
                float d = (_path[i] - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return _path.Count < 2 ? 1f : best / (float)(_path.Count - 1);
        }

        sealed class Sleeper
        {
            public Image Img;
            public Vector2 At;
            public float Wake;
            public bool Awake;
        }

        // ------------------------------------------------------------------ logo
        void BuildLogo()
        {
            var host = UIKit.Box("Logo", Content, new Vector2(1000f, 460f), new Vector2(.5f, .5f),
                                 new Vector2(0f, 640f));
            var bloom = UIKit.Img("Bloom", host, Art.Glow(128, 1.6f), new Color(1f, .84f, .42f, .20f),
                                  new Vector2(1120f, 640f), new Vector2(.5f, .5f), new Vector2(0f, 20f));
            Tween.Breathe(bloom.transform, .07f, 4.2f);

            Word(host, "GLIMMER", 140, Pal.Gold, new Vector2(0f, 66f));
            Word(host, "GROVE", 96, Pal.Cream, new Vector2(0f, -62f));

            UIKit.Titled("Sub", host, "a puzzle of light", 34, new Color(1f, .95f, .82f, .62f),
                         TextAnchor.MiddleCenter, new Vector2(900f, 50f), new Vector2(.5f, .5f),
                         new Vector2(0f, -152f), 3f, 3f);
        }

        void Word(Transform parent, string word, int size, Color colour, Vector2 pos)
        {
            var row = UIKit.Box("W_" + word, parent, new Vector2(10f, size * 1.4f), new Vector2(.5f, .5f), pos);
            var g = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.childAlignment = TextAnchor.MiddleCenter;
            g.childControlWidth = true; g.childControlHeight = true;
            g.childForceExpandWidth = false; g.childForceExpandHeight = false;
            g.spacing = size * .02f;
            var fit = row.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var ch in word)
            {
                var t = UIKit.Titled("L", row, ch.ToString(), size, colour, TextAnchor.MiddleCenter,
                                     outline: 7f, shadow: 6f);
                var csf = t.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                _letters.Add(t);
                _letterTint.Add(colour);
            }
        }

        void BuildCaption()
        {
            _flavour = UIKit.Titled("Flavour", Content, Flavour[0], 33, new Color(1f, .95f, .84f, .85f),
                                    TextAnchor.MiddleCenter, new Vector2(900f, 48f), new Vector2(.5f, 0f),
                                    new Vector2(0f, 168f), 3f, 4f);

            UIKit.Titled("Credit", Content, "art & audio by CraftPix", 22, new Color(1, 1, 1, .32f),
                         TextAnchor.MiddleCenter, new Vector2(700f, 34f), new Vector2(.5f, 0f),
                         new Vector2(0f, 96f), 0f, 0f);
        }

        // ------------------------------------------------------------- the light
        void Update()
        {
            _shown = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * LightRate);

            float dawn = Mathf.SmoothStep(0f, 1f, _shown);
            if (_dawn) _dawn.color = new Color(1, 1, 1, dawn);
            if (_stars) _stars.alpha = Mathf.Clamp01(1f - dawn * 1.25f);

            if (_sun)
            {
                _sun.color = new Color(1f, .80f, .46f, .40f * dawn);
                _sunRT.anchoredPosition = new Vector2(120f, Mathf.Lerp(-980f, -430f, dawn));
            }
            if (_isle) _isle.color = Color.Lerp(Asleep, Color.white, Mathf.SmoothStep(0f, 1f, _shown * 1.1f));
            if (_isleBloom) _isleBloom.color = new Color(1f, .80f, .44f, .34f * dawn);

            WalkLight();
        }

        /// <summary>
        /// Moves the wisp to wherever the load has got to, lights the dust behind it, blooms
        /// whatever it has passed and wakes whoever it has reached. Interpolated along the
        /// curve rather than stepped between anchors, so it keeps moving even when the load
        /// does not — which is what stops a slow launch reading as a frozen one.
        /// </summary>
        void WalkLight()
        {
            if (_path.Count < 2 || _wisp == null) return;

            float span = _path.Count - 1;
            float walk = Mathf.Clamp(_shown * span, 0f, span);
            int seg = Mathf.Min(Mathf.FloorToInt(walk), _path.Count - 2);

            var at = Vector2.Lerp(_path[seg], _path[seg + 1], walk - seg);
            ((RectTransform)_wisp.transform).anchoredPosition = at;
            ((RectTransform)_wispHalo.transform).anchoredPosition = at;

            float breath = .78f + .22f * Mathf.Sin(Time.unscaledTime * 5.2f);
            _wispHalo.color = Pal.A(Awake, .50f * breath);
            _wisp.transform.localScale = Vector3.one * (.92f + .12f * breath);

            // Behind the wisp the dust stays lit and twinkles; ahead of it there is nothing.
            // The few grains right under the head burn brighter, which is the comet.
            float t0 = Time.unscaledTime;
            for (int i = 0; i < _dust.Count; i++)
            {
                var d = _dust[i];
                if (!d) continue;
                float behind = walk - i;
                if (behind < 0f) { d.color = Pal.A(Awake, 0f); continue; }

                float head = Mathf.Clamp01(1f - behind / 5f);
                float twinkle = .78f + .22f * Mathf.Sin(t0 * 2.6f + _dustSeed[i]);
                d.color = Pal.A(Awake, (.30f * Mathf.Clamp01(behind) + .45f * head) * twinkle);
            }

            for (int i = 0; i < _bloom.Count; i++)
            {
                float reach = Mathf.Clamp01((walk - i * PerSegment) / PerSegment);
                bool last = i == _bloom.Count - 1;
                if (_bloom[i]) _bloom[i].color = Pal.A(Awake, (last ? .42f : .26f) * reach);
            }
            if (_hearth)
            {
                float reach = Mathf.Clamp01((walk - (_path.Count - 1 - PerSegment)) / PerSegment);
                _hearth.color = Pal.A(new Color(1f, .88f, .58f), .55f * reach);
            }

            float here = walk / span;
            for (int i = 0; i < _sleepers.Count; i++)
                if (!_sleepers[i].Awake && here >= _sleepers[i].Wake) Wake(_sleepers[i]);

            while (_litBlooms < _bloom.Count &&
                   walk >= _litBlooms * PerSegment) Touch(_litBlooms++);

            if (!_flared && _shown > .999f) { _flared = true; Flare(); }
        }

        /// <summary>A grain of confetti where the light touches down. No sound: see
        /// <see cref="Flare"/> for why the whole screen makes exactly one.</summary>
        void Touch(int i)
        {
            if (i >= Route.Length) return;
            Burst.Sparks(_wire, OnIsle(Route[i]), Awake, 5, 90f, 15f, .5f);
        }

        /// <summary>
        /// A critter waking, which is the same beat the game itself is made of: the colour
        /// comes back, it stands up, and the light it was waiting for scatters.
        /// </summary>
        void Wake(Sleeper s)
        {
            s.Awake = true;
            if (!s.Img) return;

            Tween.Tint(s.Img, Color.white, .28f);
            Tween.Scale(s.Img.transform, 1f, .34f, Ease.OutBack);
            Tween.Punch(s.Img.transform, .26f, .42f).Delay(.2f);
            Burst.Sparks(_wire, s.At, Awake, 12, 170f, 22f, .62f);
        }

        /// <summary>
        /// The moment the light reaches the door: a ring off the cottage and a gold pass
        /// through the wordmark.
        ///
        /// <para>
        /// <b>This is the only sound the launch screen makes.</b> It used to chime once per
        /// stop, pitched up the scale — eight of them inside two seconds, which is not a
        /// melody, it is a machine gun, and it was the first thing anybody said about the
        /// screen. A launch screen is two and a half seconds long and the music is already
        /// playing; one arrival is all the punctuation it can carry.
        /// </para>
        /// </summary>
        void Flare()
        {
            var at = OnIsle(Hearth);
            var ring = UIKit.Img("Flare", _wire, Art.Ring(128, 9f), Pal.A(Pal.Radiance, .9f),
                                 Vector2.one * 90f, new Vector2(.5f, .5f), at);
            var rt = (RectTransform)ring.transform;
            Tween.Run(.9f, Ease.OutCubic, t =>
            {
                if (!rt) return;
                rt.sizeDelta = Vector2.one * Mathf.Lerp(90f, 820f, t);
                ring.color = Pal.A(Pal.Radiance, .8f * (1f - t));
            }, ring).OnDone(() => { if (ring) Destroy(ring.gameObject); });

            for (int i = 0; i < _letters.Count; i++)
            {
                var t = _letters[i];
                var home = _letterTint[i];
                if (t == null) continue;
                Tween.After(i * .035f, () =>
                {
                    if (!t) return;
                    Tween.Tint(t, Pal.Radiance, .12f).OnDone(() =>
                    {
                        if (t) Tween.Tint(t, home, .4f);
                    });
                    Tween.Punch(t.transform, .22f, .3f);
                }, t);
            }

            Audio.Sfx("chime2", .42f, 1.02f);
        }

        // ------------------------------------------------------------- the load
        /// <summary>
        /// Content first, because it names the chapters; then the shared chrome; then
        /// only the art of the chapter the player is actually about to see. Nothing
        /// here grows as the catalog does — the fiftieth chapter costs the same to
        /// launch as the first.
        /// </summary>
        IEnumerator Run()
        {
            float started = Time.unscaledTime;
            StartCoroutine(RotateFlavour());

            yield return LoadContent();                    // → .12

            // The residents, now that the roster the content carries exists. See
            // BuildSleepers for why they are not built with the rest of the screen.
            BuildSleepers();

            yield return LoadGlobalAssets();               // → .82

            // generated shapes: real CPU work, spread over a few frames
            var shapes = Preload.Shapes();
            for (int i = 0; i < shapes.Count; i++)
            {
                shapes[i]();
                _target = .82f + .08f * ((i + 1) / (float)shapes.Count);
                if ((i & 1) == 0) yield return null;
            }

            yield return LoadOpeningChapter();             // → 1.0

            _target = 1f;
            ContentBootstrap.BeginBackgroundRefresh();

            // All four of these are fire-and-forget for the same reason: nothing between
            // tapping the icon and playing a glade is allowed to wait on a network.
            CloudSaveService.BeginSync();

            // Prices. Deliberately fetched now rather than when somebody opens the shop:
            // asking the store for product metadata is a round trip that takes a second or
            // more on a cold cellular connection, and a shop whose cards are blank for that
            // second is a shop players back out of. It also picks up anything bought on a
            // previous launch and never credited, which is the recovery path for a purchase
            // interrupted by a crash — the one thing here worth starting early even if
            // nobody ever opens the tab. Started after the content has loaded, because the
            // list of products to ask about comes out of it.

            // The population's move counts, for the one line on the victory panel that
            // compares a player to everybody else. It is the most disposable request the
            // game makes — no sign-in, no writes, and an outcome nothing waits on — which
            // is why it is started here and never checked again.
            CloudSaveService.BeginStatsRefresh();

            StoreService.BeginConnect();

            // Consent, then mediation, in that order and never the other one. This is the
            // only thing on the splash that can put a dialog in front of the player — the
            // CMP's form, and on iOS Apple's tracking prompt — and it is here rather than in
            // Boot because neither belongs before the first scene has loaded. Nothing waits
            // on it: the offer buttons light up when readiness arrives, which is what
            // RewardedAds.Changed is for. See RewardedAds.StartAsync for why the order is
            // owned there rather than written out at this call site.
            RewardedAds.BeginStart();

            while (Time.unscaledTime - started < MinimumShow || _shown < .999f) yield return null;

            yield return new WaitForSecondsRealtime(FinaleHold);
            Flow.Go<HomeScreen>();
        }

        /// <summary>Shared chrome: buttons, icons, critters, sounds, the font.</summary>
        IEnumerator LoadGlobalAssets()
        {
            var progress = new Progress<float>(t => _target = .12f + .70f * t);
            var task = AssetLibrary.PreloadAsync(AssetManifest.GlobalAssets(), progress);

            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);

            // The worn companion, and only that one. The rest of the roster is loaded
            // by the screens that show it and dropped when they close, which is what
            // keeps launch costing the same whether there are five companions or a
            // hundred. Warmed here rather than lazily so the hub's first frame has it.
            Profile.WarmWornAvatar();
        }

        /// <summary>
        /// The art of whichever chapter the player will land in — usually the first,
        /// or wherever they left off. Every other chapter's art stays on disk.
        /// </summary>
        IEnumerator LoadOpeningChapter()
        {
            var catalog = GameContent.Catalog;

            var target = LevelUnlock.NextToPlay(catalog.Index);
            if (!target.IsValid) target = catalog.First;

            var chapterId = catalog.ChapterOf(target);
            if (!chapterId.IsValid) { _target = 1f; yield break; }

            // The one chapter body the game reads at launch, and it lands here on
            // purpose: the splash already has a progress readout, and it is the same
            // chapter whose art is about to be fetched anyway. Every other chapter's
            // grids stay on disk until the player walks into them.
            var bodyTask = catalog.ChapterAsync(chapterId);
            while (!bodyTask.IsCompleted) yield return null;

            if (bodyTask.IsFaulted) { Debug.LogException(bodyTask.Exception); _target = 1f; yield break; }
            if (bodyTask.Result == null) { _target = 1f; yield break; }

            var progress = new Progress<float>(t => _target = .90f + .10f * t);
            var task = AssetLibrary.EnsureChapterAsync(bodyTask.Result, progress);

            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
        }

        /// <summary>
        /// Levels and strings, read from the on-device cache or the bundled files.
        ///
        /// Deliberately offline: the network is never on the path between tapping the
        /// icon and playing. Once the game is up, a background refresh pulls anything
        /// newer into the cache for the next launch.
        /// </summary>
        IEnumerator LoadContent()
        {
            var content = ContentBootstrap.LoadAsync();
            while (!content.IsCompleted) yield return null;
            _target = .06f;

            if (content.IsFaulted)
                Debug.LogException(content.Exception);
            else if (content.Result.Catalog.IsEmpty)
                Debug.LogError("[Boot] no levels available; check Assets/StreamingAssets/Content");

            var loc = Loc.LoadAsync(ContentBootstrap.LocalSource);
            while (!loc.IsCompleted) yield return null;
            if (loc.IsFaulted) Debug.LogException(loc.Exception);

            // The reward table reads through the same layered source, so a downloaded
            // pack can retune the curve exactly the way it can add a chapter.
            var rules = ProgressionRules.LoadAsync(ContentBootstrap.LocalSource);
            while (!rules.IsCompleted) yield return null;
            if (rules.IsFaulted) Debug.LogException(rules.Exception);

            _target = .12f;
        }

        IEnumerator RotateFlavour()
        {
            int i = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(1.05f);
                if (_flavour == null) yield break;
                i = (i + 1) % Flavour.Length;
                var text = _flavour;
                Tween.Tint(text, Pal.A(text.color, 0f), .2f).OnDone(() =>
                {
                    if (!text) return;
                    text.text = Flavour[i];
                    Tween.Tint(text, new Color(1f, .95f, .84f, .85f), .25f);
                });
            }
        }
    }

    /// <summary>
    /// Two copies of one tileable strip, slid sideways for ever.
    ///
    /// <para>
    /// A component rather than a tween because the wrap is a fact about the pair — each
    /// copy jumps a full width the moment it leaves the frame, and doing that from a
    /// tween means owning both transforms' state inside a closure that outlives them.
    /// Negative <paramref name="speed"/> scrolls the other way, which is how the near
    /// bank and the far bank pull against each other.
    /// </para>
    /// </summary>
    public sealed class MistDrift : MonoBehaviour
    {
        RectTransform _a, _b;
        float _width, _speed;

        public static MistDrift Attach(RectTransform a, RectTransform b, float width, float speed)
        {
            var d = a.gameObject.AddComponent<MistDrift>();
            d._a = a; d._b = b; d._width = width; d._speed = speed;
            return d;
        }

        void Update()
        {
            if (_a == null || _b == null) return;
            float step = _speed * Time.unscaledDeltaTime;
            Slide(_a, step);
            Slide(_b, step);
        }

        void Slide(RectTransform rt, float step)
        {
            var p = rt.anchoredPosition;
            p.x += step;
            if (p.x <= -_width) p.x += _width * 2f;
            else if (p.x >= _width) p.x -= _width * 2f;
            rt.anchoredPosition = p;
        }
    }

    /// <summary>
    /// The generated shapes the splash warms up.
    ///
    /// The list of *delivered* assets used to live here too, hardcoded down to the
    /// individual backdrop names — which meant every content drop needed someone to
    /// remember to edit this screen. That list now comes from
    /// <see cref="AssetManifest"/>, derived from the catalog.
    /// </summary>
    public static class Preload
    {
        /// <summary>Generated shapes, warmed so no frame pays for them mid-game.</summary>
        public static List<System.Action> Shapes()
        {
            return new List<System.Action>
            {
                () => Art.Round(6), () => Art.Round(18), () => Art.Round(22), () => Art.Round(24),
                () => Art.Round(28), () => Art.Round(30), () => Art.Round(40),
                () => Art.RoundOutline(22, 3f), () => Art.RoundOutline(22, 5f),
                () => Art.RoundOutline(28, 3f), () => Art.RoundOutline(30, 3f), () => Art.RoundOutline(40, 4f),
                () => Art.Disc(64), () => Art.Disc(96), () => Art.Disc(256),
                () => Art.Ring(128, 9f), () => Art.Glow(64, 1.2f), () => Art.Glow(64, 1.3f),
                () => Art.Glow(64, 1.7f), () => Art.Glow(96, 1.8f), () => Art.Glow(96, 1.9f),
                () => Art.Glow(128, 1.5f), () => Art.Glow(128, 1.6f), () => Art.Glow(128, 1.9f),
                () => Art.Glow(128, 2f), () => Art.Glow(128, 2.1f), () => Art.Glow(128, 2.4f),
                () => Art.Capsule(24, 96), () => Art.SoftCapsule(40, 120),
                () => Art.Spark(64), () => Art.Crystal(128), () => Art.Vignette(256),
                () => Art.FadeUp(64), () => { var _ = Art.Pixel; },
            };
        }
    }
}
