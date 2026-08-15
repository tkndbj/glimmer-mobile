using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Sprite and font lookup, plus the generated shapes.
    ///
    /// Painted assets are fetched through <see cref="AssetLibrary"/>, which owns the
    /// caching and decides whether an address is global chrome or chapter art. This
    /// type deliberately no longer knows where assets come from, so switching the
    /// delivery mechanism does not touch any of the several hundred `Art.S` calls.
    ///
    /// The conduit and glow shapes below are generated as signed-distance-field
    /// textures instead, so they stay crisp at any board size and tint freely.
    /// </summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> _gen = new Dictionary<string, Sprite>();
        static Font _font;

        // ------------------------------------------------------------- delivered
        public static Sprite S(string path) => AssetLibrary.Sprite(AssetManifest.ArtRoot + path);

        public static Sprite[] Frames(string folder) => AssetLibrary.Frames(AssetManifest.ArtRoot + folder);

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = AssetLibrary.Font(AssetManifest.FontAddress);
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        // ----------------------------------------------------------- generated
        static Sprite Make(string key, int w, int h, System.Func<float, float, float> alpha,
                           Vector4 border = default)
        {
            if (_gen.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "gen:" + key,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(alpha(x + .5f, y + .5f));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f + .5f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f, 0,
                                   SpriteMeshType.FullRect, border);
            sp.name = key;
            sp.hideFlags = HideFlags.HideAndDontSave;
            _gen[key] = sp;
            return sp;
        }

        /// <summary>
        /// As <see cref="Make"/>, but the painter returns a colour rather than coverage.
        ///
        /// Everything else generated here is a white mask coloured by <c>Image.color</c>,
        /// which is what makes one shape serve a dozen tints. A glyph built from several
        /// colours at once cannot work that way — a tint multiplies, so the darkest part of
        /// the sprite decides the result and the whole thing goes to mud.
        /// </summary>
        static Sprite MakeRGBA(string key, int w, int h, System.Func<float, float, Color> paint)
        {
            if (_gen.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "gen:" + key,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = paint(x + .5f, y + .5f);
            tex.SetPixels32(px);
            tex.Apply(false, true);

            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f, 0,
                                   SpriteMeshType.FullRect);
            sp.name = key;
            sp.hideFlags = HideFlags.HideAndDontSave;
            _gen[key] = sp;
            return sp;
        }

        /// <summary>Straight-alpha source-over, for compositing generated layers.</summary>
        static Color Over(Color dst, Color src, float coverage)
        {
            float sa = src.a * Mathf.Clamp01(coverage);
            if (sa <= 0f) return dst;

            float a = sa + dst.a * (1f - sa);
            if (a <= 0f) return default;

            float k = dst.a * (1f - sa);
            return new Color((src.r * sa + dst.r * k) / a,
                             (src.g * sa + dst.g * k) / a,
                             (src.b * sa + dst.b * k) / a,
                             a);
        }

        const float Feather = 1.35f;
        static float Cover(float d) => Mathf.Clamp01(.5f - d / Feather);

        static float SdRoundBox(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px - cx) - (hx - r);
            float qy = Mathf.Abs(py - cy) - (hy - r);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>White 1x1, the workhorse for flat fills.</summary>
        public static Sprite Pixel => Make("px", 4, 4, (x, y) => 1f);

        /// <summary>Nine-sliced rounded rectangle.</summary>
        public static Sprite Round(int radius = 24)
        {
            int size = radius * 2 + 8;
            float h = size * .5f;
            int b = radius + 3;
            return Make($"round{radius}", size, size,
                (x, y) => Cover(SdRoundBox(x, y, h, h, h, h, radius)),
                new Vector4(b, b, b, b));
        }

        /// <summary>Nine-sliced rounded rectangle outline.</summary>
        public static Sprite RoundOutline(int radius = 24, float thickness = 4f)
        {
            int size = radius * 2 + 10;
            float h = size * .5f;
            int b = radius + 4;
            return Make($"roundo{radius}_{thickness}", size, size, (x, y) =>
            {
                float d = SdRoundBox(x, y, h, h, h - 1f, h - 1f, radius);
                return Cover(Mathf.Abs(d + thickness * .5f) - thickness * .5f);
            }, new Vector4(b, b, b, b));
        }

        public static Sprite Disc(int size = 128)
        {
            float h = size * .5f;
            return Make($"disc{size}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;
                return Cover(Mathf.Sqrt(dx * dx + dy * dy) - (h - 1f));
            });
        }

        public static Sprite Ring(int size = 128, float thickness = 10f)
        {
            float h = size * .5f;
            float r = h - thickness * .5f - 1f;
            return Make($"ring{size}_{thickness}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                return Cover(Mathf.Abs(d) - thickness * .5f);
            });
        }

        /// <summary>Soft radial falloff. Higher power = tighter core.</summary>
        public static Sprite Glow(int size = 128, float power = 2.2f)
        {
            float h = size * .5f;
            return Make($"glow{size}_{power}", size, size, (x, y) =>
            {
                float dx = (x - h) / h, dy = (y - h) / h;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Pow(Mathf.Clamp01(1f - d), power);
            });
        }

        /// <summary>Vertical soft gradient, 1 at the bottom fading to 0 at the top.</summary>
        public static Sprite FadeUp(int h = 64)
            => Make($"fadeup{h}", 4, h, (x, y) => 1f - (y / (float)h), new Vector4(0, 0, 0, 0));

        /// <summary>Capsule aligned to +Y, used for conduit arms.</summary>
        public static Sprite Capsule(int thickness = 24, int length = 96)
        {
            float r = thickness * .5f;
            return Make($"cap{thickness}_{length}", thickness, length, (x, y) =>
            {
                float hx = thickness * .5f, hy = length * .5f;
                return Cover(SdRoundBox(x, y, hx, hy, hx, hy, r));
            }, new Vector4(0, thickness, 0, thickness));
        }

        /// <summary>Capsule with a wide soft falloff, the bloom around a live conduit.</summary>
        public static Sprite SoftCapsule(int thickness = 40, int length = 120)
        {
            float core = thickness * .22f;
            return Make($"scap{thickness}_{length}", thickness, length, (x, y) =>
            {
                float hx = thickness * .5f, hy = length * .5f;
                float d = SdRoundBox(x, y, hx, hy, hx, hy, core);
                float k = Mathf.Clamp01(-d / (hx - core + .001f));
                return k * k * .9f;
            }, new Vector4(0, thickness, 0, thickness));
        }

        /// <summary>Four pointed star (astroid), for sparkles.</summary>
        public static Sprite Spark(int size = 96)
        {
            float h = size * .5f;
            return Make($"spark{size}", size, size, (x, y) =>
            {
                float dx = Mathf.Abs(x - h) / (h - 1f), dy = Mathf.Abs(y - h) / (h - 1f);
                float v = Mathf.Pow(dx, .45f) + Mathf.Pow(dy, .45f);
                return Mathf.Clamp01((1f - v) * 5f);
            });
        }

        /// <summary>
        /// A faceted gem in the shipped art's own idiom: a darker ring of the body's own
        /// hue, a flat face, and a lit facet with a gloss on it.
        ///
        /// <para>
        /// It exists because the set ships no glyph for experience. <see cref="Spark"/> stood
        /// in and was wrong twice over — a thin astroid washes out at icon size, and it sat
        /// next to a painted, glossy coin looking like a different game. Nothing paintable
        /// could be borrowed either: <c>ic_gem</c> is the gems currency, <c>ic_star3d</c>
        /// collides with the star row, and every painted glyph here carries its own colour,
        /// so tinting one to mean something else multiplies to mud.
        /// </para>
        /// <para>
        /// Generated rather than commissioned, which buys the thing invariant 7b keeps
        /// asking for: there is no address to register, no group to belong to, and no frame
        /// where the chip is a white rectangle because the art had not arrived. A painted
        /// <c>ic_xp.png</c> would look richer and would have to earn all of that back.
        /// </para>
        /// <para>
        /// The ring is the body's own hue driven down rather than a shared outline colour,
        /// because that is what the shipped art does — the coin's ring is brown, the gem's
        /// is plum. A common ink outline reads as UI chrome; a hue-matched one reads as an
        /// object.
        /// </para>
        /// </summary>
        public static Sprite Gem(int size, Color body)
        {
            var ring = Color.Lerp(body, new Color(.06f, .16f, .10f), .78f);
            var facet = Color.Lerp(body, Color.white, .55f);
            var gloss = new Color(1f, 1f, 1f, .55f);
            facet.a = .95f;

            return MakeRGBA($"gem{size}:{ColorUtility.ToHtmlStringRGBA(body)}", size, size, (x, y) =>
            {
                Color c = Over(default, ring, Diamond(x, y, size, 1.00f, 0f, 0f));
                c = Over(c, body, Diamond(x, y, size, .88f, 0f, 0f));
                c = Over(c, facet, Diamond(x, y, size, .40f, -.203f, .203f));
                c = Over(c, gloss, Diamond(x, y, size, .16f, -.313f, .344f));
                return c;
            });
        }

        /// <summary>
        /// Coverage of the <see cref="Crystal"/> diamond, scaled and offset. Offsets are
        /// fractions of the half-size so a facet lands in the same place at every resolution.
        /// </summary>
        static float Diamond(float x, float y, int size, float scale, float ox, float oy)
        {
            float h = size * .5f;
            float dx = x - h - ox * h, dy = y - h - oy * h;
            float rx = (dx + dy) * .70710678f, ry = (dx - dy) * .70710678f;
            float q = h * .68f * scale;
            return Cover(SdRoundBox(rx, ry, 0f, 0f, q, q, q * .3f));
        }

        /// <summary>Rounded diamond, the shape of a heart-crystal source.</summary>
        public static Sprite Crystal(int size = 128)
        {
            float h = size * .5f;
            return Make($"cry{size}", size, size, (x, y) =>
            {
                float dx = (x - h), dy = (y - h);
                // rotate 45 degrees then round-box
                float rx = (dx + dy) * .70710678f, ry = (dx - dy) * .70710678f;
                float s = h * .68f;
                return Cover(SdRoundBox(rx, ry, 0, 0, s, s, s * .3f));
            });
        }

        /// <summary>Hexagon, used for lamp haloes.</summary>
        public static Sprite Hex(int size = 128)
        {
            float h = size * .5f;
            return Make($"hex{size}", size, size, (x, y) =>
            {
                float px = Mathf.Abs(x - h) / (h - 1f), py = Mathf.Abs(y - h) / (h - 1f);
                float d = Mathf.Max(px * .866f + py * .5f, py) - .92f;
                return Mathf.Clamp01(-d * (h - 1f) / Feather + .5f);
            });
        }

        /// <summary>
        /// Fan of light rays from a hollow centre, for the thing behind a prize.
        ///
        /// Generated rather than painted because it is only ever spun and tinted: a
        /// sprite would be one more address to register, one more file to keep in step
        /// with the palette, and it would band when scaled to a tile.
        /// </summary>
        public static Sprite Rays(int size = 256, int count = 12)
        {
            float h = size * .5f;
            return Make($"rays{size}_{count}", size, size, (x, y) =>
            {
                float dx = (x - h) / h, dy = (y - h) / h;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= 1f) return 0f;

                float wedge = .5f + .5f * Mathf.Cos(Mathf.Atan2(dy, dx) * count);
                wedge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wedge - .42f) / .38f));

                // hollow in the middle so whatever it sits behind is not washed out,
                // and faded at the rim so the fan has no edge to catch the eye
                float inner = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - .16f) / .22f));
                float outer = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - d) / .42f));
                return wedge * inner * outer;
            });
        }

        /// <summary>
        /// A moon at a given phase: 0 is new, 1 is full, waxing from the right.
        ///
        /// The streak page prints one per night, so the shape has to come from the
        /// number rather than from a set of hand-drawn phases — a ladder retuned to ten
        /// nights would otherwise need ten new sprites.
        /// </summary>
        public static Sprite Moon(int size = 96, float phase = 1f)
        {
            float k = Mathf.Clamp01(phase);
            float h = size * .5f;
            return Make($"moon{size}_{k:0.00}", size, size, (x, y) =>
            {
                float dx = (x - h) / (h - 1f), dy = (y - h) / (h - 1f);
                float disc = Cover((Mathf.Sqrt(dx * dx + dy * dy) - 1f) * (h - 1f));
                if (disc <= 0f) return 0f;

                // the terminator is a half-ellipse whose width is the phase
                float edge = Mathf.Cos(Mathf.PI * k) * Mathf.Sqrt(Mathf.Max(0f, 1f - dy * dy));
                float lit = Mathf.Clamp01((dx - edge) * (h - 1f) / Feather + .5f);
                return disc * lit;
            });
        }

        /// <summary>
        /// A flower, as open as you ask it to be: a bud at 0, fully spread at 1.
        ///
        /// <para>
        /// It exists because an event needed a mark and the set ships none. <c>ic_stars</c>
        /// stood in and was wrong twice over — three stars are what a <em>glade</em> pays, so
        /// the one place in the game that meant "limited time" was drawn in the vocabulary of
        /// the one thing that is always there; and being a fixed silhouette it could say
        /// nothing about how far through the track the player was.
        /// </para>
        /// <para>
        /// Generated rather than commissioned, for the reason <see cref="Gem"/> gives: there
        /// is no address to register, no group to belong to, and no frame where the box draws
        /// a white rectangle because the art had not arrived. It is a plain coverage mask, so
        /// it takes the event's own colour from <c>Image.color</c> and a second, smaller one
        /// layered over it makes the two-tone flower without a second texture.
        /// </para>
        /// <para>
        /// <b><paramref name="open"/> is quantised to eighths, and has to be.</b> Every shape
        /// here is cached by its key, so a value driven straight from a tween would mint a
        /// texture per frame and never hit the cache. Eighths is finer than the eight
        /// milestones an event may carry (<c>EventRules.MaxMilestones</c>), so no track can
        /// show two rungs as the same flower.
        /// </para>
        /// </summary>
        public static Sprite Bloom(int size = 128, int petals = 6, float open = 1f)
        {
            float k = Mathf.Round(Mathf.Clamp01(open) * 8f) / 8f;
            int n = Mathf.Clamp(petals, 3, 12);

            // Both numbers carry the openness, and both had to be tuned against the screen
            // rather than reasoned about. Drawing a closed bud at a third of the size — the
            // obvious first move — reads as a dot at icon size, which says nothing to the one
            // player the mark most needs to speak to: the one who has finished none of the
            // track. Holding the size constant and lobing alone fails the other way, because
            // 0 and 4 of 4 then look like the same flower and the mark stops being progress.
            // Two thirds and shallow is the shape that reads as a bud, and it has to grow by
            // half again to open, which is a change nobody can miss.
            float shape = Mathf.Lerp(.62f, 1f, k);
            float lobing = Mathf.Lerp(.12f, .62f, k);

            float h = size * .5f;
            float r = h - 1f;

            return Make($"bloom{size}_{n}_{k:0.00}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // half-angle, so n petals rather than 2n lobes
                float lobe = Mathf.Abs(Mathf.Cos(Mathf.Atan2(dy, dx) * n * .5f));
                float rad = r * shape * (1f - lobing + lobing * Mathf.Pow(lobe, .65f));
                return Cover(d - rad);
            });
        }

        /// <summary>Screen vignette; dark at the edges, clear in the middle.</summary>
        public static Sprite Vignette(int size = 256)
        {
            float h = size * .5f;
            return Make($"vig{size}", size, size, (x, y) =>
            {
                float dx = (x - h) / h, dy = (y - h) / h;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((d - .45f) / .75f);
            });
        }
    }
}
