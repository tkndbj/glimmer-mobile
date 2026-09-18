using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The fourth and fifth charms going off: a furnace pouring into a turret, and an hourglass
    /// stopping the hill.
    ///
    /// <para>
    /// <b>Its own file because these two land somewhere the first three do not.</b> A prism, a
    /// lance and a stormglass are drawn on the field and up at the hill
    /// (<c>SiegeView.Charms</c>); a furnace's payoff arrives at a <em>post</em> and an
    /// hourglass's arrives on the hill's <em>clock</em>, and both arrive a beat after the stone
    /// has gone (invariant 37s), off the board's own report rather than off the spark.
    /// </para>
    /// <para>
    /// <b>Every drawing here is a replay of what the model already decided</b> (invariant 30i):
    /// <see cref="Forged"/> is handed the ward and whether it banked, and <see cref="Stilled"/>
    /// the seconds the hill was stopped for. Nothing here works anything out.
    /// </para>
    /// <para>
    /// <b>Neither charge is dilated, for the stormglass's reason.</b> Both are booked to land
    /// <c>SiegeTuning.FuelLands</c> ahead in <em>model</em> seconds, so a slowed clock through the
    /// wind-up would be a lit stone the player stares at. The hourglass takes its beat of slow
    /// motion when the wave crosses the hill, which is the moment there is something to watch.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the furnace
        /// <summary>
        /// A furnace going off: the stone heats, bursts, and pours into the turret of its colour.
        ///
        /// <para>
        /// <b>The pour is the sentence.</b> What a furnace does is put a charge on one turret,
        /// and a player has to see <em>which</em> - so the stone throws a thread of its own colour
        /// straight at the post it is worth, timed to arrive on the frame the model banks the
        /// charge (<see cref="Forged"/> draws the arrival). A burst with no thread would be a
        /// charm that did something somewhere.
        /// </para>
        /// </summary>
        void Forging(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);

            Holding(ForgeCharge + ForgeSettle);

            Charge(at, tint, ForgeCharge);

            // The stone heating: a bloom that grows for the whole charge and runs white at the
            // top, because a furnace's colour is the colour of the ward it is about to feed and
            // its *heat* is the thing that says it is a furnace.
            var heat = UIKit.Img("Furnace heat", _fx, Art.Glow(96, 2.0f),
                                 Pal.A(Pal.Lift(tint, .5f), 0f),
                                 new Vector2(Cell * 1.2f, Cell * 1.2f));
            heat.raycastTarget = false;
            heat.rectTransform.anchoredPosition = at;

            var heatRt = heat.rectTransform;

            Tween.Run(ForgeCharge, Ease.InQuad, t =>
            {
                if (!heatRt) return;
                heatRt.localScale = Vector3.one * Mathf.Lerp(.6f, 3.6f, t * t);
                heat.color = Pal.A(Color.Lerp(Pal.Lift(tint, .5f), Pal.Cream, t * t),
                                   Mathf.Min(1f, t * 1.6f) * .85f);
            }, heat).OnDone(() => { if (heat) Destroy(heat.gameObject); });

            // Embers shed off it while it heats, at a rate that climbs: what a thing about to
            // pour looks like is a thing that is starting to spill.
            float shed = Time.unscaledTime;
            Tween.Run(ForgeCharge, Ease.Linear, t =>
            {
                if (_fx == null) return;
                if (Time.unscaledTime - shed < Mathf.Lerp(.09f, .035f, t)) return;
                shed = Time.unscaledTime;
                Cinder(at + new Vector2(Random.Range(-Cell * .3f, Cell * .3f), Cell * .2f),
                       Pal.Lift(tint, .3f), Cell * .22f, .35f,
                       new Vector2(Random.Range(-Cell * .3f, Cell * .3f), Cell * .8f));
            }, _fx);

            Detonate(at, colour, tint, 4.0f, ForgeCharge);

            // **The pour**, to the post of its colour and to no other. Two threads a beat apart,
            // the second thicker, so the stream reads as building rather than as one line
            // appearing - and none at all when the line does not stand that colour, which a
            // shipped level cannot author (`SiegeLayout.Check`) and a fixture can.
            int ward = PostOf(colour);

            if (ward >= 0)
            {
                var post = new Vector2(PostX(ward), _lineY + Cell * .9f);
                Streak(at, post, Pal.Lift(tint, .25f), ForgeCharge * .40f, ForgeCharge * .30f);
                Streak(at, post, Pal.Lift(tint, .55f), ForgeCharge * .62f, ForgeCharge * .38f);
            }

            // A rising note for the heat and a low bell when it pours - the same two materials
            // every other charm uses, at a furnace's pitch (invariant 39e: one set, one place).
            Audio.SfxVaried("lit", .45f, .06f);
            Tween.After(ForgeCharge, () => Audio.Sfx("chime", .5f, .66f));
        }

        /// <summary>
        /// How long a furnace draws heat before it pours. The model's own figure, never typed
        /// (<c>SiegeBoard.Break</c> books the charge <c>FuelLands</c> ahead), for the stormglass's
        /// reason: typed, the pour and the bank would drift apart.
        /// </summary>
        static float ForgeCharge => SiegeTuning.FuelLands(0);

        /// <summary>The beat after the pour the fall waits out, so the arrival is seen.</summary>
        const float ForgeSettle = .30f;

        /// <summary>The post standing this colour, or -1. Read off the board, never off a table.</summary>
        int PostOf(int colour)
        {
            if (_board == null || _posts == null) return -1;

            var wards = _board.Wards;
            for (int w = 0; w < _posts.Length && w < wards.Count; w++)
                if (wards[w].Colour == colour) return w;

            return -1;
        }

        /// <summary>
        /// A furnace arriving at the line: the charge banks, or the post refuses it.
        ///
        /// <para>
        /// <b>Both halves are drawn, because both are the answer to a decision.</b> A bank is the
        /// overcharge glyph lighting with a bell under it - the same glyph <see cref="Ready"/>
        /// pulses, so what the player learns is that the tap they already know is now loaded. A
        /// refusal is the glyph <em>shaking its head</em> and a puff of dead smoke: the ward had
        /// fallen or was already full, and a furnace that silently did nothing would teach that
        /// furnaces sometimes do nothing.
        /// </para>
        /// </summary>
        void Forged(SiegeForged forged)
        {
            if (_posts == null || forged.Ward < 0 || forged.Ward >= _posts.Length) return;

            var post = _posts[forged.Ward];
            if (post == null || post.Node == null) return;

            var tint = TintOf(post.Colour);
            var at = new Vector2(PostX(forged.Ward), _lineY + Cell * .55f);

            if (!forged.Banked)
            {
                if (post.Dump != null) Refuse(post.Dump.rectTransform);
                Burst.Sparks(_fx, at, DarkCoat, 10, Cell * 1.6f, Cell * .2f, .5f);
                Audio.Sfx("blocked", .45f, .9f);
                return;
            }

            Pop(at, Pal.Cream, 2.6f, .30f);
            Shockwave(at, Pal.Lift(tint, .5f), 3.4f, .40f);
            Burst.Sparks(_fx, at, tint, 16, Cell * 2.8f, Cell * .24f, .5f);

            if (post.Dump != null) Tween.Pop(post.Dump.transform, 0f, .42f);
            if (post.Halo != null) Tween.Pop(post.Halo.transform, 0f, .42f);

            Tween.Punch(post.Node, .18f, .32f);
            ShakeBoard(8f);
            Audio.Sfx("reward", .55f, 1.15f);
        }

        // ------------------------------------------------------------------ the hourglass
        /// <summary>
        /// An hourglass going off: the stone charges, bursts, and throws a thread of glass up the
        /// hill. The stop itself is drawn when the model says it began (<see cref="Stilled"/>).
        ///
        /// <b>A thread rather than a wave here, because nothing has stopped yet.</b> The model
        /// books the stop <c>FuelLands</c> ahead, so what this beat can honestly show is the time
        /// leaving the field for the hill; the hill standing still is the next beat's news.
        /// </summary>
        void Sanding(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);

            Holding(SandCharge + SandSettle);

            Charge(at, tint, SandCharge);

            // The stone turning over: a glass ring that spins as it closes, because what an
            // hourglass does is turn. Drawn on top of `Charge`'s own ring, in glass rather than in
            // the gem's colour, so the two say different things - one that a charm is about to go,
            // one which charm it is.
            var glass = UIKit.Img("Hourglass turn", _fx, Art.Ring(128, 6f),
                                  Pal.A(Pal.Glass, 0f), new Vector2(Cell, Cell));
            glass.raycastTarget = false;
            glass.rectTransform.anchoredPosition = at;

            var glassRt = glass.rectTransform;

            Tween.Run(SandCharge, Ease.InOutQuad, t =>
            {
                if (!glassRt) return;
                glassRt.localScale = Vector3.one * Mathf.Lerp(2.6f, 1.1f, t);
                glassRt.localRotation = Quaternion.Euler(0f, 0f, t * 180f);
                glass.color = Pal.A(Pal.Glass, t < .4f ? t / .4f * .9f : .9f);
            }, glass).OnDone(() => { if (glass) Destroy(glass.gameObject); });

            Detonate(at, colour, tint, 4.0f, SandCharge);

            // The time leaving: a thread of glass straight up the hill from the stone to the
            // crest, opening as the charge ends. Its arrival is the wave `Stilled` draws.
            Streak(at, new Vector2(at.x, _hillTop), Pal.Glass, SandCharge * .55f, SandCharge * .45f);

            // A tick where it starts and a tock where the sand runs out - the one pair in this
            // set that is literally a clock (`sfx.tsv`: the same block of wood a fourth apart).
            Audio.Sfx("tick", .5f, 1f);
            Tween.After(SandCharge, () => Audio.Sfx("tock", .5f, 1f));
        }

        /// <summary>How long an hourglass charges: the model's own figure, for <see cref="ForgeCharge"/>'s reason.</summary>
        static float SandCharge => SiegeTuning.FuelLands(0);

        /// <summary>The beat after the charge the fall waits out, so the thread is seen leaving.</summary>
        const float SandSettle = .30f;

        /// <summary>
        /// The hill stopping: a wave of glass sweeping from the line to the crest, and a ring
        /// closing on every raider as it passes.
        ///
        /// <para>
        /// <b>Drawn off the report, on the frame the model stopped the hill</b>, and never off the
        /// spark: the stop begins <c>FuelLands</c> after the stone, and a wave drawn on the swap
        /// would sweep a hill that was still walking. What makes the stop legible afterwards is
        /// the tint <see cref="Follow"/> keeps on every body while <c>SiegeBoard.Stilled</c> is
        /// true - the same cold coat a stunned raider wears, because a stop is a stun the whole
        /// hill took at once.
        /// </para>
        /// <para>
        /// <b>A beat of slow motion on the wave and no more.</b> The hill is stopped by the
        /// model, so nothing here needs a dilated clock to make it legible - and every frame the
        /// line's clock is slowed is a frame of the window the charm bought being spent on
        /// watching rather than on firing (invariant 37cq: the model is handed the seconds).
        /// </para>
        /// </summary>
        void Stilled(float seconds)
        {
            if (_fx == null) return;

            float from = _lineY + Cell * .6f;
            float to = _hillTop;

            // **Its own front, and not the stormglass's beam.** This drew `laser` stretched to
            // the width of the board, which is a bar sliding past rather than time freezing: a
            // laser is flat at full alpha across its middle and has no leading edge, so what
            // crossed the hill was a blue stripe. `stillwave` is a wavefront - a hot edge with
            // graduations and shards crystallising behind it - and it is the same vocabulary the
            // dial below carries, so the two read as one thing arriving twice.
            //
            // **And it carries its own paint now**, so `Color.white` is lent rather than
            // `Pal.Glass` imposed: a near-white multiplied down is one hue going grey, which is
            // what "smokey, dead white" was (`make_siege_art.ramp`).
            Wall("Still wave", Reel("stillwave"), from, to, StillSweep, StillFront, 1f, 0f);

            // **A second wall a beat behind the first**, shallower and dimmer. One front crossing
            // a hill is a line moving; two at different depths and speeds is a *volume* of stopped
            // time arriving, which is the whole of what "thicker" means on a sprite that is one
            // band however brightly it is drawn.
            Wall("Still wake", Reel("stillwave"), from, to, StillSweep * 1.18f,
                 StillFront * 1.7f, .52f, StillSweep * .16f);

            // **A ring closing on every body as the wave reaches it**, staggered by how far up
            // the hill it stands - so the stop visibly *travels*, which is the one thing a tint
            // switching on everywhere at once cannot say.
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null || mob.Falling) continue;

                var at = mob.Node.anchoredPosition;
                float share = Mathf.InverseLerp(from, to, at.y);
                float when = Mathf.Clamp01(share) * StillSweep;
                float size = mob.Boss ? 3.2f : 1.7f;
                var node = mob.Node;

                Tween.After(when, () =>
                {
                    if (_fx == null || !node) return;
                    var here = node.anchoredPosition;

                    // **Two rings rather than one, and both in glass rather than in white.**
                    // `Pal.Glass` is `#DCEBF5` - a near-white - so every ring, spark and mote the
                    // stop drew was the same pale nothing the front was, and the whole payoff
                    // read as one grey event. The colour is the charm's now, and the second ring
                    // an instant behind the first is what makes a body look *seized* rather than
                    // splashed.
                    Shockwave(here, StillGlow, size, .40f);
                    Tween.After(.09f, () => Shockwave(here, StillCore, size * .62f, .30f), _fx);

                    Burst.Sparks(_fx, here, StillCore, mob.Boss ? 16 : 8, Cell * 1.35f,
                                 Cell * .16f, .55f);
                }, _fx);
            }

            // Motes rising off the hill for as long as it stands, thinning as the sand runs out:
            // a stopped hill with nothing moving on it reads as the game having frozen
            // (invariant 20g's fault, met on a payoff), so something has to keep moving that is
            // plainly not the hill.
            float drift = Time.unscaledTime;
            float top = _hillTop, foot = _hillFoot;
            float half = Span.x * .46f;

            Tween.Run(seconds, Ease.Linear, t =>
            {
                if (_fx == null) return;
                if (Time.unscaledTime - drift < Mathf.Lerp(.05f, .16f, t)) return;
                drift = Time.unscaledTime;
                var spot = new Vector2(Random.Range(-half, half), Random.Range(foot, top));
                Cinder(spot, StillGlow, Cell * .22f, .70f,
                       new Vector2(Random.Range(-Cell * .15f, Cell * .15f), Cell * .7f));
            }, _fx);

            Dial("stilldial", StillGlow, seconds);

            // **The wash is the charm's colour and it lasts long enough to be a colour.** A fifth
            // of a second of near-white over a lit board is a flicker a player reads as a frame
            // dropping; half a second of glass says the whole screen went cold.
            Flow.Flash(Pal.A(StillGlow, .42f), .30f, .55f);

            // **And the slow motion is spent on the sweep rather than on the bang.** A quarter of
            // a second of it ended while the wave was still a third of the way up the hill.
            Dilate(.34f, StillSweep * .68f);

            ShakeBoard(Cell * .05f);
            Audio.Sfx("shatter", .45f, 1.35f);

            // And the sand running out: a tock on the last beat, so the hill walking again is
            // announced rather than noticed.
            Tween.After(Mathf.Max(0f, seconds - .15f), () => Audio.Sfx("tock", .45f, .9f), _fx);
        }

        /// <summary>
        /// One front crossing the hill: the drawing both charms that sweep it are made of.
        ///
        /// <para>
        /// <b>Shared because the two fronts are the same object at two temperatures</b> - an
        /// hourglass's wall of stopped time and an anvil's wall of driven ground travel the same
        /// way, broaden the same way and end the same way, and everything a player tells them
        /// apart by is in the reel (<c>make_siege_art.STILL_RAMP</c> against <c>HEAVE_RAMP</c>).
        /// Two copies of this drifted once already, which is how one came to open as it climbed
        /// and the other to close.
        /// </para>
        /// <para>
        /// <b>The rate is derived from the reel rather than typed.</b> It was 30fps against a
        /// twelve-frame reel because that came to exactly the old sweep - two numbers in two files
        /// holding one fact, and the moment the sweep was slowed the reel looped twice in the
        /// middle of a single pass. Asking the reel how long it is means the boil plays through
        /// once however long the climb takes.
        /// </para>
        /// <para>
        /// <b>It is lent <c>Color.white</c>, which is not an oversight.</b> Both reels carry their
        /// own paint now, and <c>Image.color</c> is a multiply - so anything but white would take
        /// a hot lip toward its own hue and put back the flat, washed-out front this was re-cut to
        /// get rid of (invariant 44g, met on an effect).
        /// </para>
        /// </summary>
        void Wall(string name, Sprite[] frames, float from, float to, float over, float deep,
                  float ink, float delay, float endInk = .34f)
        {
            if (_fx == null || over <= 0f) return;

            if (delay > 0f)
            {
                Tween.After(delay, () => Wall(name, frames, from, to, over, deep, ink, 0f, endInk),
                            _fx);
                return;
            }

            var face = frames != null && frames.Length > 0 ? frames[0] : Art.SoftCapsule(64);

            var wall = UIKit.Img(name, _fx, face, Pal.A(Color.white, ink),
                                 new Vector2(Span.x, Cell * deep));
            wall.raycastTarget = false;

            if (frames != null && frames.Length > 0)
                Flipbook.Attach(wall, frames, frames.Length / over, true);

            var rt = wall.rectTransform;
            rt.anchoredPosition = new Vector2(0f, from);

            Tween.Run(over, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, t));

                // **It opens rather than closing.** The first cut thinned to a third as it went,
                // which reads as a thing running out of energy; a front should arrive at the crest
                // as wide as it left the line and simply stop being lit.
                rt.localScale = new Vector3(1f, Mathf.Lerp(.80f, 1.24f, t), 1f);

                // **It holds its brightness nearly to the crest.** It faded on a square curve and
                // was gone by halfway, so the half of the sweep a player is actually watching was
                // a ghost. What ends it is the front leaving the hill, not the front giving up.
                wall.color = Pal.A(Color.white, Mathf.Lerp(ink, ink * endInk, t * t * t));
            }, wall).OnDone(() => { if (wall) Destroy(wall.gameObject); });
        }

        /// <summary>
        /// How long the wave takes to cross the hill.
        ///
        /// <para>
        /// <b>Two and a half times what it was, and the old figure's reasoning was the fault.</b>
        /// It was held under half a second on the argument that the model has already stopped the
        /// hill, so a slower wave would be a stop visibly arriving late at the crest. True, and
        /// beside the point: the hill is stopped for three seconds (<c>SiegeTuning.HourglassFor</c>),
        /// so a wave taking a fifth of that is not late by any measure a player can take - it is
        /// simply <em>gone before it was seen</em>, which is what "the animation is barely
        /// visible" was. The thing a charm this rare is bought for cannot be shorter than the
        /// glance it takes to look up at it.
        /// </para>
        /// </summary>
        const float StillSweep = 1.05f;

        /// <summary>How deep the wavefront is drawn, in cells. See <c>make_siege_art.stillwave</c>.</summary>
        const float StillFront = 1.55f;

        /// <summary>
        /// How wide the dial is drawn, how much of it is lit, and how long it takes to arrive and
        /// to break.
        ///
        /// <b>Wide and faint rather than small and solid.</b> It is drawn over the thing the
        /// player is watching, so it has to be read *through*: at four and a half cells it frames
        /// the hill rather than sitting on it, and at a little over a third of an alpha the bodies
        /// under it are never in doubt. The figures are the one part of this that is taste, and
        /// the direction to move the alpha is down.
        /// </summary>
        const float DialWide = 4.2f, DialInk = .84f, DialIn = .22f, DialOut = .30f;

        /// <summary>
        /// The colours the two sweeping charms are drawn in away from their reels.
        ///
        /// <b>Neither is <c>Pal.Glass</c> or <c>Pal.Rope</c> any more, and that pair was the
        /// fault.</b> Glass is <c>#DCEBF5</c> and Rope is <c>#D9C39A</c> - a near-white and a dull
        /// tan - so every ring, spark, mote and wash either charm drew came out pale and grey
        /// whatever the reel behind it was doing. They are the board's own azure and amber now,
        /// lifted for the core, which is the same colour the stone the player matched is painted.
        /// </summary>
        static readonly Color StillGlow = Pal.Azure,
                              StillCore = Pal.Lift(Pal.Azure, .45f),
                              HeaveGlow = Pal.Amber,
                              HeaveCore = Pal.Lift(Pal.Amber, .42f);

        /// <summary>
        /// The clock a sweeping charm hangs over the hill: it arrives with the front, runs for as
        /// long as the payoff lasts, and breaks at the end of it.
        ///
        /// <para>
        /// <b>What makes a charm legible rather than merely loud.</b> A wavefront says
        /// <em>something arrived</em>; only a clock face says what the charm did to time, and for
        /// both of the two that hang one that is the entire thing the gem is bought for. Every
        /// other charm on this board announces itself with a shape a player can name — a lance is
        /// a line, a stormglass is a beam, a furnace is a nugget going into a turret — and these
        /// two had a blue flash and a beige one.
        /// </para>
        /// <para>
        /// <b>One method and two reels, which is the whole of how the anvil got a clock.</b> An
        /// hourglass hangs <c>stilldial</c>, whose hand runs forward; an anvil hangs
        /// <c>heavedial</c>, which is the same face in ember with its hand running
        /// <em>anti-clockwise</em> and an arrow to say so in a still frame (the owner's
        /// instruction, 2026-09-18). Reversing the reel here instead would have reversed the
        /// smear with it, so the hand would drag its wake into the way it was going.
        /// </para>
        /// <para>
        /// <b>Taking the reel as an argument costs a gate, and the cost is already paid.</b>
        /// <c>artnames.py</c> reads literals at call sites, so <c>Reel(reel)</c> is invisible to
        /// it exactly as a name routed through a table is (<c>Skins</c>'s own note). Both names
        /// are still checked, because <c>SiegeMode</c> preloads each of them by hand - which it
        /// has to anyway, since a charm's payoff may not wait on a load.
        /// </para>
        /// <para>
        /// <b>Its hands run</b> (<c>make_siege_art.clockface</c>), which is a reversal of what
        /// this shipped as. A still face over a still hill is a decal: the argument that a ticking
        /// clock is a working one is sound and cost the piece its whole read.
        /// </para>
        /// <para>
        /// <b>Drawn at the hill's own middle rather than at the stone that sprang it.</b> What
        /// either charm does is a fact about the whole hill, and a dial hanging off the field
        /// would say it was something the gem did to one place.
        /// </para>
        /// </summary>
        void Dial(string reel, Color glow, float seconds)
        {
            if (_fx == null) return;

            var frames = Reel(reel);
            if (frames == null || frames.Length == 0) return;

            float mid = (_hillTop + _hillFoot) * .5f;
            float size = Cell * DialWide;

            // Lent white for `Wall`'s reason: the face carries its own paint (a cold ring with
            // warm sand in it, or an ember one with an arrow), and a multiply could only flatten
            // the pair back to one hue. What `glow` still colours is the shockwave and the sparks
            // the break throws, which are drawn rather than sprited.
            var dial = UIKit.Img("Dial " + reel, _fx, frames[0], Pal.A(Color.white, 0f),
                                 new Vector2(size, size));
            dial.raycastTarget = false;
            dial.preserveAspect = true;
            dial.rectTransform.anchoredPosition = new Vector2(0f, mid);

            // **Under everything else the stop draws**, so the rings closing on each body and the
            // motes rising read over it rather than through it.
            dial.transform.SetAsFirstSibling();

            Flipbook.Attach(dial, frames, 24f, true);

            var rt = dial.rectTransform;

            // It arrives turning and settles, which is the face itself doing what its hand does.
            Tween.Run(DialIn, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.localScale = Vector3.one * Mathf.Lerp(1.70f, 1f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-34f, 0f, t));
                dial.color = Pal.A(Color.white, DialInk * Mathf.Sqrt(t));
            }, dial);

            // And it breaks rather than fading: the sand running out is a beat, and the hill
            // walking again on a dial that merely dimmed would be the charm ending by omission.
            float hold = Mathf.Max(0f, seconds - DialOut);

            Tween.After(hold, () =>
            {
                if (!rt) return;

                Shockwave(new Vector2(0f, mid), glow, DialWide * .9f, DialOut);
                Burst.Sparks(_fx, new Vector2(0f, mid), glow, 18, size * .60f,
                             Cell * .20f, .58f);

                Tween.Run(DialOut, Ease.InQuad, t =>
                {
                    if (!rt) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, 1.32f, t);
                    dial.color = Pal.A(Color.white, DialInk * (1f - t));
                }, dial).OnDone(() => { if (dial) Destroy(dial.gameObject); });
            }, dial);
        }
    }
}
