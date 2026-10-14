using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>A laser gun</b> (the Shuffle lane's <c>laser_core</c>, MODES.md 59m): what a turret the
    /// build has made a laser looks like - every shot a beam that is there the instant it fires,
    /// ricocheting body to body, out of a lens that hums on the turret's barrel.
    ///
    /// <para>
    /// <b>A beam, not a projectile, and that is the whole of what separates it from every other
    /// shot on this board.</b> A comet crosses the hill over a fifth of a second; a laser is
    /// already at the raider on the frame it fires, so it is drawn as a line that <em>snaps
    /// open</em> along its whole length, holds white-hot for a breath with a flicker in its width,
    /// and collapses to a thread - five additive layers (a wide soft halo, the coloured sheath, the
    /// bought beam reel streaming along it, the white core, and a lens flare at each end), so the
    /// overlaps go white the way light does (invariant 37eu). A bounce is the same beam leaving the
    /// body it last struck a few hundredths later, so a shot reads as light travelling through a
    /// crowd rather than four lines appearing at once.
    /// </para>
    /// <para>
    /// <b>Reached only through a lasered ward</b>: <see cref="Lasered"/> answers false on every
    /// board that holds no build, the board reports <see cref="SiegeVia.Laser"/> on no other, and
    /// <see cref="Lasers"/> runs from <c>Arsenal</c>, which returns on the plain line first. A
    /// chapter and the Infinite lane draw exactly what they drew before this file existed.
    /// </para>
    /// <para>
    /// <b>Pooled, because a laser line is the busiest thing a build can make.</b> Four lasered
    /// turrets firing a spun-up cadence and bouncing three times is well over a hundred beams a
    /// second; each is one pooled node of six images, handed back on a timer (<see cref="Ends"/>'
    /// reason), and the pool is capped so a pathological second costs drawing and never memory.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        /// <summary>How long one beam lives: snap open, hold, collapse to a thread.</summary>
        const float LaserOpen = .035f, LaserHold = .07f, LaserShut = .13f;

        /// <summary>A primary beam's core width, and a bounce's, in cells. The other layers are multiples.</summary>
        const float LaserWide = .17f, LaserBounceWide = .12f;

        /// <summary>How long the light takes to leave one body for the next.</summary>
        const float LaserHop = .045f;

        /// <summary>The most beams standing at once; past it a shot still lands, it is only not drawn.</summary>
        const int MostBeams = 64;

        /// <summary>The fewest real seconds between two of one turret's laser sounds.</summary>
        const float LaserVoiceEvery = .085f;

        // ------------------------------------------------------------------ the pool
        sealed class LaserBeam
        {
            public RectTransform Host;
            public Image Halo, Sheath, Stream, Core, Muzzle, Impact;
            public bool Out;
        }

        readonly Stack<LaserBeam> _beams = new Stack<LaserBeam>(24);
        int _beamsOut;

        LaserBeam LendBeam()
        {
            if (_fx == null || _beamsOut >= MostBeams) return null;

            LaserBeam beam = null;
            while (_beams.Count > 0 && beam == null)
            {
                beam = _beams.Pop();
                if (beam.Host == null) beam = null;
            }

            if (beam == null)
            {
                var host = UIKit.Node("Laser", _fx);

                beam = new LaserBeam
                {
                    Host = host,
                    Halo = Lit("Halo", host, Art.SoftCapsule(40, 120), Color.clear, Vector2.one),
                    Sheath = Lit("Sheath", host, Art.Shaft(64, 64, 1.3f), Color.clear, Vector2.one),
                    Core = Lit("Core", host, Art.Capsule(24, 96), Color.clear, Vector2.one),
                };

                // The bought beam reel, streaming along the beam: drawn along x, so it is laid
                // along the beam's own length and turned with it.
                var reel = Reel("beam");
                if (reel != null && reel.Length > 0)
                {
                    beam.Stream = Lit("Stream", host, reel[0], Color.clear, Vector2.one);
                    Flipbook.Attach(beam.Stream, reel, 40f, true);
                }

                var flare = Kit(StrikeFx.Flare);
                if (flare != null)
                {
                    beam.Muzzle = Lit("Muzzle", host, flare, Color.clear, Vector2.one);
                    beam.Impact = Lit("Impact", host, flare, Color.clear, Vector2.one);
                }
            }

            beam.Out = true;
            _beamsOut++;
            beam.Host.gameObject.SetActive(true);
            beam.Host.SetAsLastSibling();
            beam.Host.localScale = Vector3.one;
            return beam;
        }

        void GiveBeam(LaserBeam beam)
        {
            if (beam == null || !beam.Out) return;

            beam.Out = false;
            _beamsOut = Mathf.Max(0, _beamsOut - 1);

            if (beam.Host == null) return;

            Tween.KillAll(beam.Host);
            beam.Host.gameObject.SetActive(false);
            _beams.Push(beam);
        }

        // ------------------------------------------------------------------ one beam
        /// <summary>
        /// One beam from <paramref name="from"/> to <paramref name="to"/> in <paramref name="tint"/>,
        /// <paramref name="after"/> seconds from now: snapped open along its whole length, held
        /// white-hot with a flicker in its width, collapsed to a thread. A lens flare burns at the
        /// muzzle when <paramref name="muzzle"/> and at the far end always.
        /// </summary>
        void Lase(Vector2 from, Vector2 to, Color tint, float wide, float after, bool muzzle)
        {
            var beam = LendBeam();
            if (beam == null) return;

            var span = to - from;
            float length = Mathf.Max(Cell * .2f, span.magnitude);
            float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg;
            var mid = (from + to) * .5f;
            var lift = Color.Lerp(tint, Color.white, .35f);
            float phase = Random.Range(0f, 30f);

            // Every layer lies along x and is turned to the beam, so one rotation lays them all.
            void Layer(Image img, float across, float over)
            {
                if (img == null) return;
                var rt = img.rectTransform;
                rt.anchoredPosition = mid;
                rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                rt.sizeDelta = new Vector2(length + over, across);
            }

            // A capsule is drawn tall, so it is turned a quarter further than a reel drawn along x.
            void Upright(Image img, float across, float over)
            {
                if (img == null) return;
                var rt = img.rectTransform;
                rt.anchoredPosition = mid;
                rt.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                rt.sizeDelta = new Vector2(across, length + over);
            }

            float core = Cell * wide;

            void Lay(float k, float flick)
            {
                float w = core * k * flick;
                Upright(beam.Halo, w * 6.5f, w * 3f);
                Upright(beam.Sheath, w * 2.6f, w);
                Layer(beam.Stream, w * 3.4f, 0f);
                Upright(beam.Core, Mathf.Max(core * .22f, w * .9f), w * .6f);
            }

            Lay(0f, 1f);
            Paint(beam, Color.clear, Color.clear, 0f);

            if (beam.Muzzle != null)
            {
                beam.Muzzle.rectTransform.anchoredPosition = from;
                beam.Muzzle.rectTransform.sizeDelta = Vector2.one * Cell * (muzzle ? 1.9f : 1.1f);
            }

            if (beam.Impact != null)
            {
                beam.Impact.rectTransform.anchoredPosition = to;
                beam.Impact.rectTransform.sizeDelta = Vector2.one * Cell * 1.7f;
            }

            float life = LaserOpen + LaserHold + LaserShut;

            Tween.Run(life, Ease.Linear, t =>
            {
                if (beam.Host == null) return;

                float s = t * life;

                // Open with an overshoot, hold with a flicker, then collapse: the width goes and
                // the core stays a beat longer, so a beam ends as a white thread rather than a fade.
                float k, glow;
                if (s < LaserOpen) { k = Ease.OutBack(s / LaserOpen) * 1.15f; glow = s / LaserOpen; }
                else if (s < LaserOpen + LaserHold) { k = 1f; glow = 1f; }
                else
                {
                    float u = (s - LaserOpen - LaserHold) / LaserShut;
                    k = 1f - Ease.InQuad(u);
                    glow = 1f - u * u;
                }

                float flick = 1f + .14f * Mathf.Sin(s * 170f + phase) + .07f * Mathf.Sin(s * 63f + phase);
                Lay(Mathf.Max(0f, k), flick);
                Paint(beam, tint, lift, Mathf.Clamp01(glow));

                if (beam.Muzzle != null)
                {
                    beam.Muzzle.color = muzzle ? Pal.A(lift, glow) : Pal.A(lift, .6f * glow);
                    beam.Muzzle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle + s * 260f);
                    beam.Muzzle.rectTransform.localScale = Vector3.one * (.6f + .55f * k);
                }

                if (beam.Impact != null)
                {
                    beam.Impact.color = Pal.A(lift, glow);
                    beam.Impact.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle - s * 300f);
                    beam.Impact.rectTransform.localScale = Vector3.one * (.5f + .8f * k);
                }
            }, beam.Host).Delay(after).OnDone(() => GiveBeam(beam));

            // Handed back on a timer too, in case the tween is torn down with the layer.
            Tween.After(after + life + .2f, () => GiveBeam(beam), beam.Host);

            // The sparks a beam throws where it lands, sprayed back along it and off to the sides.
            Tween.After(after + LaserOpen * .6f, () =>
            {
                if (_fx == null) return;

                var back = span.sqrMagnitude > 1f ? -span.normalized : Vector2.up;
                var side = new Vector2(-back.y, back.x);

                for (int i = 0; i < 3; i++)
                {
                    var kick = back * Cell * Random.Range(.6f, 1.5f) + side * Cell * Random.Range(-1.3f, 1.3f);
                    Cinder(to, i == 0 ? Color.white : lift, Cell * Random.Range(.16f, .3f), Random.Range(.18f, .3f), kick);
                }
            }, beam.Host);
        }

        /// <summary>One beam's colours at <paramref name="glow"/>: the halo soft, the sheath its colour, the stream lit, the core white.</summary>
        static void Paint(LaserBeam beam, Color tint, Color lift, float glow)
        {
            if (beam.Halo != null) beam.Halo.color = Pal.A(tint, .42f * glow);
            if (beam.Sheath != null) beam.Sheath.color = Pal.A(tint, .9f * glow);
            if (beam.Stream != null) beam.Stream.color = Pal.A(lift, .85f * glow);
            if (beam.Core != null) beam.Core.color = Pal.A(Color.white, glow);
        }

        // ------------------------------------------------------------------ a laser gun firing
        /// <summary>Whether ward <paramref name="ward"/> is a laser gun on this board. False on every board but a build's.</summary>
        bool Lasered(int ward) => _board != null && !_board.Boosts.IsIdentity && _board.Lasered(ward);

        float[] _laserVoice;

        /// <summary>
        /// A laser gun's own shot: the barrel kicks, a beam from every barrel to the raider, the
        /// lens on the turret flares, and the hit lands at once - there is no flight to wait for.
        /// </summary>
        void Lasing(SiegeBolt shot)
        {
            var post = _posts[shot.Ward];
            var ward = _board.Wards[shot.Ward];
            var mob = MobOf(shot.Raider);
            if (mob == null || mob.Node == null) return;

            var tint = TintOf(LookOf(shot, ward.Colour).Colour);
            var to = mob.Node.anchoredPosition;
            var muzzle = Muzzle(shot.Ward);

            post.Recoil = RecoilFor;
            Kick(post);

            int barrels = Barrels(ward.Model);
            for (int b = 0; b < barrels; b++)
            {
                float step = BarrelStep(ward.Model, b, Cell);
                Lase(muzzle + new Vector2(step, 0f), to + new Vector2(step * ApartOnArrival, 0f), tint,
                     LaserWide * (barrels > 1 ? .8f : 1f), 0f, true);
            }

            Flared(shot.Ward);

            Pop(to, shot.Weak ? Pal.Gold : tint, shot.Weak ? 1.7f : 1.25f, .2f);
            Number(shot.Raider, to, shot.Damage, shot.Weak);
            if (shot.Killed) Audio.SfxVaried("zap", .42f);
            if (mob.Body != null) Tween.Punch(mob.Body.transform, .1f, .14f);

            Voice(shot.Ward);
        }

        /// <summary>A beam a laser gun's light carried on into another body: from the one it left.</summary>
        void Bounced(SiegeBolt shot, Vector2 from, Vector2 to)
        {
            var tint = shot.Ward >= 0 && shot.Ward < _board.Wards.Count
                     ? TintOf(_board.Wards[shot.Ward].Colour)
                     : Pal.Radiance;

            // How far down this frame's chain this beam is, so the light is seen travelling.
            float after = Hop(shot.Ward) * LaserHop;
            bool fromMuzzle = shot.From < 0;

            Lase(from, to, tint, fromMuzzle ? LaserWide : LaserBounceWide, after, fromMuzzle);

            var raider = shot.Raider;
            int damage = shot.Damage;
            bool killed = shot.Killed;

            Tween.After(after, () =>
            {
                if (this == null) return;
                Pop(to, tint, 1.15f, .18f);
                Number(raider, to, damage, false);
                if (killed) KillSound();
            }, _fx);
        }

        int[] _hops;
        int _hopFrame = -1;

        /// <summary>How many beams of <paramref name="ward"/>'s have been drawn this frame before this one.</summary>
        int Hop(int ward)
        {
            if (_posts == null) return 0;
            if (_hops == null || _hops.Length != _posts.Length) _hops = new int[_posts.Length];

            if (_hopFrame != Time.frameCount)
            {
                _hopFrame = Time.frameCount;
                System.Array.Clear(_hops, 0, _hops.Length);
            }

            if (ward < 0 || ward >= _hops.Length) return 0;
            return ++_hops[ward];
        }

        /// <summary>The barrel's kick, on top of the pack's own recoil frames - the comet's own, shared.</summary>
        void Kick(Post post)
        {
            var body = post.Body;
            if (body == null) return;

            Tween.KillChannel(body, "kick");
            Tween.Run(RecoilFor, Ease.OutQuad, t =>
            {
                if (!body) return;
                float k = t < .3f ? t / .3f : 1f - (t - .3f) / .7f;
                body.rectTransform.anchoredPosition = new Vector2(0f, Cell * .06f - Cell * .11f * k);
                body.rectTransform.localScale = new Vector3(1f + k * .06f, 1f - k * .05f, 1f);
            }, body, "kick");
        }

        /// <summary>A laser's voice: a bright zap, held to one per turret every <see cref="LaserVoiceEvery"/>.</summary>
        void Voice(int ward)
        {
            if (_posts == null) return;
            if (_laserVoice == null || _laserVoice.Length != _posts.Length) _laserVoice = new float[_posts.Length];

            float now = Time.unscaledTime;
            if (now - _laserVoice[ward] < LaserVoiceEvery) return;
            _laserVoice[ward] = now;

            Audio.SfxVaried("arc", .2f, .12f);
            Audio.Sfx("shot", .12f, 1.35f);
        }

        // ------------------------------------------------------------------ the emitter
        /// <summary>
        /// The lens a laser gun wears on its barrel: a white core breathing in the turret's colour,
        /// a focusing ring turning, a glint turning the other way, and two motes orbiting it - so a
        /// player can see which turrets the build has made lasers before any of them fires.
        /// </summary>
        sealed class LaserRig
        {
            public RectTransform Host;
            public Image Bloom, Core, Ring, Glint;
            public Image[] Motes;
            public float Flare, Age;
        }

        LaserRig[] _rigs;
        bool[] _rigLasered;

        /// <summary>
        /// Keeps every laser gun's lens drawn true, every frame, and plays the conversion the frame
        /// a turret becomes one. From <c>Arsenal</c>, which is shut on the plain line.
        /// </summary>
        void Lasers(float dt)
        {
            if (_posts == null || _board.Boosts.LaserGuns <= 0) return;

            if (_rigs == null || _rigs.Length != _posts.Length)
            {
                _rigs = new LaserRig[_posts.Length];
                _rigLasered = new bool[_posts.Length];
            }

            for (int w = 0; w < _posts.Length; w++)
            {
                bool lasered = _board.Lasered(w) && w < _board.Wards.Count;
                var rig = _rigs[w];

                // A board dealt again took the layer the lens was drawn in with it.
                if (rig != null && rig.Host == null) rig = _rigs[w] = null;

                if (!lasered)
                {
                    if (rig != null) { Destroy(rig.Host.gameObject); _rigs[w] = null; }
                    _rigLasered[w] = false;
                    continue;
                }

                if (rig == null) rig = _rigs[w] = RaiseRig(w);
                if (!_rigLasered[w])
                {
                    _rigLasered[w] = true;
                    Converted(w);
                }

                Tend(rig, w, dt);
            }
        }

        LaserRig RaiseRig(int ward)
        {
            var tint = TintOf(_board.Wards[ward].Colour);
            var host = UIKit.Node("Laser lens", _fx);

            var rig = new LaserRig
            {
                Host = host,
                Bloom = Lit("Bloom", host, Art.Glow(128, 1.6f), Pal.A(tint, 0f), Vector2.one * Cell * 1.9f),
                Ring = Lit("Ring", host, Art.Ring(128, 7f), Pal.A(tint, 0f), Vector2.one * Cell * .95f),
                Core = Lit("Core", host, Art.Glow(96, 2.4f), Pal.A(Color.white, 0f), Vector2.one * Cell * .55f),
                Glint = Lit("Glint", host, StrikeFx.Glint, Pal.A(Color.white, 0f), Vector2.one * Cell * .9f),
                Motes = new Image[2],
            };

            for (int i = 0; i < rig.Motes.Length; i++)
                rig.Motes[i] = Lit("Mote", host, Art.Glow(64, 2.2f), Pal.A(tint, 0f), Vector2.one * Cell * .22f);

            return rig;
        }

        /// <summary>One frame of a lens: where the barrel is, how bright the last shot left it.</summary>
        void Tend(LaserRig rig, int ward, float dt)
        {
            rig.Age += dt;
            rig.Flare = Mathf.Max(0f, rig.Flare - dt * 6f);

            bool standing = _board.Wards[ward].Alive;
            var tint = TintOf(_board.Wards[ward].Colour);
            var lift = Color.Lerp(tint, Color.white, .45f);

            rig.Host.anchoredPosition = Muzzle(ward) + new Vector2(0f, -Cell * .12f);

            float beat = .82f + .18f * Mathf.Sin(rig.Age * 5.2f + ward);
            float lit = (standing ? 1f : .15f) * Mathf.Min(1f, rig.Age * 3f);
            float flare = rig.Flare;

            if (rig.Bloom)
            {
                rig.Bloom.color = Pal.A(tint, (.32f + .45f * flare) * lit * beat);
                rig.Bloom.rectTransform.localScale = Vector3.one * (.85f + .35f * flare);
            }

            if (rig.Core)
            {
                rig.Core.color = Pal.A(Color.white, (.7f + .3f * flare) * lit);
                rig.Core.rectTransform.localScale = Vector3.one * (beat + .5f * flare);
            }

            if (rig.Ring)
            {
                rig.Ring.color = Pal.A(lift, .7f * lit);
                rig.Ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rig.Age * 140f);
                rig.Ring.rectTransform.localScale = new Vector3(1f, .62f, 1f) * (1f - .25f * flare);
            }

            if (rig.Glint)
            {
                rig.Glint.color = Pal.A(Color.white, (.45f + .55f * flare) * lit);
                rig.Glint.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rig.Age * 90f);
                rig.Glint.rectTransform.localScale = Vector3.one * (.75f + .6f * flare);
            }

            for (int i = 0; i < rig.Motes.Length; i++)
            {
                var mote = rig.Motes[i];
                if (!mote) continue;

                float a = rig.Age * 4.4f + i * Mathf.PI;
                mote.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * Cell * .52f, Mathf.Sin(a) * Cell * .2f);
                mote.color = Pal.A(Mathf.Sin(a) > 0f ? lift : tint, .9f * lit);
            }
        }

        /// <summary>A laser gun firing: its lens flares, read off by <see cref="Tend"/>.</summary>
        void Flared(int ward)
        {
            if (_rigs == null || ward < 0 || ward >= _rigs.Length || _rigs[ward] == null) return;
            _rigs[ward].Flare = 1f;
        }

        /// <summary>
        /// The moment a turret becomes a laser gun: a scan of light climbing the turret from its
        /// foot to its barrel, the lens igniting in a flash and a ring, the sky lit for a beat in
        /// its colour - once, the frame the card lands.
        /// </summary>
        void Converted(int ward)
        {
            if (Over || _fx == null || _posts == null || _posts[ward] == null) return;

            var tint = TintOf(_board.Wards[ward].Colour);
            var lift = Color.Lerp(tint, Color.white, .45f);
            var top = Muzzle(ward);
            var foot = new Vector2(top.x, _lineY - Cell * .7f);

            // The scan: a band of light rising up the turret, leaving it lit behind it.
            var scan = Lit("Laser scan", _fx, Art.Shaft(64, 64, 1.1f), Pal.A(lift, 0f),
                           new Vector2(Cell * BodyWide * 1.15f, Cell * .32f));
            if (scan != null)
            {
                var rt = scan.rectTransform;
                rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rt.sizeDelta = new Vector2(Cell * .32f, Cell * BodyWide * 1.15f);

                Tween.Run(.42f, Ease.InOutSine, t =>
                {
                    if (!scan) return;
                    rt.anchoredPosition = Vector2.Lerp(foot, top, t);
                    scan.color = Pal.A(lift, Mathf.Sin(t * Mathf.PI) * .95f);
                }, scan).OnDone(() => { if (scan) Destroy(scan.gameObject); });
            }

            Tween.After(.4f, () =>
            {
                if (_fx == null) return;

                Flash(top, tint, 3.2f);
                Ripple(top, lift, Cell * 4.4f, .5f, 0f, 1f);
                Twinkles(top, tint, 6, Cell * 1.1f);
                Lightup(tint, .12f, .45f);
                Flared(ward);

                if (_posts[ward] != null && _posts[ward].Node != null) Tween.Punch(_posts[ward].Node, .14f, .3f);

                Audio.Sfx("chime", .5f, 1.3f);
            }, _fx);

            Audio.Sfx("charge", .45f, 1.1f);
        }
    }
}
