using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A lash: any number of whips thrown from one place at many, drawn as one mesh.
    ///
    /// <para>
    /// <b>What a curse reaches the hill as.</b> A whip leaves its source, runs to the body it
    /// was thrown at along a bowed and writhing line, stands on it for a beat, and is then
    /// drawn <em>into</em> the body from its far end - so what the eye follows is a length of
    /// dark travelling from the board to a raider and going in. It is <see cref="Lightning"/>'s
    /// strip with three differences, and each is the reason this is not that class: a whip is
    /// drawn between a <b>tail and a head</b> that both move (a bolt is only ever revealed from
    /// its start); its width is its own at every joint, thin at the tail and swollen at the
    /// head (<see cref="Girth"/>); and its wander is <b>smooth in time</b> - a bolt is rebuilt
    /// on a new seed because what says electricity is that the next frame is unrelated, and
    /// what says <em>alive</em> is the opposite.
    /// </para>
    /// <para>
    /// <b>One graphic per layer whatever the count</b>, which is the whole reason it is a class:
    /// a curse falls on every raider standing, and a hill can stand thirty. Thirty whips of
    /// three layers as thirty objects each would be ninety graphics re-meshed every frame;
    /// here they are three, sharing one list (<see cref="Layered"/>).
    /// </para>
    /// <para>
    /// <b>The shape is a pure function of the whip, the clock and nothing else</b>
    /// (<see cref="Point"/>, <see cref="Window"/>), so <c>SiegeLashTests</c> can hold the two
    /// things that matter without a screen: a whip's head, once landed, <em>is</em> its target,
    /// and nothing is drawn before it leaves or after it has gone in.
    /// </para>
    /// </summary>
    public sealed class Lash : MaskableGraphic
    {
        /// <summary>One whip: where it runs, how it bows, and its four beats in seconds.</summary>
        public sealed class Whip
        {
            public Vector2 From, To;

            /// <summary>How far the middle of the whip stands off the straight line, in units. Signed.</summary>
            public float Bow;

            /// <summary>When it leaves, how long it flies, how long it stands, how long it takes to go in.</summary>
            public float Leaves, Flight, Held, Withdraw;

            public int Seed;

            /// <summary>When its head reaches its target, on the lash's clock.</summary>
            public float Lands => Leaves + Flight;

            /// <summary>When the last of it has gone in.</summary>
            public float Ends => Leaves + Flight + Held + Withdraw;
        }

        /// <summary>Joints a whole whip is drawn with. A part of one is drawn with its share.</summary>
        public const int Joints = 22;

        /// <summary>Seconds one wander takes to become the next.</summary>
        public const float Beat = .07f;

        /// <summary>How much wider than its body a flying whip's head is.</summary>
        public const float Swell = 1.35f;

        /// <summary>How far back from the head the swell reaches, in cells.</summary>
        public const float SwellReach = .38f;

        /// <summary>Seconds the swell takes to go down once the head has landed.</summary>
        public const float SwellDown = .14f;

        /// <summary>The thinnest a whip's tail is drawn, against its body.</summary>
        public const float TailLeast = .10f;

        List<Whip> _whips;
        float _clock, _cell = 100f, _jag = .2f, _width = 12f, _power = 1.6f;
        Sprite _profile;

        readonly List<Vector2> _path = new List<Vector2>(Joints + 6);
        readonly List<float> _girth = new List<float>(Joints + 6);

        /// <summary>The whips drawn, shared between the layers of one lash.</summary>
        public void Bind(List<Whip> whips, float cell, float jag)
        {
            _whips = whips;
            _cell = Mathf.Max(1f, cell);
            _jag = jag;
            SetVerticesDirty();
        }

        /// <summary>Seconds since the lash began. Every whip is drawn where this says it is.</summary>
        public float Clock
        {
            get => _clock;
            set { _clock = value; SetVerticesDirty(); }
        }

        /// <summary>A whip's width in units, at its body.</summary>
        public float Width
        {
            get => _width;
            set { if (!Mathf.Approximately(_width, value)) { _width = value; SetVerticesDirty(); } }
        }

        /// <summary>
        /// How steeply the cross-section falls off: <see cref="Art.Profile"/>'s power. Low is a
        /// flat, solid band - what a line of <em>dark</em> wants, because a dark that fades from
        /// its own middle is a smudge - and high is a glow.
        /// </summary>
        public float Falloff
        {
            get => _power;
            set { _power = value; _profile = null; SetMaterialDirty(); }
        }

        public override Texture mainTexture
        {
            get
            {
                if (_profile == null) _profile = Art.Profile(64, _power);
                return _profile != null ? _profile.texture : s_WhiteTexture;
            }
        }

        // ------------------------------------------------------------------ the shape
        /// <summary>
        /// How much of <paramref name="whip"/> is drawn at <paramref name="clock"/>: from
        /// <paramref name="tail"/> to <paramref name="head"/>, each a share of its length.
        /// Both are nought before it leaves and both are one once it has gone in, so in either
        /// case nothing is drawn.
        ///
        /// <b>The head accelerates and the tail does too</b>: a whip that eases <em>out</em>
        /// arrives gently, and a curse that arrives gently has not struck anything.
        /// </summary>
        public static void Window(Whip whip, float clock, out float tail, out float head)
        {
            tail = head = 0f;
            if (whip == null) return;

            float s = clock - whip.Leaves;
            if (s <= 0f) return;

            float flown = whip.Flight > 0f ? Mathf.Clamp01(s / whip.Flight) : 1f;
            head = flown >= 1f ? 1f : Mathf.Lerp(flown, flown * flown, .65f);

            float back = s - whip.Flight - whip.Held;
            if (back <= 0f) return;

            float drawn = whip.Withdraw > 0f ? Mathf.Clamp01(back / whip.Withdraw) : 1f;
            tail = drawn >= 1f ? 1f : drawn * drawn;
        }

        /// <summary>
        /// Where <paramref name="whip"/> is at <paramref name="t"/> of its length, at
        /// <paramref name="clock"/>.
        ///
        /// <b>Both ends are the two points, to the bit</b> (<c>Lightning.Joints</c>' rule): the
        /// bow and the wander are both pinched by <c>sin(pi t)</c>, and the ends are returned
        /// rather than computed, so a whip lands on the body it was thrown at because its last
        /// joint is that body.
        /// </summary>
        public static Vector2 Point(Whip whip, float t, float clock, float cell, float jag)
        {
            if (t <= 0f) return whip.From;
            if (t >= 1f) return whip.To;

            var span = whip.To - whip.From;
            float length = span.magnitude;
            if (length < .001f) return whip.From;

            var side = new Vector2(-span.y, span.x) / length;
            float pinch = Mathf.Sin(t * Mathf.PI);

            // Two wanders, a beat apart, and the whip is somewhere between them: it writhes
            // from one shape into the next instead of jumping.
            float beats = Mathf.Max(0f, clock) / Beat;
            int beat = Mathf.FloorToInt(beats);
            float blend = Mathf.SmoothStep(0f, 1f, beats - beat);

            float wander = Mathf.Lerp(Wander(t, whip.Seed + beat), Wander(t, whip.Seed + beat + 1), blend);

            return whip.From + span * t + side * ((whip.Bow + wander * jag * cell) * pinch);
        }

        /// <summary>A smooth wander along the length, in -1..1: a slow swing and a quick one on it.</summary>
        static float Wander(float t, int seed)
            => (Noise(t * 3.1f, seed) + Noise(t * 8.3f, seed + 7919) * .45f) / 1.45f;

        static float Noise(float x, int seed)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), f);
        }

        static float Hash(int n, int seed)
        {
            unchecked
            {
                uint h = (uint)n * 374761393u + (uint)seed * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFu) / 65535f * 2f - 1f;
            }
        }

        /// <summary>
        /// How wide a whip is <paramref name="along"/> the part of it that is drawn (nought at
        /// the tail end, one at the head), as a share of its body.
        ///
        /// <para>
        /// <b>Three things multiplied.</b> The <b>tail</b> thins to a point, but only once it
        /// has let go of its source (<paramref name="loose"/>) - a whip still rooted where it
        /// was thrown from is as thick there as anywhere. The <b>head</b> is swollen while it
        /// flies and goes down once it lands (<paramref name="swell"/>), which is what makes a
        /// travelling line read as a thing with a front. And the very <b>tip</b> is closed
        /// round, because a strip's end is a straight cut and a straight cut at the width of
        /// the swell is a nail head.
        /// </para>
        /// </summary>
        /// <param name="reach">How far the drawn part runs, in units.</param>
        public static float Girth(float along, float reach, float cell, float loose, float swell)
        {
            float tail = Mathf.Lerp(1f, Mathf.Lerp(TailLeast, 1f, Mathf.SmoothStep(0f, 1f, along / .45f)),
                                    Mathf.Clamp01(loose));

            float behind = (1f - along) * reach;
            float back = behind / Mathf.Max(1f, cell * SwellReach);
            float head = 1f + Swell * swell * Mathf.Exp(-back * back);

            float cap = Mathf.Max(1f, cell * .16f);
            float tip = behind >= cap ? 1f : Mathf.Sqrt(Mathf.Clamp01(1f - (1f - behind / cap) * (1f - behind / cap)));

            return tail * head * Mathf.Max(.06f, tip);
        }

        /// <summary>
        /// The joints of the part of <paramref name="whip"/> between <paramref name="tail"/> and
        /// <paramref name="head"/>, and how wide each is.
        ///
        /// <b>Four extra joints inside the tip</b>, because the round cap is a shape the even
        /// spacing cannot see: a whip a thousand units long has a joint every fifty, and the cap
        /// is twenty.
        /// </summary>
        public static void Trace(Whip whip, float tail, float head, float clock, float cell, float jag,
                                 List<Vector2> into, List<float> girth)
        {
            into.Clear();
            girth?.Clear();
            if (whip == null || head <= tail) return;

            float length = (whip.To - whip.From).magnitude;
            float reach = length * (head - tail);
            if (reach < 1f) return;

            float landed = clock - whip.Lands;
            float swell = landed <= 0f ? 1f : Mathf.Clamp01(1f - landed / SwellDown);
            float loose = tail / .06f;

            int steps = Mathf.Clamp(Mathf.CeilToInt(Joints * (head - tail)), 3, Joints);
            float cap = Mathf.Min(.5f, cell * .16f / reach);

            void Add(float along)
            {
                into.Add(Point(whip, Mathf.Lerp(tail, head, along), clock, cell, jag));
                girth?.Add(Girth(along, reach, cell, loose, swell));
            }

            for (int i = 0; i < steps; i++)
            {
                float along = i / (float)steps;
                if (along >= 1f - cap) break;
                Add(along);
            }

            Add(1f - cap);
            Add(1f - cap * .55f);
            Add(1f - cap * .25f);
            Add(1f - cap * .07f);
            Add(1f);
        }

        // ------------------------------------------------------------------ the mesh
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_whips == null || _width <= 0f) return;

            var tint = color;

            for (int i = 0; i < _whips.Count; i++)
            {
                Window(_whips[i], _clock, out float tail, out float head);
                if (head <= tail) continue;

                Trace(_whips[i], tail, head, _clock, _cell, _jag, _path, _girth);
                Lightning.Strip(vh, _path, _width, tint, false, _girth);
            }
        }

        // ------------------------------------------------------------------ a lash, layered
        /// <summary>
        /// A lash drawn as three layers on one list of whips: a wide soft glow and a sheath,
        /// both <em>added</em>, and over them a core that is <b>laid on, not added</b> - the one
        /// layer here the additive material may not draw, because it is dark, and adding black
        /// adds nothing (<see cref="Additive"/>). The dark is what the line <em>is</em>; the
        /// light round it is what lets a black line be seen on a dark board.
        /// </summary>
        public sealed class Layered
        {
            public RectTransform Host;
            public CanvasGroup Group;
            public Lash Halo, Sheath, Core;

            public readonly List<Whip> Whips = new List<Whip>();

            /// <summary>Seconds since the lash began, on all three layers.</summary>
            public float Clock
            {
                set
                {
                    if (Halo != null) Halo.Clock = value;
                    if (Sheath != null) Sheath.Clock = value;
                    if (Core != null) Core.Clock = value;
                }
            }

            /// <summary>When the last whip has gone in.</summary>
            public float Ends
            {
                get
                {
                    float ends = 0f;
                    for (int i = 0; i < Whips.Count; i++) ends = Mathf.Max(ends, Whips[i].Ends);
                    return ends;
                }
            }

            public void Destroy()
            {
                if (Host != null) Object.Destroy(Host.gameObject);
                Host = null;
            }
        }

        /// <summary>
        /// Builds a <see cref="Layered"/> lash under <paramref name="parent"/>: its light in
        /// <paramref name="glow"/>, its line in <paramref name="ink"/>. <paramref name="width"/>
        /// is the dark core's.
        /// </summary>
        public static Layered Grow(string name, RectTransform parent, Color glow, Color ink,
                                   float width, float cell, float jag)
        {
            // Stretched to the parent rather than a point, for `Lightning.Grow`'s reason: a
            // `RectMask2D` culls a graphic whose rect falls outside the clip.
            var host = UIKit.Node(name, parent);
            var lash = new Layered { Host = host, Group = UIKit.Group(host) };

            Lash Layer(string layer, Color colour, float wide, float falloff, bool lit)
            {
                var node = UIKit.Node(layer, host);

                var whip = node.gameObject.AddComponent<Lash>();
                whip.raycastTarget = false;
                whip.color = colour;
                whip.Width = wide;
                whip.Falloff = falloff;
                whip.Bind(lash.Whips, cell, jag);
                return lit ? Additive.Lit(whip) : whip;
            }

            lash.Halo = Layer("halo", new Color(glow.r, glow.g, glow.b, .46f), width * 5.4f, 1.6f, true);
            lash.Sheath = Layer("sheath", new Color(glow.r, glow.g, glow.b, .95f), width * 2.5f, 1.3f, true);
            lash.Core = Layer("core", new Color(ink.r, ink.g, ink.b, 1f), width * 1.3f, .55f, false);
            return lash;
        }
    }
}
