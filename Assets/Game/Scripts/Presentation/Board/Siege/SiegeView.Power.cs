using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>The Shuffle lane's field cards, drawn</b> (MODES.md 59m): a power gem being forged and
    /// standing on the field, a rocket tearing down its line, a nova calling lightning onto every
    /// gem of its colour, and a match going off as a blast.
    ///
    /// <para>
    /// <b>Every drawing here is a replay of what the model already decided</b> (invariant 30i),
    /// exactly as <c>SiegeView.Charms</c> is: a forge arrives as <see cref="SiegeBeat.Forged"/>, a
    /// blast as <see cref="SiegeBeat.Blasts"/> and <see cref="SiegeBeat.Blasted"/>, a rocket and a
    /// nova as a <see cref="SiegeSpark"/> of their own charm, and the cells each took in the beat's
    /// own <see cref="SiegeBeat.Cleared"/>. Nothing here works anything out.
    /// </para>
    /// <para>
    /// <b>Each one holds the fall and bends the clock for exactly as long as it plays</b>, the
    /// lance's bargain (<c>Dilate</c>, <c>Holding</c>): the board does not refill into a rocket
    /// still crossing it, and the hill has not walked while the player watched.
    /// </para>
    /// <para>
    /// <b>Additive light stacked out of the strike kit and the bought reels</b> (invariant 37eu),
    /// never a picture of light: a rocket is a flare with a burning trail throwing sparks and a
    /// burst in every cell it passes; a nova is a sun - rays turning, a white core, rings closing
    /// on it - that detonates and forks lightning into its colour; a blast is the bought fire
    /// reel, a flash, a ring and embers thrown up out of the run.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        /// <summary>How long a forge takes: the run flies in, the gem ignites.</summary>
        const float ForgeIn = .17f, ForgeFor = .42f;

        /// <summary>When a blast goes off after its match, and how long the board waits for it.</summary>
        const float BlastAt = .05f, BlastFor = .36f;

        /// <summary>A nova's wind-up before it detonates, and how long its whole sequence runs.</summary>
        const float NovaCharge = .34f, NovaTail = .45f;

        /// <summary>How long a rocket's trail burns after its head has left the field.</summary>
        const float RocketTail = .3f;

        /// <summary>
        /// The beat being drawn, so a nova can find the cells it took (<see cref="SiegeBeat.Cleared"/>)
        /// - a spark carries its own cell and colour, never the beat's.
        /// </summary>
        SiegeBeat _beatNow;

        /// <summary>
        /// How long after the spark the wavefront leaves its cell: nought for a lance (whose
        /// timing shipped as it is), a rocket's or a nova's wind-up for theirs, so a gem comes
        /// apart the moment the head or the bolt reaches it (<c>ClearDelay</c>).
        /// </summary>
        float _sprangLead;

        // ------------------------------------------------------------------ standing on the field
        /// <summary>
        /// Shows, changes or takes away the light a power gem wears over its face. A child of the
        /// gem for the halo's reason (<c>Gem.Ring</c>): it falls, swaps and is destroyed with it.
        /// </summary>
        void Empowered(Gem gem, SiegeCharm charm)
        {
            if (gem == null || gem.Img == null) return;

            bool power = SiegeCharms.IsPower(charm);

            if (gem.Power != null && (!power || gem.Powered != charm))
            {
                Destroy(gem.Power.gameObject);
                gem.Power = null;
                gem.PowerParts = null;
            }

            gem.Powered = power ? charm : SiegeCharm.None;
            if (!power || gem.Power != null) return;

            var tint = gem.Colour >= 0 && gem.Colour < SiegeLayout.Letters.Length ? TintOf(gem.Colour) : Pal.Cream;
            var lift = Color.Lerp(tint, Color.white, .5f);

            var host = UIKit.Node("Power", (RectTransform)gem.Img.transform);
            gem.Power = host;

            if (SiegeCharms.IsRocket(charm))
            {
                host.localRotation = Quaternion.Euler(0f, 0f, charm == SiegeCharm.RocketColumn ? 90f : 0f);

                // A burning streak along the line it will take, two flares at its tips and four
                // motes racing outward along it - a rocket is a gem that is already going somewhere.
                var parts = new List<Image>(8)
                {
                    Lit("Streak", host, StrikeFx.Streak, Pal.A(lift, 0f), new Vector2(Cell * 1.35f, Cell * .42f)),
                    Lit("Tip", host, StrikeFx.Glint, Pal.A(Color.white, 0f), Vector2.one * Cell * .42f),
                    Lit("Tip", host, StrikeFx.Glint, Pal.A(Color.white, 0f), Vector2.one * Cell * .42f),
                };

                for (int i = 0; i < 4; i++)
                    parts.Add(Lit("Spark", host, Art.Glow(64, 2.2f), Pal.A(lift, 0f), Vector2.one * Cell * .16f));

                gem.PowerParts = parts.ToArray();
            }
            else
            {
                // A small sun: rays turning, a ring closing and opening, a white core, a glint.
                gem.PowerParts = new[]
                {
                    Lit("Rays", host, Art.Rays(256, 12), Pal.A(lift, 0f), Vector2.one * Cell * 1.7f),
                    Lit("Ring", host, Art.Ring(128, 6f), Pal.A(lift, 0f), Vector2.one * Cell * 1.05f),
                    Lit("Core", host, Art.Glow(128, 2.1f), Pal.A(Color.white, 0f), Vector2.one * Cell * .75f),
                    Lit("Glint", host, StrikeFx.Glint, Pal.A(Color.white, 0f), Vector2.one * Cell * .95f),
                };
            }

            gem.PowerAge = Random.Range(0f, 2f);
        }

        /// <summary>
        /// The light every power gem on the field gives off, one frame of it. Beside the charms'
        /// own breathing (<c>Breathe</c>), and as cheap: a field holds a handful of them at most.
        /// </summary>
        void Glowing(float dt)
        {
            for (int i = 0; i < _gems.Count; i++)
            {
                var gem = _gems[i];
                if (gem == null || gem.Power == null || gem.PowerParts == null) continue;

                gem.PowerAge += dt;
                float age = gem.PowerAge;
                var parts = gem.PowerParts;

                var tint = gem.Colour >= 0 && gem.Colour < SiegeLayout.Letters.Length ? TintOf(gem.Colour) : Pal.Cream;
                var lift = Color.Lerp(tint, Color.white, .5f);

                if (SiegeCharms.IsRocket(gem.Powered))
                {
                    float throb = .75f + .25f * Mathf.Sin(age * 9f);

                    if (parts[0]) { parts[0].color = Pal.A(lift, .62f * throb); parts[0].rectTransform.localScale = new Vector3(1f, .8f + .3f * throb, 1f); }

                    for (int t = 1; t <= 2; t++)
                    {
                        if (!parts[t]) continue;
                        float side = t == 1 ? -1f : 1f;
                        parts[t].rectTransform.anchoredPosition = new Vector2(side * Cell * .5f, 0f);
                        parts[t].rectTransform.localRotation = Quaternion.Euler(0f, 0f, age * 200f * side);
                        parts[t].color = Pal.A(Color.white, .55f + .4f * throb);
                    }

                    // Sparks racing out from the middle to both tips, two each way.
                    for (int s = 3; s < parts.Length; s++)
                    {
                        if (!parts[s]) continue;
                        int n = s - 3;
                        float side = n % 2 == 0 ? -1f : 1f;
                        float run = age * 1.9f + (n / 2) * .5f;
                        run -= Mathf.Floor(run);
                        parts[s].rectTransform.anchoredPosition = new Vector2(side * Cell * .52f * run, 0f);
                        parts[s].color = Pal.A(Color.white, Mathf.Sin(run * Mathf.PI) * .95f);
                    }
                }
                else
                {
                    float beat = .82f + .18f * Mathf.Sin(age * 6f);

                    if (parts[0]) { parts[0].color = Pal.A(lift, .5f * beat); parts[0].rectTransform.localRotation = Quaternion.Euler(0f, 0f, age * 40f); }
                    if (parts[1])
                    {
                        float ring = (age * .9f) % 1f;
                        parts[1].rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, .55f, ring);
                        parts[1].color = Pal.A(lift, Mathf.Sin(ring * Mathf.PI) * .85f);
                    }
                    if (parts[2]) { parts[2].color = Pal.A(Color.white, .55f * beat); parts[2].rectTransform.localScale = Vector3.one * beat; }
                    if (parts[3]) { parts[3].color = Pal.A(Color.white, .7f * beat); parts[3].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -age * 120f); }
                }
            }
        }

        // ------------------------------------------------------------------ the forge
        /// <summary>
        /// Which forge each cell of this beat's forged runs flies into, or null on a beat that
        /// forged nothing - which is every beat on every board but a build's.
        /// </summary>
        static Dictionary<int, int> Forges(SiegeBeat beat)
        {
            if (beat.Forged.Count == 0) return null;

            var into = new Dictionary<int, int>();
            for (int f = 0; f < beat.Forged.Count; f++)
            {
                var forged = beat.Forged[f];
                for (int i = 0; i < forged.Run.Length; i++) into[forged.Run[i]] = forged.Cell;
            }

            return into;
        }

        /// <summary>
        /// One gem of a forged run going into its forge: pulled across to it, shrinking and
        /// brightening, and its fuel leaving from there - so the run is seen <em>becoming</em> the
        /// power gem rather than vanishing beside it.
        /// </summary>
        void IntoForge(Gem gem, int cell, int forge, int paid, Color tint)
        {
            var img = gem.Img;
            var at = CentreOf(forge);

            Tween.KillAll(img.rectTransform);
            Tween.Move(img.rectTransform, at, ForgeIn, Ease.InQuad);
            Tween.Scale(img.transform, .35f, ForgeIn, Ease.InQuad);
            Tween.After(ForgeIn + .02f, () => { if (img) Destroy(img.gameObject); });

            Cinder(CentreOf(cell), Color.Lerp(tint, Color.white, .5f), Cell * .5f, ForgeIn, (at - CentreOf(cell)) / ForgeIn * .9f);

            int ward = paid >= 0 && paid < SiegeLayout.Letters.Length ? _layout.WardOf(SiegeLayout.Letters[paid]) : -1;
            if (ward >= 0) Mote(at, ward, tint, ForgeIn, Stretched(SiegeTuning.FuelFlight));
        }

        /// <summary>The forge itself: light closing on the cell, then the gem igniting as what it became.</summary>
        void Forging(SiegeForging forged)
        {
            var at = CentreOf(forged.Cell);
            var tint = TintOf(forged.Colour);
            var lift = Color.Lerp(tint, Color.white, .5f);
            bool nova = forged.Charm == SiegeCharm.Nova;

            Holding(ForgeFor);
            Charge(at, tint, ForgeIn);

            var gem = forged.Cell >= 0 && forged.Cell < _gems.Count ? _gems[forged.Cell] : null;

            Tween.After(ForgeIn, () =>
            {
                if (_fx == null) return;

                if (gem != null && gem.Img != null)
                {
                    Charmed(gem, forged.Charm);
                    Tween.Punch(gem.Img.transform, .3f, .3f);
                }

                Flash(at, tint, nova ? 3.4f : 2.6f);
                Ripple(at, lift, Cell * (nova ? 4.6f : 3.4f), .42f, 0f, 1f);
                Twinkles(at, tint, nova ? 7 : 4, Cell * .9f);
                Burst.Sparks(_fx, at, lift, nova ? 16 : 10, Cell * 2.4f, Cell * .2f, .45f);

                // Two notes a beat apart: the forge's chime, and a rising one for what it made.
                Audio.Sfx("chime", .5f, nova ? 1.55f : 1.35f);
                Tween.After(.07f, () => Audio.SfxVaried("lit", .45f, .04f));
            }, _fx);
        }

        // ------------------------------------------------------------------ a rocket
        /// <summary>
        /// A rocket going off: it winds up, then two flares tear out of it to both ends of its
        /// line, each burning a trail that throws sparks, every cell bursting as a head passes it,
        /// and each head blowing out against the field's edge.
        /// </summary>
        void Rocketed(int cell, int colour, Color tint, bool across)
        {
            var at = CentreOf(cell);
            int ax = cell % Width, ay = cell / Width;
            int span = across ? Width : Height;

            float runs = LanceCharge + (span - 1) * WaveStep + RocketTail;
            Dilate(LancePace, runs);
            Holding(runs);
            _sprangLead = LanceCharge;

            Charge(at, tint, LanceCharge);

            for (int side = -1; side <= 1; side += 2)
            {
                int far = across ? (side < 0 ? ax : Width - 1 - ax) : (side < 0 ? ay : Height - 1 - ay);
                int edge = across ? IndexOf(side < 0 ? 0 : Width - 1, ay) : IndexOf(ax, side < 0 ? 0 : Height - 1);

                var end = CentreOf(edge);
                var heading = far > 0 ? (end - at).normalized : (across ? new Vector2(side, 0f) : new Vector2(0f, -side));
                end += heading * Cell * .55f;

                Flight(at, end, tint, LanceCharge, far * WaveStep + WaveStep, colour);

                // Every cell the head passes, bursting as it goes by - the lance's own wavefront.
                for (int k = 1; k <= far; k++)
                {
                    int x = across ? ax + side * k : ax, y = across ? ay : ay + side * k;
                    Seared(CentreOf(IndexOf(x, y)), colour, tint, LanceCharge + k * WaveStep);
                }
            }

            // Its own cell, at the moment the heads leave it.
            Seared(at, colour, tint, LanceCharge);

            Audio.SfxVaried("whoosh", .55f, .05f);
            Tween.After(LanceCharge, () => Audio.Sfx("charge", .4f, 1.45f), _fx);
        }

        /// <summary>
        /// One cell a rocket's head passes, bursting the moment it does: the colour's impact reel
        /// and shards thrown across the line. Drawn whether or not a gem stood there, for the
        /// lance's reason - the line is a fact about the field, and a gap would read as a miss.
        /// </summary>
        void Seared(Vector2 at, int colour, Color tint, float after)
        {
            Tween.After(after, () =>
            {
                if (_fx == null) return;
                Boom(at, CharmBurst(colour), Cell * 1.75f, 22f);
                Burst.Sparks(_fx, at, tint, 6, Cell * 2.2f, Cell * .2f, .38f);
            }, _fx);
        }

        /// <summary>One rocket's flight from <paramref name="from"/> to <paramref name="to"/>: a head of light, a burning trail behind it, sparks shed as it goes, a blast where it ends.</summary>
        void Flight(Vector2 from, Vector2 to, Color tint, float after, float over, int colour)
        {
            if (_fx == null) return;

            var lift = Color.Lerp(tint, Color.white, .45f);
            var host = UIKit.Node("Rocket", _fx);

            var trail = Lit("Trail", host, Art.SoftCapsule(40, 120), Pal.A(tint, 0f), Vector2.one);
            var core = Lit("Thread", host, Art.Capsule(24, 96), Pal.A(Color.white, 0f), Vector2.one);
            var flare = Lit("Flare", host, StrikeFx.Flare, Pal.A(lift, 0f), Vector2.one * Cell * 1.5f);
            var head = Lit("Head", host, Art.Glow(96, 2.2f), Pal.A(Color.white, 0f), Vector2.one * Cell * .7f);

            var span = to - from;
            float turn = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;
            float nextSpark = 0f;

            void Lay(Vector2 tip, float alpha)
            {
                var mid = (from + tip) * .5f;
                float length = Vector2.Distance(from, tip);

                foreach (var (img, wide, a) in new[] { (trail, Cell * .62f, .55f), (core, Cell * .14f, .95f) })
                {
                    if (!img) continue;
                    var rt = img.rectTransform;
                    rt.anchoredPosition = mid;
                    rt.localRotation = Quaternion.Euler(0f, 0f, turn);
                    rt.sizeDelta = new Vector2(wide, length + wide);
                    img.color = Pal.A(img == core ? Color.white : tint, a * alpha);
                }

                if (flare) { flare.rectTransform.anchoredPosition = tip; flare.color = Pal.A(lift, alpha); }
                if (head) { head.rectTransform.anchoredPosition = tip; head.color = Pal.A(Color.white, alpha); }
            }

            Lay(from, 0f);

            Tween.Run(over, Ease.Linear, t =>
            {
                if (!host) return;

                var tip = Vector2.Lerp(from, to, t);
                Lay(tip, 1f);

                if (flare) flare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 540f);
                if (head) head.rectTransform.localScale = Vector3.one * (.9f + .2f * Mathf.Sin(t * 60f));

                if (t >= nextSpark)
                {
                    nextSpark = t + .12f;
                    var back = span.sqrMagnitude > 1f ? -span.normalized : Vector2.zero;
                    var side = new Vector2(-back.y, back.x) * Random.Range(-1f, 1f);
                    Cinder(tip, Random.value < .5f ? Color.white : lift, Cell * Random.Range(.18f, .32f), .3f,
                           (back * Random.Range(.6f, 1.4f) + side) * Cell * 1.4f);
                }
            }, host).Delay(after).OnDone(() =>
            {
                if (!host) return;

                // The head blows out against the field's edge; the trail burns down behind it.
                Boom(to, CharmBurst(colour), Cell * 2.2f);
                Pop(to, lift, 1.8f, .22f);
                Burst.Sparks(_fx, to, lift, 10, Cell * 1.8f, Cell * .2f, .4f);
                ShakeBoard(Cell * .05f);
                if (flare) flare.color = Color.clear;
                if (head) head.color = Color.clear;

                Tween.Run(RocketTail, Ease.OutQuad, k =>
                {
                    if (!host) return;
                    if (trail) trail.color = Pal.A(tint, .55f * (1f - k));
                    if (core) core.color = Pal.A(Color.white, .95f * (1f - k) * (1f - k));
                    host.localScale = Vector3.one;
                }, host).OnDone(() => { if (host) Destroy(host.gameObject); });
            });

            Tween.After(after + over + RocketTail + .4f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        // ------------------------------------------------------------------ a nova
        /// <summary>
        /// A nova going off: the gem draws light in - rings closing on it, the board washed in its
        /// colour - then detonates, and lightning forks out of it into every gem of its colour, each
        /// bursting as the bolt reaches it.
        /// </summary>
        void Novaed(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);
            var lift = Color.Lerp(tint, Color.white, .5f);

            // The gems it took: every cell of its colour this beat cleared, the nova's own aside.
            var targets = new List<Vector2>(12);
            int far = 0;
            var beat = _beatNow;
            if (beat != null)
            {
                for (int i = 0; i < beat.Cleared.Count && i < beat.Paid.Count; i++)
                {
                    int c = beat.Cleared[i];
                    if (c == cell || beat.Paid[i] != colour) continue;

                    targets.Add(CentreOf(c));
                    far = Mathf.Max(far, Mathf.Max(Mathf.Abs(c % Width - cell % Width), Mathf.Abs(c / Width - cell / Width)));
                }
            }

            float runs = NovaCharge + far * WaveStep + NovaTail;
            Dilate(.2f, runs);
            Holding(runs);
            _sprangLead = NovaCharge;

            // The draw: two rings closing a beat apart and the board lit in its colour.
            Charge(at, tint, NovaCharge);
            Tween.After(.12f, () => { if (_fx != null) Charge(at, Color.white, NovaCharge - .12f); }, _fx);
            Lightup(tint, .14f, NovaCharge + .35f);
            Audio.Sfx("charge", .5f, .9f);

            Detonate(at, colour, tint, 5.2f, NovaCharge);

            Tween.After(NovaCharge, () =>
            {
                if (_fx == null) return;

                Ripple(at, lift, Cell * 7.5f, .65f, 0f, 1f);
                Ripple(at, Color.white, Cell * 5f, .5f, .08f, 1f);
                ShakeBoard(Cell * .1f);

                if (targets.Count > 0) Fork(at, targets, lift);

                Audio.Sfx("thunder", .38f, 1.25f);
                Audio.Sfx("chime", .55f, 1.6f);
            }, _fx);

            // Each gem it took bursts as its bolt arrives - the wavefront `ClearDelay` keeps the
            // gem itself on.
            if (beat == null) return;
            for (int i = 0; i < beat.Cleared.Count && i < beat.Paid.Count; i++)
            {
                int c = beat.Cleared[i];
                if (c == cell || beat.Paid[i] != colour) continue;

                int d = Mathf.Max(Mathf.Abs(c % Width - cell % Width), Mathf.Abs(c / Width - cell / Width));
                var spot = CentreOf(c);

                Tween.After(NovaCharge + d * WaveStep, () =>
                {
                    if (_fx == null) return;
                    Boom(spot, CharmBurst(colour), Cell * 1.9f);
                    Twinkles(spot, tint, 2, Cell * .4f);
                }, _fx);
            }
        }

        /// <summary>
        /// The lightning a nova forks into its colour: one fan of bolts, struck, re-struck down new
        /// channels twice, and let go on a flicker - lightning is the next frame being unrelated to
        /// the last (<c>SiegeView.Strike</c>).
        /// </summary>
        void Fork(Vector2 at, List<Vector2> targets, Color lift)
        {
            var bolt = Lightning.Grow("Nova", _fx, lift, Cell * .06f, haloAlpha: .55f);
            int seed = Random.Range(0, 1 << 20);
            bolt.Fan(at, targets, Cell, .38f, seed);

            for (int n = 1; n <= 2; n++)
            {
                int again = seed + n;
                Tween.After(n * .07f, () => { if (bolt.Host != null) bolt.Fan(at, targets, Cell, .38f, again); }, bolt.Host);
            }

            Tween.Run(.5f, Ease.Linear, t =>
            {
                if (bolt.Host == null) return;
                float held = t < .3f ? 1f : 1f - (t - .3f) / .7f;
                bolt.Group.alpha = held * (.6f + .4f * Mathf.Abs(Mathf.Sin(t * 45f)));
            }, bolt.Host).OnDone(bolt.Destroy);

            Tween.After(.6f, bolt.Destroy, bolt.Host);
        }

        // ------------------------------------------------------------------ a blast
        /// <summary>
        /// This beat's blasts: every run going off as fire along its own length, a flash and a
        /// ring and embers thrown up out of it - drawn once per run, one shake and one boom a beat.
        /// The gems it took are thrown outward and burst by the clear (<see cref="Blown"/>).
        /// </summary>
        HashSet<int> Blasting(SiegeBeat beat)
        {
            if (beat.Blasts.Count == 0) return null;

            Holding(BlastFor);

            for (int i = 0; i < beat.Blasts.Count; i++)
            {
                var run = beat.Blasts[i];
                var a = CentreOf(run.First);
                var b = CentreOf(run.Last);
                var centre = (a + b) * .5f;
                float length = Vector2.Distance(a, b) / Mathf.Max(1f, Cell) + 1f;
                var tint = TintOf(run.Colour);
                var hot = Color.Lerp(tint, Pal.Ember, .45f);

                Tween.After(BlastAt, () =>
                {
                    if (_fx == null) return;

                    Boom(centre, Blast("boom_fire"), Cell * (1.8f + .45f * length), 30f);
                    Flash(centre, hot, 2f + .3f * length);
                    Ripple(centre, Pal.Sun, Cell * (3f + .5f * length), .42f, 0f, 1f);
                    Embers(centre, hot, 6 + (int)length, Cell * 1.2f, Cell * .28f);
                }, _fx);
            }

            Tween.After(BlastAt, () =>
            {
                if (_fx == null) return;
                ShakeBoard(Cell * .07f);
                Audio.SfxVaried("boom", .34f, .06f);
            }, _fx);

            var blown = new HashSet<int>();
            for (int i = 0; i < beat.Blasted.Count; i++) blown.Add(beat.Blasted[i]);
            return blown;
        }

        /// <summary>
        /// One gem a blast took: thrown out from the blast for a moment, spinning, and burst in
        /// fire. <paramref name="late"/> is when it goes; the clear draws the burst itself.
        /// </summary>
        void Blown(Gem gem, int cell, SiegeBeat beat, float late)
        {
            var img = gem.Img;
            var at = CentreOf(cell);

            // Away from the nearest run's middle.
            var from = at;
            float best = float.MaxValue;
            for (int i = 0; i < beat.Blasts.Count; i++)
            {
                var mid = (CentreOf(beat.Blasts[i].First) + CentreOf(beat.Blasts[i].Last)) * .5f;
                float d = (mid - at).sqrMagnitude;
                if (d < best) { best = d; from = mid; }
            }

            var push = (at - from).sqrMagnitude > 1f ? (at - from).normalized : Vector2.up;

            Tween.After(BlastAt, () =>
            {
                if (!img) return;
                Tween.Move(img.rectTransform, at + push * Cell * .28f, late - BlastAt, Ease.OutQuad);
                Tween.RotateBy(img.rectTransform, Random.Range(-90f, 90f), late - BlastAt, Ease.OutQuad);
            });
        }
    }
}
