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

            var frames = Reel("laser");
            var face = frames != null && frames.Length > 0 ? frames[0] : Art.SoftCapsule(64);

            var wave = UIKit.Img("Still wave", _fx, face, Pal.A(Pal.Glass, .95f),
                                 new Vector2(Span.x, Cell * .9f));
            wave.raycastTarget = false;

            if (frames != null && frames.Length > 0) Flipbook.Attach(wave, frames, 30f, true);

            var rt = wave.rectTransform;
            rt.anchoredPosition = new Vector2(0f, from);

            Tween.Run(StillSweep, Ease.OutQuad, t =>
            {
                if (!rt) return;
                rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, t));
                rt.localScale = new Vector3(1f, Mathf.Lerp(1f, .35f, t), 1f);
                wave.color = Pal.A(Pal.Glass, Mathf.Lerp(.95f, .2f, t));
            }, wave).OnDone(() => { if (wave) Destroy(wave.gameObject); });

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
                    Shockwave(here, Pal.Glass, size, .34f);
                    Burst.Sparks(_fx, here, Pal.Glass, mob.Boss ? 10 : 5, Cell * 1.2f,
                                 Cell * .14f, .45f);
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
                Cinder(spot, Pal.Glass, Cell * .16f, .55f,
                       new Vector2(Random.Range(-Cell * .15f, Cell * .15f), Cell * .7f));
            }, _fx);

            Flow.Flash(Pal.A(Pal.Glass, .55f), .22f, .30f);
            Dilate(.45f, .28f);
            ShakeBoard(Cell * .05f);
            Audio.Sfx("shatter", .45f, 1.35f);

            // And the sand running out: a tock on the last beat, so the hill walking again is
            // announced rather than noticed.
            Tween.After(Mathf.Max(0f, seconds - .15f), () => Audio.Sfx("tock", .45f, .9f), _fx);
        }

        /// <summary>
        /// How long the wave takes to cross the hill. Under half a second, because it is drawn
        /// on a hill the model has already stopped: a wave that took longer would be a stop that
        /// visibly arrived late at the crest.
        /// </summary>
        const float StillSweep = .42f;
    }
}
