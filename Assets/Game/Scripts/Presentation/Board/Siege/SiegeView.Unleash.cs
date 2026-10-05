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
    /// <b>It is drawn as its own event, in the ward's own colour, and it is electricity and
    /// nothing else</b> (invariant 37ev). It borrowed the firepot's fireball first, and then it
    /// was a beam that landed as a splat on the floor; what it is now is a <em>discharge</em>:
    /// the tube's charge gathers at the muzzle (<see cref="Gather"/>) - motes pulled in, a ring
    /// closing, arcs crackling off the barrel - goes as a channel of lightning that is re-struck
    /// for as long as it stands (<see cref="Channel"/>), and lands as a burst of arcs with a
    /// fork to every body it hurt (<see cref="Arcburst"/>). Every piece of it is additive
    /// (<c>Additive</c>), every piece is in the air, and none of it is left on the ground.
    /// </para>
    /// <para>
    /// <b>The rules resolve at the tap; the drawing takes a third of a second to say so.</b> The
    /// model has already hurt and killed by the time the tube is tapped, so the killed raiders
    /// are claimed (<c>_striking</c>) until the channel lands and felled then, the figures pop
    /// then, and the verdict waits for it - a victory panel over a bolt still in the air is the
    /// fault the storm already answers the same way.
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
        /// Seconds a glyph takes to settle when the hill has nothing for it and to wake when it
        /// has. Short enough to read as the same frame the hill changed on, long enough that a
        /// hill emptying and filling inside a second is not a blink.
        /// </summary>
        const float SettleSeconds = .16f;

        /// <summary>
        /// What a held glyph with nothing to throw at is drawn at: its share of the live size and
        /// of the live light. Plainly still there and plainly not asking to be pressed.
        /// </summary>
        const float RestScale = .86f, RestAlpha = .6f, RestHalo = .3f;

        /// <summary>How much faster a kept tap beats than an ordinary armed glyph, and how much oftener it arcs.</summary>
        const float KeptBeat = 2f;

        /// <summary>
        /// Paints every tube's readiness: the glyph that says a charge is held, and the pulse
        /// that says a tap would throw it.
        ///
        /// <para>
        /// <b>Two readings, and they used to be one.</b> Whether the glyph is <em>there</em> is
        /// the tube's answer (<c>SiegeBoard.Charged</c>): a charge the player banked is theirs
        /// until they spend it. Whether it is <em>live</em> is the hill's
        /// (<c>SiegeBoard.CanOvercharge</c>): there is something a throw would hurt. Drawing
        /// both off the hill's answer took the key off the turret on every frame the hill had
        /// nothing to hurt - between two waves, on a boss's walk in - and brought it back with
        /// its entrance a moment later, which came back from
        /// play as <em>the overcharge icon disappears and appears</em>.
        /// </para>
        /// <para>
        /// <b>A held glyph always answers a tap, and the three answers are the board's.</b>
        /// Thrown when it can land; kept when a boss is still walking on
        /// (<see cref="Keeping"/>); and shaken off, with the screen's own sentence, over a hill
        /// with nothing on it. The raycast is on for as long as the glyph is drawn, so no tap is
        /// ever lost to the frame the answer changed on.
        /// </para>
        /// <para>
        /// <b>An armed glyph crackles.</b> A short arc jumps off it every half second or so
        /// (<see cref="Crackle"/>): a thing holding a charge is a thing that cannot quite hold
        /// it, and it is what says <em>overcharged</em> where a pulse alone says <em>button</em>.
        /// A resting one does neither, which is the whole of how the two are told apart.
        /// </para>
        /// </summary>
        void Ready()
        {
            if (_posts == null || _board == null) return;

            float now = Time.unscaledTime;
            float step = Time.unscaledDeltaTime / SettleSeconds;

            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                if (post == null || post.Dump == null) continue;

                var ward = _board.Wards[i];

                bool held = _board.Charged(i);
                if (!held) post.Kept = false;

                // **A kept tap is drawn live**, because it is the one state in which the player
                // has already said *now* and the board is the thing that is waiting.
                bool live = held && (post.Kept || _board.CanOvercharge(i));

                post.Dump.raycastTarget = held;

                if (!held)
                {
                    post.LitAt = -1f;
                    post.NextArc = -1f;
                    post.Live = 0f;
                }
                else if (post.LitAt < 0f)
                {
                    // Arrives in whichever state it is in, rather than waking into it: the
                    // entrance is the event, and a second ease on top of it would smear it.
                    post.LitAt = now;
                    post.NextArc = now + ArriveSeconds;
                    post.Live = live ? 1f : 0f;
                }

                post.Live = Mathf.MoveTowards(post.Live, live ? 1f : 0f, step);

                float age = held ? now - post.LitAt : 0f;
                float beat = post.Kept ? PulseHz * KeptBeat : PulseHz;

                // **It arrives, then it beats.** The bolt springs up from nothing with an
                // overshoot, and from then on swells by a fifth and settles, never dimming: the
                // owner's note on the glyph before this one was that it could not be seen, and an
                // alpha pulse spends half of every beat being harder to see. What breathes is the
                // size and the light behind it, on a cosine timed from the arrival so every bolt
                // opens its beat at rest rather than mid-swell.
                float arrive = held ? Ease.OutBack(Mathf.Clamp01(age / ArriveSeconds)) : 0f;
                float swell = held ? (.5f - .5f * Mathf.Cos(age * beat * 2f * Mathf.PI)) * post.Live : 0f;

                // **White, because the glyph carries its own colour.** Tinting it would be the
                // multiply invariant 37l records - `Image.color` can only ever darken, so a
                // coloured badge asked to look *lit* comes out muddy.
                post.Dump.color = Pal.A(Color.white, held ? Mathf.Lerp(RestAlpha, 1f, post.Live) : 0f);
                post.Dump.rectTransform.localScale =
                    Vector3.one * (arrive * (Mathf.Lerp(RestScale, 1f, post.Live) + swell * PulseReach));

                if (post.Halo != null)
                {
                    post.Halo.color = Pal.A(Pal.Lift(TintOf(ward.Colour), .35f),
                                            Mathf.Clamp01(arrive) * (.45f + swell * .5f)
                                            * Mathf.Lerp(RestHalo, 1f, post.Live));
                    post.Halo.rectTransform.localScale = Vector3.one * (arrive * (.9f + swell * .45f));
                }

                if (live && post.NextArc >= 0f && now >= post.NextArc)
                {
                    Crackle(new Vector2(PostX(i), _lineY + ChargeY), TintOf(ward.Colour), Cell * .62f, .14f);
                    post.NextArc = now + Random.Range(CrackleLeast, CrackleMost) / (post.Kept ? KeptBeat : 1f);
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
        /// Throws every kept tap the hill can now take, and lets go of any it never will.
        ///
        /// <para>
        /// <b>The first frame, and exactly the throw a tap on that frame would have been.</b> A
        /// kept tap goes through <see cref="Throw"/>, which is the door a live tap goes through,
        /// so there is one overcharge in this mode and it is drawn, claimed, felled and judged
        /// one way. Called once a frame after the step has been drawn, which is where a tap from
        /// the event system lands too: between two steps, never inside one.
        /// </para>
        /// <para>
        /// <b>Let go rather than carried when the boss is gone.</b> A tap kept against a boss
        /// that the rest of the line has since finished would otherwise go at the first raider
        /// of the next wave, seconds after it was asked for - see <c>SiegeBoard.CanHold</c>. A
        /// charge drained, chained or buried lets go in <see cref="Ready"/>, which runs on a
        /// held board too.
        /// </para>
        /// </summary>
        void Keeping()
        {
            if (_posts == null || _board == null) return;

            for (int i = 0; i < _posts.Length && Tappable; i++)
            {
                var post = _posts[i];
                if (post == null || !post.Kept) continue;

                if (_board.CanOvercharge(i))
                {
                    post.Kept = false;
                    Throw(i);
                    continue;
                }

                if (!_board.CanHold(i)) post.Kept = false;
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

        /// <summary>
        /// Seconds the leader takes to run from the muzzle to its target. The name is the beam's,
        /// kept because the tutorial and the felling clock are timed off it.
        /// </summary>
        const float BeamFlight = .09f;

        /// <summary>Seconds the channel stands lit after it lands, re-struck, before it is let go.</summary>
        const float DischargeHold = .20f;

        /// <summary>Seconds the channel takes to die, on a flicker.</summary>
        const float DischargeFade = .26f;

        /// <summary>Seconds between one re-strike of a standing channel and the next.</summary>
        const float DischargeBeat = .045f;

        /// <summary>The channel's core width, in cells; its sheath and halo are multiples (<c>Lightning.Grow</c>).</summary>
        const float DischargeCore = .125f;

        /// <summary>How far a joint of the channel may wander sideways, in cells, and how many branches it throws.</summary>
        const float DischargeJag = .36f;
        const int DischargeForks = 3;

        /// <summary>How many arms the burst throws where it lands, how far they reach in cells, and how wide their core is.</summary>
        const int NovaArms = 10;
        const float NovaReach = 2.5f, NovaCore = .06f;

        /// <summary>When the burst is re-dealt, in seconds after it lands. Each is a new set of arms.</summary>
        static readonly float[] NovaBeats = { 0f, .05f, .105f, .17f };

        /// <summary>Seconds the burst and the forks take to die after their last re-deal.</summary>
        const float NovaFade = .24f;

        /// <summary>The core width of a fork from the landing to a struck body, in cells.</summary>
        const float ForkCore = .075f;

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

            var post = _posts != null && ward >= 0 && ward < _posts.Length ? _posts[ward] : null;

            if (_board.CanOvercharge(ward))
            {
                if (post != null) post.Kept = false;
                Throw(ward);
                return;
            }

            // **A boss still walking on: the tap is kept, and said to be.** The glyph quickens
            // and arcs (`Ready`), and `Keeping` throws it on the frame the boss plants. A second
            // tap in the walk is the same answer; every tap after it plants is its own throw.
            if (post != null && _board.CanHold(ward))
            {
                if (!post.Kept) Audio.Sfx("charge", .5f, 1.25f);

                post.Kept = true;
                Crackle(new Vector2(PostX(ward), _lineY + ChargeY),
                        TintOf(_board.Wards[ward].Colour), Cell * .7f, .16f);
                return;
            }

            if (post != null)
            {
                post.Kept = false;
                if (post.Dump != null) Refuse(post.Dump.rectTransform);
            }

            Rejected?.Invoke();
        }

        /// <summary>
        /// One banked charge thrown and drawn. The one door: a live tap and a kept one
        /// (<see cref="Keeping"/>) both come through here.
        /// </summary>
        void Throw(int ward)
        {
            _strikes.Clear();
            var blast = _board.Overcharge(ward, _strikes);

            // `CanOvercharge` is this call's own two refusals, asked a line ago by both callers,
            // so this is unreachable - and kept, because a throw drawn for a charge the board
            // did not spend would be the one way this file could invent damage.
            if (!blast.Landed) return;

            // **The killed are claimed until the channel lands.** The rules have already killed
            // them; `Reap` would take their bodies down this frame, a third of a second before
            // anything visibly reaches them. See `_striking`, which the storm uses the same way.
            var hits = new List<SiegeStrike>(_strikes);
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Killed && !_striking.Contains(hits[i].Raider)) _striking.Add(hits[i].Raider);

            Felling(GatherFor + BeamFlight + DyingFor);
            Discharge(blast, hits);

            Changed?.Invoke();
        }

        /// <summary>The whole event, in order: gather, channel, burst, and only then the reckoning.</summary>
        void Discharge(SiegeUnleash blast, List<SiegeStrike> hits)
        {
            var post = blast.Ward >= 0 && blast.Ward < _posts.Length ? _posts[blast.Ward] : null;
            if (post == null || _fx == null) { Reckon(hits); return; }

            var tint = TintOf(_board.Wards[blast.Ward].Colour);
            var muzzle = new Vector2(PostX(blast.Ward), _lineY + Cell * 1.0f);
            var target = BoxAt(blast.Lane, blast.Row);
            int seed = Random.Range(0, 1 << 20);

            Gather(post, muzzle, tint);

            Tween.After(GatherFor, () =>
            {
                if (_fx == null) return;
                Channel(post, muzzle, target, tint, seed);
            }, this);

            Tween.After(GatherFor + BeamFlight, () =>
            {
                if (_fx == null) return;
                Arcburst(target, tint, Struck(hits), seed);
                Reckon(hits);
            }, this);
        }

        /// <summary>
        /// Where every body the blast hurt is standing, once each, on the frame it lands.
        ///
        /// <b>Asked before <see cref="Reckon"/> fells anything</b>, which is why the killed are
        /// still there to be asked about (<c>_striking</c>): a fork drawn to where a raider
        /// <em>was</em> is a fork into empty ground.
        /// </summary>
        List<Vector2> Struck(List<SiegeStrike> hits)
        {
            var at = new List<Vector2>(hits.Count);
            var seen = new HashSet<int>();

            for (int i = 0; i < hits.Count; i++)
            {
                if (!seen.Add(hits[i].Raider)) continue;

                var mob = MobOf(hits[i].Raider);
                if (mob == null || mob.Node == null) continue;

                at.Add(mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .25f));
            }

            return at;
        }

        /// <summary>The figures, the bodies and the verdict - once the channel has landed.</summary>
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
        /// The channel out of the turret that spent itself: lightning, and nothing that is not.
        ///
        /// <para>
        /// <b>It was a beam - three straight bars with arcs wrapped round them - and a beam is a
        /// laser.</b> What the owner asked for is "pure electricity" (2026-09-29), and the
        /// difference between the two is the one <c>SiegeView.Strike</c> already records: a
        /// thing that stands still and is switched off is a beam, and a thing that is
        /// <em>replaced</em> every few hundredths is lightning. So this is the stormcall's
        /// grammar turned on its side - a dim leader finding its way from the muzzle
        /// (<see cref="BeamFlight"/>), the return stroke lighting the channel white with its
        /// forks, and the channel re-struck down a new path every <see cref="DischargeBeat"/>
        /// for as long as it stands, then let go on a flicker.
        /// </para>
        /// <para>
        /// <b>Drawn from the muzzle to the box rather than to the raider</b>, because the raider
        /// it was aimed at is very often dead by the time this runs - and a channel that ends
        /// where the burst is is the same fact said twice, which is what makes the pair read as
        /// one event. Its last joint <em>is</em> the box (<c>Lightning.Joints</c>).
        /// </para>
        /// <para>
        /// <b>Heavier than a stormcall's bolt by half</b> (<see cref="DischargeCore"/>), with the
        /// pack's painted strand laid along it (<see cref="Strand"/>) for the tendrils a drawn
        /// trunk does not have: one bolt of nine is weather, and this is the whole of a tube.
        /// </para>
        /// </summary>
        void Channel(Post post, Vector2 from, Vector2 to, Color tint, int seed)
        {
            if ((to - from).sqrMagnitude <= 1f) return;

            var bright = Color.Lerp(tint, Color.white, .5f);

            var bolt = Lightning.Grow("Discharge", _fx, tint, Cell * DischargeCore, haloAlpha: .6f);
            bolt.Strike(from, to, Cell, DischargeJag, 0, seed);
            bolt.Reveal = 0f;
            bolt.Group.alpha = .5f;

            // The leader, with a bead of light on its head: the charge finding its way.
            var bead = Lit("Bead", _fx, StrikeFx.Flare, Pal.A(bright, 1f), Vector2.one * Cell * 1.2f);

            Tween.Run(BeamFlight, Ease.InQuad, t =>
            {
                if (bolt.Host == null) return;

                bolt.Reveal = t;
                bolt.Group.alpha = .5f + .25f * Mathf.Abs(Mathf.Sin(t * 60f));

                if (!bead) return;
                bead.rectTransform.anchoredPosition = Vector2.Lerp(from, to, t);
                bead.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 140f);
            }, bolt.Host).OnDone(() =>
            {
                if (bead) Destroy(bead.gameObject);
                Standing(bolt, from, to, tint, seed);
            });

            // The muzzle: a flare at the barrel and a streak across it.
            Flash(from, tint, 2.1f);

            Tween.Punch(post.Node, .22f, .3f);
            ShakeBoard(16f);
            Audio.SfxVaried("arc", .7f, .08f);
        }

        /// <summary>The channel once it has landed: lit white, re-struck while it stands, and let go on a flicker.</summary>
        void Standing(Lightning.Layered bolt, Vector2 from, Vector2 to, Color tint, int seed)
        {
            if (bolt.Host == null) return;

            bolt.Reveal = 1f;
            bolt.Strike(from, to, Cell, DischargeJag, DischargeForks, seed + 1);
            bolt.Group.alpha = 1f;

            var strand = Strand(_fx, from, to, tint, seed);

            // **Re-struck, never animated.** A new channel every beat, the strand turned over
            // with it: a bolt that moves is a snake and a bolt that is replaced is lightning.
            int beats = Mathf.FloorToInt(DischargeHold / DischargeBeat);

            for (int i = 1; i <= beats; i++)
            {
                int n = i;
                Tween.After(n * DischargeBeat, () =>
                {
                    if (bolt.Host == null) return;

                    bolt.Strike(from, to, Cell, DischargeJag, DischargeForks - (n & 1), seed + 1 + n);
                    bolt.Group.alpha = 1f;

                    if (strand == null) return;
                    var s = strand.rectTransform.localScale;
                    strand.rectTransform.localScale = new Vector3(-s.x, s.y, 1f);
                }, bolt.Host);
            }

            float total = DischargeHold + DischargeFade;
            float phase = Random.Range(0f, 40f);

            Tween.Run(total, Ease.Linear, t =>
            {
                if (bolt.Host == null) return;

                float s = t * total;
                float a = s < DischargeHold
                        ? Mathf.Max(bolt.Group.alpha * .94f, .72f)
                        : Mathf.Pow(1f - (s - DischargeHold) / DischargeFade, 1.4f)
                          * (.55f + .45f * Mathf.Abs(Mathf.Sin(s * 38f + phase)));

                bolt.Group.alpha = a;
                if (strand != null) strand.color = Pal.A(tint, StrandAlpha * a);
            }, bolt.Host).OnDone(() =>
            {
                bolt.Destroy();
                if (strand) Destroy(strand.gameObject);
            });
        }

        /// <summary>
        /// The burst where the discharge lands: a flash, a shock in the air, arcs thrown out in
        /// every direction, a fork to every body the blast hurt, and sparks.
        ///
        /// <para>
        /// <b>Nothing is left on the ground, and nothing is drawn on it.</b> It used to land as
        /// the strike kit's slam - a white splat that lingered a second, a warm glow under it,
        /// the blast's boxes scorched in the tint - and the splat came back from the owner as
        /// something nobody wants described on a game board (2026-09-29): "it shouldn't leave a
        /// mark on the floor, it should just look like a lightning". So every piece here is
        /// light in the air, gone inside half a second, and the rings are <em>round</em> -
        /// a ring squashed to the hill (<see cref="GroundSquash"/>) is a ring lying on the
        /// floor, which is the one place this may not draw.
        /// </para>
        /// <para>
        /// <b>The forks are what the scorched boxes were for.</b> A blast takes the box it was
        /// thrown at and the four touching it, and the panes said so (invariant 33g, asked of
        /// the feedback). A fork from the landing to each body the blast hurt says the same
        /// thing about the only part of it a player cares for - <em>who</em> - and says it as
        /// electricity: each is a trunk, so each ends on the body it struck
        /// (<c>Lightning.Spread</c>, held by <c>SiegeStrikeTests</c>).
        /// </para>
        /// <para>
        /// <b>One mesh for the arms and one for the forks</b>, whatever the count
        /// (<c>Lightning.Nova</c>, <c>.Fan</c>): ten arms and nine forks as objects of their own
        /// would be fifty-seven graphics for a quarter of a second of light.
        /// </para>
        /// </summary>
        void Arcburst(Vector2 at, Color tint, List<Vector2> struck, int seed)
        {
            Lightup(tint, .26f, .3f);

            // The bloom under everything: what the air does round a discharge. In the tint and
            // under three quarters, because the flare over it is already white in its middle -
            // a white bloom under a white flare is a disc, and the mirror said so.
            var bloom = Lit("Bloom", _fx, Art.Glow(128, 1.7f), Pal.A(tint, .72f), Vector2.one * Cell * 3.0f);
            bloom.rectTransform.anchoredPosition = at;
            Tween.Run(.34f, Ease.OutQuad, t =>
            {
                if (!bloom) return;
                bloom.rectTransform.localScale = Vector3.one * Mathf.Lerp(.35f, 1.15f, Ease.OutQuint(Mathf.Clamp01(t / .4f)));
                bloom.color = Pal.A(tint, .72f * (1f - t) * (1f - t));
            }, bloom).OnDone(() => { if (bloom) Destroy(bloom.gameObject); });

            Flash(at, tint, 3.0f);

            // The shock, in the air: round, because a squashed ring lies on the floor.
            Ripple(at, tint, Cell * 5.0f, .42f, 0f, 1f);
            Ripple(at, tint, Cell * 3.3f, .36f, .06f, 1f);

            // The arms and the forks, re-dealt on the same beats.
            var nova = Lightning.Grow("Nova", _fx, tint, Cell * NovaCore / Lightning.BranchWidth, haloAlpha: .5f);
            var forks = struck.Count > 0 ? Lightning.Grow("Forks", _fx, tint, Cell * ForkCore, haloAlpha: .55f) : null;

            for (int i = 0; i < NovaBeats.Length; i++)
            {
                int n = i;
                Tween.After(NovaBeats[i], () =>
                {
                    if (nova.Host != null)
                    {
                        // Fewer and shorter each time: the charge is running out.
                        float spent = n / (float)NovaBeats.Length;
                        nova.Nova(at, Cell * .18f, Cell * NovaReach * (1f - .35f * spent),
                                  NovaArms - n * 2, Cell, .42f, seed + 101 + n);
                        nova.Group.alpha = 1f - .2f * spent;
                    }

                    if (forks != null && forks.Host != null)
                    {
                        forks.Fan(at, struck, Cell, .30f, seed + 211 + n);
                        forks.Group.alpha = 1f;
                    }
                }, nova.Host);
            }

            float held = NovaBeats[NovaBeats.Length - 1] + .03f;
            float total = held + NovaFade;
            float phase = Random.Range(0f, 40f);

            Tween.Run(total, Ease.Linear, t =>
            {
                float s = t * total;
                if (s < held) return;

                float a = Mathf.Pow(1f - (s - held) / NovaFade, 1.3f)
                        * (.5f + .5f * Mathf.Abs(Mathf.Sin(s * 42f + phase)));

                if (nova.Host != null) nova.Group.alpha = a * .8f;
                if (forks != null && forks.Host != null) forks.Group.alpha = a;
            }, nova.Host).OnDone(() =>
            {
                nova.Destroy();
                forks?.Destroy();
            });

            // Every body it reached: a flare on it and the charge crawling over it.
            for (int i = 0; i < struck.Count; i++) Jolt(struck[i], tint, i * .012f);

            Embers(at, tint, 26, Cell * 2.6f, Cell * .36f);
            Twinkles(at, tint, 7, Cell * 1.8f);

            // A hit-stop: the hill held for a few frames while the burst lands, which is what
            // every fighting game does with a heavy blow and is the cheapest weight there is.
            Dilate(.45f, .14f);
            ShakeBoard(26f);

            Audio.Sfx("boom", .78f, 1.06f);
            Audio.SfxVaried("arc", .6f, .1f);
        }

        /// <summary>One body taking the charge: a flare on it, and two arcs crawling over it a beat apart.</summary>
        void Jolt(Vector2 at, Color tint, float delay)
        {
            Tween.After(delay, () =>
            {
                if (_fx == null) return;

                var flare = Lit("Jolt", _fx, StrikeFx.Flare, Pal.A(Color.Lerp(tint, Color.white, .6f), 1f),
                                Vector2.one * Cell * 1.5f);
                if (flare != null)
                {
                    var rt = flare.rectTransform;
                    rt.anchoredPosition = at;
                    rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 60f));
                    Tween.Run(.26f, Ease.OutQuad, t =>
                    {
                        if (!flare) return;
                        rt.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, Ease.OutQuint(t));
                        flare.color = Pal.A(Color.Lerp(Color.white, tint, t), 1f - t * t);
                    }, flare).OnDone(() => { if (flare) Destroy(flare.gameObject); });
                }

                Crackle(at, tint, Cell * .5f, .13f);
            }, this);

            Tween.After(delay + .09f, () => Crackle(at, tint, Cell * .55f, .13f), this);
        }
    }
}
