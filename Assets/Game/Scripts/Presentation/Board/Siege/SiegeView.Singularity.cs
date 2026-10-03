using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The singularity (<see cref="SiegeLayout.Singularity"/>): three void stones line up, a
    /// black hole opens on the field and eats it, and what comes back out is a beam across the
    /// hill.
    ///
    /// <para>
    /// <b>Two beats and two drawings, because the model books it in two</b> (invariant 37s, the
    /// anvil's and the curse's shape). The field going in is the field's beat and is drawn off
    /// <see cref="SiegeBeat.Swallowed"/>; the beam is the hill's beat,
    /// <c>SiegeTuning.VoidGather</c> of model time later, and is drawn off the report
    /// (<see cref="Beamed"/>) - never off a clock of this file's own, so the figures land on the
    /// frame the light does. What joins the two is the hole itself (<see cref="_maw"/>), which
    /// stands on the field from the first until the second.
    /// </para>
    /// <para>
    /// <b>The hole is the gravity well's own stack</b> (<c>SiegeView.Gravity</c>,
    /// <see cref="GravityFx"/>), raised over the field rather than under the hill - the stone
    /// wears the Gravity Hole's picture, so the thing it opens is that thing. Nothing here loads
    /// a piece of its own but the stone.
    /// </para>
    /// <para>
    /// <b>The refill waits for the beam and is never allowed to wait for ever</b>
    /// (<see cref="Voiding"/>): a run that ends, or a board dealt again, between the two beats
    /// would otherwise leave a field with nothing on it and a latch nothing lifts.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        const int VoidColour = SiegeLayout.SingularityFace;

        /// <summary>The hole's disc and horizon on the field, in cells. Wider than a well: it is eating a board.</summary>
        const float MawWide = 6.4f, MawCore = 1.9f;

        /// <summary>
        /// Seconds a gem takes to fall in, and the most its start is held back by its distance.
        ///
        /// <b>Half as long again since 2026-10-03</b> (.58 and .30 before), at the owner's
        /// instruction that the swallow went by too fast to see. The shape of the fall is
        /// untouched; only its clock is longer, and <see cref="SwallowPace"/> is what keeps the
        /// beam from firing into gems still on their way in.
        /// </summary>
        internal const float GulpFor = .87f, GulpSpread = .45f;

        /// <summary>
        /// How fast the run's own clock runs while the field is going in.
        ///
        /// <para>
        /// <b>The charms' bargain, asked of the swallow</b> (<c>Dilate</c>, invariant 37cq): the
        /// beam is booked <c>SiegeTuning.VoidGather</c> of <em>model</em> time after the
        /// collapse, so a longer fall drawn at full pace would have the beam leave while gems
        /// were still falling in. Slowing the clock for exactly the fall hands the board the
        /// same seconds more slowly instead of moving when it fires - no rule, no figure and no
        /// model timing changes, the hill does not gain a step on the player while they watch,
        /// and the beam still lands on the frame its picture fires (37s).
        /// </para>
        /// <para>
        /// <b>The one inequality it must keep</b>: the model time that passes while the gems
        /// fall (this pace over <see cref="GulpSpread"/> plus <see cref="GulpFor"/>) is less than
        /// <c>VoidGather</c>, so the last gem is under the horizon before the beam leaves. Held
        /// by <c>SiegeSingularityTests.TheFieldIsSwallowedBeforeTheBeamLeaves</c>. Slower
        /// slowdowns from a charm on the same cascade only widen the margin, because a dilation
        /// keeps the slowest pace asked for.
        /// </para>
        /// </summary>
        internal const float SwallowPace = .6f;

        /// <summary>
        /// Seconds the beam stands, and the slow motion it stands in.
        ///
        /// <b>A second since 2026-10-03</b> (.62 before), so the beam and the figures it throws
        /// can be read. Everything the beam draws is sized off this one figure - the column's
        /// open and close, the lightning's re-deals, the pulses, the slow motion, the glow, the
        /// hole falling shut and the refill's wait - so lengthening it stretches the whole
        /// picture as one.
        /// </summary>
        internal const float BeamFor = 1f, BeamPace = .22f;

        /// <summary>Seconds one pulse takes to race the length of the beam.</summary>
        const float PulseFor = .24f;

        /// <summary>How many pulses race up the beam while it stands.</summary>
        const int Pulses = 8;

        /// <summary>Seconds of a live run the refill will wait for a beam before giving up on it.</summary>
        internal const float VoidPatience = 4f;

        /// <summary>
        /// The beam's four layers, outside in: width in cells, colour, alpha, and how tight its
        /// falloff is.
        ///
        /// <b>Wide and soft outside, narrow and hard inside, and none of them at full alpha but
        /// the core</b> - the layers are added, so four full-strength columns of the same shape
        /// sum to a white bar with no colour in it, which is what the first cut drew. Measured
        /// on a render through the real material, not argued.
        /// </summary>
        static readonly (float wide, Color tint, float alpha, float power)[] BeamLayers =
        {
            (9.0f, new Color(.42f, .22f, 1f), .40f, .8f),
            (6.0f, new Color(1f, .36f, .84f), .46f, 1.1f),
            (3.6f, new Color(1f, .80f, .30f), .62f, 1.5f),
            (1.5f, Color.white, .95f, 2.0f),
        };

        sealed class VoidHole
        {
            public Pit Pit;

            /// <summary>Whether its beam has left, and for how many real seconds.</summary>
            public bool Fired;
            public float Since;
        }

        VoidHole _maw;

        /// <summary>Beams the model fired before the view had drawn the hole they belong to.</summary>
        int _beamsEarly;

        // ------------------------------------------------------------------ the lesson
        /// <summary>Raised once, the first time a void stone is standing in its socket.</summary>
        public System.Action VoidDealt;

        bool _voidTaught;

        /// <summary>A void stone standing in its own socket, for the lesson to ring - or null.</summary>
        public RectTransform LiveVoid()
        {
            if (_board == null) return null;

            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
            {
                if (_board.At(i) != SiegeLayout.Singularity) continue;

                var gem = _gems[i];
                if (gem == null || gem.Img == null || gem.Colour != VoidColour) continue;

                var rt = gem.Img.rectTransform;
                if ((rt.anchoredPosition - CentreOf(i)).sqrMagnitude > Cell * Cell * .01f) continue;

                return rt;
            }

            return null;
        }

        // ------------------------------------------------------------------ the frame
        /// <summary>
        /// Turns the hole on the field and takes it down, and raises the stone's lesson. Run
        /// before the gate that holds a run, with <c>Welling</c> and for its reason.
        /// </summary>
        void Mawing(float dt)
        {
            var plan = _board != null ? _board.Layout : null;
            if (plan == null || !plan.Singular) return;

            if (!_voidTaught && VoidDealt != null && LiveVoid() != null)
            {
                _voidTaught = true;
                VoidDealt();
            }

            if (_maw == null) return;

            var pit = _maw.Pit;

            // A board dealt again took the layer this was drawn in with it.
            if (pit == null || !pit.Under || !pit.Over) { _maw = null; return; }

            pit.Age += dt;
            if (_maw.Fired) _maw.Since += dt;

            // It opens, swells as it feeds, kicks as the beam leaves and falls in on itself
            // once the beam has gone.
            float open = Ease.OutBack(Mathf.Clamp01(pit.Age / WellOpens));
            float fed = 1f + .22f * Mathf.Clamp01(pit.Age / SiegeTuning.VoidGather);
            float kick = _maw.Fired ? 1f + .25f * Mathf.Clamp01(1f - _maw.Since / .18f) : 1f;
            float gone = _maw.Fired
                       ? Ease.InCubic(Mathf.Clamp01((_maw.Since - BeamFor) / WellShuts))
                       : 0f;

            if (gone >= 1f)
            {
                Destroy(pit.Under.gameObject);
                Destroy(pit.Over.gameObject);
                _maw = null;
                return;
            }

            float size = open * fed * kick * (1f - gone) * (1f + .03f * Mathf.Sin(pit.Age * 9f));
            float light = Mathf.Clamp01(pit.Age / (WellOpens * .6f)) * (1f - gone);

            pit.Under.localScale = pit.Over.localScale = Vector3.one * Mathf.Max(0f, size);
            pit.Spin -= dt * WellSpin * WellHaste * (_maw.Fired ? 1.6f : 1f);

            Turn(pit.DiscBack, pit.Spin, light);
            Turn(pit.DiscFront, pit.Spin, light);
            Turn(pit.ArmsBack, pit.Spin * 2.3f, light * .9f);
            Turn(pit.ArmsFront, pit.Spin * 2.3f, light * .9f);
            Turn(pit.Inner, -pit.Spin * .62f + 40f, light * .72f);

            if (pit.Ring != null)
            {
                float beat = .80f + .20f * Mathf.Sin(pit.Age * 14f);
                pit.Ring.color = new Color(1f, 1f, 1f, light * beat);
            }

            if (pit.Veil != null) pit.Veil.color = Pal.A(WellDark, .70f * light);

            if (_maw.Fired) return;

            pit.Mote += dt;
            while (pit.Mote >= WellMote) { pit.Mote -= WellMote; Infall(pit); }

            pit.Pulse += dt;
            if (pit.Pulse >= WellPulse * .7f) { pit.Pulse = 0f; Closing(pit, MawWide, .42f); }
        }

        // ------------------------------------------------------------------ the field going in
        /// <summary>
        /// A singularity collapsing on the field: the hole opens where the three stones met and
        /// every gem the beat cleared is drawn round and into it.
        ///
        /// <para>
        /// <b>Answers every cell as taken</b>, so the ordinary clear in <c>Beat</c> shatters
        /// nothing and flies no fuel: nothing here was matched and nothing here was paid
        /// (<c>SiegeBoard.Devour</c>). Cells a curse took on the same beat are left to the
        /// curse's own drawing.
        /// </para>
        /// </summary>
        HashSet<int> Swallowing(SiegeBeat beat, HashSet<int> taken)
        {
            if (taken == null) taken = new HashSet<int>();
            if (beat == null || !beat.Swallowed || _fx == null) return taken;

            var centre = Vector2.zero;
            int stones = 0;
            for (int i = 0; i < beat.Eaters.Count; i++) { centre += CentreOf(beat.Eaters[i]); stones++; }
            if (stones == 0) centre = new Vector2(0f, _gemCentre);
            else centre /= stones;

            float reach = 0f;
            for (int i = 0; i < beat.Cleared.Count; i++)
                reach = Mathf.Max(reach, (CentreOf(beat.Cleared[i]) - centre).magnitude);
            if (reach < Cell) reach = Cell;

            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int cell = beat.Cleared[i];
                if (!taken.Add(cell)) continue;

                var gem = cell >= 0 && cell < _gems.Count ? _gems[cell] : null;
                if (gem == null || gem.Img == null) continue;

                var from = gem.Img.rectTransform.anchoredPosition;
                Gulped(gem.Img, from, centre, (from - centre).magnitude / reach * GulpSpread, cell);
            }

            // An old hole still falling shut is cleared away rather than drawn under a new one.
            if (_maw != null && _maw.Pit != null)
            {
                if (_maw.Pit.Under) Destroy(_maw.Pit.Under.gameObject);
                if (_maw.Pit.Over) Destroy(_maw.Pit.Over.gameObject);
            }

            var pit = Raise(centre, _fx, _fx, Cell * MawWide, Cell * MawCore);
            pit.Under.SetAsLastSibling();
            pit.Over.SetAsLastSibling();

            _maw = new VoidHole { Pit = pit };

            // The model's beam can beat the drawing here when an earlier beat of the same
            // cascade was held for a charm; the hole then has nothing left to wait for.
            if (_beamsEarly > 0)
            {
                _beamsEarly--;
                _maw.Fired = true;
                _maw.Since = BeamFor;
            }

            // The run's clock slowed for exactly the fall, so the beam - model time - leaves
            // after the last gem is under the horizon. See `SwallowPace`. Only while the beam
            // is still owed: one the model already fired has nothing left to wait for, and
            // slowing the hill then would be a second of slow motion over nothing.
            if (!_maw.Fired) Dilate(SwallowPace, GulpSpread + GulpFor);

            Lightup(WellViolet, .16f, .5f);
            Closing(pit, MawWide * 1.5f, WellOpens + .1f);
            ShakeBoard(Cell * .07f);

            Audio.Sfx("charge", .90f, .60f);
            Audio.Sfx("whoosh", .60f, .55f);
            Tween.After(GulpSpread, () => Audio.Sfx("whoosh", .45f, .80f), _fx);

            return taken;
        }

        /// <summary>
        /// One gem going into the hole: it swings round the centre as it falls, stretching
        /// toward it, and is gone under the horizon.
        /// </summary>
        void Gulped(Image img, Vector2 from, Vector2 to, float delay, int seed)
        {
            var rt = img.rectTransform;
            var arm = from - to;
            float swing = (1.1f + (seed % 5) * .16f) * Mathf.PI;

            // Lifted off its socket first, so the eye catches every gem leaving before any has
            // arrived.
            Tween.Punch(rt, .16f, .16f);

            Tween.Run(GulpFor, Ease.Linear, t =>
            {
                if (!rt) return;

                float fall = t * t * t;
                float turn = -swing * t * t;
                float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
                var at = new Vector2(arm.x * cos - arm.y * sin, arm.x * sin + arm.y * cos) * (1f - fall);

                rt.anchoredPosition = to + new Vector2(at.x, at.y * Mathf.Lerp(1f, WellTilt, fall));
                rt.localRotation = Quaternion.Euler(0f, 0f, turn * Mathf.Rad2Deg * 1.6f);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, .12f, fall);
                img.color = Color.Lerp(Color.white, WellViolet, fall * .8f);
            }, img).Delay(delay).OnDone(() => { if (img) Destroy(img.gameObject); });

            // Whatever the tween's fate, the widget does not outlive the swallow.
            Tween.After(delay + GulpFor + .05f, () => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// Holds the refill until the beam has left - or until there is no longer a beam coming.
        ///
        /// <para>
        /// <b>Waited on as a condition and bounded by a patience that only runs while the run
        /// does.</b> The beam is model time, so a panel raised mid-swallow stops it and must
        /// stop this wait with it; a run that has ended will never fire it and must not be
        /// waited on at all. The patience is the net under both: a field left empty behind a
        /// latch is a board nobody can play.
        /// </para>
        /// </summary>
        IEnumerator Voiding()
        {
            float waited = 0f;

            while (_maw != null && !_maw.Fired && !Over && waited < VoidPatience)
            {
                if (Live) waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // No beam came: the hole shuts on its own and the field comes back.
            if (_maw != null && !_maw.Fired)
            {
                _maw.Fired = true;
                _maw.Since = BeamFor;
                yield break;
            }

            // The beam is watched out before the gems fall through it.
            float tail = 0f;
            while (tail < BeamFor * .9f)
            {
                tail += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ------------------------------------------------------------------ the beam
        /// <summary>
        /// The beam, on the frame the model fired it: out of the hole, up through the line and
        /// over the whole hill, with every body it reached struck as it stands.
        ///
        /// <para>
        /// <b>Drawn whether or not it touched anything</b> - a beam over an empty hill is a
        /// wrong moment, and it has to look like one rather than like nothing.
        /// </para>
        /// </summary>
        void Beamed(List<SiegeStrike> hits)
        {
            var from = new Vector2(0f, _gemCentre);

            if (_maw != null && _maw.Pit != null && !_maw.Fired)
            {
                from = _maw.Pit.At;
                _maw.Fired = true;
                _maw.Since = 0f;
            }
            else
            {
                _beamsEarly++;
            }

            if (_fx == null) return;

            VoidRay(from);

            // The whole of the slow motion is the beam: the line keeps firing through it
            // (invariant 37cq), and it is over before the field has finished falling back in.
            Dilate(BeamPace, BeamFor * .8f);
            ShakeBoard(Cell * .30f);
            Lightup(Pal.Cream, .42f, .22f);
            Lightup(WellViolet, .26f, BeamFor + .3f);

            // The blast is the beam's voice on a phone: the thunder and the boom under it are
            // almost all below 500 Hz, which a handset speaker barely plays (Tools/sfx.tsv).
            Audio.Sfx("voidblast", 1f);
            Audio.Sfx("thunder", .95f, 1.22f);
            Audio.Sfx("boom", .80f, .62f);
            Audio.Sfx("arc", .70f, .80f);

            if (hits == null) return;

            for (int i = 0; i < hits.Count; i++)
            {
                var mob = MobOf(hits[i].Raider);

                if (mob != null && mob.Node)
                {
                    var at = mob.Node.anchoredPosition;
                    Pop(at, Pal.Bloom, mob.Boss ? 3.4f : 2.0f, .36f);
                    Burst.Sparks(_fx, at, WellViolet, 8, Cell * 2.4f, Cell * .18f, .45f);
                }

                Hurt(hits[i]);
                if (hits[i].Killed) Fell(hits[i].Raider);
            }

            Settled();
        }

        /// <summary>
        /// The light itself: four columns of it stacked from the hole to the top of the board,
        /// snapping open, flickering while they stand and closing to a thread; pulses racing
        /// up it; lightning wrapped round it; and the flare where it leaves the hole.
        /// </summary>
        void VoidRay(Vector2 from)
        {
            float top = Span.y * .5f;
            float tall = Mathf.Max(Cell, top - from.y);

            var host = UIKit.Box("Beam", _fx, Vector2.zero, new Vector2(.5f, .5f),
                                 new Vector2(from.x, from.y + tall * .5f));
            host.SetAsLastSibling();

            var columns = new Image[BeamLayers.Length];

            for (int i = 0; i < BeamLayers.Length; i++)
            {
                var layer = BeamLayers[i];
                columns[i] = Lit("Column", host, Art.Shaft(64, 64, layer.power),
                                 Pal.A(layer.tint, 0f), new Vector2(Cell * layer.wide, tall));
            }

            // **Where it leaves the hole, for as long as it stands**: a shaft's foot fades in,
            // so the base wants a light of its own or the beam reads as starting in mid air.
            var mouth = Lit("Mouth", host, Art.Glow(128, 1.4f), Pal.A(Pal.Bloom, 0f),
                            new Vector2(Cell * 10f, Cell * 5f));
            var heart = Lit("Heart", host, Art.Glow(128, 1.4f), Pal.A(Color.white, 0f),
                            new Vector2(Cell * 4.6f, Cell * 3.2f));
            mouth.rectTransform.anchoredPosition = heart.rectTransform.anchoredPosition =
                new Vector2(0f, -tall * .5f);

            Tween.Run(BeamFor, Ease.Linear, t =>
            {
                if (!host) return;

                // Open in a snap that overshoots, stand, and close to nothing.
                float wide = t < .14f ? Ease.OutBack(t / .14f)
                           : t > .72f ? 1f - Ease.InQuad((t - .72f) / .28f)
                           : 1f;
                float flick = 1f + .07f * Mathf.Sin(t * 150f) + .04f * Mathf.Sin(t * 67f);

                host.localScale = new Vector3(Mathf.Max(0f, wide) * flick, 1f, 1f);

                for (int i = 0; i < columns.Length; i++)
                {
                    if (!columns[i]) continue;

                    // The core outlasts the sheath: as it closes it goes white, not dim.
                    float keep = i == columns.Length - 1 ? 1f : Mathf.Clamp01(wide * 1.4f);
                    columns[i].color = Pal.A(BeamLayers[i].tint, BeamLayers[i].alpha * keep);
                }

                float lit = Mathf.Clamp01(wide);
                if (mouth) mouth.color = Pal.A(Pal.Bloom, .80f * lit);
                if (heart) heart.color = Pal.A(Color.white, .90f * lit);
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            // Pulses racing up it, so the column reads as something travelling rather than as
            // a bar that appeared.
            // Spread over however long the beam stands, so the last pulse reaches the top as the
            // column closes - at .62 seconds this was the .055 the first cut typed.
            float pulseEvery = Mathf.Max(0f, BeamFor - PulseFor) / (Pulses - 1);

            for (int n = 0; n < Pulses; n++)
            {
                var pulse = Lit("Pulse", host, Art.Glow(96, 1.6f), Pal.A(Color.white, 0f),
                                new Vector2(Cell * 2.6f, Cell * 4.2f));
                var rt = pulse.rectTransform;
                var tint = n % 2 == 0 ? Color.white : Pal.Bloom;

                Tween.Run(PulseFor, Ease.Linear, t =>
                {
                    if (!pulse) return;
                    rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(-tall * .5f, tall * .5f, t));
                    pulse.color = Pal.A(tint, Mathf.Sin(t * Mathf.PI) * .75f);
                }, pulse).Delay(n * pulseEvery).OnDone(() => { if (pulse) Destroy(pulse.gameObject); });
            }

            // Lightning down both flanks, re-dealt every few hundredths (MODES.md 37eu).
            var apex = new Vector2(from.x, top);

            for (int side = -1; side <= 1; side += 2)
            {
                var arc = Lightning.Grow("Beam arc", _fx, Pal.Bloom, Cell * .07f, haloAlpha: .45f);
                var flank = new Vector2(Cell * 1.5f * side, 0f);
                int seed = Random.Range(0, 1 << 20);
                int beats = Mathf.CeilToInt(BeamFor * .8f / .05f);

                for (int b = 0; b < beats; b++)
                {
                    int n = b;
                    Tween.After(n * .05f, () =>
                    {
                        if (arc.Host == null) return;
                        arc.Strike(apex + flank, from + flank, Cell, .55f, 2, seed + n);
                    }, arc.Host);
                }

                Tween.After(BeamFor * .8f + .04f, arc.Destroy, arc.Host);
            }

            // Where it leaves the hole.
            Flash(from, Pal.Bloom, 5.2f);
            Ripple(from, WellViolet, Cell * 8f, .55f, 0f, 1f);
            Ripple(from, Pal.Sun, Cell * 5f, .42f, .06f, 1f);
            Embers(from, Pal.Bloom, 16, Cell * 1.6f, Cell * .32f);
        }
    }
}
