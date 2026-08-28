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

        /// <summary>
        /// A capsule whose radius changes along its length — a finger, or a thumb.
        ///
        /// The radius is interpolated rather than solved for the true slanted cone, which
        /// understates the distance slightly along a strong taper. That is invisible here: the
        /// only consumers are <see cref="Cover"/> and an outline a couple of pixels wide, and
        /// both want a smooth monotonic field rather than a metrically exact one.
        /// </summary>
        static float SdRoundCone(float px, float py, float ax, float ay, float bx, float by,
                                 float ra, float rb)
        {
            float bax = bx - ax, bay = by - ay;
            float l2 = bax * bax + bay * bay;
            if (l2 < 1e-9f)
                return Mathf.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay)) - ra;

            float t = Mathf.Clamp01(((px - ax) * bax + (py - ay) * bay) / l2);
            float cx = ax + bax * t, cy = ay + bay * t;

            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - Mathf.Lerp(ra, rb, t);
        }

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

        /// <summary>
        /// One isometric floor tile: a 2:1 diamond with a soft inner edge.
        ///
        /// <para>
        /// Generated for <see cref="Bloom"/> and <c>Art.Dial</c>'s reason, and with the
        /// strongest case of any of them: the floor is the first thing the Grovement draws and
        /// it draws hundreds of it, so an <c>Image</c> with no sprite would not be one white
        /// rectangle but a screenful (invariant 7b). It also means the feature works before the
        /// tile art exists, and a content file that names real art simply overrides it.
        /// </para>
        /// <para>
        /// The <paramref name="inset"/> is the gap between one tile and the next: a hairline of
        /// transparency is what makes a field of them read as a grid rather than as one flat
        /// wash of colour, and it costs nothing to draw.
        /// </para>
        /// </summary>
        public static Sprite IsoTile(int width = 128, float inset = 1.5f)
        {
            float hw = width * .5f, hh = width * .25f;
            int height = Mathf.Max(2, Mathf.RoundToInt(width * .5f));

            return Make($"isotile{width}_{inset}", width, height, (x, y) =>
            {
                // A diamond is |dx|/hw + |dy|/hh <= 1, scaled back to pixels so the feather is
                // the same width on every edge however wide the tile is.
                float dx = Mathf.Abs(x - hw) / hw;
                float dy = Mathf.Abs(y - hh) / hh;
                float d = (dx + dy - 1f) * Mathf.Min(hw, hh);
                return Cover(d + inset);
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

        /// <summary>
        /// A hexagonal ring: the silhouette a Lightweave bead wears.
        ///
        /// <para>
        /// <b>It exists because a circle was already taken.</b> A bead was first drawn with
        /// <see cref="Ring"/>, which is also what a sleeping critter wears to say which colour it
        /// wants — so a grove came out carrying eleven rings in six colours, five of them places
        /// to go through and six of them creatures to reach, told apart only by whether something
        /// was standing inside. That is a distinction you have to look for, on the one screen
        /// where reading the board at a glance is the entire game. A hexagon is a third
        /// silhouette against the critter's circle and the crystal's diamond, and it is legible
        /// at a cell's width on a phone, which a dashed or double ring is not.
        /// </para>
        /// <para>
        /// Hollow, and that is the half that carries the meaning: a bead is somewhere light
        /// passes <em>through</em>, and its own channel is drawn through the hole. A filled shape
        /// would read as a thing in the way, which is exactly the wrong half of what it is.
        /// </para>
        /// </summary>
        public static Sprite HexRing(int size = 128, float thickness = 10f)
        {
            float h = size * .5f;
            float r = h - thickness * .5f - 1f;

            return Make($"hexring{size}_{thickness}", size, size, (x, y) =>
            {
                // The same flat-topped hexagon Hex draws, as a signed distance in pixels so the
                // ring is the same width all the way round rather than pinching at the corners.
                float dx = Mathf.Abs(x - h), dy = Mathf.Abs(y - h);
                float d = Mathf.Max(dx * .866f + dy * .5f, dy) - r;
                return Cover(Mathf.Abs(d) - thickness * .5f);
            });
        }

        /// <summary>
        /// A clock face: a ring with two hands, generated rather than drawn.
        ///
        /// <para>
        /// Generated for the reason <see cref="Bloom"/> and <see cref="PrismRing"/> are.
        /// This is the glyph on the continue offer, which is shown at the instant a run is
        /// lost — an <c>Image</c> whose sprite has not finished loading is a white
        /// rectangle rather than a blank (invariant 7b), and a white rectangle on the panel
        /// asking somebody to watch a video is the worst possible moment to look broken. It
        /// also needs no address, no group and no audit entry, which is the whole argument
        /// for a shape this simple.
        /// </para>
        /// <para>
        /// The hands are fixed at ten past ten. That is the position every watch in every
        /// advertisement has worn for a century, and the reason is the same here: it frames
        /// the face symmetrically and reads as a clock at 48px, where a vertical pair reads
        /// as a line. Nothing about this dial tracks a real time — it is a noun, not a
        /// readout, and a hand that moved would imply the offer was itself on a countdown.
        /// </para>
        /// </summary>
        public static Sprite Dial(int size = 128, float thickness = 9f)
        {
            float h = size * .5f;
            float r = h - thickness * .5f - 1f;

            // Ten past ten, as angles from twelve o'clock, plus the length of each hand as a
            // fraction of the face. The hour hand is stubbier than the minute hand by more
            // than a real watch's, because at icon size a small difference reads as a
            // drawing error rather than as two hands.
            const float HourTurn = -60f, MinuteTurn = 50f;
            const float HourLen = .46f, MinuteLen = .68f;

            return Make($"dial{size}_{thickness}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;

                float ring = Cover(Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - r) - thickness * .5f);

                float hour = Cover(Hand(dx, dy, HourTurn, r * HourLen, thickness * .42f));
                float minute = Cover(Hand(dx, dy, MinuteTurn, r * MinuteLen, thickness * .34f));

                return Mathf.Max(ring, Mathf.Max(hour, minute));
            });
        }

        /// <summary>
        /// Distance from a point to a hand: a capsule from the centre out along
        /// <paramref name="degrees"/>, measured clockwise from twelve o'clock.
        ///
        /// Split out of <see cref="Dial"/> only because doing it inline twice put the same
        /// six lines of trigonometry in one lambda, where a sign error in the second copy
        /// would be a hand pointing somewhere nobody meant.
        /// </summary>
        static float Hand(float dx, float dy, float degrees, float length, float halfWidth)
        {
            float a = degrees * Mathf.Deg2Rad;
            float ux = Mathf.Sin(a), uy = Mathf.Cos(a);

            // Projection onto the hand, clamped to its length, which is what turns an
            // infinite line into a rounded capsule rooted at the centre.
            float t = Mathf.Clamp(dx * ux + dy * uy, 0f, length);
            float px = dx - ux * t, py = dy - uy * t;

            return Mathf.Sqrt(px * px + py * py) - halfWidth;
        }

        /// <summary>
        /// A ring painted in the three light channels at once: the mark of a critter that
        /// has no favourite colour.
        ///
        /// <para>
        /// It exists because a flat cream ring was doing two jobs. Every other halo on a
        /// board is an <see cref="Pal.EnergyColour"/>, so cream read as a fifth colour
        /// rather than as "no colour required" — and that only became ambiguous on the first
        /// board where an unfussy critter sat beside a fussy one, which is exactly the board
        /// where it matters. Three arcs say "any of these" in a way no translation has to
        /// carry (invariant 6), and the arcs are the actual channels rather than a rainbow,
        /// so the ring is a statement about this game's rules and not decoration.
        /// </para>
        /// <para>
        /// The third generated shape carrying its own colour, after <see cref="Gem"/> and
        /// <see cref="Gradient"/>, and for their reason: a tint multiplies, so a
        /// three-coloured mask painted white and tinted would come out as one colour.
        /// </para>
        /// </summary>
        public static Sprite PrismRing(int size = 128, float thickness = 10f)
        {
            float h = size * .5f;
            float r = h - thickness * .5f - 1f;

            return MakeRGBA($"prismring{size}_{thickness}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                float a = Cover(Mathf.Abs(d) - thickness * .5f);
                if (a <= 0f) return new Color(0, 0, 0, 0);

                // Turn clockwise from the top, so the first arc sits where the eye lands.
                float turn = Mathf.Repeat(Mathf.Atan2(dx, dy) / (Mathf.PI * 2f), 1f) * 3f;
                int arc = Mathf.FloorToInt(turn);
                float t = turn - arc;

                var here = Arcs[arc % 3];
                var next = Arcs[(arc + 1) % 3];

                // Blended across a slice of each arc rather than butted together: a hard
                // seam at this size reads as three separate marks instead of one ring.
                const float Blend = .18f;
                var c = t > 1f - Blend
                    ? Color.Lerp(here, next, (t - (1f - Blend)) / (Blend * 2f) + .5f)
                    : t < Blend
                        ? Color.Lerp(Arcs[(arc + 2) % 3], here, t / (Blend * 2f) + .5f)
                        : here;

                return new Color(c.r, c.g, c.b, a);
            });
        }

        /// <summary>The three channels, in the order <see cref="PrismRing"/> walks them.</summary>
        static readonly Color[] Arcs = { Pal.Ember, Pal.Verdant, Pal.Azure };

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

        /// <summary>
        /// Vertical three-stop gradient carrying its own colour, for a coloured backdrop.
        ///
        /// <para>
        /// One of the two generated shapes that is not a white mask, for the reason
        /// <see cref="Gem"/> is the other: <c>Image.color</c> multiplies, so a layer holding
        /// more than one colour cannot be a mask tinted at the call site — the darkest stop
        /// would decide the result.
        /// </para>
        /// <para>
        /// A sprite rather than a stack of faded plates because a full-screen layer costs the
        /// same fill rate whether it is opaque or nearly transparent, and on a phone that is
        /// the expensive part of a backdrop. Three stops in one draw is a third of the cost of
        /// three washes, and the only thing a stack bought was being able to tween the stops
        /// independently, which nothing needs.
        /// </para>
        /// </summary>
        public static Sprite Gradient(Color bottom, Color middle, Color top, int height = 128)
        {
            int h = Mathf.Max(2, height);
            string key = $"grad{h}:{ColorUtility.ToHtmlStringRGBA(bottom)}"
                       + $":{ColorUtility.ToHtmlStringRGBA(middle)}"
                       + $":{ColorUtility.ToHtmlStringRGBA(top)}";

            return MakeRGBA(key, 4, h, (x, y) =>
            {
                float v = (y - .5f) / (h - 1);
                return v < .5f ? Color.Lerp(bottom, middle, v * 2f)
                               : Color.Lerp(middle, top, (v - .5f) * 2f);
            });
        }

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

        /// <summary>
        /// One cell of hedge: a band of foliage that light cannot pass through.
        ///
        /// <para>
        /// <b>One cell, not one hedge, and that is what lets a run be any length.</b> A hedge is
        /// grown as a run of closed edges, so the grove draws one of these per edge and they abut.
        /// The band therefore reaches the full width of the texture with no rounding at either
        /// end, and the lobes repeat on a third of its width, so two side by side read as one
        /// continuous hedge rather than as beads on a string. A single stretched sprite was the
        /// alternative and it is worse in the way that matters: a run of two and a run of nine
        /// would carry the same number of leaves at wildly different sizes.
        /// </para>
        /// <para>
        /// <b>Lobes rather than a plain bar</b>, because a plain bar on this board is a
        /// <em>channel</em> — that is exactly what a drawn channel looks like, a capsule of colour
        /// laid along a line of cells. A barrier that reads as somebody's channel is the worst
        /// possible confusion here, since one is ground you may never enter and the other is
        /// ground you may not enter <em>yet</em>. The bumpy silhouette is the whole of what tells
        /// them apart at a glance, and it is why this is generated rather than being a tinted
        /// <see cref="Capsule"/>.
        /// </para>
        /// <para>
        /// Drawn lying flat, along x. An upright hedge is this turned a quarter turn, which is one
        /// sprite for both orientations — the same bargain <see cref="Wedge"/> makes for a wheel.
        /// </para>
        /// <para>
        /// <b>The core has to be thick enough that two cells of it read as one hedge</b>, which is
        /// the whole of what the numbers below were tuned for. At a third of the texture the lobes
        /// pinched together at every cell boundary and a four-cell run came out as beads on a
        /// string — the one silhouette this must not have, since a row of round things on the
        /// ground is what a bead already is. Measured on the real board at the real size, which is
        /// the only way to see it.
        /// </para>
        /// </summary>
        public static Sprite Hedge(int size = 64)
            => Make($"hedge{size}", size, size, (x, y) =>
            {
                float u = x / size, v = y / size;

                // Full width, no rounding along u: the ends are where the next cell's hedge
                // begins, and a rounded end there is a visible seam down the middle of a run.
                float band = Cover(SdRoundBox(u, v, .5f, .5f, .5f, .22f, .0f) * size);

                float lobes = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float cx = (i * 2f + 1f) / 6f;
                    lobes = Mathf.Max(lobes, Lobe(u, v, cx, .68f, size));
                    lobes = Mathf.Max(lobes, Lobe(u, v, cx, .32f, size));
                }

                return Mathf.Clamp01(Mathf.Max(band, lobes));
            });

        /// <summary>One leafy bulge of a hedge, measured in the texture's own 0..1 space.</summary>
        static float Lobe(float u, float v, float cx, float cy, int size)
        {
            float dx = u - cx, dy = v - cy;
            return Cover((Mathf.Sqrt(dx * dx + dy * dy) - .17f) * size);
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

        /// <summary>
        /// A leaf, tip up: two arcs meeting at a point, with the midrib cut out of it.
        ///
        /// Generated for the reason <see cref="Bloom"/> is, and it earns it twice over here
        /// — the event page hangs one of these every few dozen pixels along a vine whose
        /// length is decided by content, so the alternative is either an address the asset
        /// audit has to know about or a sprite that is the wrong size at every scale but
        /// one. A coverage mask takes its colour from <c>Image.color</c>, so the same shape
        /// is the grown leaf and the dry one.
        ///
        /// <para>
        /// The vein is punched out rather than drawn over, so a leaf laid on a lit stem
        /// shows the stem through its own midrib instead of a darker stripe that only
        /// happens to match on one background.
        /// </para>
        /// </summary>
        public static Sprite Leaf(int size = 96, float vein = .07f)
        {
            float v = Mathf.Clamp(vein, 0f, .3f);

            return Make($"leaf{size}_{v:0.00}", size, size, (x, y) =>
            {
                // Normalised to the sprite, with the leaf running bottom-centre to top-centre.
                float u = x / size, w = y / size;

                // Widest a third of the way up and pointed at the tip, which is the profile
                // that reads as a leaf rather than as an eye: w^a (1-w)^b peaks at a/(a+b),
                // and the exponents are chosen so that lands low with a long taper above.
                w = Mathf.Clamp01(w);
                float half = .883f * Mathf.Pow(w, .5f) * Mathf.Pow(1f - w, 1f);

                float body = Cover((Mathf.Abs(u - .5f) - half * .62f) * size);

                float rib = Cover((Mathf.Abs(u - .5f) - v * .5f) * size) *
                            Cover((Mathf.Abs(w - .5f) - .46f) * size);

                return Mathf.Clamp01(body - rib);
            });
        }

        /// <summary>
        /// The thorns laid across a briar's closed way: a bar with a barb above it and a barb
        /// below, drawn once and turned with the tile.
        ///
        /// <para>
        /// Generated rather than addressed, for <see cref="Bloom"/>'s reason — an
        /// <c>Image</c> whose sprite has not arrived is a white rectangle, and a white
        /// rectangle laid across a conduit is a tile whose rule the player would read exactly
        /// backwards.
        /// </para>
        /// <para>
        /// The barbs are offset along the bar rather than facing each other, which is the
        /// whole of what stops it reading as a plus sign — the one shape this board must
        /// never put on a tile, since a crossroads is what a briar is not.
        /// </para>
        /// </summary>
        public static Sprite Thorn(int size = 64)
            => Make($"thorn{size}", size, size, (x, y) =>
            {
                float u = x / size, v = y / size;

                float bar = Cover(SdRoundBox(u, v, .5f, .5f, .42f, .095f, .065f) * size);
                float up = Barb(u, v, .31f, size);
                float down = Barb(u, 1f - v, .69f, size);

                return Mathf.Clamp01(Mathf.Max(bar, Mathf.Max(up, down)));
            });

        /// <summary>One barb of a thorn: a spike tapering out of the bar, measured upward.</summary>
        static float Barb(float u, float v, float centre, int size)
        {
            float t = (v - .585f) / .26f;                // 0 at the bar's face, 1 at the tip
            if (t < 0f) return 0f;

            // Past the tip the half-width goes negative, so the coverage fades out on its own
            // rather than being cut off — a spike that ends in a hard edge reads as a chip.
            return Cover((Mathf.Abs(u - centre) - .095f * (1f - t)) * size);
        }

        /// <summary>
        /// One wedge of a wheel, with the hub cut out of it.
        ///
        /// <para>
        /// <b>One sprite for every slice.</b> The wedge is drawn pointing straight up and each
        /// slice is a rotation of it, so a wheel of any size costs one texture and a handful of
        /// <c>Image</c>s that can each be tinted independently — the same "one shape, many
        /// tints" bargain the rest of this file makes, and the reason the slice count can be
        /// content without an art order following it.
        /// </para>
        /// <para>
        /// The obvious alternative is a radial <see cref="Image.Type.Filled"/> over a disc,
        /// which needs no art at all. It is not used, and the reason is antialiasing: a filled
        /// image cuts <em>geometry</em>, so the two straight edges of every wedge come out
        /// stair-stepped while the arc stays smooth. On a wheel the straight edges are what
        /// separate one prize from the next, and a jagged one under a pointer is precisely the
        /// place a player looks hardest.
        /// </para>
        /// <para>
        /// The angular edge is feathered against <em>arc length</em> rather than against the
        /// angle, so the softness is the same width in pixels at the hub and at the rim. Fading
        /// on angle alone gives a wedge that looks crisp outside and blurred in the middle.
        /// </para>
        /// </summary>
        public static Sprite Wedge(int size = 256, int count = 8, float hub = .22f)
        {
            if (count < 2) count = 2;

            float h = size * .5f;
            float outer = h - 1.5f;
            float inner = outer * Mathf.Clamp01(hub);
            float half = Mathf.PI / count;              // half the wedge's own angle, in radians

            return Make($"wedge{size}_{count}_{hub:0.00}", size, size, (x, y) =>
            {
                float dx = x - h, dy = y - h;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < 0.0001f) return 0f;

                // Zero straight up, growing clockwise, and folded so only the distance from the
                // wedge's own centre line matters.
                float a = Mathf.Abs(Mathf.Atan2(dx, dy));

                float rim = Cover(r - outer);
                float bore = Cover(inner - r);
                float side = Cover(r * (a - half));

                return Mathf.Min(rim, Mathf.Min(bore, side));
            });
        }

        /// <summary>
        /// The marker that sits over a wheel and says which slice won: a teardrop, point down.
        ///
        /// <para>
        /// A teardrop rather than a triangle because it has to read at a glance against a rim
        /// full of coloured wedges, and a triangle at this size is three straight lines that the
        /// rim's own edges keep rhyming with. The round shoulder gives it a silhouette nothing
        /// else on the panel has — the argument <see cref="HexRing"/> makes about a bead against
        /// a critter, on a screen where the shapes are all curves instead of all circles.
        /// </para>
        /// <para>
        /// Built from <see cref="SdRoundCone"/>, which is already here for the coaching hand, so
        /// the taper is a real distance field and the tip fades out rather than ending in a
        /// chipped pixel.
        /// </para>
        /// </summary>
        public static Sprite Pointer(int size = 96)
        {
            float h = size * .5f;

            return Make($"pointer{size}", size, size, (x, y) =>
                Cover(SdRoundCone(x, y, h, size * .74f, h, size * .12f,
                                  size * .23f, size * .015f)));
        }

        /// <summary>
        /// A pointing hand, for the lessons that teach a gesture rather than a rule.
        ///
        /// <para>
        /// <b>Generated, and this is the strongest case of any of them.</b> An <c>Image</c>
        /// whose sprite has not arrived is a white rectangle, and this one is drawn on top of a
        /// dimmed board over a modal that is only ever shown once in a player's life — there is
        /// no second showing to catch it at. A hand is also chrome in the strictest sense: it
        /// belongs to no chapter, so it would sit in the global group and be loaded by every
        /// screen in the game to be used by two.
        /// </para>
        /// <para>
        /// Painted with <see cref="MakeRGBA"/> rather than as a white mask, because it needs an
        /// ink rim and a drop shadow of its own. A tinted mask cannot carry either, and without
        /// them the glyph disappears wherever the board underneath it happens to be pale — which
        /// on a grove is wherever a channel has just been drawn, so the hand would vanish exactly
        /// where it is doing its work.
        /// </para>
        /// <para>
        /// The fingertip is at <c>(.251, .89)</c> of the sprite, and callers pivot there rather
        /// than at the centre: the point of the demonstration is where the finger is, and a hand
        /// centred on the cell it is pressing covers the cell.
        /// </para>
        /// </summary>
        public static Sprite Hand(int size = 128)
        {
            var ink = new Color(.08f, .11f, .16f, 1f);
            var lit = Color.white;
            var shade = new Color(.76f, .81f, .88f, 1f);
            var deep = new Color(.55f, .62f, .74f, 1f);
            var shadow = new Color(.01f, .03f, .06f, .45f);

            float rim = size * .026f;
            float drop = size * .045f;
            float soft = size * .090f;

            // The light is up and to the left, so the form turns away along its lower-right.
            // Measured by asking how close a point is to the edge *in that direction* rather
            // than by a second gradient: a gradient darkens the bottom of the finger and the
            // bottom of the fist by the same amount, which is a flat shape with a flat shadow
            // on it. This one follows the silhouette, so the thumb rounds and the cuff sits
            // behind the knuckles.
            float turn = size * .055f;
            float band = size * .085f;

            return MakeRGBA($"hand{size}", size, size, (x, y) =>
            {
                float d = HandSd(x, y, size);

                // The shadow is the shape sampled a little above, so it falls below the hand.
                // Ramped over several pixels rather than through Cover, because a hard-edged
                // shadow reads as a second, darker hand.
                float below = HandSd(x, y + drop, size);
                Color c = Over(default, shadow, Mathf.Clamp01(.5f - below / soft));

                c = Over(c, ink, Cover(d - rim));

                // Whiter at the fingertip than at the wrist, which is all the modelling a
                // silhouette this size can carry — a highlight and a shaded edge on top of it
                // would only be two more things to read.
                float v = Mathf.Clamp01((y / size - .08f) / .82f);
                var body = Color.Lerp(shade, lit, v * v * (3f - 2f * v));

                float t = Mathf.Clamp01(HandSd(x + turn, y - turn, size) / band);
                body = Color.Lerp(body, deep, t * t * (3f - 2f * t) * .75f);

                c = Over(c, body, Cover(d));

                return c;
            });
        }

        /// <summary>
        /// Signed distance, in pixels, to the hand: an index finger up, a thumb out and the
        /// rest closed.
        ///
        /// <para>
        /// The two scallops are subtracted from the closed side, and they are what make it a
        /// fist rather than a mitten — without them the silhouette reads as a glove, which is
        /// the one shape that does not say "your finger goes here".
        /// </para>
        /// </summary>
        /// <summary>
        /// The glyph's frame, and the axis of its index finger, named because two things need
        /// them: <see cref="HandSd"/> draws from them and <see cref="HandFingertip"/> works the
        /// tip back out of them. They were literals in one place and a hand-derived constant in
        /// another, which is a pivot that silently stops matching its own art the first time the
        /// finger moves.
        /// </summary>
        const float HandTilt = .32f, HandScale = .92f, HandLift = .035f;
        const float KnuckleU = .382f, KnuckleV = .470f;
        const float TipU = .372f, TipV = .930f, TipR = .064f;

        /// <summary>
        /// Where the fingertip sits in <see cref="Hand"/>, as a pivot in sprite space.
        ///
        /// <para>
        /// Derived, never typed. The whole hand is positioned by this point — it is what the
        /// fingertip is placed with — so a stale one slides the hand off the route it is
        /// tracing and the demonstration quietly stops pointing at anything, which is invisible
        /// in a compile and easy to miss in motion.
        /// </para>
        /// </summary>
        public static Vector2 HandFingertip
        {
            get
            {
                // The extreme tip: the end cap's centre, pushed out along the finger's own axis.
                float ax = TipU - KnuckleU, ay = TipV - KnuckleV;
                float len = Mathf.Sqrt(ax * ax + ay * ay);
                if (len < 1e-6f) len = 1f;

                float u = TipU + ax / len * TipR - .5f;
                float v = TipV + ay / len * TipR - .5f;

                // Back out of the tilted frame HandSd samples in.
                float cos = Mathf.Cos(HandTilt), sin = Mathf.Sin(HandTilt);

                return new Vector2((u * cos - v * sin) * HandScale + .5f,
                                   (u * sin + v * cos) * HandScale + .5f + HandLift);
            }
        }

        static float HandSd(float x, float y, int size)
        {
            // Sampled in the glyph's own frame, which is tilted anticlockwise about the centre
            // of the sprite. A pointing hand drawn upright is not a pointing hand — a single
            // finger raised straight up from a closed fist is a gesture this game must never
            // put on a teaching panel in any market, and no amount of thumb makes it read as
            // anything else. The tilt is the fix rather than a flourish: it is what puts the
            // fingertip up and to one side of the knuckles, which is the whole silhouette of
            // pointing, and it is also the angle a real hand reaches a phone screen at.
            float cos = Mathf.Cos(HandTilt), sin = Mathf.Sin(HandTilt);

            float px = (x / size - .5f) / HandScale, py = (y / size - .5f - HandLift) / HandScale;
            float u = px * cos + py * sin + .5f;
            float v = -px * sin + py * cos + .5f;

            // The finger and the thumb taper and the wrist ends in a cuff, which is the whole
            // difference between this and the four flat boxes it replaced. A limb of constant
            // width reads as a tube, so the old glyph was a mitten with a spike on it: fine at a
            // glance and visibly cheap at the 156 points it is actually drawn at.
            float d = SdRoundBox(u, v, .530f, .330f, .215f, .190f, .110f);                      // closed hand
            d = Mathf.Min(d, SdRoundCone(u, v, KnuckleU, KnuckleV, TipU, TipV, .090f, TipR));   // index finger
            d = Mathf.Min(d, SdRoundCone(u, v, .330f, .398f, .268f, .318f, .078f, .066f));      // thumb
            d = Mathf.Min(d, SdRoundBox(u, v, .535f, .060f, .152f, .150f, .072f));              // wrist

            // Scalloped out of the closed side, and they carry more of the read than they look
            // like they should: without them the silhouette is a mitten, and a mitten with one
            // finger out is the shape this glyph is not allowed to be.
            d = Mathf.Max(d, -(Mathf.Sqrt((u - .775f) * (u - .775f) + (v - .470f) * (v - .470f)) - .078f));
            d = Mathf.Max(d, -(Mathf.Sqrt((u - .793f) * (u - .793f) + (v - .290f) * (v - .290f)) - .074f));

            return d * size * HandScale;
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
