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

            // **A hit an unlocked power dealt** is drawn as the power (`SiegeView.Secrets`).
            if (SecretHit(shot, mob, to, source, tint, land)) return;

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
                    Cinder(to, Pal.Sun, Cell * 1.3f, .3f, new Vector2(0f, Cell * .8f));
                    Cinder(to, Pal.Ember, Cell * .7f, .45f, new Vector2(Random.Range(-Cell, Cell), Cell * 1.4f));
                    land();
                    break;

                // **The three set pieces' hits are pooled embers, never a fresh image** (59i's
                // "cheap where it repeats"): a vortex bites every body in its box on a cadence, the
                // ray every body in its lane ten times a second and the nuke the whole hill at once,
                // which on a late Shuffle hill is dozens of hits on one frame. The spectacle is the
                // set piece itself (`EddyBite`, `Sweeping`, `NukeLands`); a hit only says where.
                case SiegeVia.Vortex:
                {
                    if (!Flourished(SiegeVia.Vortex, -1)) EddyBite();
                    var eye = _eddy != null ? _eddy.Pit.At : to;
                    Cinder(to, Pal.Lift(VortexHue, .3f), Cell * .62f, .3f, (eye - to) * .7f);
                    land();
                    break;
                }

                case SiegeVia.Nuke:
                    Cinder(to, Pal.Radiance, Cell * 1.7f, .34f, new Vector2(0f, Cell * .9f));
                    Cinder(to, Pal.Sun, Cell * .9f, .5f, new Vector2(Random.Range(-Cell, Cell), Cell * 1.6f));
                    land();
                    break;

                case SiegeVia.Ray:
                    Cinder(to, Color.Lerp(RayHue, Color.white, .55f), Cell * .8f, .2f, new Vector2(0f, Cell * .5f));
                    land();
                    break;

                case SiegeVia.Hydra:
                    Fly(source, to, HydraHue, Cell * .4f, .2f, Random.value < .5f ? .3f : -.3f, HydraHue,
                        () => { Pop(to, HydraHue, 1.4f, .2f); land(); });
                    break;

                // A laser gun's light going on into another body (`SiegeView.Laser`), or its own
                // ability's hit drawn as the beam it now is. It lands its own number.
                case SiegeVia.Laser:
                    Bounced(shot, source, to);
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
                    EddyOpened(at);
                    break;

                case SiegeVia.Nuke:
                    if (volley.In > 0f) NukeFalls(volley.In);
                    else NukeLands();
                    break;

                case SiegeVia.Ray:
                    Fused();
                    break;

                // Every place an unlocked power set something off (`SiegeView.Secrets`).
                default:
                    SecretVolley(volley);
                    break;
            }
        }

        // ------------------------------------------------------------------ the meteor
        /// <summary>How far up and to the side of its box a meteor enters, in cells: off the top of the board.</summary>
        const float MeteorFrom = 3.4f, MeteorOver = 2.5f;

        /// <summary>Seconds the crater glows after it lands: about the burn it leaves on the bodies it hit.</summary>
        const float CraterFor = 1.6f;

        /// <summary>Seconds between two embers off the crater.</summary>
        const float CraterEmber = .09f;

        /// <summary>
        /// A meteor on its way, for exactly the fall the model books (<c>SiegeArsenal.MeteorFall</c>):
        /// a target ring tightening on the box it will hit and the ground there heating up, its
        /// shadow growing, and the rock itself coming down out of the sky on a two-layer burning
        /// tail, shedding embers and smoke.
        ///
        /// <para>
        /// <b>A meteor can come every few seconds and several cards can stack its clock</b>, so
        /// every piece here is a handful of images for under a second, hosted on one node and
        /// handed back on its tween and on a timer, and everything it sheds is a pooled ember.
        /// The landing is <see cref="MeteorLands"/>, drawn off the model's second volley (37s).
        /// </para>
        /// </summary>
        void MeteorFalls(Vector2 at, float over)
        {
            var mark = UIKit.Node("Meteor mark", _fx);

            var shadow = UIKit.Img("Shadow", mark, Art.Glow(96, 1.6f), new Color(0f, 0f, 0f, 0f),
                                   new Vector2(Cell * 4.2f, Cell * 4.2f * GroundSquash));
            shadow.raycastTarget = false;
            shadow.rectTransform.anchoredPosition = at;

            var heat = Lit("Heat", mark, Art.Glow(128, 1.8f), Pal.A(Pal.Ember, 0f),
                           new Vector2(Cell * 3.6f, Cell * 3.6f * GroundSquash));
            heat.rectTransform.anchoredPosition = at;

            var ring = Lit("Target", mark, StrikeFx.Ring, Pal.A(MeteorHue, 0f),
                           new Vector2(Cell * 4.4f, Cell * 4.4f * GroundSquash));
            if (ring != null) ring.rectTransform.anchoredPosition = at;

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!mark) return;

                float rise = Mathf.Clamp01(t / .15f);
                float blink = Mathf.Sin(Mathf.PI * 2f * (3f * t + 6f * t * t)) > 0f ? 1f : .45f;

                shadow.color = new Color(0f, 0f, 0f, .45f * t * t);
                shadow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.25f, 1f, t * t);

                heat.color = Pal.A(Color.Lerp(Pal.Ember, Pal.Sun, t), .55f * t * t);
                heat.rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, t);

                if (ring)
                {
                    ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, .75f, Ease.OutCubic(t));
                    ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 140f);
                    ring.color = Pal.A(Color.Lerp(MeteorHue, Pal.Sun, t), .85f * rise * blink);
                }
            }, mark).OnDone(() => { if (mark) Destroy(mark.gameObject); });

            Tween.After(over + .1f, () => { if (mark) Destroy(mark.gameObject); }, mark);

            Rock(at, over);
            Audio.Sfx("whoosh", .5f, .62f);
        }

        /// <summary>
        /// The rock: a dark body inside a burning aura, white-hot on the side it is falling
        /// toward, trailing a wide ember tail with a narrow gold one inside it, falling faster
        /// the nearer it gets.
        /// </summary>
        void Rock(Vector2 at, float over)
        {
            var sky = new Vector2(at.x - Cell * MeteorOver, Span.y * .5f + Cell * MeteorFrom);

            var node = UIKit.Box("Meteor", _fx, Vector2.zero, new Vector2(.5f, .5f), sky);
            var tail = Lit("Tail", node, Art.SoftCapsule(40, 120), Pal.A(Pal.Ember, .8f), Vector2.one);
            var inner = Lit("Inner", node, Art.SoftCapsule(40, 120), Pal.A(Pal.Sun, .9f), Vector2.one);
            var aura = Lit("Aura", node, Art.Glow(96, 1.5f), Pal.A(Pal.Ember, .85f), Vector2.one * Cell * 2.4f);
            var flare = Lit("Flare", node, StrikeFx.Flare, Pal.A(Pal.Sun, .7f), Vector2.one * Cell * 2.2f);

            // The rock is the one dark thing in it, drawn with ordinary alpha, and the lit edge
            // over it is what says it is burning on the way in.
            var body = UIKit.Img("Body", node, Art.Glow(96, 4f), new Color(.22f, .1f, .06f, .95f), Vector2.one * Cell * .9f);
            body.raycastTarget = false;
            var edge = Lit("Edge", node, Art.Glow(96, 2.4f), Color.white, Vector2.one * Cell * .75f);

            var tailRt = tail.rectTransform;
            var innerRt = inner.rectTransform;
            var last = sky;
            float nextEmber = 0f;

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!node) return;

                var spot = Vector2.Lerp(sky, at, t * t);
                var dir = spot - last;
                node.anchoredPosition = spot;

                if (dir.sqrMagnitude > .01f)
                {
                    var back = -dir.normalized;
                    float turn = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                    float len = Cell * Mathf.Lerp(1.4f, 5f, t);

                    tailRt.sizeDelta = new Vector2(Cell * Mathf.Lerp(.7f, 1.1f, t), len);
                    tailRt.anchoredPosition = back * len * .45f;
                    tailRt.localRotation = Quaternion.Euler(0f, 0f, turn);

                    innerRt.sizeDelta = new Vector2(Cell * .4f, len * .65f);
                    innerRt.anchoredPosition = back * len * .3f;
                    innerRt.localRotation = Quaternion.Euler(0f, 0f, turn);

                    edge.rectTransform.anchoredPosition = -back * Cell * .14f;

                    if (t >= nextEmber)
                    {
                        nextEmber = t + .04f;
                        var side = new Vector2(-back.y, back.x) * Random.Range(-.35f, .35f) * Cell;
                        Cinder(spot + back * Cell * .5f, Random.value < .5f ? Pal.Sun : Pal.Ember,
                               Cell * Random.Range(.3f, .5f), .38f, back * Cell * 1.2f + side);
                        if (Random.value < .5f)
                            Cinder(spot + back * Cell * .9f, Pal.Hollow, Cell * Random.Range(.4f, .65f), .5f,
                                   back * Cell * .6f + new Vector2(0f, Cell * .5f));
                    }
                }

                float throb = .88f + .12f * Mathf.Sin(t * 60f);
                aura.rectTransform.localScale = Vector3.one * Mathf.Lerp(.75f, 1.2f, t) * throb;
                body.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 400f);
                if (flare) flare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 480f);

                last = spot;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });

            Tween.After(over + .1f, () => { if (node) Destroy(node.gameObject); }, node);
        }

        /// <summary>
        /// The landing, on the frame the model struck the box: a flash, a fireball, the ground
        /// cracked and thrown up, two fronts racing out over the hill, smoke rolling off it,
        /// rubble and sparks flung up and falling back, and a crater left glowing about as long
        /// as the burn it gives the bodies it hit.
        /// </summary>
        void MeteorLands(Vector2 at, int reach)
        {
            float wide = Cell * (3f + reach * 1.8f);

            Flash(at, Pal.Sun, 4.6f);
            Fireball(at, .5f, .9f);
            Boom(at, Blast("boom_fire"), wide * 1.2f);
            Boom(at + new Vector2(0f, Cell * .4f), Blast("boom_smoke"), wide * 1.3f, 18f);

            Answer(at, Pal.Sun, wide / Cell * 1.1f, StrikeFx.Crack, wide / Cell * .9f);
            Ripple(at, MeteorHue, wide * 1.5f, .55f, 0f);
            Ripple(at, Pal.Sun, wide * 1.05f, .45f, .07f);

            Embers(at, Pal.Ember, 16, Cell * .9f, Cell * .3f);
            Embers(at, Pal.Hollow, 8, Cell * .7f, Cell * .42f);
            Crater(at, wide);

            Lightup(MeteorHue, .18f, .35f);
            ShakeBoard(Cell * .24f);

            Audio.Sfx("boom", .6f, .75f);
            Audio.Sfx("land", .4f, .7f);
        }

        /// <summary>
        /// The crater: a molten glow on the ground, flickering and cooling from gold to ember,
        /// breathing a few embers up off it. One image, its embers pooled.
        /// </summary>
        void Crater(Vector2 at, float wide)
        {
            var pit = Lit("Crater", _fx, Art.Glow(128, 2f), Pal.A(Pal.Sun, 0f),
                          new Vector2(wide * .8f, wide * .8f * GroundSquash));
            var rt = pit.rectTransform;
            rt.anchoredPosition = at;
            rt.SetAsFirstSibling();

            float nextEmber = 0f;
            float seed = Random.value * 20f;

            Tween.Run(CraterFor, Ease.Linear, t =>
            {
                if (!pit) return;

                float flicker = .8f + .2f * Mathf.Sin(t * 37f + seed) * Mathf.Sin(t * 13f);
                pit.color = Pal.A(Color.Lerp(Pal.Sun, Pal.Ember, t), .6f * (1f - t) * flicker);
                rt.localScale = Vector3.one * Mathf.Lerp(.7f, 1f, Ease.OutCubic(Mathf.Clamp01(t * 4f)));

                if (t >= nextEmber && t < .8f)
                {
                    nextEmber = t + CraterEmber / CraterFor;
                    var from = at + new Vector2(Random.Range(-wide, wide) * .3f, Random.Range(-wide, wide) * .3f * GroundSquash);
                    Cinder(from, Random.value < .5f ? Pal.Ember : Pal.Sun, Cell * Random.Range(.18f, .3f), .5f,
                           new Vector2(Random.Range(-Cell, Cell) * .3f, Cell * Random.Range(.8f, 1.4f)));
                }
            }, pit).OnDone(() => { if (pit) Destroy(pit.gameObject); });

            Tween.After(CraterFor + .1f, () => { if (pit) Destroy(pit.gameObject); }, pit);
        }

        // ------------------------------------------------------------------ the nuke
        /// <summary>How wide the lock-on is drawn, in cells, and how far its four ticks close from.</summary>
        const float NukeLock = 6.4f, NukeTicksFrom = 4.6f, NukeTicksTo = 2.3f;

        /// <summary>When the hill flashes red, as shares of the fall: closer together as it comes.</summary>
        static readonly float[] NukeAlarms = { 0f, .40f, .63f, .79f, .90f };

        /// <summary>Seconds the fireball, and the column of smoke over it, stand after the blast.</summary>
        const float FireballFor = 1.35f, PlumeFor = 1.7f;

        /// <summary>Where every nuke lands: the middle of the hill (<c>SiegeBoard.Nukes</c> strikes all of it).</summary>
        Vector2 GroundZero => new Vector2(0f, MarchY(.5f));

        /// <summary>
        /// A nuke on its way, for exactly the fall the model books (<c>SiegeArsenal.NukeFall</c>):
        /// the hill locked on, the board flashing red faster and faster, and the warhead coming
        /// down out of the sky on a burning contrail, its shadow growing under it.
        ///
        /// <para>
        /// <b>Every piece is a few images for under a second</b>, all hosted on one node handed
        /// back on its own tween and a timer, so a nuke on a crowded hill costs what a nuke on an
        /// empty one does. The blast itself is <see cref="NukeLands"/>, drawn off the model's
        /// second volley, never off this clock (37s): the light lands on the frame the damage does.
        /// </para>
        /// </summary>
        void NukeFalls(float over)
        {
            var centre = GroundZero;

            // **The lock-on.** Two rings on the ground turning against each other, four ticks
            // closing on the middle, all blinking faster as it comes: the one thing a player
            // reads in under a second as "something is about to hit there".
            var mark = UIKit.Node("Nuke lock", _fx);
            var shadow = UIKit.Img("Shadow", mark, Art.Glow(96, 1.6f), new Color(0f, 0f, 0f, 0f),
                                   new Vector2(Cell * 7f, Cell * 7f * GroundSquash));
            shadow.raycastTarget = false;
            shadow.rectTransform.anchoredPosition = centre;

            var outer = Lit("Lock", mark, StrikeFx.Ring, Pal.A(Pal.Poppy, 0f),
                            new Vector2(Cell * NukeLock, Cell * NukeLock * GroundSquash));
            var inner = Lit("Lock", mark, StrikeFx.Ring, Pal.A(Pal.Poppy, 0f),
                            new Vector2(Cell * NukeLock * .5f, Cell * NukeLock * .5f * GroundSquash));
            if (outer != null) outer.rectTransform.anchoredPosition = centre;
            if (inner != null) inner.rectTransform.anchoredPosition = centre;

            var ticks = new Image[4];
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = Lit("Tick", mark, Art.Capsule(24, 96), Pal.A(Pal.Poppy, 0f),
                               new Vector2(Cell * .16f, Cell * .8f));
                ticks[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
            }

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!mark) return;

                // A blink whose rate climbs with the fall: about three a second at first, about
                // seventeen at the end.
                float blink = Mathf.Sin(Mathf.PI * 2f * (3f * t + 7f * t * t)) > 0f ? 1f : .35f;
                float rise = Mathf.Clamp01(t / .15f);
                float close = Ease.OutCubic(t);
                var hue = Color.Lerp(Pal.Poppy, Pal.Sun, t * t);

                shadow.color = new Color(0f, 0f, 0f, .5f * t * t);
                shadow.rectTransform.localScale = Vector3.one * Mathf.Lerp(.25f, 1f, t * t);

                if (outer)
                {
                    outer.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, close);
                    outer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 120f);
                    outer.color = Pal.A(hue, .85f * rise * blink);
                }

                if (inner)
                {
                    inner.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, close);
                    inner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 200f);
                    inner.color = Pal.A(hue, .7f * rise * blink);
                }

                float reach = Cell * Mathf.Lerp(NukeTicksFrom, NukeTicksTo, close);
                for (int i = 0; i < ticks.Length; i++)
                {
                    if (!ticks[i]) continue;
                    float a = i * Mathf.PI * .5f;
                    ticks[i].rectTransform.anchoredPosition =
                        centre + new Vector2(Mathf.Cos(a) * reach, Mathf.Sin(a) * reach * GroundSquash);
                    ticks[i].color = Pal.A(hue, rise * blink);
                }
            }, mark).OnDone(() => { if (mark) Destroy(mark.gameObject); });

            Tween.After(over + .1f, () => { if (mark) Destroy(mark.gameObject); }, mark);

            for (int i = 0; i < NukeAlarms.Length; i++)
                Tween.After(NukeAlarms[i] * over, () => { if (this) Lightup(Pal.Poppy, .13f, .2f); }, this);

            Warhead(centre, over);

            Audio.Sfx("charge", .55f, .6f);
            Tween.After(Mathf.Max(0f, over - .38f), () => { if (this) Audio.Sfx("whoosh", .6f, .5f); }, this);
        }

        /// <summary>
        /// The warhead: a white-hot head in a gold sheath trailing a long burning tail, falling
        /// faster the nearer it gets, shedding embers off its contrail.
        /// </summary>
        void Warhead(Vector2 centre, float over)
        {
            var sky = new Vector2(centre.x - Cell * 1.6f, Span.y * .5f + Cell * 2.5f);

            var node = UIKit.Box("Warhead", _fx, Vector2.zero, new Vector2(.5f, .5f), sky);
            var tail = Lit("Tail", node, Art.SoftCapsule(40, 120), Pal.A(Pal.Ember, .85f), Vector2.one);
            var sheath = Lit("Sheath", node, Art.Glow(96, 1.6f), Pal.A(Pal.Sun, .9f), Vector2.one * Cell * 2.2f);
            Lit("Head", node, Art.Glow(96, 2.4f), Color.white, Vector2.one * Cell * 1.1f);
            var flare = Lit("Flare", node, StrikeFx.Flare, Pal.A(Pal.Sun, .8f), Vector2.one * Cell * 2.6f);

            var tailRt = tail.rectTransform;
            var last = sky;
            float nextEmber = 0f;

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!node) return;

                // Accelerating: it is falling, not sliding.
                var at = Vector2.Lerp(sky, centre, t * t);
                var dir = at - last;
                node.anchoredPosition = at;

                if (dir.sqrMagnitude > .01f)
                {
                    var back = -dir.normalized;
                    float len = Cell * Mathf.Lerp(1.6f, 6.5f, t);
                    tailRt.sizeDelta = new Vector2(Cell * Mathf.Lerp(.55f, .9f, t), len);
                    tailRt.anchoredPosition = back * len * .45f;
                    tailRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);

                    if (t >= nextEmber)
                    {
                        nextEmber = t + .035f;
                        var side = new Vector2(-back.y, back.x) * Random.Range(-.3f, .3f) * Cell;
                        Cinder(at + back * Cell * .6f, Random.value < .5f ? Pal.Sun : Pal.Ember,
                               Cell * Random.Range(.35f, .6f), .42f, back * Cell * 1.4f + side);
                    }
                }

                float throb = .85f + .15f * Mathf.Sin(t * 70f);
                sheath.rectTransform.localScale = Vector3.one * Mathf.Lerp(.7f, 1.25f, t) * throb;
                if (flare)
                {
                    flare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 540f);
                    flare.rectTransform.localScale = Vector3.one * Mathf.Lerp(.6f, 1.3f, t);
                }

                last = at;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });

            Tween.After(over + .1f, () => { if (node) Destroy(node.gameObject); }, node);

        }

        /// <summary>
        /// The blast, on the frame the model struck the hill: a whiteout, a fireball that swells
        /// and cools, a shock front racing over the ground and one through the air, the ground
        /// cracked under it, a column of fire and smoke rising, debris thrown up, and an
        /// aftershock a beat behind.
        /// </summary>
        void NukeLands()
        {
            var centre = GroundZero;

            Lightup(Color.white, .9f, .7f);
            Lightup(Pal.Sun, .3f, 1.5f);
            Flash(centre, Pal.Radiance, 9f);

            Fireball(centre);
            Boom(centre, Blast("boom_burst"), Cell * 9.5f, 22f);
            Boom(centre, Blast("boom_fire"), Cell * 7f);
            Tween.After(.07f, () => { if (this) Boom(centre + new Vector2(-Cell * 2.6f, Cell * .3f), Blast("boom_fire"), Cell * 4.6f); }, this);
            Tween.After(.13f, () => { if (this) Boom(centre + new Vector2(Cell * 2.8f, -Cell * .2f), Blast("boom_fire"), Cell * 4.6f); }, this);

            // The ground front, the second front behind it, and the air front: round, because it
            // is not on the floor (37ev), white and quick.
            Ripple(centre, Pal.Radiance, Span.x * 1.9f, .85f, 0f, .6f);
            Ripple(centre, Pal.Sun, Span.x * 1.35f, .7f, .1f, .6f);
            Ripple(centre, Color.white, Span.x * 1.1f, .45f, 0f, 1f);

            Answer(centre, Pal.Sun, 8f, StrikeFx.Crack, 6.5f);
            Plume(centre);
            Embers(centre, Pal.Sun, 30, Cell * 1.4f, Cell * .36f);
            Twinkles(centre, Pal.Radiance, 10, Cell * 3f);

            ShakeBoard(Cell * .5f);
            Tween.After(.32f, () => { if (this) ShakeBoard(Cell * .2f); }, this);

            Audio.Sfx("boom", .95f, .6f);
            Audio.Sfx("thunder", .55f, .8f, .05f);
            Tween.After(.28f, () => { if (this) Audio.Sfx("boom", .5f, .42f); }, this);
        }

        /// <summary>
        /// The fireball: three glows of the same middle, swelling in a snap and then cooling
        /// from the inside out - the white core first, the gold next, the ember last - and
        /// lifting as it goes, because fire rises.
        /// </summary>
        void Fireball(Vector2 centre, float scale = 1f, float life = FireballFor)
        {
            var host = UIKit.Box("Fireball", _fx, Vector2.zero, new Vector2(.5f, .5f), centre);

            var outer = Lit("Outer", host, Art.Glow(128, 1.4f), Pal.A(Pal.Ember, 0f), Vector2.one * Cell * 11f * scale);
            var mid = Lit("Mid", host, Art.Glow(128, 1.8f), Pal.A(Pal.Sun, 0f), Vector2.one * Cell * 7.5f * scale);
            var core = Lit("Core", host, Art.Glow(128, 2.4f), Pal.A(Color.white, 0f), Vector2.one * Cell * 4.4f * scale);

            Tween.Run(life, Ease.Linear, t =>
            {
                if (!host) return;

                float s = t * life;
                float swell = Ease.OutQuint(Mathf.Clamp01(s / .28f));

                host.localScale = new Vector3(1f, .82f, 1f) * Mathf.Lerp(.2f, 1f, swell);
                host.anchoredPosition = centre + new Vector2(0f, Cell * 1.3f * scale * t);

                // Each layer holds, then lets go: the core at a third of a second, the gold by
                // nine tenths, the ember over the whole of it.
                if (core) core.color = Pal.A(Color.white, .95f * (1f - Mathf.Clamp01(s / .38f)));
                if (mid) mid.color = Pal.A(Color.Lerp(Pal.Sun, Pal.Ember, t), .8f * (1f - Mathf.Clamp01(s / .9f)));
                if (outer) outer.color = Pal.A(Color.Lerp(Pal.Ember, Pal.Poppy, t), .6f * (1f - t) * (1f - t));
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(life + .1f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        /// <summary>
        /// The column: a shaft of fire standing up out of ground zero, and smoke billowing off
        /// its top and climbing - the mushroom, in the board's own reels. The smoke is the one
        /// thing here drawn with ordinary alpha, because it is the one thing that is dark.
        /// </summary>
        void Plume(Vector2 centre)
        {
            var host = UIKit.Box("Plume", _fx, Vector2.zero, new Vector2(.5f, .5f), centre);

            // The stem: a shaft whose soft end is turned to the top, so it is brightest at the ground.
            var stem = Lit("Stem", host, Art.Shaft(64, 64, 1.1f, .3f), Pal.A(Pal.Ember, 0f),
                           new Vector2(Cell * 1.8f, Cell * 5.5f));
            var stemRt = stem.rectTransform;
            stemRt.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var crown = UIKit.Box("Crown", host, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
            var glow = Lit("Glow", crown, Art.Glow(128, 1.5f), Pal.A(Pal.Ember, 0f),
                           new Vector2(Cell * 6.5f, Cell * 3.6f));

            var smoke = Blast("boom_smoke");
            if (smoke != null && smoke.Length > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var puff = UIKit.Img("Smoke", crown, smoke[0], new Color(1f, 1f, 1f, .85f),
                                         Vector2.one * Cell * (5.4f - i * .8f));
                    puff.raycastTarget = false;
                    puff.rectTransform.anchoredPosition = new Vector2((i - 1) * Cell * 1.5f, i == 1 ? Cell * .5f : 0f);

                    var book = Flipbook.Attach(puff, smoke, 13f + i * 2f, false);
                    if (book != null) book.OnFinished = () => { if (puff) Destroy(puff.gameObject); };
                }
            }

            Tween.Run(PlumeFor, Ease.Linear, t =>
            {
                if (!host) return;

                float rise = Ease.OutCubic(t);
                float tall = Cell * 5.5f * Mathf.Lerp(.15f, 1f, Ease.OutQuint(Mathf.Clamp01(t / .45f)));

                stemRt.sizeDelta = new Vector2(Cell * Mathf.Lerp(1.8f, 1.1f, t), tall);
                stemRt.anchoredPosition = new Vector2(0f, tall * .5f);
                stem.color = Pal.A(Color.Lerp(Pal.Sun, Pal.Ember, t), .75f * (1f - t * t));

                crown.anchoredPosition = new Vector2(0f, Cell * Mathf.Lerp(.6f, 5.4f, rise));
                crown.localScale = Vector3.one * Mathf.Lerp(.55f, 1.35f, rise);
                if (glow) glow.color = Pal.A(Color.Lerp(Pal.Sun, Pal.Poppy, t), .55f * (1f - t));
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(PlumeFor + .1f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        // ------------------------------------------------------------------ what persists
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
            Lasers(dt);

            // And what an unlocked secret or evolution leaves standing (`SiegeView.Secrets`).
            Secrets(dt);
        }

        // ------------------------------------------------------------------ the toxic pools
        /// <summary>
        /// One pool on the ground, built once and re-tinted every frame: a dark stain and the
        /// liquid in it (ordinary blending, so they read on a bright hill where light alone
        /// vanishes), a glow and a rim over them, a sheen sliding across, and bubbles swelling
        /// and popping.
        /// </summary>
        sealed class PoolLook
        {
            public RectTransform Root;
            public Image Stain, Liquid, Glow, Rim, Sheen;
            public Image[] Bubbles;
            public Vector2[] BubbleAt;
            public float[] BubbleT, BubbleFor;
            public float Age;
        }

        /// <summary>
        /// The pools' own layer: <b>on the ground, under the raiders</b> (owner, 2026-10-14: the
        /// pools were not visible on the hill - one additive glow over the effects was lost on
        /// bright grass), as the last child of the hill. <b>A canvas of its own</b> for
        /// <c>_fx</c>'s reason: twenty pools re-tinted every frame would otherwise re-mesh the
        /// whole board's canvas, ground, line and gems with them.
        /// </summary>
        RectTransform _puddles;

        readonly Dictionary<int, PoolLook> _poolArt = new Dictionary<int, PoolLook>(8);
        readonly HashSet<int> _poolsSeen = new HashSet<int>();
        readonly List<int> _poolsGone = new List<int>(8);
        float _fume;

        const int PoolBubbles = 3;

        /// <summary>Seconds a pool takes to splash open, and the seconds of its life it spends drying up.</summary>
        const float PoolOpens = .28f, PoolDries = .6f;

        static readonly Color PoolDeep = new Color(.06f, .2f, .07f), PoolAcid = Color.Lerp(Pal.Verdant, Pal.Pollen, .3f);

        void Pools(float dt)
        {
            var pools = _board.Pools;
            _poolsSeen.Clear();

            _fume -= dt;
            bool fume = _fume <= 0f;
            if (fume) _fume = .14f;

            if (pools.Count > 0 && !_puddles && _hill)
            {
                _puddles = UIKit.Node("Pools", _hill);
                _puddles.gameObject.AddComponent<Canvas>().overrideSorting = false;
            }

            float wide = BoxWide * .92f, tall = Mathf.Min(BoxTall * .62f, wide * .5f);
            float clock = Time.unscaledTime;

            for (int i = 0; i < pools.Count && _puddles; i++)
            {
                var pool = pools[i];
                int key = pool.Row * SiegeTuning.Lanes + pool.Lane;
                _poolsSeen.Add(key);

                var at = BoxAt(pool.Lane, pool.Row);

                if (!_poolArt.TryGetValue(key, out var look) || look == null || !look.Root)
                {
                    look = Puddle(at, wide, tall);
                    _poolArt[key] = look;
                    Splash(at, wide);
                }

                look.Age += dt;
                look.Root.anchoredPosition = at;

                // Splashed open, then full, then drying up over its last moments.
                float opened = Mathf.Clamp01(look.Age / PoolOpens);
                float dry = Mathf.Clamp01(pool.Left / PoolDries);
                float shown = Mathf.Min(opened, dry);
                float scale = Ease.OutBack(opened) * Mathf.Lerp(.6f, 1f, dry);
                look.Root.localScale = new Vector3(scale, scale, 1f);

                float throb = .5f + .5f * Mathf.Sin(clock * 4.2f + key);

                look.Stain.color = Pal.A(PoolDeep, .55f * shown);
                look.Liquid.color = Pal.A(Color.Lerp(PoolAcid, Pal.Verdant, throb), .78f * shown);
                look.Glow.color = Pal.A(ToxicHue, (.35f + .2f * throb) * shown);
                look.Rim.color = Pal.A(Color.Lerp(PoolAcid, Color.white, .25f), (.55f + .3f * throb) * shown);

                // The sheen slides across the surface and back.
                float slide = Mathf.Sin(clock * 1.3f + key * .7f);
                look.Sheen.rectTransform.anchoredPosition = new Vector2(slide * wide * .22f, tall * .12f);
                look.Sheen.color = Pal.A(Color.white, (.22f + .12f * throb) * shown);

                Bubble(look, dt, wide, tall, shown);

                // Fumes rising off it, often enough to read as poison from across the hill.
                if (fume && Random.value < .7f * shown)
                    Cinder(at + new Vector2(Random.Range(-wide, wide) * .4f, Random.Range(-tall, tall) * .3f),
                           Color.Lerp(ToxicHue, PoolAcid, Random.value), Cell * Random.Range(.22f, .34f), .7f,
                           new Vector2(Random.Range(-.15f, .15f) * Cell, Cell * Random.Range(.7f, 1.1f)));
            }

            if (_poolArt.Count == _poolsSeen.Count) return;

            _poolsGone.Clear();
            foreach (var pair in _poolArt) if (!_poolsSeen.Contains(pair.Key)) _poolsGone.Add(pair.Key);
            foreach (int key in _poolsGone)
            {
                var look = _poolArt[key];
                if (look != null && look.Root) Destroy(look.Root.gameObject);
                _poolArt.Remove(key);
            }
        }

        /// <summary>A new pool's pieces, under its own node so the whole of it scales as one.</summary>
        PoolLook Puddle(Vector2 at, float wide, float tall)
        {
            var look = new PoolLook { Root = UIKit.Node("Pool", _puddles) };
            look.Root.anchoredPosition = at;
            look.Root.localScale = Vector3.zero;

            look.Stain = UIKit.Img("Stain", look.Root, Art.Glow(128, .6f), Color.clear, new Vector2(wide * 1.12f, tall * 1.2f));
            look.Stain.raycastTarget = false;

            look.Liquid = UIKit.Img("Liquid", look.Root, Art.Glow(128, .45f), Color.clear, new Vector2(wide * .86f, tall * .9f));
            look.Liquid.raycastTarget = false;

            look.Glow = Lit("Glow", look.Root, Art.Glow(128, 1.2f), Color.clear, new Vector2(wide * 1.3f, tall * 1.7f));
            look.Rim = Lit("Rim", look.Root, Art.Ring(128, 9f), Color.clear, new Vector2(wide * .84f, tall * .86f));
            look.Sheen = Lit("Sheen", look.Root, Art.Glow(96, 1.6f), Color.clear, new Vector2(wide * .34f, tall * .22f));

            look.Bubbles = new Image[PoolBubbles];
            look.BubbleAt = new Vector2[PoolBubbles];
            look.BubbleT = new float[PoolBubbles];
            look.BubbleFor = new float[PoolBubbles];

            for (int b = 0; b < PoolBubbles; b++)
            {
                look.Bubbles[b] = Lit("Bubble", look.Root, Art.Ring(64, 7f), Color.clear, Vector2.one);
                look.BubbleFor[b] = Random.Range(.45f, .8f);
                look.BubbleT[b] = -b * .22f; // staggered, so three never rise as one
            }

            return look;
        }

        /// <summary>The bubbles: each swells somewhere in the liquid, pops into a ring and starts again elsewhere.</summary>
        void Bubble(PoolLook look, float dt, float wide, float tall, float shown)
        {
            for (int b = 0; b < look.Bubbles.Length; b++)
            {
                var img = look.Bubbles[b];
                if (!img) continue;

                look.BubbleT[b] += dt;
                float t = look.BubbleT[b] / look.BubbleFor[b];

                if (t >= 1f)
                {
                    look.BubbleT[b] = 0f;
                    look.BubbleFor[b] = Random.Range(.45f, .8f);
                    var spot = Random.insideUnitCircle;
                    look.BubbleAt[b] = new Vector2(spot.x * wide * .32f, spot.y * tall * .28f);
                    t = 0f;
                }

                if (t < 0f) { img.color = Color.clear; continue; }

                // Swells for most of its life, then pops: a quick widening ring that fades.
                bool popping = t > .78f;
                float size = popping ? Mathf.Lerp(.3f, .55f, (t - .78f) / .22f) : Mathf.Lerp(.06f, .3f, t / .78f);
                float alpha = popping ? 1f - (t - .78f) / .22f : .9f;

                img.rectTransform.anchoredPosition = look.BubbleAt[b];
                img.rectTransform.sizeDelta = new Vector2(Cell * size, Cell * size * .8f);
                img.color = Pal.A(Color.Lerp(PoolAcid, Color.white, .45f), alpha * shown);
            }
        }

        /// <summary>A pool landing: a ring of acid thrown out across the ground and drops flung up.</summary>
        void Splash(Vector2 at, float wide)
        {
            Ripple(at, ToxicHue, wide * 1.2f, .35f, 0f);
            for (int i = 0; i < 5; i++)
                Cinder(at, PoolAcid, Cell * .26f, .45f,
                       new Vector2(Random.Range(-1f, 1f) * Cell * 1.4f, Cell * Random.Range(.5f, 1.3f)));
        }

        // ------------------------------------------------------------------ the vortex
        /// <summary>The vortex's disc and horizon, in cells: a little smaller than a Gravity Hole's, which takes a quarter of the hill.</summary>
        const float EddyWide = 5f, EddyCore = 1.3f;

        /// <summary>Seconds the vortex takes to fall in on itself once the model lets go.</summary>
        const float EddyShuts = .34f;

        /// <summary>Seconds between two motes falling in, two rings closing, and two deals of its lightning.</summary>
        const float EddyMote = .05f, EddyPulse = .34f, EddyCrackle = .06f;

        /// <summary>How fast it turns, as a multiple of a well's: winding up over its life, fastest as it shuts.</summary>
        const float EddyHasteFrom = 1.5f, EddyHasteTo = 3.4f;

        /// <summary>How far from its eye a body is drawn streaming light into it, in cells (about its box, 59i).</summary>
        const float EddyReach = 2.6f;

        /// <summary>
        /// What the well's pieces are washed in: the Gravity Hole's gold-to-indigo pulled toward
        /// the vortex card's violet, so the two holes on this hill are kin and never the same thing.
        /// </summary>
        static readonly Color EddyWash = new Color(.84f, .64f, 1f);

        sealed class Eddy
        {
            public Pit Pit;

            /// <summary>The storm caught in it: arcs between the horizon and the rim, re-dealt on a beat.</summary>
            public RectTransform Sky;
            public Lightning.Layered Storm;

            /// <summary>Seconds it has been shutting (or -1), the swell a bite gave it, and the lightning's clock.</summary>
            public float Shut = -1f, Kick, Crackle;
            public int Seed;
        }

        Eddy _eddy;

        /// <summary>
        /// The vortex (<c>SiegeBoard.Vortices</c>), put up, turned and taken down off
        /// <c>VortexOpen</c> every frame, as the Gravity Hole is off <c>Sinking</c> (48l): a
        /// maelstrom on the crowd it gathers, built from the well's own stack
        /// (<see cref="Raise"/>) so the near half of the disc stands in front of the bodies it
        /// holds and the far half behind them, with a storm caught in it, light falling in off
        /// every side, rings closing on it, the held bodies streaming light into its eye, a kick
        /// on every bite (<see cref="EddyBite"/>) and a collapse that throws the light back out.
        ///
        /// <para>
        /// <b>Built once when it opens and moved every frame after</b>; only the motes and the
        /// rings are made while it stands, a few a second, and the streams are pooled embers.
        /// </para>
        /// </summary>
        void Vortex(float dt)
        {
            bool open = _board.VortexOpen && !Over;

            // A board dealt again took the layers this was drawn in with it.
            if (_eddy != null && (!_eddy.Pit.Under || !_eddy.Pit.Over)) ScrapEddy();

            if (open && _eddy != null && _eddy.Shut >= 0f) ScrapEddy();
            if (open && _eddy == null) _eddy = RaiseEddy();
            if (_eddy == null) return;

            var eddy = _eddy;
            var pit = eddy.Pit;
            pit.Age += dt;

            if (!open && eddy.Shut < 0f) EddyShut(eddy);

            if (eddy.Shut >= 0f)
            {
                eddy.Shut += dt;
                if (eddy.Shut >= EddyShuts) { ScrapEddy(); return; }
            }

            float life = Mathf.Max(.1f, _board.Boosts.Arsenal.VortexFor);
            float grown = Ease.OutBack(Mathf.Clamp01(pit.Age / WellOpens));
            float gone = eddy.Shut < 0f ? 0f : Ease.InCubic(Mathf.Clamp01(eddy.Shut / EddyShuts));
            eddy.Kick = Mathf.MoveTowards(eddy.Kick, 0f, dt * 4.5f);

            float size = grown * (1f - gone) * (1f + .04f * Mathf.Sin(pit.Age * 8f) + .16f * eddy.Kick);
            float light = Mathf.Clamp01(pit.Age / (WellOpens * .6f)) * (1f - gone);

            pit.Under.localScale = pit.Over.localScale = Vector3.one * Mathf.Max(0f, size);

            float haste = Mathf.Lerp(EddyHasteFrom, EddyHasteTo, Mathf.Clamp01(pit.Age / life));
            pit.Spin -= dt * WellSpin * haste * (eddy.Shut >= 0f ? 1.8f : 1f);

            var wash = Color.Lerp(EddyWash, Color.white, eddy.Kick * .6f);
            Twirl(pit.DiscBack, pit.Spin, light, wash);
            Twirl(pit.DiscFront, pit.Spin, light, wash);
            Twirl(pit.ArmsBack, pit.Spin * 2.4f, light * .95f, wash);
            Twirl(pit.ArmsFront, pit.Spin * 2.4f, light * .95f, wash);
            Twirl(pit.Inner, -pit.Spin * .7f + 40f, light * .75f, wash);

            if (pit.Ring != null)
            {
                float beat = .8f + .2f * Mathf.Sin(pit.Age * 13f);
                pit.Ring.color = Pal.A(Color.Lerp(Pal.Lift(VortexHue, .4f), Color.white, eddy.Kick), light * beat);
            }

            if (pit.Veil != null) pit.Veil.color = Pal.A(WellDark, .55f * light);

            // The storm: re-dealt, never animated, because the next frame of lightning is
            // unrelated to the last (37eu). Squashed and leant with the disc by its holder.
            if (eddy.Storm != null && eddy.Storm.Host != null)
            {
                eddy.Sky.localScale = new Vector3(Mathf.Max(0f, size), Mathf.Max(0f, size) * WellTilt, 1f);
                eddy.Crackle += dt;

                if (eddy.Crackle >= EddyCrackle && eddy.Shut < 0f)
                {
                    eddy.Crackle = 0f;
                    eddy.Storm.Nova(Vector2.zero, Cell * EddyCore * .5f, Cell * EddyWide * (.36f + .08f * Random.value),
                                    4 + (eddy.Seed & 1), Cell, .5f, ++eddy.Seed);
                }

                eddy.Storm.Group.alpha = light * (eddy.Shut >= 0f ? 1f - gone : .55f + .45f * Mathf.Abs(Mathf.Sin(pit.Age * 31f)));
            }

            if (eddy.Shut >= 0f) return;

            pit.Mote += dt;
            while (pit.Mote >= EddyMote) { pit.Mote -= EddyMote; Infall(pit); }

            pit.Pulse += dt;
            if (pit.Pulse >= EddyPulse) { pit.Pulse -= EddyPulse; Closing(pit, EddyWide * .95f, .42f); }

            Streaming(pit.At, dt);
        }

        /// <summary>A piece of the well turned and washed: <see cref="Turn"/> with a colour.</summary>
        static void Twirl(Image piece, float degrees, float alpha, Color wash)
        {
            if (piece == null) return;

            piece.rectTransform.localRotation = Quaternion.Euler(0f, 0f, degrees);
            piece.color = Pal.A(wash, Mathf.Clamp01(alpha));
        }

        Eddy RaiseEddy()
        {
            if (_hill == null || _fx == null) return null;

            var at = new Vector2(LaneX(_board.VortexLane), MarchY(_board.VortexMarch));
            var pit = Raise(at, _hill, _fx, Cell * EddyWide, Cell * EddyCore);
            pit.Over.SetAsFirstSibling();

            // **The storm's holder is a box the size of the board**, centred on the eye, so the
            // lightning's own rect is never culled and its origin is the eye (`Lightning.Grow`).
            var sky = UIKit.Box("Eddy storm", _fx, Span, new Vector2(.5f, .5f), at);
            sky.localRotation = Quaternion.Euler(0f, 0f, WellLean);
            sky.localScale = Vector3.zero;

            var storm = Lightning.Grow("Storm", sky, Pal.Lift(VortexHue, .25f), Cell * .05f, haloAlpha: .4f);
            storm.Group.alpha = 0f;

            return new Eddy { Pit = pit, Sky = sky, Storm = storm, Seed = Random.Range(0, 1 << 20) };
        }

        void ScrapEddy()
        {
            if (_eddy == null) return;

            if (_eddy.Pit.Under) Destroy(_eddy.Pit.Under.gameObject);
            if (_eddy.Pit.Over) Destroy(_eddy.Pit.Over.gameObject);
            if (_eddy.Sky) Destroy(_eddy.Sky.gameObject);
            _eddy = null;
        }

        /// <summary>
        /// The moment it opens (the model's volley): the sky dims violet, a great ring closes
        /// on the eye, a flare where it tears open, and every body near enough to be taken is
        /// drawn a thread of light into it.
        /// </summary>
        void EddyOpened(Vector2 at)
        {
            if (_eddy != null) at = _eddy.Pit.At;

            Lightup(WellViolet, .16f, .45f);
            Flash(at, Pal.Bloom, 3.2f);
            if (_eddy != null) Closing(_eddy.Pit, EddyWide * 1.8f, WellOpens + .12f);
            ShakeBoard(Cell * .08f);

            float reach = Cell * EddyReach * 1.2f;
            int drawn = 0;

            for (int i = 0; i < _mob.Count && drawn < 12; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Node || mob.Falling || mob.Boss) continue;

                var from = mob.Node.anchoredPosition;
                if ((from - at).sqrMagnitude > reach * reach) continue;

                Taken(from, at, i);
                drawn++;
            }

            Audio.Sfx("voidblast", .45f, .9f);
            Audio.Sfx("whoosh", .55f, .6f);
        }

        /// <summary>
        /// A bite (the model's cadence, <c>SiegeArsenal.VortexTick</c>): the vortex swells and
        /// goes white for a beat and a ring closes on it. Once a frame, however many it bit.
        /// </summary>
        void EddyBite()
        {
            if (_eddy == null || _eddy.Shut >= 0f) return;

            _eddy.Kick = 1f;
            Closing(_eddy.Pit, EddyWide * .8f, .28f);
            Audio.SfxVaried("arc", .16f);
        }

        /// <summary>
        /// The light being pulled off every body near the eye, in pooled embers: a few a frame
        /// at most, so a crowd in the vortex costs what a handful does.
        /// </summary>
        void Streaming(Vector2 eye, float dt)
        {
            float reach = Cell * EddyReach;
            float chance = dt * 7f;
            int left = 5;

            for (int i = 0; i < _mob.Count && left > 0; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Node || mob.Falling || mob.Boss) continue;

                var at = mob.Node.anchoredPosition;
                var toward = eye - at;
                if (toward.sqrMagnitude > reach * reach || Random.value > chance) continue;

                // Bent round the eye rather than straight at it: it is turning.
                var swirl = new Vector2(-toward.y, toward.x) * .35f;
                Cinder(at + Random.insideUnitCircle * Cell * .3f,
                       Random.value < .3f ? Pal.Bloom : Pal.Lift(VortexHue, .3f),
                       Cell * Random.Range(.24f, .4f), .34f, toward * .9f + swirl);
                left--;
            }
        }

        /// <summary>
        /// The vortex letting go: it falls in on itself, and what comes back out is light - a
        /// flare, two rings racing out, sparks thrown up and stars - with a thud under it.
        /// </summary>
        void EddyShut(Eddy eddy)
        {
            eddy.Shut = 0f;

            // Nothing is thrown over a finished run: the panel is on its way.
            if (Over || _fx == null) return;

            var at = eddy.Pit.At;

            Tween.After(EddyShuts * .8f, () =>
            {
                if (_fx == null) return;

                Flash(at, Pal.Bloom, 3.4f);
                Ripple(at, WellViolet, Cell * 6f, .5f, 0f);
                Ripple(at, Pal.Sun, Cell * 3.8f, .42f, .06f);
                Embers(at, Pal.Bloom, 14, Cell * 1.5f, Cell * .26f);
                Twinkles(at, WellViolet, 7, Cell * 1.5f);
                ShakeBoard(Cell * .1f);

                Audio.Sfx("boom", .5f, .5f);
                Audio.Sfx("whoosh", .4f, 1.3f);
            }, _fx);
        }

        // ------------------------------------------------------------------ the ray
        /// <summary>
        /// The ray's four layers, outside in: width in lanes, colour, alpha, and how tight its
        /// falloff is. <b>Wide and soft outside, narrow and hard inside, none at full alpha but the
        /// core</b> - the singularity's beam's lesson (<c>BeamLayers</c>): added columns of one
        /// shape sum to a white bar with no colour in it.
        /// </summary>
        static readonly (float wide, Color tint, float alpha, float power)[] RayLayers =
        {
            (1.9f, new Color(.55f, .24f, 1f), .36f, .8f),
            (1.15f, new Color(1f, .36f, .84f), .5f, 1.1f),
            (.62f, new Color(1f, .82f, .34f), .62f, 1.5f),
            (.24f, Color.white, .95f, 2f),
        };

        /// <summary>Seconds the ray takes to snap open, and to close to a thread once the model lets go.</summary>
        const float RayOpens = .14f, RayShuts = .3f;

        /// <summary>Pulses racing up the column, and how many a second each laps it.</summary>
        const int RayPulses = 5;
        const float RayLaps = 2.2f;

        /// <summary>Afterimages left behind on the sweep, how often one is laid, and how long one lasts.</summary>
        const int RayGhosts = 10;
        const float RayGhostEvery = .045f, RayGhostLife = .4f;

        /// <summary>Seconds between two deals of the lightning down its flanks.</summary>
        const float RayCrackle = .05f;

        sealed class Sweep
        {
            public RectTransform Host;
            public Image[] Column, Pulses, Ghosts, Beads;
            public float[] GhostAge;
            public float[] GhostX;
            public int GhostNext;
            public float GhostClock;

            public Image[][] Feeds;

            /// <summary>Where the four feeds meet, lit, and the ground glowing under it.</summary>
            public Image Focus, Flare, Ground;

            public Lightning.Layered[] Arcs;
            public float Crackle;
            public int Seed;

            public float X, Age, Shut = -1f;
        }

        Sweep _sweep;

        /// <summary>A lane's width on the hill, for the ray's widths.</summary>
        float LanePitch => Mathf.Max(Cell, Mathf.Abs(LaneX(1f) - LaneX(0f)));

        /// <summary>Where the ray stands on the line, where it reaches on the hill, and where a post's feed leaves it.</summary>
        float RayFoot => _lineY + Cell * 1.6f;
        float RayTop => Mathf.Min(Span.y * .5f, MarchY(0f) + Cell * 1.5f);
        Vector2 Muzzle(int ward) => new Vector2(PostX(ward), _lineY + Cell);

        /// <summary>
        /// The fused ray (<c>SiegeBoard.Rays</c>), put up, swept and taken down off
        /// <c>RayLive</c> and <c>RayLane</c> every frame: four additive columns stacked into one
        /// beam, fed by a beam from every standing post into a lit focus on the line, pulses
        /// racing up it, lightning down both flanks, afterimages smeared behind it on the sweep,
        /// sparks thrown off its leading edge - and, when the model lets go, a close to a thread
        /// and a flash.
        ///
        /// <para>
        /// <b>Built once when it opens and moved every frame after</b>: the pulses, the
        /// afterimages and the beads are a fixed ring of images re-laid where they belong, the
        /// lightning is two meshes re-dealt, the sparks are pooled embers. A ray costs the same
        /// on the twentieth wave as on the first.
        /// </para>
        /// </summary>
        void Ray(float dt)
        {
            bool live = _board.RayLive && !Over;

            // A board dealt again took the layer this was drawn in with it.
            if (_sweep != null && !_sweep.Host) _sweep = null;

            if (live && _sweep != null && _sweep.Shut >= 0f) ScrapSweep();
            if (live && _sweep == null) _sweep = RaiseSweep();
            if (_sweep == null) return;

            var sweep = _sweep;
            sweep.Age += dt;

            if (!live && sweep.Shut < 0f) SweepShut(sweep);

            if (sweep.Shut >= 0f)
            {
                sweep.Shut += dt;
                if (sweep.Shut >= RayShuts) { ScrapSweep(); return; }
            }

            float was = sweep.X;
            if (live) sweep.X = LaneX(_board.RayLane);
            float moved = sweep.X - was;

            Sweeping(sweep, dt, moved);
        }

        Sweep RaiseSweep()
        {
            var sweep = new Sweep
            {
                Host = UIKit.Node("Ray", _fx),
                X = LaneX(_board.RayLane),
                Seed = Random.Range(0, 1 << 20),
            };

            var host = sweep.Host;
            int posts = _posts != null ? _posts.Length : 0;

            // Under the column, so it is the column that reads first.
            sweep.Ghosts = new Image[RayGhosts];
            sweep.GhostAge = new float[RayGhosts];
            sweep.GhostX = new float[RayGhosts];
            for (int i = 0; i < RayGhosts; i++)
            {
                sweep.Ghosts[i] = Lit("Ghost", host, Art.Shaft(64, 64, 1.2f), Color.clear, Vector2.one);
                sweep.GhostAge[i] = RayGhostLife;
            }

            sweep.Ground = Lit("Ground", host, Art.Glow(128, 1.6f), Color.clear, new Vector2(Cell * 4.4f, Cell * 2f));

            sweep.Feeds = new Image[posts][];
            sweep.Beads = new Image[posts];
            for (int w = 0; w < posts; w++)
            {
                sweep.Feeds[w] = Shaft(host, Muzzle(w), new Vector2(sweep.X, RayFoot), RayHue, Cell * .26f);
                sweep.Beads[w] = Lit("Bead", host, Art.Glow(96, 2f), Color.clear, Vector2.one * Cell * .9f);
            }

            sweep.Column = new Image[RayLayers.Length];
            for (int i = 0; i < RayLayers.Length; i++)
                sweep.Column[i] = Lit("Column", host, Art.Shaft(64, 64, RayLayers[i].power), Color.clear, Vector2.one);

            sweep.Pulses = new Image[RayPulses];
            for (int i = 0; i < RayPulses; i++)
                sweep.Pulses[i] = Lit("Pulse", host, Art.Glow(96, 1.6f), Color.clear, new Vector2(Cell * 1.8f, Cell * 3.2f));

            sweep.Focus = Lit("Focus", host, Art.Glow(128, 2f), Color.clear, Vector2.one * Cell * 3.4f);
            sweep.Flare = Lit("Flare", host, StrikeFx.Flare, Color.clear, Vector2.one * Cell * 3.2f);

            sweep.Arcs = new Lightning.Layered[2];
            for (int side = 0; side < 2; side++)
            {
                sweep.Arcs[side] = Lightning.Grow("Ray arc", host, Pal.Bloom, Cell * .06f, haloAlpha: .45f);
                sweep.Arcs[side].Group.alpha = 0f;
            }

            return sweep;
        }

        /// <summary>Lays the whole ray where the sweep has got to, at the strength its open and close allow.</summary>
        void Sweeping(Sweep sweep, float dt, float moved)
        {
            float foot = RayFoot, top = RayTop, tall = Mathf.Max(Cell, top - foot);
            float x = sweep.X, pitch = LanePitch, now = sweep.Age;

            float open = sweep.Age < RayOpens ? Ease.OutBack(sweep.Age / RayOpens) : 1f;
            float shut = sweep.Shut < 0f ? 1f : 1f - Ease.InQuad(Mathf.Clamp01(sweep.Shut / RayShuts));
            float flick = 1f + .07f * Mathf.Sin(now * 150f) + .04f * Mathf.Sin(now * 67f);
            float wide = Mathf.Max(0f, open * shut) * flick;
            float lit = Mathf.Clamp01(open) * (sweep.Shut < 0f ? 1f : shut);

            var mid = new Vector2(x, (foot + top) * .5f);

            for (int i = 0; i < sweep.Column.Length; i++)
            {
                var img = sweep.Column[i];
                if (!img) continue;

                // The core outlasts the sheath: as it closes it goes white, not dim.
                bool core = i == sweep.Column.Length - 1;
                var layer = RayLayers[i];
                float across = layer.wide * pitch * (core ? Mathf.Max(wide, .35f * lit) : wide);

                var rt = img.rectTransform;
                rt.anchoredPosition = mid;
                rt.sizeDelta = new Vector2(across, tall);
                img.color = Pal.A(layer.tint, layer.alpha * (core ? lit : Mathf.Clamp01(wide * 1.3f)));
            }

            // Pulses racing up it, so it reads as something travelling rather than a bar.
            for (int i = 0; i < sweep.Pulses.Length; i++)
            {
                var pulse = sweep.Pulses[i];
                if (!pulse) continue;

                float lap = now * RayLaps + i / (float)sweep.Pulses.Length;
                lap -= Mathf.Floor(lap);

                pulse.rectTransform.anchoredPosition = new Vector2(x, Mathf.Lerp(foot, top, lap));
                pulse.rectTransform.localScale = new Vector3(Mathf.Max(.2f, wide), 1f, 1f);
                pulse.color = Pal.A(i % 2 == 0 ? Color.white : Pal.Bloom, Mathf.Sin(lap * Mathf.PI) * .7f * lit);
            }

            // The focus, where the four feeds meet: a light, and a flare turning in it.
            var focus = new Vector2(x, foot);
            float beat = .85f + .15f * Mathf.Sin(now * 22f);

            if (sweep.Focus)
            {
                sweep.Focus.rectTransform.anchoredPosition = focus;
                sweep.Focus.rectTransform.localScale = Vector3.one * beat * Mathf.Max(.2f, wide);
                sweep.Focus.color = Pal.A(Color.white, .9f * lit);
            }

            if (sweep.Flare)
            {
                sweep.Flare.rectTransform.anchoredPosition = focus;
                sweep.Flare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, now * 220f);
                sweep.Flare.rectTransform.localScale = Vector3.one * (.8f + .3f * beat) * Mathf.Max(.2f, wide);
                sweep.Flare.color = Pal.A(Pal.Bloom, .85f * lit);
            }

            if (sweep.Ground)
            {
                sweep.Ground.rectTransform.anchoredPosition = focus;
                sweep.Ground.color = Pal.A(RayHue, .5f * lit * beat);
            }

            // A feed from every standing post, and a bead of light racing down each to the focus.
            for (int w = 0; w < sweep.Feeds.Length; w++)
            {
                bool standing = w < _board.Wards.Count && _board.Wards[w].Alive;
                var feed = sweep.Feeds[w];
                var bead = sweep.Beads[w];

                for (int i = 0; i < feed.Length; i++) if (feed[i]) feed[i].enabled = standing && lit > 0f;
                if (bead) bead.enabled = standing && lit > 0f;
                if (!standing) continue;

                var from = Muzzle(w);
                Lay(feed, from, focus, Cell * .26f * Mathf.Max(.25f, wide));
                for (int i = 0; i < feed.Length; i++)
                    if (feed[i]) feed[i].color = Pal.A(feed[i].color, (i == 2 ? .95f : .7f) * lit);

                if (bead)
                {
                    float run = now * 3.2f + w * .27f;
                    run -= Mathf.Floor(run);
                    bead.rectTransform.anchoredPosition = Vector2.Lerp(from, focus, run * run);
                    bead.color = Pal.A(Color.white, Mathf.Sin(run * Mathf.PI) * .9f * lit);
                }
            }

            // Afterimages: a ring of columns laid behind the sweep and let go, oldest first.
            sweep.GhostClock += dt;
            if (sweep.Shut < 0f && Mathf.Abs(moved) > .01f && sweep.GhostClock >= RayGhostEvery)
            {
                sweep.GhostClock = 0f;
                sweep.GhostX[sweep.GhostNext] = x;
                sweep.GhostAge[sweep.GhostNext] = 0f;
                sweep.GhostNext = (sweep.GhostNext + 1) % sweep.Ghosts.Length;
            }

            for (int i = 0; i < sweep.Ghosts.Length; i++)
            {
                var ghost = sweep.Ghosts[i];
                if (!ghost) continue;

                sweep.GhostAge[i] += dt;
                float fade = 1f - Mathf.Clamp01(sweep.GhostAge[i] / RayGhostLife);
                ghost.enabled = fade > 0f;
                if (fade <= 0f) continue;

                var rt = ghost.rectTransform;
                rt.anchoredPosition = new Vector2(sweep.GhostX[i], mid.y);
                rt.sizeDelta = new Vector2(pitch * Mathf.Lerp(.35f, .9f, fade), tall);
                ghost.color = Pal.A(Color.Lerp(RayLayers[0].tint, RayLayers[1].tint, fade), .3f * fade * fade);
            }

            // Lightning down both flanks, re-dealt on a beat.
            sweep.Crackle += dt;
            bool deal = sweep.Crackle >= RayCrackle && sweep.Shut < 0f;
            if (deal) sweep.Crackle = 0f;

            for (int side = 0; side < sweep.Arcs.Length; side++)
            {
                var arc = sweep.Arcs[side];
                if (arc == null || arc.Host == null) continue;

                if (deal)
                {
                    var flank = new Vector2((side == 0 ? -1f : 1f) * pitch * .42f, 0f);
                    arc.Strike(new Vector2(x, top) + flank, focus + flank, Cell, .5f, 1, ++sweep.Seed);
                }

                arc.Group.alpha = lit * (.6f + .4f * Mathf.Abs(Mathf.Sin(now * 37f + side)));
            }

            if (sweep.Shut >= 0f) return;

            // Sparks thrown off the leading edge, the way it is going.
            float ahead = moved >= 0f ? 1f : -1f;
            int sparks = Mathf.Min(3, Mathf.FloorToInt(dt * 45f + Random.value));
            for (int i = 0; i < sparks; i++)
            {
                var at = new Vector2(x + ahead * pitch * .35f, Mathf.Lerp(foot, top, Random.value));
                Cinder(at, Random.value < .4f ? Pal.Sun : Random.value < .5f ? Pal.Bloom : Color.white,
                       Cell * Random.Range(.2f, .36f), .32f,
                       new Vector2(ahead * Cell * Random.Range(1.2f, 2.6f), Random.Range(-Cell, Cell * 1.4f)));
            }

            if (Random.value < dt * 6f) ShakeBoard(Cell * .04f);
        }

        /// <summary>
        /// The moment the ray fuses (the model's volley): a bolt from every standing post into
        /// the focus, a flare and a ring where they meet, and the sky lit pink.
        /// </summary>
        void Fused()
        {
            var focus = new Vector2(_sweep != null ? _sweep.X : LaneX(_board.RayLane), RayFoot);

            Lightup(RayHue, .16f, .5f);
            Flash(focus, Pal.Bloom, 4.2f);
            Ripple(focus, RayHue, Cell * 6f, .5f, 0f);
            Twinkles(focus, Pal.Bloom, 6, Cell * 1.2f);
            ShakeBoard(Cell * .12f);

            int posts = _posts != null ? _posts.Length : 0;
            for (int w = 0; w < posts; w++)
            {
                if (w >= _board.Wards.Count || !_board.Wards[w].Alive) continue;

                var bolt = Lightning.Grow("Fuse", _fx, Pal.Bloom, Cell * .07f, haloAlpha: .5f);
                bolt.Strike(Muzzle(w), focus, Cell, .45f, 1, Random.Range(0, 1 << 20));

                Tween.Run(.28f, Ease.Linear, t =>
                {
                    if (bolt.Host == null) return;
                    bolt.Group.alpha = (1f - t) * (.6f + .4f * Mathf.Abs(Mathf.Sin(t * 40f)));
                }, bolt.Host).OnDone(bolt.Destroy);

                Tween.After(.32f, bolt.Destroy, bolt.Host);
            }

            Audio.Sfx("charge", .5f, .8f);
            Audio.Sfx("arc", .6f, .85f);
            Audio.Sfx("thunder", .35f, 1.3f);
        }

        /// <summary>The ray closing: a flash at the focus where it ends and stars where it stood.</summary>
        void SweepShut(Sweep sweep)
        {
            sweep.Shut = 0f;

            if (Over || _fx == null) return;

            var focus = new Vector2(sweep.X, RayFoot);
            Flash(focus, Pal.Bloom, 3f);
            Twinkles(new Vector2(sweep.X, (RayFoot + RayTop) * .5f), Pal.Bloom, 6, Cell * 1.2f);
            Audio.Sfx("whoosh", .35f, 1.4f);
        }

        void ScrapSweep()
        {
            if (_sweep == null) return;
            if (_sweep.Host) Destroy(_sweep.Host.gameObject);
            _sweep = null;
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
