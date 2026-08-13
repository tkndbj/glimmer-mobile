using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Sprite/font/audio registry. Painted assets come from Resources, while the
    /// conduit and glow shapes are generated as signed-distance-field textures so
    /// they stay crisp at any board size and can be tinted freely.
    /// </summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite[]> _frames = new Dictionary<string, Sprite[]>();
        static readonly Dictionary<string, Sprite> _gen = new Dictionary<string, Sprite>();
        static Font _font;

        // ------------------------------------------------------------- resources
        public static Sprite S(string path)
        {
            if (_sprites.TryGetValue(path, out var s)) return s;
            s = Resources.Load<Sprite>("Art/" + path);
            if (s == null) Debug.LogWarning($"[Art] missing sprite Art/{path}");
            _sprites[path] = s;
            return s;
        }

        public static Sprite[] Frames(string folder)
        {
            if (_frames.TryGetValue(folder, out var f)) return f;
            f = Resources.LoadAll<Sprite>("Art/" + folder);
            if (f == null || f.Length == 0) Debug.LogWarning($"[Art] missing frames Art/{folder}");
            else System.Array.Sort(f, (a, b) => string.CompareOrdinal(a.name, b.name));
            _frames[folder] = f;
            return f;
        }

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.Load<Font>("Fonts/GameFont");
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
