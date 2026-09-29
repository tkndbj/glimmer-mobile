using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One bolt of lightning, drawn as one mesh.
    ///
    /// <para>
    /// <b>A jagged polyline with branches, built as a triangle strip with a soft cross-section,
    /// so a whole bolt - trunk, forks and all - is one <c>Graphic</c> and one draw.</b> The boss
    /// storm's older bolts (<c>SiegeView.Storm</c>) are chains of capsule <c>Image</c>s, three
    /// per segment, which is fine for a handful of hops and is several hundred objects for a
    /// stormcall falling on nine raiders with three layers each. Here every segment is two
    /// vertices and every layer of a bolt is one component; the strip's <c>u</c> runs across its
    /// width and samples <see cref="Art.Profile"/>, so the edge is soft at any width and the
    /// three layers of a strike (halo, sheath, core) are three of these sharing one set of
    /// joints (<see cref="Follow"/>).
    /// </para>
    /// <para>
    /// <b>The geometry is a pure function of a seed, and the seed is what flickers.</b> Real
    /// lightning is a leader creeping down, a return stroke lighting the channel, and two or
    /// three re-strikes down a slightly different channel a few hundredths apart; what says
    /// <em>electricity</em> to an eye is that the next frame is unrelated to the last
    /// (<c>CRAFT.md</c> on the legendary reels). So a strike is drawn by calling
    /// <see cref="Strike"/> again with a new seed rather than by animating what was drawn -
    /// which also means the same board draws the same bolts twice given the same seeds, and
    /// <c>SiegeStrikeTests</c> can hold the shape without a screen.
    /// </para>
    /// <para>
    /// <b>The jag is pinched at both ends</b>: the offset is scaled by <c>sin(pi*t)</c>, nought
    /// at either end, so the first joint <em>is</em> where the bolt came from and the last
    /// <em>is</em> what it hit. That one term is the difference between lightning and a broken
    /// line, and it is the guarantee the strike fixture holds - a bolt lands on the thing it
    /// struck because its last joint is that thing, not because a frame was framed right.
    /// </para>
    /// <para>
    /// <see cref="Reveal"/> draws the first fraction of the trunk only, which is how a leader
    /// creeps: nothing about the joints moves, the strip is simply cut short and the cut end
    /// interpolated. Branches appear only once the trunk is whole.
    /// </para>
    /// </summary>
    public sealed class Lightning : MaskableGraphic
    {
        /// <summary>
        /// Segments per bolt at most. A ceiling rather than a count: the length decides, at about
        /// two thirds of a cell a segment, so a hop between neighbours is not a scribble and a
        /// strike out of the sky is not a smooth curve.
        /// </summary>
        public const int MostSegments = 16;

        /// <summary>Segments per bolt at least, so even a short hop has a bend in it.</summary>
        public const int FewestSegments = 3;

        /// <summary>A branch's width against its trunk's.</summary>
        public const float BranchWidth = .55f;

        /// <summary>How far a branch reaches, as a fraction of the trunk's length.</summary>
        public const float BranchReachLeast = .22f, BranchReachMost = .42f;

        readonly List<List<Vector2>> _paths = new List<List<Vector2>>(4);
        readonly List<Vector2> _cut = new List<Vector2>(MostSegments + 2);

        /// <summary>
        /// How many of <see cref="_paths"/>, from the front, are <b>trunks</b>: drawn at the full
        /// width, untapered, cut to <see cref="Reveal"/>, and ending on the thing they struck.
        /// Everything after them is a <b>limb</b> - a branch or a nova's arm - tapered to a point
        /// and drawn only once the trunks are whole.
        ///
        /// <b>A count rather than a flag per path</b>, because every builder here lays its
        /// trunks first: a strike is one trunk and its forks, a fan is one trunk per target, a
        /// nova is all limbs. One integer cannot disagree with the list it describes.
        /// </summary>
        int _trunks = 1;

        float _width = 12f;
        float _reveal = 1f;
        Sprite _profile;

        /// <summary>The trunk's width in units; branches are <see cref="BranchWidth"/> of it.</summary>
        public float Width
        {
            get => _width;
            set { if (!Mathf.Approximately(_width, value)) { _width = value; SetVerticesDirty(); } }
        }

        /// <summary>
        /// How much of the trunk is drawn, from its start: 1 is the whole bolt. Below 1 no
        /// branch is drawn at all.
        /// </summary>
        public float Reveal
        {
            get => _reveal;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(_reveal, value)) return;
                _reveal = value;
                SetVerticesDirty();
            }
        }

        /// <summary>The trunk's joints, first to last. Empty until <see cref="Strike"/> has been called.</summary>
        public IReadOnlyList<Vector2> Trunk => _paths.Count > 0 ? _paths[0] : (IReadOnlyList<Vector2>)System.Array.Empty<Vector2>();

        /// <summary>How many limbs the bolt carries: branches off a trunk, or a nova's arms.</summary>
        public int Branches => Mathf.Max(0, _paths.Count - Mathf.Min(_trunks, _paths.Count));

        public override Texture mainTexture
        {
            get
            {
                if (_profile == null) _profile = Art.Profile();
                return _profile != null ? _profile.texture : s_WhiteTexture;
            }
        }

        /// <summary>
        /// Builds a bolt from <paramref name="a"/> to <paramref name="b"/>, in the parent's
        /// local units, with <paramref name="forks"/> branches, off <paramref name="seed"/>.
        /// <paramref name="jag"/> is how far a joint may wander sideways, in cells.
        /// </summary>
        public void Strike(Vector2 a, Vector2 b, float cell, float jag, int forks, int seed)
        {
            Build(_paths, a, b, cell, jag, forks, new System.Random(seed));
            _trunks = 1;
            SetVerticesDirty();
        }

        /// <summary>
        /// One bolt from <paramref name="from"/> to <b>each</b> of <paramref name="to"/>: what a
        /// discharge does when it lands among several bodies. Every one of them is a trunk, so
        /// every one ends on the thing it struck (<see cref="Spread"/>).
        /// </summary>
        public void Fan(Vector2 from, IReadOnlyList<Vector2> to, float cell, float jag, int seed)
        {
            Spread(_paths, from, to, cell, jag, new System.Random(seed));
            _trunks = _paths.Count;
            SetVerticesDirty();
        }

        /// <summary>
        /// Arms thrown out of <paramref name="at"/> in every direction, each tapered to a point:
        /// the burst of arcs where a discharge lands (<see cref="Radiate"/>).
        /// </summary>
        public void Nova(Vector2 at, float inner, float outer, int arms, float cell, float jag, int seed)
        {
            Radiate(_paths, at, inner, outer, arms, cell, jag, new System.Random(seed));
            _trunks = 0;
            SetVerticesDirty();
        }

        /// <summary>Takes another bolt's joints, so two layers of one strike bend together.</summary>
        public void Follow(Lightning other)
        {
            _paths.Clear();
            if (other == null) { SetVerticesDirty(); return; }

            foreach (var path in other._paths) _paths.Add(new List<Vector2>(path));
            _trunks = other._trunks;
            SetVerticesDirty();
        }

        /// <summary>Forgets the bolt, so nothing is drawn until the next <see cref="Strike"/>.</summary>
        public void Clear()
        {
            _paths.Clear();
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------ the shape
        /// <summary>
        /// The joints of one run from <paramref name="a"/> to <paramref name="b"/>: a straight
        /// line pushed sideways at every joint but the two ends.
        /// </summary>
        public static List<Vector2> Joints(Vector2 a, Vector2 b, float cell, float jag, System.Random rng)
        {
            var dir = b - a;
            float len = dir.magnitude;

            int steps = Mathf.Clamp(Mathf.RoundToInt(len / Mathf.Max(1f, cell * .66f)),
                                    FewestSegments, MostSegments);
            var side = len > .001f ? new Vector2(-dir.y, dir.x) / len : Vector2.right;

            var joints = new List<Vector2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float pinch = Mathf.Sin(t * Mathf.PI);
                float push = (float)(rng.NextDouble() * 2.0 - 1.0) * jag * cell * pinch;
                joints.Add(Vector2.Lerp(a, b, t) + side * push);
            }

            // **The two ends are the two points, to the bit.** `Lerp` at one is `a + (b - a)`,
            // which in a float is not always `b`, and `sin(pi)` is not quite nought either; a
            // bolt that lands a hundredth of a unit off its raider is invisible today and is the
            // kind of drift that becomes visible the day something is anchored to the last joint.
            joints[0] = a;
            joints[steps] = b;

            return joints;
        }

        /// <summary>
        /// A trunk and its branches into <paramref name="paths"/>, trunk first.
        ///
        /// A branch leaves from an interior joint and goes a fraction of the trunk's length again
        /// in a direction of its own, and it never reaches the target - a second line arriving
        /// where the first did would read as two bolts rather than as one splitting.
        /// </summary>
        public static void Build(List<List<Vector2>> paths, Vector2 a, Vector2 b, float cell,
                                 float jag, int forks, System.Random rng)
        {
            paths.Clear();

            var trunk = Joints(a, b, cell, jag, rng);
            paths.Add(trunk);

            var away = (b - a).normalized;
            var side = new Vector2(-away.y, away.x);
            float span = (b - a).magnitude;

            for (int f = 0; f < forks; f++)
            {
                if (trunk.Count < 3) break;

                int at = 1 + rng.Next(trunk.Count - 2);
                var root = trunk[at];

                float turn = Mathf.Lerp(.5f, 1.3f, (float)rng.NextDouble()) * (rng.NextDouble() < .5 ? -1f : 1f);
                float reach = span * Mathf.Lerp(BranchReachLeast, BranchReachMost, (float)rng.NextDouble());
                var tip = root + (away + side * turn).normalized * reach;

                paths.Add(Joints(root, tip, cell, jag * 1.4f, rng));
            }
        }

        /// <summary>
        /// One trunk per target into <paramref name="paths"/>, in the order the targets came.
        ///
        /// <b>Each is <see cref="Joints"/> and nothing more</b>, which is the whole point: the
        /// guarantee that a bolt's last joint <em>is</em> what it struck is that builder's, so a
        /// fan of nine inherits it nine times rather than re-deriving it once. A target standing
        /// on the source is skipped - a bolt of no length is a dot with a mitre in it.
        /// </summary>
        public static void Spread(List<List<Vector2>> paths, Vector2 from, IReadOnlyList<Vector2> to,
                                  float cell, float jag, System.Random rng)
        {
            paths.Clear();
            if (to == null) return;

            for (int i = 0; i < to.Count; i++)
            {
                if ((to[i] - from).sqrMagnitude < 1f) continue;
                paths.Add(Joints(from, to[i], cell, jag, rng));
            }
        }

        /// <summary>
        /// <paramref name="arms"/> arms out of <paramref name="at"/>, dealt round the circle with
        /// each one's angle and reach its own: every arm starts <paramref name="inner"/> from the
        /// middle and ends somewhere between half way to <paramref name="outer"/> and all of it.
        ///
        /// <b>Dealt by sector rather than thrown at random</b>, so nine arms are nine directions:
        /// angles drawn freely clump, and a burst with a bald side reads as a thing that was cut
        /// off by the edge of something.
        /// </summary>
        public static void Radiate(List<List<Vector2>> paths, Vector2 at, float inner, float outer,
                                   int arms, float cell, float jag, System.Random rng)
        {
            paths.Clear();
            if (arms <= 0 || outer <= inner) return;

            float turn = (float)rng.NextDouble() * Mathf.PI * 2f;
            float sector = Mathf.PI * 2f / arms;

            for (int i = 0; i < arms; i++)
            {
                float angle = turn + sector * (i + Mathf.Lerp(-.38f, .38f, (float)rng.NextDouble()));
                float reach = Mathf.Lerp(inner + (outer - inner) * .5f, outer, (float)rng.NextDouble());

                var away = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                paths.Add(Joints(at + away * inner, at + away * reach, cell, jag, rng));
            }
        }

        // ------------------------------------------------------------------ the mesh
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_paths.Count == 0 || _width <= 0f) return;

            var tint = color;
            int trunks = Mathf.Min(_trunks, _paths.Count);

            // The trunks, cut to `Reveal`.
            for (int i = 0; i < trunks; i++)
            {
                Cut(_paths[i], _reveal, _cut);
                Strip(vh, _cut, _width, tint, taperEnd: false);
            }

            if (_reveal < 1f) return;

            for (int i = trunks; i < _paths.Count; i++)
                Strip(vh, _paths[i], _width * BranchWidth, tint, taperEnd: true);
        }

        /// <summary>The first <paramref name="fraction"/> of a path by length, into <paramref name="into"/> - what a leader draws.</summary>
        public static void Cut(List<Vector2> path, float fraction, List<Vector2> into)
        {
            into.Clear();
            if (path.Count == 0) return;

            if (fraction >= 1f) { into.AddRange(path); return; }

            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++) total += Vector2.Distance(path[i], path[i + 1]);

            float want = total * fraction;
            into.Add(path[0]);

            for (int i = 0; i + 1 < path.Count; i++)
            {
                float seg = Vector2.Distance(path[i], path[i + 1]);
                if (seg <= want) { want -= seg; into.Add(path[i + 1]); continue; }

                into.Add(Vector2.Lerp(path[i], path[i + 1], seg > 0f ? want / seg : 0f));
                return;
            }
        }

        /// <summary>
        /// Two vertices per joint, either side of the line along the joint's own normal - the
        /// average of the two segments meeting there, so bends are mitred rather than notched.
        /// The mitre is capped, or a sharp bend throws a spike. Both ends are pushed out by half
        /// a width so the soft profile has room to fall off, and a tapered end narrows to a point.
        ///
        /// <paramref name="girth"/>, when it is given, is a width of its own for every joint as a
        /// share of <paramref name="width"/> - which is how <see cref="Lash"/> draws a whip that
        /// is thin at its tail and swollen at its head on this same strip.
        /// </summary>
        internal static void Strip(VertexHelper vh, List<Vector2> path, float width, Color tint,
                                   bool taperEnd, List<float> girth = null)
        {
            int n = path.Count;
            if (n < 2) return;

            int first = vh.currentVertCount;
            float half = width * .5f;

            for (int i = 0; i < n; i++)
            {
                var back = i > 0 ? (path[i] - path[i - 1]).normalized : (path[i + 1] - path[i]).normalized;
                var fore = i + 1 < n ? (path[i + 1] - path[i]).normalized : back;

                var dir = (back + fore).normalized;
                if (dir.sqrMagnitude < .001f) dir = fore;

                var normal = new Vector2(-dir.y, dir.x);

                // A mitre grows as 1/cos(half the turn); capped at a third more than the width.
                float cosHalf = Mathf.Max(.75f, Vector2.Dot(normal, new Vector2(-fore.y, fore.x)));
                float reach = half / cosHalf;

                float taper = 1f;
                if (taperEnd && n > 2)
                {
                    float t = i / (float)(n - 1);
                    taper = Mathf.Lerp(1f, .25f, Mathf.Clamp01((t - .55f) / .45f));
                }

                if (girth != null && i < girth.Count) taper *= girth[i];

                var at = path[i];
                if (i == 0) at -= fore * half * (girth != null ? taper : 1f);
                else if (i == n - 1) at += back * half * (girth != null ? taper : taperEnd ? .5f : 1f);

                var off = normal * (reach * taper);

                vh.AddVert(at - off, tint, new Vector2(0f, .5f));
                vh.AddVert(at + off, tint, new Vector2(1f, .5f));
            }

            for (int i = 0; i + 1 < n; i++)
            {
                int v = first + i * 2;
                vh.AddTriangle(v, v + 1, v + 3);
                vh.AddTriangle(v, v + 3, v + 2);
            }
        }

        // ------------------------------------------------------------------ a strike, layered
        /// <summary>
        /// A strike drawn as three bolts on one set of joints: a wide soft halo, a sheath in the
        /// strike's own colour, and a near-white core. Built under <paramref name="host"/>, which
        /// carries the group everything is faded through.
        /// </summary>
        public sealed class Layered
        {
            public RectTransform Host;
            public CanvasGroup Group;
            public Lightning Halo, Sheath, Core;

            /// <summary>Rebuilds all three layers on the same new joints.</summary>
            public void Strike(Vector2 a, Vector2 b, float cell, float jag, int forks, int seed)
            {
                if (Core == null) return;
                Core.Strike(a, b, cell, jag, forks, seed);
                Shared();
            }

            /// <summary>All three layers as one bolt to each target. See <see cref="Lightning.Fan"/>.</summary>
            public void Fan(Vector2 from, IReadOnlyList<Vector2> to, float cell, float jag, int seed)
            {
                if (Core == null) return;
                Core.Fan(from, to, cell, jag, seed);
                Shared();
            }

            /// <summary>All three layers as a burst of arms. See <see cref="Lightning.Nova"/>.</summary>
            public void Nova(Vector2 at, float inner, float outer, int arms, float cell, float jag, int seed)
            {
                if (Core == null) return;
                Core.Nova(at, inner, outer, arms, cell, jag, seed);
                Shared();
            }

            void Shared()
            {
                if (Sheath != null) Sheath.Follow(Core);
                if (Halo != null) Halo.Follow(Core);
            }

            public float Reveal
            {
                set
                {
                    if (Core != null) Core.Reveal = value;
                    if (Sheath != null) Sheath.Reveal = value;
                    if (Halo != null) Halo.Reveal = value;
                }
            }

            public void Destroy()
            {
                if (Host != null) Object.Destroy(Host.gameObject);
                Host = null;
            }
        }

        /// <summary>
        /// Builds a <see cref="Layered"/> strike under <paramref name="parent"/>, lit additively
        /// where the build allows (<see cref="Additive"/>).
        ///
        /// <paramref name="width"/> is the core's; the sheath is about twice it and the halo
        /// about five times, in the proportions the boss storm settled on (a white filament in a
        /// soft glow comes out white at phone size; the sheath at full strength is what carries
        /// the colour).
        /// </summary>
        public static Layered Grow(string name, RectTransform parent, Color tint, float width,
                                  float haloAlpha = .5f)
        {
            // **Stretched to the parent rather than a point**, on purpose: a `RectMask2D` culls
            // a graphic whose rect falls outside the clip, and a bolt's vertices reach far past
            // any rect a point node would have. A node the size of its layer is never culled
            // and its local origin is the layer's centre, which is the space every effect here
            // is placed in.
            var host = UIKit.Node(name, parent);
            var group = UIKit.Group(host);

            Lightning Layer(string layer, Color colour, float wide)
            {
                var node = UIKit.Node(layer, host);

                var bolt = node.gameObject.AddComponent<Lightning>();
                bolt.raycastTarget = false;
                bolt.color = colour;
                bolt.Width = wide;
                return Additive.Lit(bolt);
            }

            return new Layered
            {
                Host = host,
                Group = group,
                Halo = Layer("halo", Pal.A(tint, haloAlpha), width * 5.2f),
                Sheath = Layer("sheath", Pal.A(tint, .95f), width * 2.1f),
                Core = Layer("core", Pal.A(Color.Lerp(tint, Color.white, .8f), 1f), width),
            };
        }
    }
}
