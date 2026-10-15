using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A ceremony's light: one layer of pooled, additive motes simulated by one loop.
    ///
    /// <para>
    /// <b>It exists because the turret ceremonies were built out of tweens and helpers that
    /// allocate.</b> <c>Burst.Sparks</c> builds a host, N images and a core glow per call and
    /// pools none of it, and a tween per mote is a closure per mote - fine for a dozen sparks,
    /// and the reason a celebration could never afford the hundreds of pieces of light a payoff
    /// in this genre is made of. Here every mote is a slot in one list, advanced by one
    /// <c>Update</c>, drawn by an <c>Image</c> borrowed from a pool that is warmed while the room
    /// is still fading in - so the loudest frame of the ceremony allocates nothing
    /// (<c>ui-effects-cost-the-canvas</c>'s two fixes, met before the fault rather than after).
    /// </para>
    /// <para>
    /// <b>A nested canvas</b>, for <c>SiegeView</c>'s reason: a canvas rebuilds all of its
    /// geometry when any graphic on it moves, and these move every frame. As a child canvas the
    /// rebuild stops here, and the captions, plate and buttons of the screen around it are never
    /// re-meshed by a spark. <c>overrideSorting</c> stays off so the layer keeps its place in the
    /// hierarchy's order; it carries no raycaster, because nothing drawn here takes a touch.
    /// </para>
    /// <para>
    /// <b>One child node per texture, and that is free only because the light adds.</b> A canvas
    /// batches neighbours that share a material and a texture, and pooled motes interleaved by
    /// birth would alternate textures and cost a draw call each. Additive blending is
    /// commutative - the order two lights are summed in does not change the sum - so motes can
    /// be sorted by sprite without changing a single pixel, and every family of light on the
    /// screen is one draw call however many of it there are.
    /// </para>
    /// <para>
    /// <b>Unscaled time</b>, like every tween in this game: a ceremony must not freeze because
    /// something set <c>Time.timeScale</c> (the ads plugin's Editor stub leaves it at nought).
    /// </para>
    /// </summary>
    public sealed class CeremonyFx : MonoBehaviour
    {
        // ------------------------------------------------------------------ the mote
        /// <summary>
        /// Everything one mote is, decided at birth. A struct, so emitting one allocates nothing.
        ///
        /// Positions are in the layer's local units with the origin at its centre - the space
        /// <c>UIKit.Box</c> places a centre-anchored child in, which is the space every band of
        /// the ceremonies is written in.
        /// </summary>
        public struct Mote
        {
            public Sprite Sprite;
            public Color Colour;

            public Vector2 At, Velocity;

            /// <summary>Seconds alive, and seconds before it is born.</summary>
            public float Life, Delay;

            /// <summary>Diameter at birth and at death, eased by <see cref="Grow"/>.</summary>
            public float Size, SizeEnd;

            /// <summary>Null is linear. <c>Ease.OutQuint</c> is a shockwave.</summary>
            public Easing Grow;

            /// <summary>Length added per unit of speed: nought is round, above it a streak along its path.</summary>
            public float Stretch;

            /// <summary>Downward pull in units per second squared; negative rises.</summary>
            public float Gravity;

            /// <summary>Exponential slowing, per second.</summary>
            public float Drag;

            /// <summary>Turn at birth and per second, in degrees. A stretched mote faces its path instead.</summary>
            public float Rotation, Spin;

            /// <summary>Vertical scale, for a ring lying on the floor. Nought means one.</summary>
            public float Squash;

            /// <summary>The share of its life it spends fading up, and how hard it fades out.</summary>
            public float FadeIn, FadePower;

            /// <summary>Flicker rate in hertz; nought is steady.</summary>
            public float Twinkle;

            /// <summary>Sideways sway, in units, for something drifting rather than thrown.</summary>
            public float Sway;

            // ---- an orbit: spirals into Centre instead of flying
            public bool Orbit;
            public Vector2 Centre;

            /// <summary>Distance from the centre at birth, angle at birth (radians) and radians turned over the life.</summary>
            public float Radius, Angle, Turn;
        }

        struct Live
        {
            public Mote M;
            public Image Img;
            public Bucket Bucket;
            public float Age;
            public Vector2 Pos, Last;
            public float Phase;
            public bool Born;
        }

        sealed class Bucket
        {
            public RectTransform Node;
            public readonly Stack<Image> Free = new Stack<Image>();
        }

        /// <summary>
        /// A continuous source: <see cref="Make"/> is asked for one mote per emission, at
        /// <see cref="Rate"/> a second, for <see cref="For"/> seconds (negative is for ever).
        /// </summary>
        public sealed class Stream
        {
            public Func<int, Mote> Make;
            public float Rate, For;
            internal float Clock, Owed;
            internal int Count;
            internal bool Dead;

            /// <summary>Stops emitting; what is already alive finishes its life.</summary>
            public void Stop() => Dead = true;
        }

        /// <summary>
        /// The most motes alive at once. A ceremony never needs more, and the cap is what keeps a
        /// skip-spamming player from compounding streams into a frame the phone cannot draw.
        /// </summary>
        public const int Most = 420;

        readonly List<Live> _live = new List<Live>(Most);
        readonly List<Stream> _streams = new List<Stream>();
        readonly Dictionary<Sprite, Bucket> _buckets = new Dictionary<Sprite, Bucket>();

        RectTransform _node;
        Material _material;

        /// <summary>The layer's own node - stretched over its parent, origin at the centre.</summary>
        public RectTransform Node => _node;

        // ------------------------------------------------------------------ build
        /// <summary>
        /// A layer stretched over <paramref name="parent"/>, added as its last child.
        /// </summary>
        /// <param name="warm">
        /// How many images each sprite's pool should hold before the loud beat, so the frame that
        /// throws the light builds none. Built a few per frame rather than all at once (see
        /// <see cref="WarmPerFrame"/>), so opening the screen does not hitch either.
        /// </param>
        public static CeremonyFx Attach(RectTransform parent, string name,
                                        params (Sprite sprite, int count)[] warm)
        {
            var node = UIKit.Node(name, parent);

            var canvas = node.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = false;
            if (Flow.Canvas != null) canvas.additionalShaderChannels = Flow.Canvas.additionalShaderChannels;

            var fx = node.gameObject.AddComponent<CeremonyFx>();
            fx._node = node;
            fx._material = Additive.Material;

            if (warm != null)
                foreach (var (sprite, count) in warm)
                    if (sprite != null && count > 0)
                        fx._warming.Enqueue((fx.BucketFor(sprite), sprite, count));

            return fx;
        }

        /// <summary>
        /// How many pooled images are built per frame while warming. Every ceremony opens on a
        /// half-second or more of room fading in before anything is thrown, which at this rate
        /// builds well over a thousand - far more than any pool here asks for - without one frame
        /// paying for more than a sliver of it.
        /// </summary>
        public const int WarmPerFrame = 32;

        readonly Queue<(Bucket bucket, Sprite sprite, int count)> _warming =
            new Queue<(Bucket bucket, Sprite sprite, int count)>();

        void Warm()
        {
            int budget = WarmPerFrame;

            while (budget > 0 && _warming.Count > 0)
            {
                var (bucket, sprite, count) = _warming.Peek();

                if (bucket.Free.Count >= count) { _warming.Dequeue(); continue; }

                bucket.Free.Push(Build(bucket, sprite));
                budget--;
            }
        }

        Bucket BucketFor(Sprite sprite)
        {
            if (_buckets.TryGetValue(sprite, out var bucket)) return bucket;

            bucket = new Bucket { Node = UIKit.Node(sprite != null ? sprite.name : "none", _node) };
            _buckets[sprite] = bucket;
            return bucket;
        }

        Image Build(Bucket bucket, Sprite sprite)
        {
            var img = UIKit.Img("m", bucket.Node, sprite, Color.clear, Vector2.one * 8f,
                                new Vector2(.5f, .5f), Vector2.zero);
            img.raycastTarget = false;
            if (_material != null) img.material = _material;
            img.enabled = false;
            return img;
        }

        /// <summary>
        /// One additive image on this layer that is <em>not</em> a mote - a core, a flare, a glyph
        /// the caller animates itself. Above every mote, so a hero light is never buried.
        /// </summary>
        public Image Light(string name, Sprite sprite, Color colour, Vector2 size, Vector2 at)
        {
            var img = UIKit.Img(name, _node, sprite, colour, size, new Vector2(.5f, .5f), at);
            img.raycastTarget = false;
            return Additive.Lit(img);
        }

        // ------------------------------------------------------------------ emit
        public void Emit(Mote m)
        {
            if (_node == null || m.Sprite == null || m.Life <= 0f) return;
            if (_live.Count >= Most) return;

            var bucket = BucketFor(m.Sprite);
            var img = bucket.Free.Count > 0 ? bucket.Free.Pop() : Build(bucket, m.Sprite);

            var live = new Live
            {
                M = m,
                Img = img,
                Bucket = bucket,
                Age = -Mathf.Max(0f, m.Delay),
                Phase = UnityEngine.Random.value * 10f,
            };

            live.Pos = m.Orbit ? OrbitAt(ref m, 0f) : m.At;
            live.Last = live.Pos;

            _live.Add(live);
        }

        public Stream Run(float rate, float seconds, Func<int, Mote> make)
        {
            var stream = new Stream { Rate = rate, For = seconds, Make = make };
            _streams.Add(stream);
            return stream;
        }

        /// <summary>
        /// Ends everything now - every mote back to its pool and every stream stopped. What a
        /// skip calls before it lays out the settled state.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _live.Count; i++) Retire(_live[i]);
            _live.Clear();
            _streams.Clear();
        }

        void Retire(Live live)
        {
            if (live.Img == null) return;
            live.Img.enabled = false;
            live.Bucket.Free.Push(live.Img);
        }

        // ------------------------------------------------------------------ simulate
        void Update()
        {
            // Clamped, so the frame after a resume or a hitch is one frame of motion rather than
            // a jump - TweenCycle's rule, at the size this layer needs.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            if (_warming.Count > 0) Warm();
            if (dt <= 0f) return;

            Feed(dt);
            Step(dt);
        }

        void Feed(float dt)
        {
            for (int s = _streams.Count - 1; s >= 0; s--)
            {
                var stream = _streams[s];

                if (stream.Dead || (stream.For >= 0f && stream.Clock >= stream.For) || stream.Make == null)
                {
                    _streams.RemoveAt(s);
                    continue;
                }

                stream.Clock += dt;
                stream.Owed += stream.Rate * dt;

                while (stream.Owed >= 1f)
                {
                    stream.Owed -= 1f;
                    Emit(stream.Make(stream.Count++));
                }
            }
        }

        void Step(float dt)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var live = _live[i];
                live.Age += dt;

                if (live.Img == null) { _live.RemoveAt(i); continue; }

                if (live.Age < 0f) { _live[i] = live; continue; }

                ref var m = ref live.M;
                float t = live.Age / m.Life;

                if (t >= 1f)
                {
                    Retire(live);
                    _live.RemoveAt(i);
                    continue;
                }

                if (!live.Born)
                {
                    live.Born = true;
                    live.Img.enabled = true;
                }

                live.Last = live.Pos;

                if (m.Orbit)
                {
                    live.Pos = OrbitAt(ref m, t);
                }
                else
                {
                    if (m.Drag > 0f) m.Velocity *= Mathf.Exp(-m.Drag * dt);
                    m.Velocity.y -= m.Gravity * dt;
                    live.Pos += m.Velocity * dt;
                }

                var drawn = live.Pos;
                if (m.Sway != 0f) drawn.x += Mathf.Sin(live.Phase + live.Age * 2.3f) * m.Sway;

                Draw(ref live, t, drawn, dt);
                _live[i] = live;
            }
        }

        static Vector2 OrbitAt(ref Mote m, float t)
        {
            // Pulled in slowly and then fast, and turning faster the closer it gets - the shape
            // anything falling into a well has, which is what makes a spiral read as a *pull*
            // rather than as decoration going round.
            float r = m.Radius * (1f - t * t);
            float a = m.Angle + m.Turn * t * t;
            return m.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        void Draw(ref Live live, float t, Vector2 at, float dt)
        {
            ref var m = ref live.M;
            var rt = live.Img.rectTransform;

            float grow = m.Grow != null ? m.Grow(t) : t;
            float size = Mathf.LerpUnclamped(m.Size, m.SizeEnd, grow);

            float fadeIn = m.FadeIn > 0f ? Mathf.Clamp01(t / m.FadeIn) : 1f;
            float fadeOut = Mathf.Pow(1f - t, m.FadePower > 0f ? m.FadePower : 1.6f);
            float flicker = m.Twinkle > 0f ? .62f + .38f * Mathf.Sin(live.Phase + live.Age * m.Twinkle * Mathf.PI * 2f) : 1f;

            var c = m.Colour;
            c.a *= fadeIn * fadeOut * flicker;
            live.Img.color = c;

            rt.anchoredPosition = at;

            if (m.Stretch > 0f)
            {
                var v = (live.Pos - live.Last) / dt;
                float speed = v.magnitude;
                float length = size + speed * m.Stretch;

                rt.sizeDelta = new Vector2(length, size);
                if (speed > 1f) rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
            }
            else
            {
                rt.sizeDelta = new Vector2(size, size * (m.Squash > 0f ? m.Squash : 1f));
                rt.localRotation = Quaternion.Euler(0f, 0f, m.Rotation + m.Spin * live.Age);
            }
        }

        // ------------------------------------------------------------------ recipes
        // The handful of gestures both ceremonies are made of. Here rather than in either screen
        // so a spark means the same thing on both (invariant 5b's rule, asked of a look).

        /// <summary>A ring of light leaving a point, thinning as it goes.</summary>
        public void Shock(Sprite ring, Vector2 at, Color colour, float from, float to, float life,
                          float delay = 0f, float squash = 0f)
        {
            Emit(new Mote
            {
                Sprite = ring, Colour = colour, At = at, Life = life, Delay = delay,
                Size = from, SizeEnd = to, Grow = Ease.OutQuint, FadePower = 1.4f, Squash = squash,
            });
        }

        /// <summary>
        /// Light thrown out of a point in every direction, slowed by the air and pulled down - a
        /// streak while it is fast and a dot as it slows, which is what reads as hot.
        /// </summary>
        public void Spray(Sprite sprite, Vector2 at, Color[] colours, int count, float slow, float fast,
                          float size, float life, float gravity = 900f, float drag = 2.6f,
                          float stretch = .05f, float lift = 0f, float delay = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-.3f, .3f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float speed = UnityEngine.Random.Range(slow, fast);

                Emit(new Mote
                {
                    Sprite = sprite,
                    Colour = colours[i % colours.Length],
                    At = at,
                    Velocity = dir * speed + Vector2.up * lift,
                    Life = life * UnityEngine.Random.Range(.7f, 1.25f),
                    Delay = delay,
                    Size = size * UnityEngine.Random.Range(.6f, 1.3f),
                    SizeEnd = size * .15f,
                    Gravity = gravity,
                    Drag = drag,
                    Stretch = stretch,
                    FadePower = 1.2f,
                });
            }
        }

        /// <summary>Hard-edged pieces flung out and tumbling under gravity.</summary>
        public void Shards(Sprite sprite, Vector2 at, Color[] colours, int count, float slow, float fast,
                           float size, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.Range(-.25f, .25f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * .85f + .25f);

                Emit(new Mote
                {
                    Sprite = sprite,
                    Colour = colours[i % colours.Length],
                    At = at,
                    Velocity = dir.normalized * UnityEngine.Random.Range(slow, fast),
                    Life = life * UnityEngine.Random.Range(.75f, 1.2f),
                    Size = size * UnityEngine.Random.Range(.55f, 1.25f),
                    SizeEnd = size * .35f,
                    Gravity = 1500f,
                    Drag = 1.1f,
                    Rotation = UnityEngine.Random.Range(0f, 360f),
                    Spin = UnityEngine.Random.Range(-540f, 540f),
                    Twinkle = UnityEngine.Random.Range(5f, 9f),
                    FadePower = 2.2f,
                });
            }
        }

        /// <summary>
        /// Light dragged into a point along spirals, for <paramref name="seconds"/>. Every mote is
        /// timed to <em>arrive</em> no later than the stream ends, so the gathering finishes on the
        /// beat the caller hangs its collapse on rather than trickling past it.
        /// </summary>
        public Stream Vortex(Sprite sprite, Vector2 centre, Color[] colours, float rate, float seconds,
                             float near, float far, float size, float turn = 2.6f)
        {
            return Run(rate, seconds, i =>
            {
                float left = Mathf.Max(.18f, seconds - i / Mathf.Max(1f, rate));

                return new Mote
                {
                    Sprite = sprite,
                    Colour = colours[i % colours.Length],
                    Orbit = true,
                    Centre = centre,
                    Radius = UnityEngine.Random.Range(near, far),
                    Angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                    Turn = turn * (UnityEngine.Random.value < .5f ? 1f : 1.25f),
                    Life = Mathf.Min(left, UnityEngine.Random.Range(.55f, .95f)),
                    Size = size * UnityEngine.Random.Range(.6f, 1.3f),
                    SizeEnd = size * .3f,
                    Stretch = .045f,
                    FadeIn = .25f,
                    FadePower = .5f,
                };
            });
        }

        /// <summary>Embers lifting off a band and drifting up - the room still warm after the bang.</summary>
        public Stream Rise(Sprite sprite, Vector2 centre, float spread, Color[] colours, float rate,
                           float seconds, float size, float speed = 160f)
        {
            return Run(rate, seconds, i => new Mote
            {
                Sprite = sprite,
                Colour = colours[i % colours.Length],
                At = centre + new Vector2(UnityEngine.Random.Range(-spread, spread), UnityEngine.Random.Range(-20f, 20f)),
                Velocity = new Vector2(UnityEngine.Random.Range(-30f, 30f), speed * UnityEngine.Random.Range(.7f, 1.3f)),
                Life = UnityEngine.Random.Range(1.4f, 2.4f),
                Size = size * UnityEngine.Random.Range(.6f, 1.3f),
                SizeEnd = size * .2f,
                Gravity = -40f,
                Drag = .4f,
                Sway = UnityEngine.Random.Range(8f, 22f),
                Twinkle = UnityEngine.Random.Range(1.5f, 3.5f),
                FadeIn = .2f,
                FadePower = 1.1f,
            });
        }

        /// <summary>Glints falling slowly from above the screen and twinkling as they come - a rain of light.</summary>
        public Stream Starfall(Sprite sprite, float halfWidth, float top, Color[] colours, float rate,
                               float seconds, float size)
        {
            return Run(rate, seconds, i => new Mote
            {
                Sprite = sprite,
                Colour = colours[i % colours.Length],
                At = new Vector2(UnityEngine.Random.Range(-halfWidth, halfWidth), top + UnityEngine.Random.Range(0f, 160f)),
                Velocity = new Vector2(UnityEngine.Random.Range(-40f, 40f), -UnityEngine.Random.Range(260f, 520f)),
                Life = UnityEngine.Random.Range(2.2f, 3.4f),
                Size = size * UnityEngine.Random.Range(.5f, 1.3f),
                SizeEnd = size * .4f,
                Rotation = UnityEngine.Random.Range(0f, 90f),
                Spin = UnityEngine.Random.Range(-90f, 90f),
                Sway = UnityEngine.Random.Range(14f, 40f),
                Twinkle = UnityEngine.Random.Range(2.5f, 5f),
                FadeIn = .1f,
                FadePower = .9f,
            });
        }

        void OnDestroy()
        {
            _live.Clear();
            _streams.Clear();
            _buckets.Clear();
            _warming.Clear();
        }
    }
}
