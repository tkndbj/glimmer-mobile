using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The anvil (<see cref="SiegeCharm.Anvil"/>): the stone falls, the ground answers, and the
    /// whole hill is driven back up the slope.
    ///
    /// <para>
    /// <b>Two beats and two drawings, because the model books it in two</b> (invariant 37s). The
    /// stone charging and falling is the field's beat and is drawn where the gem was; the hill
    /// being thrown is the hill's beat, <c>SiegeTuning.FuelLands</c> later, and is drawn off the
    /// report. Drawn as one, a wall of dust would cross a hill that was still walking - which is
    /// the fault the hourglass shipped with and had to be re-cut for (invariant 37dy).
    /// </para>
    /// <para>
    /// <b>The front travels <em>up</em>, which is the whole of what tells it from every other
    /// effect in this mode.</b> Everything else here falls down the screen at the line - bolts,
    /// spells, raiders, an hourglass's wall of stopped time. This is the one thing the line ever
    /// sends the other way, and a player reads the direction before they read anything else.
    /// </para>
    /// <para>
    /// <b>The bodies are not animated by this file.</b> The shove is a debt the model works off
    /// over <c>SiegeTuning.AnvilFor</c> (<see cref="SiegeRaider.Heave"/>), so <c>Follow</c> is
    /// already sliding every raider backwards on its own - what is added here is the *lean*, the
    /// dust under each of them and the front that explains it. A view that moved the bodies
    /// itself would be a second opinion about where they are.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the stone falling
        /// <summary>
        /// An anvil going off on the field: the stone charges, a block comes down on it, and the
        /// ground under the line answers. The shove itself is the next beat
        /// (<see cref="Heaved"/>).
        ///
        /// <b>What this beat may honestly show is the blow, not its effect.</b> The model books
        /// the shove <c>FuelLands</c> ahead, so a wall of dust drawn here would be crossing a hill
        /// that is still walking; what is true right now is that something very heavy has landed
        /// on the board.
        /// </summary>
        void Anviling(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);

            Holding(AnvilCharge + AnvilSettle);

            Charge(at, tint, AnvilCharge);

            // **The block itself, falling.** A charm whose verb is a blow needs something with
            // weight to arrive, and the one thing this mode has never drawn is an object coming
            // *down onto the field*. It is a plain slab in the gem's own colour rather than a
            // picture of an anvil (invariant 47i: a shape assembled out of primitives has no
            // artist in it, so it is deliberately a silhouette and not an illustration), and it
            // squashes on the frame it lands, which is the whole of what says *heavy*.
            var slab = UIKit.Img("Anvil fall", _fx, Art.SoftCapsule(64),
                                 Pal.A(Pal.Lift(tint, .45f), 0f),
                                 new Vector2(Cell * 1.55f, Cell * .46f));
            slab.raycastTarget = false;

            var slabRt = slab.rectTransform;
            float above = at.y + Cell * 3.4f;

            Tween.Run(AnvilCharge, Ease.InQuad, t =>
            {
                if (!slabRt) return;
                slabRt.anchoredPosition = new Vector2(at.x, Mathf.Lerp(above, at.y, t));
                slabRt.localScale = new Vector3(Mathf.Lerp(.72f, 1.25f, t),
                                                Mathf.Lerp(1.30f, .62f, t), 1f);
                slab.color = Pal.A(Pal.Lift(tint, .45f), Mathf.Min(1f, t * 2.4f));
            }, slab).OnDone(() =>
            {
                if (!slab) return;
                Tween.Fade(slab, 0f, .16f, Ease.InQuad)
                     .OnDone(() => { if (slab) Destroy(slab.gameObject); });
            });

            Detonate(at, colour, tint, 4.4f, AnvilCharge);

            // **The ground answering, drawn as a line rather than as a ring.** A shockwave out of
            // a point says *an explosion*; a bar snapping open sideways says *the floor moved*,
            // which is the thing about to happen to the hill.
            Tween.After(AnvilCharge, () =>
            {
                Split(at, Pal.Lift(tint, .55f), Cell * 5.2f, .30f);
                Split(at, new Color(1f, 1f, 1f, .9f), Cell * 3.4f, .22f);
                ShakeBoard(Cell * .16f);
                Audio.Sfx("boom", .72f, .58f);
            });

            Audio.SfxVaried("whoosh", .50f, .06f);
            Tween.After(AnvilCharge, () => Audio.Sfx("shatter", .46f, .72f));
        }

        /// <summary>How long an anvil charges: the model's own figure, for the forge's reason.</summary>
        static float AnvilCharge => SiegeTuning.FuelLands(0);

        /// <summary>The beat after the blow the board is held for, so the fall is seen landing.</summary>
        const float AnvilSettle = .26f;

        /// <summary>A bar snapping open sideways: the ground moving, rather than a blast.</summary>
        void Split(Vector2 at, Color colour, float reach, float over)
        {
            var bar = UIKit.Img("Anvil split", _fx, Art.SoftCapsule(64), Pal.A(colour, 1f),
                                new Vector2(reach, Cell * .16f));
            bar.raycastTarget = false;

            var rt = bar.rectTransform;
            rt.anchoredPosition = at;
            rt.localScale = new Vector3(.06f, 1f, 1f);

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = new Vector3(Mathf.Lerp(.06f, 1f, t), Mathf.Lerp(1f, .18f, t), 1f);
                bar.color = Pal.A(colour, 1f - t * t);
            }, bar).OnDone(() => { if (bar) Destroy(bar.gameObject); });
        }

        // ------------------------------------------------------------------ the hill thrown
        /// <summary>
        /// The hill being driven back: a front of dust and force rolling from the line to the
        /// crest, with every body it passes leaning into it.
        ///
        /// <para>
        /// <b>Drawn off the report, on the frame the model shoved</b>, and never off the spark -
        /// the stormglass's and the hourglass's rule, for their reason: the shove lands
        /// <c>FuelLands</c> after the stone, and a front drawn on the swap would throw a hill that
        /// had not been thrown.
        /// </para>
        /// <para>
        /// <b>A refusal is drawn too, and that is a rule rather than a courtesy.</b> An anvil
        /// sprung over an empty hill - or over a boss, which does not move - moves nothing, and a
        /// payoff that silently did nothing would be a broken gem rather than a wrong choice. So
        /// the front still leaves the line and visibly *dies* a cell up the slope, which is a
        /// picture of the charm working and finding nothing.
        /// </para>
        /// </summary>
        void Heaved(float share, int shoved)
        {
            if (_fx == null) return;

            float from = _lineY + Cell * .5f;
            float to = shoved > 0 ? _hillTop : Mathf.Lerp(from, _hillTop, .16f);
            float over = shoved > 0 ? HeaveSweep : HeaveSweep * .38f;

            // **Its own reel, for the hourglass's reason** (invariant 37dy): a borrowed sprite is
            // how the last charm that had to say *something crossed the hill* came to say nothing
            // at all. `heavefront` is drawn as a wall of driven dust with a hard lit edge along
            // its top, so what leads is the edge and what follows is the debris.
            //
            // **Drawn by `Wall`, which the hourglass draws with too**, and lent `Color.white`: the
            // reel is painted white-hot at the lip through amber to rust now, where it used to be
            // cut white and lent `Pal.Rope` - a dull tan - which is what made a wall of driven
            // ground read as the same grey smoke as a wall of stopped time.
            Wall("Heave front", Reel("heavefront"), from, to, over, HeaveFront, 1f, 0f,
                 shoved > 0 ? .30f : 0f);

            // **A second wall behind the first on a shove that landed.** The front is what the
            // ground did; this is the dust still coming up after it, deeper and slower, and it is
            // the difference between a line crossing the hill and a volume of it being thrown.
            if (shoved > 0)
                Wall("Heave dust", Reel("heavefront"), from, to, over * 1.22f,
                     HeaveFront * 1.9f, .50f, over * .18f);

            ShakeBoard(Cell * (shoved > 0 ? .22f : .07f));

            // **A beat of slow motion, and only on a shove that moved something.** The whole
            // payoff is a quarter of a second of model time (`SiegeTuning.AnvilFor`), so this is
            // the one charm where the slow motion is most of what the player gets to see it in
            // (invariant 37cq - the model is handed the seconds, so the line keeps firing through
            // it at the same rate).
            //
            // **It lasts the sweep now rather than a third of it.** At three tenths of a second it
            // was over while the front was still at the foot of the hill, and what the player got
            // to watch at full speed was the half of the payoff with nothing left in it.
            if (shoved > 0) Dilate(HeavePace, HeaveHold);

            // **And the clock, running backwards** (the owner's instruction, 2026-09-18). The
            // anvil's stone is a clock face and what the charm does is take ground back, so the
            // piece that explains it is the hourglass's own dial with its hand going the wrong
            // way round - `Dial`, `make_siege_art.heavedial`. Only on a shove that moved
            // something: a face saying *undone* over a hill where nothing was undone would be the
            // charm claiming a payoff it did not deliver.
            if (shoved > 0) Dial("heavedial", HeaveGlow, HeaveDialFor);

            Audio.Sfx("boom", shoved > 0 ? .78f : .40f, shoved > 0 ? .50f : .82f);

            if (shoved <= 0) return;

            Audio.SfxVaried("whoosh", .58f, .05f);

            // **Every body it passes, in the order it reaches them.** The lean and the slide are
            // `Follow`'s (the model is moving them); what is added here is the dust each one
            // kicks up as the ground goes out from under it, staggered by how far up the hill it
            // stands - so the throw visibly *travels* rather than happening everywhere at once.
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null || mob.Falling) continue;

                var at = mob.Node.anchoredPosition;
                float when = Mathf.Clamp01(Mathf.InverseLerp(from, _hillTop, at.y)) * over;
                float size = mob.Boss ? 2.4f : 1.4f;
                var node = mob.Node;
                bool boss = mob.Boss;

                Tween.After(when, () =>
                {
                    if (_fx == null || !node) return;

                    var stood = node.anchoredPosition;

                    // **A boss gets a brace rather than a cloud**, because it does not move
                    // (`SiegeRaider.Shove`). The picture has to say *it stood* rather than
                    // nothing happening to it at all, which is the same courtesy a refused
                    // furnace gets.
                    if (boss)
                    {
                        Shockwave(stood, HeaveGlow, size, .30f);
                        return;
                    }

                    // **Ember rather than rope, and twice as much of it.** Everything the throw
                    // drew away from its reel wore `Pal.Rope` - `#D9C39A`, a dull tan - so the
                    // dust under a body being thrown was the palest thing on a lit board. It is
                    // the board's own amber now, and the cloud is thrown from under the feet
                    // upward rather than out, because ground going out from under something
                    // throws its dust *up*.
                    Burst.Sparks(_fx, stood + new Vector2(0f, -Cell * .2f), HeaveCore,
                                 14, Cell * size * 1.8f, Cell * .20f, .58f);
                    Pop(stood, Pal.A(HeaveGlow, .85f), size * 1.15f, .30f);
                });
            }
        }

        /// <summary>
        /// How long the front takes to cross the hill.
        ///
        /// <para>
        /// <b>The dust outlives the shove, and it is allowed to.</b> This was the model's own
        /// quarter-second plus a fifth - under half a second in total, which is less than a glance
        /// - on the reasoning that the drawing should end when the thing it draws ends. That is
        /// right about the *bodies*, which the model is moving and which stop when their debt is
        /// paid, and wrong about the ground: earth thrown up by a shock is still in the air long
        /// after the shock has gone, so the front may keep climbing once nothing is moving under
        /// it. Nothing waits on this - the line has been firing through the whole of it
        /// (invariant 37cq) - so the only thing a longer sweep costs is that the payoff can be
        /// seen, which was the complaint.
        /// </para>
        /// </summary>
        static float HeaveSweep => SiegeTuning.AnvilFor + .95f;

        /// <summary>How deep the front is drawn, in cells.</summary>
        const float HeaveFront = 1.5f;

        /// <summary>The slow motion on a shove: how far the clock is slowed, and for how long.</summary>
        const float HeavePace = .28f, HeaveHold = .95f;

        /// <summary>
        /// How long the reversed clock hangs over the hill.
        ///
        /// <b>Longer than the shove and shorter than an hourglass's window</b>, which is the whole
        /// of what it has to be: it has to outlast the front it arrives with, or it would break
        /// while the dust it explains was still climbing; and it may not sit there like a stop,
        /// because an anvil has not stopped anything. A turn and a half of the hand, which is what
        /// <c>make_siege_art.DIAL_FRAMES</c> makes of it.
        /// </summary>
        static float HeaveDialFor => HeaveSweep + .55f;

        /// <summary>
        /// The lean a body wears while it is being thrown, read off the model every frame.
        ///
        /// <b>A rotation rather than an offset</b>, because the offset is already the model's:
        /// <c>Follow</c> puts the body where <see cref="SiegeRaider.March"/> says it is, and that
        /// is sliding backwards on its own. What the drawing adds is that the body is *off
        /// balance* - tipped back and slightly lifted, so a hill sliding uphill reads as a hill
        /// that was hit rather than as a hill that changed its mind.
        ///
        /// <b>Drawn from the model rather than latched on the blow</b>, for the douse's and the
        /// rubble's reason: the frame the debt is paid off is the frame the body stands up again,
        /// and a tween started on the report would still be tipping something that had already
        /// recovered.
        /// </summary>
        void Braced(Mob mob, SiegeRaider raider)
        {
            if (mob == null || mob.Body == null) return;

            float want = raider != null && raider.Shoved && SiegeTuning.AnvilHeave > 0f
                       ? Mathf.Clamp01(raider.Heave / SiegeTuning.AnvilHeave)
                       : 0f;

            if (want <= 0f && Mathf.Approximately(mob.Braced, 0f)) return;

            // Eased back rather than snapped, so the body rights itself over a beat instead of
            // popping upright the frame the debt clears.
            mob.Braced = want > mob.Braced
                       ? want
                       : Mathf.MoveTowards(mob.Braced, want, Time.unscaledDeltaTime * 4.5f);

            var body = mob.Body.rectTransform;
            body.localRotation = Quaternion.Euler(0f, 0f, mob.Braced * BraceLean);
            body.localScale = new Vector3(1f, 1f + mob.Braced * .08f, 1f);
        }

        /// <summary>Degrees a body is tipped back at the height of a shove.</summary>
        const float BraceLean = 22f;
    }
}
