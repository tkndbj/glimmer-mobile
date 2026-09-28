using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The overcharge: a full tube, tapped and thrown.
    ///
    /// <para>
    /// <b>The one control this mode has in the middle band</b>, which is the band that used to
    /// exist only to be read. A tube fills, it starts pulsing, and the player has a decision - let
    /// it burn down as ordinary bolts, or dump the lot now at whatever is furthest down the hill.
    /// It can be wrong, which is what makes it a decision rather than a button: a tube spent on a
    /// creeper is a tube not standing ready for the brute three beats behind it.
    /// </para>
    /// <para>
    /// <b>It is drawn as its own event, in the ward's own colour, and it used to borrow the
    /// firepot's.</b> A capsule from the turret and the firepot's fireball was one vocabulary for
    /// two things and read, in the owner's words, as lame (invariant 37ev). What it is now is a
    /// <em>discharge</em>: the tube's charge gathers at the muzzle (<see cref="Gather"/>) - motes
    /// pulled in, a ring closing, arcs crackling off the barrel - and then goes as a beam with
    /// lightning wrapped round it and a bead of light running down it (<see cref="Beam"/>), and
    /// the hill is hit with the strike kit's slam in the ward's tint (<see cref="Slam"/>). Every
    /// piece of light is additive (<c>Additive</c>), which is what makes a beam read as light
    /// rather than as a highlighter line.
    /// </para>
    /// <para>
    /// <b>The rules resolve at the tap; the drawing takes a third of a second to say so.</b> The
    /// model has already hurt and killed by the time the tube is tapped, so the killed raiders
    /// are claimed (<c>_striking</c>) until the bead lands and felled then, the figures pop then,
    /// and the verdict waits for it - a victory panel over a beam still in the air is the fault
    /// the storm already answers the same way.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        /// <summary>
        /// The overcharge glyph's beat: how long the bolt takes to spring up, how often it swells
        /// once it is up, and by how much. About a beat and a half a second and a fifth of its
        /// size, which is loud enough to find from the gems without reading as an alarm.
        /// </summary>
        const float ArriveSeconds = .32f, PulseHz = 1.5f, PulseReach = .2f;

        /// <summary>Seconds between one idle arc off an armed glyph and the next, at least and at most.</summary>
        const float CrackleLeast = .30f, CrackleMost = .75f;

        /// <summary>
        /// Paints every tube's readiness: the pulse that says a tube may be spent.
        ///
        /// <para>
        /// <b>The raycast is switched with it</b>, so a tube that cannot be spent cannot be tapped
        /// at all - a control that is live and silently refuses is one nobody learns.
        /// </para>
        /// <para>
        /// <b>And "can be spent" is the board's answer, not this file's guess.</b> It read
        /// <c>ward.Armed</c> - a charge banked, not chained, not buried - which is only half the
        /// question: <c>SiegeBoard.Overcharge</c> also needs something on the hill it could hurt,
        /// and against a boss there are frames where there is not (the walk in, and a stand
        /// already resting on its floor). So the button pulsed, invited a tap and shook it off,
        /// which is what a player meets as <em>sometimes I can use it and sometimes I cannot</em>.
        /// One reading, asked here and answered there: <c>SiegeBoard.CanOvercharge</c>.
        /// </para>
        /// <para>
        /// <b>An armed glyph crackles.</b> A short arc jumps off it every half second or so
        /// (<see cref="Crackle"/>): a thing holding a charge is a thing that cannot quite hold
        /// it, and it is what says <em>overcharged</em> where a pulse alone says <em>button</em>.
        /// </para>
        /// </summary>
        void Ready()
        {
            if (_posts == null || _board == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || post.Dump == null) continue;

                var ward = _board.Wards[i];
                bool armed = _board.CanOvercharge(i);

                post.Dump.raycastTarget = armed;

                float now = Time.unscaledTime;

                if (!armed) { post.LitAt = -1f; post.NextArc = -1f; }
                else if (post.LitAt < 0f) { post.LitAt = now; post.NextArc = now + ArriveSeconds; }

                float age = armed ? now - post.LitAt : 0f;

                // **It arrives, then it beats.** The bolt springs up from nothing with an
                // overshoot, and from then on swells by a fifth and settles, never dimming: the
                // owner's note on the glyph before this one was that it could not be seen, and an
                // alpha pulse spends half of every beat being harder to see. What breathes is the
                // size and the light behind it, on a cosine timed from the arrival so every bolt
                // opens its beat at rest rather than mid-swell.
                float arrive = armed ? Ease.OutBack(Mathf.Clamp01(age / ArriveSeconds)) : 0f;
                float swell = armed ? .5f - .5f * Mathf.Cos(age * PulseHz * 2f * Mathf.PI) : 0f;

                // **White, because the glyph carries its own colour.** Tinting it would be the
                // multiply invariant 37l records - `Image.color` can only ever darken, so a
                // coloured badge asked to look *lit* comes out muddy.
                post.Dump.color = Pal.A(Color.white, armed ? 1f : 0f);
                post.Dump.rectTransform.localScale = Vector3.one * (arrive * (1f + swell * PulseReach));

                if (post.Halo != null)
                {
                    post.Halo.color = Pal.A(Pal.Lift(TintOf(ward.Colour), .35f),
                                            Mathf.Clamp01(arrive) * (.45f + swell * .5f));
                    post.Halo.rectTransform.localScale = Vector3.one * (arrive * (.9f + swell * .45f));
                }

                if (armed && post.NextArc >= 0f && now >= post.NextArc)
                {
                    Crackle(new Vector2(PostX(i), _lineY + ChargeY), TintOf(ward.Colour), Cell * .62f, .14f);
                    post.NextArc = now + Random.Range(CrackleLeast, CrackleMost);
                }

                // **How many are held, and only once there is more than one.** A badge saying "1"
                // on every armed tube is a number nobody reads; a "2" is the one moment the count
                // is news, because it is the moment a third would be thrown away.
                if (post.Held == null) continue;

                bool many = ward.Charges > 1;

                post.Held.enabled = many;
                if (post.Pip != null) post.Pip.enabled = many;

                if (many) post.Held.text = ward.Charges.ToString();
            }
        }

        /// <summary>
        /// One short arc jumping between two points on a circle round <paramref name="at"/>,
        /// alive for <paramref name="life"/> and gone. What an armed glyph does while it waits,
        /// and what the barrel does while a charge gathers.
        /// </summary>
        void Crackle(Vector2 at, Color tint, float radius, float life)
        {
            if (_fx == null) return;

            float a = Random.Range(0f, Mathf.PI * 2f);
            float b = a + Random.Range(1.2f, 2.6f) * (Random.value < .5f ? -1f : 1f);

            var from = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .7f) * radius;
            var to = at + new Vector2(Mathf.Cos(b), Mathf.Sin(b) * .7f) * radius;

            var arc = Lightning.Grow("Crackle", _fx, tint, Cell * .05f, haloAlpha: .35f);
            arc.Strike(from, to, Cell, .3f, 1, Random.Range(0, 1 << 20));

            float phase = Random.Range(0f, 40f);
            Tween.Run(life, Ease.Linear, t =>
            {
                if (arc.Host == null) return;
                arc.Group.alpha = (1f - t) * (.6f + .4f * Mathf.Abs(Mathf.Sin(t * 50f + phase)));
            }, arc.Host).OnDone(arc.Destroy);
        }

        // ------------------------------------------------------------------ the discharge
        /// <summary>Seconds the charge gathers at the muzzle before it goes.</summary>
        const float GatherFor = .17f;

        /// <summary>Seconds the bead of light takes to run the beam to its target.</summary>
        const float BeamFlight = .09f;

        /// <summary>Seconds the beam stands at full after the bead has landed, before it collapses.</summary>
        const float DischargeHold = .10f;

        /// <summary>Seconds the beam takes to collapse to nothing.</summary>
        const float DischargeFade = .22f;

        /// <summary>The beam's core width, in cells; its sheath and glow are multiples.</summary>
        const float BeamCore = .16f;

        /// <summary>
        /// A tube tapped: one banked charge goes at once.
        ///
        /// <para>
        /// <b>It charges the run nothing</b>, and that is arithmetic rather than generosity. What
        /// an overcharge delivers is exactly what the tube would have delivered as ordinary bolts
        /// - the same fuel, the same weight, landing as an own-colour hit does - so the player has
        /// moved damage they had already matched for rather than conjured any, and invariant 39's
        /// exchange rate has nothing to price. See <c>SiegeBoard.Overcharge</c>.
        /// </para>
        /// </summary>
        void Unleashed(int ward)
        {
            if (!Tappable || _board == null) return;

            HideCoach();
            Stir();

            _strikes.Clear();
            var blast = _board.Overcharge(ward, _strikes);

            if (!blast.Landed)
            {
                var post = ward >= 0 && ward < _posts.Length ? _posts[ward] : null;
                if (post != null && post.Dump != null) Refuse(post.Dump.rectTransform);

                Rejected?.Invoke();
                return;
            }

            // **The killed are claimed until the bead lands.** The rules have already killed
            // them; `Reap` would take their bodies down this frame, a third of a second before
            // anything visibly reaches them. See `_striking`, which the storm uses the same way.
            var hits = new List<SiegeStrike>(_strikes);
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Killed && !_striking.Contains(hits[i].Raider)) _striking.Add(hits[i].Raider);

            Felling(GatherFor + BeamFlight + DyingFor);
            Discharge(blast, hits);

            Changed?.Invoke();
        }

        /// <summary>The whole event, in order: gather, beam, slam, and only then the reckoning.</summary>
        void Discharge(SiegeUnleash blast, List<SiegeStrike> hits)
        {
            var post = blast.Ward >= 0 && blast.Ward < _posts.Length ? _posts[blast.Ward] : null;
            if (post == null || _fx == null) { Reckon(hits); return; }

            var tint = TintOf(_board.Wards[blast.Ward].Colour);
            var muzzle = new Vector2(PostX(blast.Ward), _lineY + Cell * 1.0f);
            var target = BoxAt(blast.Lane, blast.Row);
            var aim = SiegeAim.OnTheHill(blast.Lane, blast.Row);

            Gather(post, muzzle, tint);

            Tween.After(GatherFor, () =>
            {
                if (_fx == null) return;
                Beam(post, muzzle, target, tint);
            }, this);

            Tween.After(GatherFor + BeamFlight, () =>
            {
                if (_fx == null) return;
                Slam(target, tint, aim);
                Reckon(hits);
            }, this);
        }

        /// <summary>The figures, the bodies and the verdict - once the bead has landed.</summary>
        void Reckon(List<SiegeStrike> hits)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                Hurt(hits[i]);
                if (hits[i].Killed) Fell(hits[i].Raider);
                _striking.Remove(hits[i].Raider);
            }

            Reap();
            Judge();
        }

        /// <summary>
        /// The charge gathering at the muzzle: motes pulled in, a ring closing on the barrel, the
        /// chassis lighting, arcs crackling off it, and a rising whine.
        /// </summary>
        void Gather(Post post, Vector2 muzzle, Color tint)
        {
            var host = UIKit.Node("Gather", _fx);
            var bright = Color.Lerp(tint, Color.white, .5f);

            // The light on the chassis, swelling.
            var glow = Lit("Glow", host, Art.Glow(128, 2.0f), Pal.A(tint, 0f), Vector2.one * Cell * 2.2f);
            glow.rectTransform.anchoredPosition = muzzle;
            Tween.Run(GatherFor, Ease.InQuad, t =>
            {
                if (!glow) return;
                glow.color = Pal.A(tint, .9f * t);
                glow.transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, t);
            }, glow);

            // A ring closing on the barrel.
            var ring = Lit("Close", host, StrikeFx.Ring, Pal.A(bright, 0f),
                           new Vector2(Cell * 2.6f, Cell * 2.6f * .6f));
            if (ring != null)
            {
                ring.rectTransform.anchoredPosition = muzzle;
                Tween.Run(GatherFor, Ease.InQuad, t =>
                {
                    if (!ring) return;
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(1f, .12f, t);
                    ring.color = Pal.A(bright, Mathf.Min(1f, t * 3f));
                }, ring);
            }

            // Motes drawn in from all round, shrinking as they arrive.
            var star = Kit(StrikeFx.Star) ?? Art.Spark(64);
            for (int i = 0; i < 12; i++)
            {
                float ang = (i / 12f) * Mathf.PI * 2f + Random.Range(-.25f, .25f);
                var from = muzzle + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * .7f) * Cell * Random.Range(1.3f, 1.9f);
                float size = Cell * Random.Range(.28f, .5f);
                float delay = Random.Range(0f, GatherFor * .35f);
                float over = GatherFor - delay;

                var mote = Lit("m", host, star, Pal.A(bright, 0f), new Vector2(size, size));
                var rt = mote.rectTransform;
                rt.anchoredPosition = from;
                Tween.Run(over, Ease.InCubic, t =>
                {
                    if (!mote) return;
                    rt.anchoredPosition = Vector2.Lerp(from, muzzle, t);
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, .25f, t);
                    mote.color = Pal.A(bright, Mathf.Min(1f, t * 4f));
                }, mote).Delay(delay);
            }

            // Arcs off the barrel while it gathers - three, on their own beats.
            for (int i = 0; i < 3; i++)
                Tween.After(i * GatherFor * .3f, () => Crackle(muzzle, tint, Cell * .55f, .1f), host);

            // The turret braces - and is put back, because `Punch` takes whatever scale it
            // finds as the rest it returns to.
            var node = post.Node;
            Tween.Run(GatherFor, Ease.InQuad, t =>
            {
                if (node) node.localScale = Vector3.one * (1f + .06f * t);
            }, node, "brace").OnDone(() => { if (node) node.localScale = Vector3.one; });

            Audio.Sfx("charge", .8f);

            Tween.After(GatherFor + .02f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        /// <summary>
        /// The beam out of the turret that spent itself.
        ///
        /// <para>
        /// <b>Drawn from the muzzle to the box rather than to the raider</b>, because the raider
        /// it was aimed at is very often dead by the time this runs - and a beam that ends where
        /// the blast is is the same fact said twice, which is what makes the pair read as one
        /// event.
        /// </para>
        /// <para>
        /// <b>Three layers, a bead and two arcs.</b> The layers are the strike's: a wide soft
        /// glow, a sheath in the ward's colour and a near-white core, all additive, so where they
        /// stack the beam goes white down its middle. The bead is a flare running muzzle to
        /// target in <see cref="BeamFlight"/>, which is what says the energy is <em>travelling</em>
        /// - a beam that simply appears is a line. And the arcs are lightning wrapped round the
        /// beam, rebuilt every few hundredths while it stands, which is what makes it electricity
        /// rather than paint.
        /// </para>
        /// </summary>
        void Beam(Post post, Vector2 from, Vector2 to, Color tint)
        {
            var span = to - from;
            float length = span.magnitude;
            if (length <= 1f) return;

            var host = UIKit.Node("Beam", _fx);
            float angle = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;
            var bright = Color.Lerp(tint, Color.white, .5f);

            // The three bars, pivoted at the muzzle so they can open along their length.
            Image Bar(string name, Sprite sprite, Color colour, float wide)
            {
                var bar = Lit(name, host, sprite, colour, new Vector2(wide, length));
                var rt = bar.rectTransform;
                rt.pivot = new Vector2(.5f, 0f);
                rt.anchoredPosition = from;
                rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                rt.localScale = new Vector3(1f, 0f, 1f);
                return bar;
            }

            var glow = Bar("glow", Art.SoftCapsule(40, 120), Pal.A(tint, .7f), Cell * BeamCore * 6.5f);
            var sheath = Bar("sheath", Art.Capsule(24, 96), Pal.A(tint, .95f), Cell * BeamCore * 2.4f);
            var core = Bar("core", Art.Capsule(24, 96), Pal.A(Color.Lerp(tint, Color.white, .85f), 1f), Cell * BeamCore);

            // Open along the length in the bead's own time, hold, then collapse across.
            float total = BeamFlight + DischargeHold + DischargeFade;
            Tween.Run(total, Ease.Linear, t =>
            {
                if (!host) return;
                float s = t * total;
                float open = Ease.OutQuad(Mathf.Clamp01(s / BeamFlight));
                float close = s <= BeamFlight + DischargeHold ? 0f
                            : Ease.InQuad(Mathf.Clamp01((s - BeamFlight - DischargeHold) / DischargeFade));
                float across = 1f - close;

                var scale = new Vector3(across, open, 1f);
                if (glow) glow.rectTransform.localScale = scale;
                if (sheath) sheath.rectTransform.localScale = scale;
                if (core) core.rectTransform.localScale = scale;

                if (glow) glow.color = Pal.A(tint, .7f * (1f - close * close));
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            // The bead.
            var bead = Lit("Bead", host, StrikeFx.Flare, Pal.A(bright, 1f), Vector2.one * Cell * 1.3f);
            if (bead != null)
            {
                var rt = bead.rectTransform;
                rt.anchoredPosition = from;
                Tween.Run(BeamFlight, Ease.InQuad, t =>
                {
                    if (!bead) return;
                    rt.anchoredPosition = Vector2.Lerp(from, to, t);
                    rt.localRotation = Quaternion.Euler(0f, 0f, t * 140f);
                }, bead).OnDone(() => { if (bead) Destroy(bead.gameObject); });
            }

            // Lightning wrapped round the beam, rebuilt while it stands.
            for (int i = 0; i < 2; i++)
            {
                var arc = Lightning.Grow("Wrap", host, tint, Cell * .055f, haloAlpha: .3f);
                int seed = Random.Range(0, 1 << 20);
                float standing = BeamFlight + DischargeHold + DischargeFade * .5f;
                int beats = Mathf.CeilToInt(standing / .045f);

                for (int b = 0; b < beats; b++)
                {
                    int n = b;
                    Tween.After(b * .045f, () =>
                    {
                        if (arc.Host == null) return;
                        arc.Strike(from, to, Cell, .28f, 1, seed + n);
                        arc.Group.alpha = 1f - Mathf.Clamp01((n * .045f - BeamFlight - DischargeHold) / (DischargeFade * .5f));
                    }, host);
                }
            }

            // The muzzle: a flare at the barrel and a streak across it.
            Flash(from, tint, 2.1f);

            Tween.Punch(post.Node, .22f, .3f);
            ShakeBoard(16f);
            Audio.SfxVaried("arc", .7f, .08f);
        }

        /// <summary>
        /// The slam: the strike kit's impact in the ward's tint, the hill lit, the boxes the
        /// blast reached scorched (invariant 33g, as the firepot does), and a hit-stop.
        /// </summary>
        void Slam(Vector2 at, Color tint, SiegeAim aim)
        {
            Scorch(aim, tint);
            Lightup(tint, .26f, .3f);

            Flash(at, tint, 3.2f);
            Ripple(at, tint, Cell * 4.6f, .46f, 0f);
            Ripple(at, tint, Cell * 3.1f, .38f, .07f);
            // The splat a step under the crack's alpha: a spiked white splash three cells wide,
            // added over a lit hill, is a blown-out disc at full - the render mirror said so.
            Answer(at, tint, 2.8f, StrikeFx.Splat, 3.1f, markAlpha: .78f);
            Embers(at, tint, 22, Cell * 2.6f, Cell * .36f);
            Twinkles(at, tint, 6, Cell * 1.6f);

            // A hit-stop: the hill held for a few frames while the slam lands, which is what
            // every fighting game does with a heavy blow and is the cheapest weight there is.
            Dilate(.45f, .14f);
            ShakeBoard(26f);

            Audio.Sfx("boom", .78f, 1.06f);
        }
    }
}
