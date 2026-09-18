using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A raider on fire: the flame it wears, the light it throws, and what each tick of the burn
    /// pays out.
    ///
    /// <para>
    /// <b>This file exists because an ember turret read as a machine gun, and the rules were never
    /// the reason.</b> A burn has always been a damage-over-time in the model
    /// (<c>SiegeBoard.Smoulder</c>), but every tick of it was reported as a <c>SiegeBolt</c> — and
    /// the one thing <see cref="Bolt"/> knows how to draw is a shot: the turret recoils, the
    /// barrel flashes, a comet crosses the hill and something lands. The tick fired every frame it
    /// took a whole point of health, so a single ember turret drew some thirty complete shots a
    /// second out of one barrel. What the player is buying is the <em>opposite</em> of that, and
    /// nothing on the screen said so.
    /// </para>
    /// <para>
    /// <b>The fix is a state rather than a faster event, which is the whole shape of it.</b> The
    /// model now ticks on a cadence and reports a <see cref="SiegeBurn"/>
    /// (<c>SiegeTuning.BurnTick</c>); the drawing here is a looping reel that lives on the raider
    /// for exactly as long as the model says it is alight, and the ticks are small flares over the
    /// top of it. So what a player reads is <em>this one is burning</em>, continuously, with a
    /// rhythm under it — which is what the ability is.
    /// </para>
    /// <para>
    /// <b>Driven off the model every frame rather than latched on the tick that lit it.</b> A burn
    /// starts on a bolt, is refreshed by later bolts, is outlived by the ward that lit it, and is
    /// ended by the clock — four edges, three of which the view never sees. <c>SiegeRaider.Alight</c>
    /// is the one question with one answer, asked once a frame, which is the same discipline
    /// <see cref="Braced"/> follows for an anvil's lean and for the same reason.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the numbers
        /// <summary>
        /// How wide the flame is drawn against the <em>width</em> of the body it is on, and where
        /// its own seat sits in its frame.
        ///
        /// <para>
        /// <b>Against the width, and sizing it by height is the mistake this records.</b> Every
        /// raider in this mode is a top-down insect drawn far wider than it is tall — a <c>mon</c>
        /// reel is about 300 x 180, so <c>Mob.Height</c> is barely half the body's own width.
        /// Scaled by that the fire came out a candle standing on a beetle four times its width:
        /// right in every number, and plainly wrong in the first picture anybody drew of it
        /// (<c>Tools/render_ward_preview.py --alight</c>). A fire is as wide as the thing burning.
        /// </para>
        /// <para>
        /// <b>The seat is <c>make_burn_fx.BASE_Y</c> and is not a taste.</b> The reel is drawn with
        /// the foot of the fire nine tenths of the way down its frame, so that the under-glow has
        /// somewhere to spread; anchoring the widget by its middle would stand the fire a tenth of
        /// its own height in the air. What is placed is the <em>seat</em>, on the ground
        /// <see cref="FootOf"/> answers — the same ground the shadow is drawn on, so a fire and
        /// the shadow under the thing burning cannot disagree about where the floor is.
        /// </para>
        /// <para>
        /// <b>Public because <c>WardFiringStage</c> asks them</b>, which is <see cref="TintOf"/>'s
        /// own note: that widget draws a turret's ability outside a board, and a second set of
        /// these numbers would be a second opinion about how big a fire is and where it stands —
        /// on the one screen whose whole job is agreeing with the hill.
        /// </para>
        /// </summary>
        public const float BlazeWide = 1.16f, BlazeSeat = .90f;

        /// <summary>
        /// The rate the flame reel is played at. <b>Must be <c>make_burn_fx.FPS</c>.</b>
        ///
        /// The reel is cut to loop over exactly its own frames, so a different rate here does not
        /// break the loop — it only changes how fast the fire moves. It is written as the tool's
        /// number all the same, because a flame running at some other speed than the one it was
        /// drawn for is the kind of drift nothing in this project can see.
        /// </summary>
        public const float BlazeFps = 20f;

        /// <summary>
        /// How long a flame takes to catch, and the seconds of burn left over which it dies down.
        ///
        /// <b>A fire that ended the instant the model stopped burning would pop out of existence</b>
        /// — the one thing in this mode a player reads as a fault rather than as an effect. The
        /// fade is read off <c>SiegeRaider.Burn</c> rather than tweened, so a burn refreshed by
        /// another bolt while it is guttering comes straight back up instead of finishing a fade
        /// that is no longer true.
        /// </summary>
        public const float BlazeIn = .18f, BlazeOut = .55f;

        /// <summary>
        /// How much of a burning body's <em>drawn width</em> the light under it covers, across
        /// and deep.
        ///
        /// <b>Off the width like the flame itself</b> (<see cref="BlazeWide"/>), because a pool of
        /// firelight is the shape of the thing standing in it — and a raider here is drawn far
        /// wider than it is tall, so a glow measured against its height would be a coin under a
        /// beetle.
        /// </summary>
        const float CoalsWide = .92f, CoalsTall = .40f;

        /// <summary>
        /// Seconds between the embers a burning body sheds, and how long one lives.
        ///
        /// <para>
        /// <b>Deliberately slow, and the reel is carrying most of this.</b> The flame is drawn with
        /// its own sparks coming off it, so these are the few that leave the picture and drift
        /// across the hill — the part a reel cannot do, because a reel is anchored to the body and
        /// a raider is walking. Three a second per burning raider against the fifty a second a
        /// boss's orb wake used to emit, which is the measurement that file records
        /// (<see cref="Cinder"/>): every <c>Image</c> re-tinted marks its canvas dirty, and this
        /// can be running on a whole wave at once.
        /// </para>
        /// </summary>
        const float EmberEvery = .30f, EmberFor = .62f;

        /// <summary>
        /// How warm a burning body is drawn, and how much the firelight on it wavers.
        ///
        /// <b>A multiply can only darken, so this is firelight and not fire</b> (invariant 44g):
        /// pulling the green and blue channels down leaves a body lit by something orange, which
        /// is exactly what is happening to it. Reaching for a brighter tint here would be the
        /// mistake that note records — the light comes from the reel, which is additive art.
        /// </summary>
        public static readonly Color Charred = new Color(1f, .78f, .58f);
        const float CharFlicker = .22f;

        // ------------------------------------------------------------------ the state
        /// <summary>
        /// Puts every burning raider's fire up, keeps it, and takes it down again.
        ///
        /// <b>Called after <see cref="Follow"/> and before <see cref="Depth"/></b>: after, because
        /// the flame is placed against a body that has already been moved this frame, and before,
        /// because <see cref="Depth"/> re-orders the whole hill and a widget built after it would
        /// spend one frame at the wrong depth.
        /// </summary>
        void Burning(float dt)
        {
            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null) continue;

                // **A falling body keeps its fire, and that is not an oversight.** `Reap` takes a
                // mob out of `_mob` the frame the model has finished with it, so a raider a burn
                // killed is already gone from here — what is left in the list mid-death is one the
                // view is still animating, and a fire that vanished a frame before the body it was
                // on would be the burn visibly failing to be the thing that killed it.
                if (mob.Falling) continue;

                var raider = _board.Find(mob.Id);

                if (raider == null || !raider.Alight) { Doused(mob); continue; }

                Kindled(mob, raider, dt);
            }
        }

        /// <summary>Lights a body, or keeps one already alight.</summary>
        void Kindled(Mob mob, SiegeRaider raider, float dt)
        {
            int colour = Burns(mob, raider);

            // **Re-lit rather than re-tinted when a fiercer ward of another colour takes it
            // over.** `SiegeRaider.Kindle` keeps the fiercer burn and its ward travels with it,
            // so this is a real change of who is paying — and the reel is baked per colour
            // (`WardModel.BurnFor`) precisely because a multiply cannot turn one flame into
            // another. It is rare by construction: a burn only changes hands when a *stronger*
            // one lands.
            if (mob.Blaze != null && mob.Burns != colour) Doused(mob);

            if (mob.Blaze == null && !Ablaze(mob, colour)) return;

            // **Read off the model rather than tweened out.** See <see cref="BlazeOut"/>.
            float held = Mathf.Clamp01(raider.Burn / BlazeOut);
            float caught = mob.Alight < BlazeIn ? mob.Alight / BlazeIn : 1f;
            float lit = Mathf.Min(held, caught);

            mob.Alight += dt;

            if (mob.Blaze != null) mob.Blaze.color = Pal.A(Color.white, lit);
            if (mob.Coals != null)
                mob.Coals.color = Pal.A(TintOf(Hue(mob.Burns)), .34f * lit
                                        * (.82f + .18f * Mathf.Sin(Time.unscaledTime * 7.3f
                                                                   + mob.Id)));

            // The few sparks that leave the picture. See <see cref="EmberEvery"/>.
            mob.Ashes -= dt;
            if (mob.Ashes > 0f || lit < .35f) return;

            mob.Ashes = EmberEvery * Random.Range(.7f, 1.35f);

            var tint = TintOf(Hue(mob.Burns));
            float across = mob.Body != null ? mob.Body.rectTransform.sizeDelta.x : mob.Height;

            var at = mob.Node.anchoredPosition
                   + new Vector2(Random.Range(-.26f, .26f) * across,
                                 BodyLift * mob.Height + Random.Range(0f, .35f) * mob.Height);

            Cinder(at, tint, mob.Height * Random.Range(.07f, .13f), EmberFor,
                   new Vector2(Random.Range(-.18f, .18f), Random.Range(.55f, .95f)) * mob.Height);
        }

        /// <summary>
        /// Builds the fire on a body, or answers false when its reel is not in hand.
        ///
        /// <b>Nothing is built until the frames are there</b>, which is <see cref="Book"/>'s rule
        /// and invariant 7b's: an <c>Image</c> with no sprite is a white rectangle a body and a
        /// half tall standing on the hill, not a blank. A line that somehow holds an ember turret
        /// whose reel failed to load draws no fire and plays exactly as it always did.
        /// </summary>
        bool Ablaze(Mob mob, int colour)
        {
            var frames = BurnArt(colour);
            if (frames == null || frames.Length == 0) return false;

            mob.Burns = colour;
            mob.Alight = 0f;
            mob.Ashes = 0f;

            float seat = FootOf(mob.Height, mob.Boss);

            // **The body's own drawn width**, which is what `Hatch` sized the picture to and is
            // the only number here that is a fact about this cast rather than about the frame it
            // was cut into. A body whose art never arrived falls back to its height, which is
            // wrong by the aspect and is still a fire rather than nothing.
            float body = mob.Body != null ? mob.Body.rectTransform.sizeDelta.x : mob.Height;

            float wide = body * BlazeWide * (mob.Boss ? .82f : 1f);
            float tall = Tall(frames, wide);

            // The light on the ground under it, behind the body. Built first so it sits under
            // everything the raider is made of.
            mob.Coals = UIKit.Img("Coals", mob.Node, Art.Glow(96, 2.0f),
                                  Pal.A(TintOf(Hue(colour)), 0f),
                                  new Vector2(wide * CoalsWide, wide * CoalsTall),
                                  new Vector2(.5f, .5f), new Vector2(0f, seat));
            mob.Coals.raycastTarget = false;
            mob.Coals.transform.SetSiblingIndex(
                mob.Shadow != null ? mob.Shadow.transform.GetSiblingIndex() + 1 : 0);

            // **The seat, not the middle.** See <see cref="BlazeSeat"/>.
            mob.Blaze = UIKit.Img("Blaze", mob.Node, frames[0], Pal.A(Color.white, 0f),
                                  new Vector2(wide, tall), new Vector2(.5f, .5f),
                                  new Vector2(0f, seat + (BlazeSeat - .5f) * tall));
            mob.Blaze.raycastTarget = false;
            mob.Blaze.preserveAspect = true;

            // **Over the body and under the readouts.** A health bar covered by fire is the one
            // reading on this hill a player cannot do without, and the bar is a later sibling.
            if (mob.Body != null)
                mob.Blaze.transform.SetSiblingIndex(mob.Body.transform.GetSiblingIndex() + 1);

            // **Its own offset into the reel, so a wave does not flicker in lockstep.** Twelve
            // raiders alight on one frame of one reel is one enormous flame rather than twelve
            // fires, which is the reading `Mob.Crackle` already records about a pair of bosses.
            Flipbook.Attach(mob.Blaze, frames, BlazeFps).Offset = mob.Id * .37f % frames.Length;

            return true;
        }

        /// <summary>
        /// Takes a fire down, once.
        ///
        /// <b>Destroyed rather than hidden</b>, which is invariant 48i's rule said about a flame:
        /// a widget that is always there and usually invisible is one that will one day be drawn
        /// when it should not be, and nothing would say so. A raider is alight for a few seconds
        /// of a run and re-lighting one costs two widgets.
        /// </summary>
        void Doused(Mob mob)
        {
            if (mob.Blaze == null && mob.Coals == null) return;

            if (mob.Blaze != null) { Destroy(mob.Blaze.gameObject); mob.Blaze = null; }
            if (mob.Coals != null) { Destroy(mob.Coals.gameObject); mob.Coals = null; }

            mob.Alight = 0f;
        }

        /// <summary>
        /// The colour a body wears while it is burning: warm, wavering, easing back to white as
        /// the fire dies.
        ///
        /// <b>Asked by <see cref="Follow"/> rather than written here</b>, so the one place that
        /// decides what colour a raider's body is stays the one place — a second writer would be
        /// the fault invariant 48l records (a repaint that draws a state, and a one-off path that
        /// switched something off it and never back on).
        /// </summary>
        Color Scorched(SiegeRaider raider)
        {
            float held = Mathf.Clamp01(raider.Burn / BlazeOut);

            // Two frequencies that do not divide each other, so the firelight never settles into a
            // pulse - and keyed on the raider's own id, so a hill of burning bodies is a hill of
            // separate fires.
            float seed = raider.Id * 1.37f;
            float waver = 1f - CharFlicker * .5f
                        * (1f + Mathf.Sin(Time.unscaledTime * 9.1f + seed)
                                * Mathf.Cos(Time.unscaledTime * 5.3f + seed * 2.1f));

            return Color.Lerp(Color.white, Charred, held * waver);
        }

        // ------------------------------------------------------------------ a tick paying out
        /// <summary>
        /// One instalment of a burn: the figure it took, and a flare on the body it came off.
        ///
        /// <para>
        /// <b>Small on purpose, and that is the whole point of the change.</b> This is what used to
        /// be a full bolt. What it says now is only "the fire is working" — the figure joins the
        /// raider's running tally exactly as every other hit does (<see cref="Number"/>), and the
        /// flare is a fifth of the size of an impact. The thing the player watches is the flame,
        /// which is standing there the whole time.
        /// </para>
        /// <para>
        /// <b>No sound on an ordinary tick.</b> A burn pays twice a second for up to six seconds
        /// and can be running on a whole wave, so a voice on each would be the loudest thing in
        /// the mix saying what the figure already says — <see cref="Land"/>'s own finding about a
        /// lit line landing eighteen hits a second. A kill still gets the turret's voice, because
        /// a kill is the beat worth marking.
        /// </para>
        /// </summary>
        void Burned(SiegeBurn burn)
        {
            Mob mob = null;
            for (int i = 0; i < _mob.Count; i++) if (_mob[i].Id == burn.Raider) mob = _mob[i];
            if (mob == null || mob.Node == null) return;

            var at = mob.Node.anchoredPosition;
            var tint = TintOf(Hue(mob.Burns));

            // A lick of light where the fire is biting, offset up the body so it reads as coming
            // out of the flame rather than as a hit landing on the ground.
            Pop(at + new Vector2(0f, mob.Height * .18f), Pal.Lift(tint, .35f), .85f, .22f);

            Number(burn.Raider, at, burn.Damage, false);

            if (mob.Body != null) Tween.Punch(mob.Body.transform, .045f, .12f);

            // **The turret's own voice on the kill**, exactly as a bolt's landing has it, and on
            // nothing else. See the note above.
            if (burn.Killed)
            {
                Audio.SfxVaried("zap", .42f);
                Burst.Sparks(_fx, at, tint, 8, mob.Height * 1.3f, mob.Height * .16f, .42f);
            }
        }

        /// <summary>
        /// How tall a reel drawn <paramref name="wide"/> across comes out, read off its own art.
        ///
        /// <b><see cref="Frame"/> the other way up</b>, and here rather than beside it because
        /// this is the only thing in the mode sized by its width: a bolt, a body and a burst are
        /// all cut to a height and allowed whatever width their animation's box came out as.
        /// Answers a square when the frames are not in hand, so a missing reel costs the picture
        /// and never the layout.
        /// </summary>
        static float Tall(Sprite[] frames, float wide)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return wide;

            var rect = frames[0].rect;
            return rect.width > 0f ? wide * rect.height / rect.width : wide;
        }

        /// <summary>
        /// Which ward colour a raider's fire is drawn in.
        ///
        /// <b>The seat's colour rather than the turret's, because a burn outlives the ward that
        /// lit it.</b> <c>SiegeRaider.BurnFrom</c> names a post, and a post can fall while its
        /// fire is still on the hill — so an index that no longer stands falls back to whatever
        /// this body was already wearing, and the flame carries on in the colour the player has
        /// been watching rather than changing hue because a turret died.
        /// </summary>
        int Burns(Mob mob, SiegeRaider raider)
            => raider.BurnFrom >= 0 && raider.BurnFrom < _board.Wards.Count
             ? _board.Wards[raider.BurnFrom].Colour
             : mob.Burns;

        /// <summary>
        /// The flame reel a ward colour burns, as frames.
        ///
        /// <b>Four literals at the lookup rather than a name built from a letter</b>, which is
        /// <see cref="GemArt"/>'s own rule and the one <c>Tools/verify/artnames.py</c> enforces: a
        /// key assembled a call away from where it is resolved is a name nothing holds to disk,
        /// and what a missing reel costs here is a white rectangle a body and a half tall walking
        /// down the hill (invariant 7b).
        /// </summary>
        static Sprite[] BurnArt(int colour)
        {
            switch (Hue(colour))
            {
                case 0: return Blast("burn_r");
                case 1: return Blast("burn_g");
                case 2: return Blast("burn_b");
                default: return Blast("burn_y");
            }
        }
    }
}
