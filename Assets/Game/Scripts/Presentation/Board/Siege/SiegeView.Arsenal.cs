using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>How the Shuffle lane's attack cards look</b> (<c>SiegeBoard.Arsenal</c>): every hit a
    /// build dealt is drawn as the thing it is - a pellet, a ricochet, a missile, a quake, a pool, a
    /// tesla arc, an ice shard, a meteor, a vortex, a nuke, the ray, a hydra's head - and so is
    /// what the build makes of the shipped cards' abilities: a chain is lightning hopping body to
    /// body, a splash a burst, a lance a beam.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, and the doors say so.</b> Everything here is reached from a
    /// hit whose <see cref="SiegeBolt.Via"/> is not <see cref="SiegeVia.None"/>, a
    /// <see cref="SiegeVolley"/>, or <see cref="Arsenal"/> - which returns on the plain line
    /// before it touches anything - and the board reports neither of the first two on any board
    /// holding <see cref="SiegeBoosts.None"/> (<c>ShuffleTests.ThePlainLineNeverDrawsTheArsenal</c>).
    /// A chapter and the Infinite lane draw exactly what they drew before this file existed.
    /// </para>
    /// <para>
    /// <b>Cheap where it repeats.</b> A spun-up line with a hydra fires dozens of heads a second,
    /// so every projectile here is a pooled <see cref="Dart"/> - one node, a head and a tail,
    /// handed back on a timer (<see cref="Ends"/>' reason) - and every per-target flourish that
    /// would stack on one spot (the splash ring, the lance beam, the coil's crackle) is drawn
    /// once a frame per source. What persists (pools, a vortex, the ray, a turret's heat) is built
    /// once and moved, never rebuilt per frame.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the colours
        static readonly Color TeslaHue = Pal.Aqua;
        static readonly Color IceHue = Pal.Azure;
        static readonly Color ToxicHue = Pal.Verdant;
        static readonly Color VortexHue = Pal.Foxglove;
        static readonly Color RayHue = Pal.Bloom;
        static readonly Color MeteorHue = Pal.Amber;
        static readonly Color QuakeHue = Pal.Parchment;
        static readonly Color HydraHue = Pal.Mint;

        // ------------------------------------------------------------------ once a frame
        /// <summary>Sources already given their flourish this frame (a splash ring, a beam, a crackle).</summary>
        readonly HashSet<long> _flourished = new HashSet<long>();

        int _flourishFrame = -1;

        /// <summary>Whether <paramref name="kind"/> from <paramref name="source"/> has been drawn this frame; marks it if not.</summary>
        bool Flourished(SiegeVia kind, int source)
        {
            if (_flourishFrame != Time.frameCount)
            {
                _flourishFrame = Time.frameCount;
                _flourished.Clear();
            }

            return !_flourished.Add(((long)kind << 32) | (uint)source);
        }

        bool _killSounded;
        int _killSoundFrame = -1;

        /// <summary>One kill sound a frame at most, however many bodies a nuke takes.</summary>
        void KillSound()
        {
            if (_killSoundFrame == Time.frameCount && _killSounded) return;

            _killSoundFrame = Time.frameCount;
            _killSounded = true;
            Audio.SfxVaried("zap", .32f);
        }

        // ------------------------------------------------------------------ the dart
        /// <summary>A pooled projectile: a lit head and a tail laid along its flight.</summary>
        sealed class Dart
        {
            public RectTransform Node;
            public Image Head, Tail;
            public bool Out;
        }

        readonly Stack<Dart> _darts = new Stack<Dart>(32);

        Dart LendDart(Color tint, float size)
        {
            Dart dart = null;
            while (_darts.Count > 0 && dart == null)
            {
                dart = _darts.Pop();
                if (dart.Node == null) dart = null;
            }

            if (dart == null)
            {
                var node = UIKit.Node("Dart", _fx);
                var tail = Lit("Tail", node, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
                var head = Lit("Head", node, Art.Glow(96, 2.0f), Color.clear, Vector2.one);
                dart = new Dart { Node = node, Head = head, Tail = tail };
            }

            dart.Out = true;
            dart.Node.gameObject.SetActive(true);
            dart.Node.SetAsLastSibling();
            dart.Node.localScale = Vector3.one;
            dart.Head.color = Pal.A(Color.Lerp(tint, Color.white, .45f), 1f);
            dart.Head.rectTransform.sizeDelta = Vector2.one * size;
            dart.Tail.color = Pal.A(tint, .75f);
            dart.Tail.rectTransform.sizeDelta = new Vector2(size * .55f, size);
            return dart;
        }

        void GiveDart(Dart dart)
        {
            if (dart == null || !dart.Out) return;
            dart.Out = false;

            if (dart.Node == null) return;

            Tween.KillAll(dart.Node);
            dart.Node.gameObject.SetActive(false);
            _darts.Push(dart);
        }

        /// <summary>
        /// Flies a dart from <paramref name="a"/> to <paramref name="b"/> over
        /// <paramref name="flight"/> seconds, bowed sideways by <paramref name="bow"/> (a share of
        /// the distance), dropping embers of <paramref name="wake"/> if it is not clear, and runs
        /// <paramref name="landed"/> where it lands.
        /// </summary>
        void Fly(Vector2 a, Vector2 b, Color tint, float size, float flight, float bow,
                 Color wake, System.Action landed)
        {
            if (_fx == null) { landed?.Invoke(); return; }

            var dart = LendDart(tint, size);
            var node = dart.Node;
            var tail = dart.Tail.rectTransform;

            var span = b - a;
            var side = span.sqrMagnitude > 1f ? new Vector2(-span.y, span.x).normalized : Vector2.zero;
            float reach = span.magnitude;
            var last = a;
            float nextWake = 0f;

            node.anchoredPosition = a;

            Tween.Run(flight, Ease.Linear, t =>
            {
                if (!node) return;

                float arc = Mathf.Sin(t * Mathf.PI) * bow * reach;
                var at = Vector2.Lerp(a, b, t) + side * arc;
                var dir = at - last;

                node.anchoredPosition = at;

                if (dir.sqrMagnitude > .01f)
                {
                    float len = Mathf.Clamp(dir.magnitude * 3.2f, size, size * 3.4f);
                    tail.sizeDelta = new Vector2(size * .55f, len);
                    tail.anchoredPosition = -dir.normalized * len * .45f;
                    tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
                }

                if (wake.a > 0f && t >= nextWake)
                {
                    nextWake = t + .12f;
                    Cinder(at, wake, size * .7f, .32f, -dir.normalized * Cell * .3f);
                }

                last = at;
            }, node).OnDone(() =>
            {
                GiveDart(dart);
                landed?.Invoke();
            });

            Tween.After(flight + .2f, () => GiveDart(dart), node);
        }

        // ------------------------------------------------------------------ a hit a build dealt
        /// <summary>
        /// Draws one hit whose <see cref="SiegeBolt.Via"/> is a build's. The damage figure is
        /// <see cref="Number"/>'s, as every hit's is; what differs is the picture of how it got
        /// there.
        /// </summary>
        void Volleyed(SiegeBolt shot)
        {
            var mob = MobOf(shot.Raider);
            if (mob == null || mob.Node == null) return;

            Vector2 to = mob.Node.anchoredPosition;
            Vector2 source = SourceOf(shot, to);
            Color tint = shot.Ward >= 0 && shot.Ward < _board.Wards.Count
                       ? TintOf(_board.Wards[shot.Ward].Colour)
                       : Pal.Cream;

            System.Action land = () =>
            {
                if (this == null) return;
                Number(shot.Raider, to, shot.Damage, false);
                if (shot.Killed) KillSound();
            };

            switch (shot.Via)
            {
                case SiegeVia.Splash:
                    if (!Flourished(SiegeVia.Splash, shot.From))
                    {
                        Shockwave(source, Pal.Lift(tint, .35f), 3.2f, .3f);
                        Pop(source, tint, 2.6f, .26f);
                    }
                    Pop(to, tint, 1.3f, .2f);
                    land();
                    break;

                case SiegeVia.Arc:
                    Arc(source, to, TeslaHue, Cell * .07f, .24f, 1, .38f);
                    Pop(to, TeslaHue, 1.4f, .2f);
                    if (!Flourished(SiegeVia.Arc, shot.From)) Audio.SfxVaried("arc", .18f);
                    land();
                    break;

                case SiegeVia.Lance:
                    if (!Flourished(SiegeVia.Lance, shot.Ward)) Lance(shot.Ward, to.x, tint);
                    Pop(to, Pal.Radiance, 1.5f, .22f);
                    land();
                    break;

                case SiegeVia.Twin:
                    Fly(source, to + new Vector2(Cell * .3f, 0f), tint, Cell * .42f, .16f, .12f, Color.clear,
                        () => { Pop(to, tint, 1.2f, .18f); land(); });
                    break;

                case SiegeVia.Execute:
                    Executed(to, mob);
                    land();
                    break;

                case SiegeVia.Blast:
                    if (!Flourished(SiegeVia.Blast, shot.From))
                    {
                        Boom(source, Blast("boom_fire"), Cell * 3.2f);
                        Audio.SfxVaried("boom", .22f);
                    }
                    Pop(to, Pal.Marigold, 1.4f, .2f);
                    land();
                    break;

                case SiegeVia.Pellet:
                    Fly(source, to, tint, Cell * .34f, .15f, Random.Range(-.12f, .12f), Color.clear,
                        () => { Pop(to, tint, 1f, .16f); land(); });
                    break;

                case SiegeVia.Ricochet:
                    Fly(source, to, Pal.Gold, Cell * .45f, .14f, .18f, Pal.Gold, () =>
                    {
                        Pop(to, Pal.Gold, 1.6f, .2f);
                        Burst.Sparks(_fx, to, Pal.Gold, 6, Cell * 1.4f, Cell * .14f, .3f);
                        land();
                    });
                    break;

                case SiegeVia.Missile:
                {
                    var from = source + new Vector2(Random.Range(-Cell * .4f, Cell * .4f), Cell * .3f);
                    Fly(from, to, MeteorHue, Cell * .5f, .42f, Random.value < .5f ? .35f : -.35f,
                        Pal.Hollow, () =>
                        {
                            Boom(to, Blast("boom_fire"), Cell * 2f);
                            Pop(to, MeteorHue, 1.8f, .22f);
                            land();
                        });
                    if (!Flourished(SiegeVia.Missile, shot.Ward)) Audio.SfxVaried("whoosh", .2f);
                    break;
                }

                case SiegeVia.Quake:
                    Pop(to, QuakeHue, 1.5f, .22f);
                    Tween.Punch(mob.Node, .12f, .18f);
                    land();
                    break;

                case SiegeVia.Toxic:
                    Pop(to, ToxicHue, 1.1f, .2f);
                    land();
                    break;

                case SiegeVia.Tesla:
                {
                    var post = new Vector2(PostX(shot.Ward), _lineY + Cell * 1.3f);
                    Arc(post, to, TeslaHue, Cell * .085f, .26f, 2, .42f);
                    Pop(to, TeslaHue, 1.6f, .22f);
                    if (!Flourished(SiegeVia.Tesla, shot.Ward))
                    {
                        Burst.Sparks(_fx, post, TeslaHue, 6, Cell * 1.2f, Cell * .12f, .26f);
                        Audio.SfxVaried("arc", .22f);
                    }
                    land();
                    break;
                }

                case SiegeVia.Shatter:
                    Fly(source, to, IceHue, Cell * .4f, .16f, .1f, Pal.Glass,
                        () => { Pop(to, IceHue, 1.5f, .2f); land(); });
                    break;

                case SiegeVia.Meteor:
                    Pop(to, MeteorHue, 1.8f, .24f);
                    land();
                    break;

                case SiegeVia.Vortex:
                    Pop(to, VortexHue, 1.1f, .2f);
                    land();
                    break;

                case SiegeVia.Nuke:
                    Pop(to, Pal.Radiance, 2.2f, .3f);
                    land();
                    break;

                case SiegeVia.Ray:
                    Pop(to, RayHue, 1.3f, .18f);
                    land();
                    break;

                case SiegeVia.Hydra:
                    Fly(source, to, HydraHue, Cell * .4f, .2f, Random.value < .5f ? .3f : -.3f, HydraHue,
                        () => { Pop(to, HydraHue, 1.4f, .2f); land(); });
                    break;

                default:
                    Pop(to, tint, 1.2f, .2f);
                    land();
                    break;
            }
        }

        /// <summary>Where a build's hit came from: the body it left, else its ward's muzzle, else above the target.</summary>
        Vector2 SourceOf(SiegeBolt shot, Vector2 to)
        {
            if (shot.From >= 0)
            {
                var from = MobOf(shot.From);
                if (from != null && from.Node != null) return from.Node.anchoredPosition;
            }

            if (shot.Ward >= 0 && _posts != null && shot.Ward < _posts.Length)
                return new Vector2(PostX(shot.Ward), _lineY + Cell);

            return to + new Vector2(0f, Cell * 2f);
        }

        /// <summary>A lance as a beam: from the ward's muzzle straight up its lane to the top of the hill.</summary>
        void Lance(int ward, float x, Color tint)
        {
            if (_fx == null || ward < 0) return;

            var foot = new Vector2(PostX(ward), _lineY + Cell);
            var top = new Vector2(x, _hillTop);

            var host = UIKit.Node("Lance", _fx);
            var beam = Shaft(host, foot, top, tint, Cell * .55f);

            Tween.Run(.32f, Ease.OutQuad, t =>
            {
                if (!host) return;
                foreach (var img in beam) if (img) img.color = Pal.A(img.color, (1f - t) * (img == beam[2] ? 1f : .8f));
                host.localScale = new Vector3(1f - t * .5f, 1f, 1f);
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Audio.SfxVaried("charge", .16f);
        }

        /// <summary>A three-layer beam between two points: a wide halo, a coloured sheath and a white core.</summary>
        Image[] Shaft(RectTransform host, Vector2 a, Vector2 b, Color tint, float wide)
        {
            var shaft = new[]
            {
                Lit("h", host, Art.SoftCapsule(40, 120), Pal.A(tint, .5f), Vector2.one),
                Lit("s", host, Art.Capsule(24, 96), Pal.A(tint, .9f), Vector2.one),
                Lit("c", host, Art.Capsule(24, 96), Pal.A(Color.white, 1f), Vector2.one),
            };

            Lay(shaft, a, b, wide);
            return shaft;
        }

        /// <summary>Lays a shaft built by <see cref="Shaft"/> between two points, at a width. Moves it; builds nothing.</summary>
        static void Lay(Image[] shaft, Vector2 a, Vector2 b, float wide)
        {
            var dir = b - a;
            float len = dir.magnitude;
            var turn = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            var mid = (a + b) * .5f;

            float[] across = { wide * 3f, wide, wide * .4f };
            float[] over = { wide * 2f, wide, wide * .4f };

            for (int i = 0; i < shaft.Length && i < 3; i++)
            {
                if (shaft[i] == null) continue;
                var rt = shaft[i].rectTransform;
                rt.anchoredPosition = mid;
                rt.localRotation = turn;
                rt.sizeDelta = new Vector2(across[i], len + over[i]);
            }
        }

        /// <summary>An execution: a red cross slashed over the body and a jolt.</summary>
        void Executed(Vector2 at, Mob mob)
        {
            if (_fx == null) return;

            var host = UIKit.Node("Execute", _fx);
            host.anchoredPosition = at;

            for (int i = 0; i < 2; i++)
            {
                var slash = Lit("x", host, Art.Capsule(24, 96), Pal.A(Pal.Poppy, 1f),
                                new Vector2(Cell * .22f, Cell * 2.2f));
                slash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 40f : -40f);
            }

            host.localScale = Vector3.one * .3f;
            Tween.Run(.34f, Ease.OutCubic, t =>
            {
                if (!host) return;
                host.localScale = Vector3.one * Mathf.Lerp(.3f, 1.15f, t);
                var group = host.GetComponentsInChildren<Image>();
                foreach (var img in group) img.color = Pal.A(Pal.Poppy, 1f - t * t);
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Pop(at, Pal.Poppy, 2f, .24f);
            if (mob != null && mob.Node != null) Tween.Shake(mob.Node, Cell * .12f, .2f);
            Audio.SfxVaried("felled", .26f);
        }

        // ------------------------------------------------------------------ a place a build set off
        /// <summary>Draws one <see cref="SiegeVolley"/>: the event around the hits it dealt.</summary>
        void Volleyed(SiegeVolley volley)
        {
            if (_fx == null) return;

            var at = new Vector2(LaneX(volley.Lane), MarchY(volley.March));

            switch (volley.Via)
            {
                case SiegeVia.Quake:
                {
                    // Rolled across the whole row: a flat ring as wide as the hill, a crack under
                    // the target, dust thrown up and the board jolted.
                    var row = new Vector2(0f, at.y);
                    Ripple(row, QuakeHue, Span.x * 1.15f, .5f, 0f, .22f);
                    Ripple(at, Pal.Sun, Cell * 4f, .38f, .04f);
                    var crack = Lit("Crack", _fx, StrikeFx.Crack, Pal.A(QuakeHue, .9f), Vector2.one * Cell * 2.6f);
                    if (crack != null)
                    {
                        crack.rectTransform.anchoredPosition = at;
                        Tween.Fade(crack, 0f, .6f, Ease.InQuad).OnDone(() => { if (crack) Destroy(crack.gameObject); });
                    }
                    Embers(at, QuakeHue, 8, Cell * .5f, Cell * .22f);
                    ShakeBoard(Cell * .1f);
                    Audio.SfxVaried("land", .3f);
                    break;
                }

                case SiegeVia.Shatter:
                    Ripple(at, IceHue, Cell * (3f + volley.Reach * 1.6f), .4f, 0f);
                    Embers(at, Pal.Glass, 14, Cell * .7f, Cell * .26f);
                    Pop(at, Pal.Glass, 2.8f, .28f);
                    Audio.SfxVaried("shatter", .3f);
                    break;

                case SiegeVia.Wildfire:
                    Boom(at, Blast("boom_fire"), Cell * (2.6f + volley.Reach));
                    Embers(at, Pal.Ember, 12, Cell * .6f, Cell * .24f);
                    Audio.SfxVaried("burst", .22f);
                    break;

                case SiegeVia.Meteor:
                    if (volley.In > 0f) MeteorFalls(at, volley.In);
                    else MeteorLands(at, volley.Reach);
                    break;

                case SiegeVia.Vortex:
                    Ripple(at, VortexHue, Cell * 5f, .45f, 0f);
                    Audio.SfxVaried("voidblast", .3f);
                    break;

                case SiegeVia.Nuke:
                    if (volley.In > 0f) NukeFalls(volley.In);
                    else NukeLands();
                    break;

                case SiegeVia.Ray:
                    Lightup(RayHue, .14f, .5f);
                    Audio.Sfx("charge", .4f, .8f);
                    break;
            }
        }

        /// <summary>A meteor's shadow on the box it will land on, and the rock coming down out of the sky.</summary>
        void MeteorFalls(Vector2 at, float over)
        {
            var shadow = UIKit.Img("Shadow", _fx, Art.Glow(96, 1.6f), new Color(0f, 0f, 0f, 0f),
                                   new Vector2(Cell * 4f, Cell * 2f));
            shadow.raycastTarget = false;
            shadow.rectTransform.anchoredPosition = at;
            shadow.rectTransform.localScale = Vector3.one * .3f;

            var mark = Lit("Mark", _fx, StrikeFx.Ring, Pal.A(Pal.Poppy, 0f), new Vector2(Cell * 3.6f, Cell * 1.8f));

            Tween.Run(over, Ease.InQuad, t =>
            {
                if (shadow) { shadow.color = new Color(0f, 0f, 0f, .45f * t); shadow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.3f, 1f, t); }
                if (mark) { mark.rectTransform.anchoredPosition = at; mark.color = Pal.A(Pal.Poppy, .8f * Mathf.Abs(Mathf.Sin(t * 14f))); }
            }, shadow).OnDone(() =>
            {
                if (shadow) Destroy(shadow.gameObject);
                if (mark) Destroy(mark.gameObject);
            });

            var sky = new Vector2(at.x - Cell * 3f, _hillTop + Cell * 4f);
            Fly(sky, at, MeteorHue, Cell * 1.3f, over, 0f, Pal.Ember, null);
            Audio.Sfx("whoosh", .4f, .7f);
        }

        void MeteorLands(Vector2 at, int reach)
        {
            float wide = Cell * (3f + reach * 1.8f);
            Boom(at, Blast("boom_fire"), wide * 1.2f);
            Boom(at + new Vector2(0f, Cell * .4f), Blast("boom_smoke"), wide * 1.3f, 18f);
            Ripple(at, MeteorHue, wide * 1.4f, .5f, 0f);
            Embers(at, Pal.Ember, 18, Cell * .8f, Cell * .3f);
            Lightup(MeteorHue, .16f, .35f);
            ShakeBoard(Cell * .22f);
            Audio.Sfx("boom", .55f, .8f);
        }

        /// <summary>A nuke on its way: the hill flashes red and something white-hot falls to its middle.</summary>
        void NukeFalls(float over)
        {
            var centre = new Vector2(0f, MarchY(.5f));

            for (int i = 0; i < 3; i++)
                Tween.After(i * over / 3f, () => { if (this) Lightup(Pal.Poppy, .16f, over / 3.5f); }, this);

            Fly(new Vector2(0f, _hillTop + Cell * 5f), centre, Pal.Radiance, Cell * 1.1f, over, 0f, Pal.Cream, null);
            Ripple(centre, Pal.Poppy, Cell * 6f, over, 0f);
            Audio.Sfx("charge", .5f, .6f);
        }

        void NukeLands()
        {
            var centre = new Vector2(0f, MarchY(.5f));

            Lightup(Color.white, .85f, .8f);
            Boom(centre, Blast("boom_burst"), Cell * 9f, 22f);
            Boom(centre, Blast("boom_fire"), Cell * 7f);
            Tween.After(.12f, () => { if (this) Boom(centre, Blast("boom_smoke"), Cell * 8f, 16f); }, this);
            Ripple(centre, Pal.Radiance, Span.x * 1.8f, .8f, 0f, .6f);
            Ripple(centre, Pal.Sun, Span.x * 1.3f, .7f, .1f, .6f);
            Embers(centre, Pal.Sun, 28, Cell * 1.2f, Cell * .34f);
            ShakeBoard(Cell * .5f);
            Audio.Sfx("boom", .9f, .6f);
            Audio.Sfx("thunder", .5f, .8f, .05f);
        }

        // ------------------------------------------------------------------ what persists
        /// <summary>A pool's picture, keyed by its box.</summary>
        readonly Dictionary<int, Image> _poolArt = new Dictionary<int, Image>(8);
        readonly HashSet<int> _poolsSeen = new HashSet<int>();
        float _bubble;

        RectTransform _vortex;
        Image[] _vortexParts;

        RectTransform _ray;
        Image[] _rayCore;
        Image[][] _rayFeeds;

        Image[] _heat;

        /// <summary>
        /// Keeps everything a build leaves standing drawn true, every frame: the pools, the
        /// vortex, the ray and each turret's heat. <b>Returns on the plain line before it reads
        /// anything</b>, which is the whole cost of this file on every other board.
        /// </summary>
        void Arsenal(float dt)
        {
            if (_board == null || _board.Boosts.IsIdentity || _fx == null) return;

            Pools(dt);
            Vortex(dt);
            Ray(dt);
            Heat(dt);
        }

        void Pools(float dt)
        {
            var pools = _board.Pools;
            _poolsSeen.Clear();
            _bubble -= dt;
            bool bubble = _bubble <= 0f;
            if (bubble) _bubble = .18f;

            for (int i = 0; i < pools.Count; i++)
            {
                var pool = pools[i];
                int key = pool.Row * SiegeTuning.Lanes + pool.Lane;
                _poolsSeen.Add(key);

                var at = BoxAt(pool.Lane, pool.Row);

                if (!_poolArt.TryGetValue(key, out var img) || img == null)
                {
                    img = Lit("Pool", _fx, Art.Glow(96, 1.4f), Pal.A(ToxicHue, 0f), new Vector2(Cell * 2.6f, Cell * 1.3f));
                    img.transform.SetAsFirstSibling();
                    _poolArt[key] = img;
                }

                float life = pool.For > 0f ? Mathf.Clamp01(pool.Left / pool.For) : 0f;
                float throb = .85f + .15f * Mathf.Sin(Time.unscaledTime * 6f + key);
                img.rectTransform.anchoredPosition = at;
                img.color = Pal.A(ToxicHue, .55f * Mathf.Sqrt(life) * throb);

                if (bubble && Random.value < .6f)
                    Cinder(at + new Vector2(Random.Range(-Cell, Cell), Random.Range(-Cell * .3f, Cell * .3f)),
                           ToxicHue, Cell * .22f, .5f, new Vector2(0f, Cell * .6f));
            }

            if (_poolArt.Count == _poolsSeen.Count) return;

            var gone = new List<int>();
            foreach (var pair in _poolArt) if (!_poolsSeen.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (int key in gone)
            {
                if (_poolArt[key] != null) Destroy(_poolArt[key].gameObject);
                _poolArt.Remove(key);
            }
        }

        void Vortex(float dt)
        {
            if (!_board.VortexOpen)
            {
                if (_vortex != null) Destroy(_vortex.gameObject);
                _vortex = null;
                return;
            }

            if (_vortex == null)
            {
                _vortex = UIKit.Node("Vortex", _fx);
                float wide = Cell * 4.4f;

                var disc = WellPiece("Disc", _vortex, GravityFx.Disc, wide, true);
                var arms = WellPiece("Arms", _vortex, GravityFx.Arms, wide * .94f, true);
                var ring = WellPiece("Ring", _vortex, GravityFx.Ring, wide * .6f, true);
                var core = UIKit.Img("Core", _vortex, Art.Glow(96, 2.4f), new Color(.05f, 0f, .1f, .9f), Vector2.one * wide * .32f);
                core.raycastTarget = false;

                _vortexParts = new[] { disc, arms, ring, core };
                foreach (var part in _vortexParts) if (part != null && part != core) part.color = Pal.A(VortexHue, .9f);
            }

            float left = _board.VortexLeft;
            float open = Mathf.Clamp01((SiegeArsenal.VortexFor - left) / .25f) * Mathf.Clamp01(left / .3f);

            _vortex.anchoredPosition = new Vector2(LaneX(_board.VortexLane), MarchY(_board.VortexMarch));
            _vortex.localScale = new Vector3(1f, .55f, 1f) * Mathf.Lerp(.2f, 1f, Ease.OutBack(open));

            float turn = Time.unscaledTime;
            if (_vortexParts[1] != null) _vortexParts[1].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -turn * 260f);
            if (_vortexParts[0] != null) _vortexParts[0].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -turn * 120f);
            if (_vortexParts[2] != null) _vortexParts[2].rectTransform.localScale = Vector3.one * (1f + .12f * Mathf.Sin(turn * 9f));

            if (Random.value < dt * 30f)
            {
                var edge = _vortex.anchoredPosition + Random.insideUnitCircle.normalized * Cell * 2.2f;
                Cinder(edge, VortexHue, Cell * .2f, .4f, (_vortex.anchoredPosition - edge) * 2.2f);
            }
        }

        void Ray(float dt)
        {
            if (!_board.RayLive)
            {
                if (_ray != null) Destroy(_ray.gameObject);
                _ray = null;
                return;
            }

            float x = LaneX(_board.RayLane);
            var foot = new Vector2(x, _lineY + Cell * 1.6f);
            var top = new Vector2(x, _hillTop - Cell);
            int posts = _posts != null ? _posts.Length : 0;

            // **Built once when the ray opens and moved every frame after**: the column and a
            // feed from each post, three stretched capsules apiece, laid again where the sweep
            // has got to (`Lay`) rather than rebuilt - a rebuild would be a dozen images made and
            // destroyed a frame on the effects canvas for the two seconds the ray lives.
            if (_ray == null)
            {
                _ray = UIKit.Node("Ray", _fx);
                _rayCore = Shaft(_ray, foot, top, RayHue, Cell * 1.1f);
                _rayFeeds = new Image[posts][];
                for (int w = 0; w < posts; w++)
                    _rayFeeds[w] = Shaft(_ray, new Vector2(PostX(w), _lineY + Cell), foot, RayHue, Cell * .28f);
            }

            float flicker = .8f + .2f * Mathf.Sin(Time.unscaledTime * 50f);
            Lay(_rayCore, foot, top, Cell * 1.1f * flicker);

            for (int w = 0; w < posts && w < _rayFeeds.Length; w++)
            {
                bool lit = _board.Wards[w].Alive;
                foreach (var img in _rayFeeds[w]) if (img != null) img.enabled = lit;
                if (lit) Lay(_rayFeeds[w], new Vector2(PostX(w), _lineY + Cell), foot, Cell * .28f * flicker);
            }

            if (Random.value < dt * 40f)
                Burst.Sparks(_fx, new Vector2(x, MarchY(Random.value)), RayHue, 3, Cell * .9f, Cell * .12f, .24f);

            if (Random.value < dt * 6f) ShakeBoard(Cell * .04f);
        }

        /// <summary>
        /// A spun-up turret glows: amber at its first shots, white-hot at its most, throbbing
        /// once it is there. Built under each post the first time a build has spin, and only then.
        /// </summary>
        void Heat(float dt)
        {
            if (_board.Boosts.SpinStep <= 0 || _posts == null) return;

            // Built again if the posts were (a relayout destroys them with their children).
            if (_heat == null || _heat.Length != _posts.Length || (_heat.Length > 0 && _heat[0] == null))
            {
                _heat = new Image[_posts.Length];
                for (int w = 0; w < _posts.Length; w++)
                {
                    if (_posts[w] == null || _posts[w].Node == null) continue;

                    var glow = Lit("Heat", _posts[w].Node, Art.Glow(96, 1.8f), Color.clear, Vector2.one * Cell * 2.6f);
                    glow.transform.SetAsFirstSibling();
                    glow.rectTransform.anchoredPosition = new Vector2(0f, Cell * .2f);
                    _heat[w] = glow;
                }
            }

            for (int w = 0; w < _heat.Length; w++)
            {
                var glow = _heat[w];
                if (glow == null) continue;

                float share = _board.SpinShare(w);
                var hue = Color.Lerp(Pal.Amber, Color.white, share * share);
                float throb = share >= 1f ? .8f + .2f * Mathf.Sin(Time.unscaledTime * 18f) : 1f;
                glow.color = Pal.A(hue, .75f * share * throb);
                glow.rectTransform.localScale = Vector3.one * (.7f + .5f * share);
            }
        }

        // ------------------------------------------------------------------ the primary bolt
        /// <summary>
        /// What a build changes about the drawing of a ward's own bolt, or the plain answer on a
        /// board that holds none: the reel's colour under wild magic, how big a heavy build's
        /// bolt is, the trail an element leaves, and whether a spun-up turret draws a tracer.
        /// </summary>
        struct BoltLook
        {
            public int Colour;
            public float Scale;
            public Color Wake;
            public bool Tracer;
        }

        float[] _lastShot;

        BoltLook LookOf(SiegeBolt shot, int colour)
        {
            var look = new BoltLook { Colour = colour, Scale = 1f, Wake = Color.clear, Tracer = false };

            var boosts = _board.Boosts;
            if (boosts.IsIdentity) return look;

            if (shot.Element >= 0) look.Colour = shot.Element;

            look.Scale = Mathf.Clamp(Mathf.Sqrt(boosts.DamagePercent / 100f), 1f, 1.6f);

            // The trail says which element the build has struck its bolts in, strongest first.
            if (shot.Element >= 0) look.Wake = TintOf(shot.Element);
            else if (boosts.BurnTenths > 0) look.Wake = Pal.Ember;
            else if (boosts.FrostTenths > 0) look.Wake = Pal.Glass;
            else if (boosts.ToxicTenths > 0) look.Wake = ToxicHue;
            else if (boosts.ChainTenths > 0 || boosts.TeslaTenths > 0) look.Wake = TeslaHue;
            else if (boosts.HexChance > 0) look.Wake = VortexHue;

            // A ward firing faster than a frame-rate's worth of full shots draws tracers between
            // its full ones: every other shot keeps its comet and drops the flash and the ring.
            if (_posts != null)
            {
                if (_lastShot == null || _lastShot.Length != _posts.Length) _lastShot = new float[_posts.Length];

                float now = Time.unscaledTime;
                look.Tracer = now - _lastShot[shot.Ward] < .16f;
                if (!look.Tracer) _lastShot[shot.Ward] = now;
            }

            return look;
        }
    }
}
