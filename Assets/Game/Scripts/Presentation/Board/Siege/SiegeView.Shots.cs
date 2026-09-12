using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a bolt is, from the muzzle to the impact, and the pooled widgets that draw it.
    ///
    /// <para>
    /// <b>Pooled</b>, which is premature everywhere else in this project and not here: a lit line
    /// is three-part shots several times a second.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the widget a shot is
        /// <summary>
        /// One drawn beat of a shot: a node that carries the aim, a reel, and a halo under it.
        ///
        /// <para>
        /// <b>Pooled, and this is the one board in the game where that is not premature.</b> A lit
        /// ward fires every <c>SiegeTuning.FireEvery</c> — seven a second — and a full line is
        /// four of those, each of which is a muzzle flash, a comet and an impact. Building and
        /// destroying twenty-odd of these a second is churn the rest of this project never asks
        /// for, and the pool is what makes the three-part shot affordable rather than something to
        /// trim back to one part.
        /// </para>
        /// <para>
        /// <b>The node carries the rotation and the reel hangs off it</b>, rather than the reel
        /// being turned about a shifted pivot. A comet's head is at <see cref="HeadAt"/> of its
        /// frame, not the middle, so the image is offset until its head sits on the node's origin
        /// — and then aiming the node aims the shot, the halo stays on the head, and the growth
        /// tween on the way in is one scale on one transform.
        /// </para>
        /// </summary>
        sealed class Puff
        {
            public RectTransform Node;
            public Image Reel, Halo;
            public Flipbook Film;
            public bool Out;
        }

        readonly Stack<Puff> _spare = new Stack<Puff>(24);

        /// <summary>
        /// A widget playing <paramref name="frames"/>, aimed, sized to the reel's own shape.
        ///
        /// <b>The shape comes off the sprite and is never a constant here.</b> A bolt's frame is
        /// three times as tall as it is wide and a burst's is square, and both of those are
        /// decisions <c>SiegeShotBake</c> makes; reading them back off the art is what stops this
        /// class holding a second opinion that can go stale when a reel is re-baked.
        /// </summary>
        Puff Lend(Sprite[] frames, Color tint, float wide, Vector2 at, float angle, float fps,
                  bool loop, float head)
        {
            var puff = _spare.Count > 0 ? _spare.Pop() : Build();
            puff.Out = true;

            var first = frames[0];
            float aspect = first.rect.width > 0f ? first.rect.height / first.rect.width : 1f;
            float tall = wide * aspect;

            puff.Node.gameObject.SetActive(true);
            puff.Node.anchoredPosition = at;
            puff.Node.localRotation = Quaternion.Euler(0f, 0f, angle);
            puff.Node.localScale = Vector3.one;

            puff.Reel.rectTransform.sizeDelta = new Vector2(wide, tall);
            puff.Reel.rectTransform.anchoredPosition = new Vector2(0f, -(head - .5f) * tall);
            puff.Reel.color = tint;

            puff.Film = Flipbook.Attach(puff.Reel, frames, fps, loop);
            return puff;
        }

        Puff Build()
        {
            var node = UIKit.Node("Shot", _fx);

            // Under the reel, because it is a light the effect sits in rather than a ring round
            // it: a bolt's own colour on a bright green hill is the one thing this board cannot
            // afford to lose, and the baked art is bright but small.
            var halo = UIKit.Img("Halo", node, Art.Glow(96, 2.0f), Color.clear,
                                 new Vector2(1f, 1f));
            halo.raycastTarget = false;

            // Built with the sprite it will be given rather than with none: an `Image` with a null
            // sprite is a white rectangle, not a blank (invariant 7b), and a pooled widget spends
            // its whole life one frame away from being shown.
            var reel = UIKit.Img("Reel", node, Art.Pixel, Color.clear, new Vector2(1f, 1f));
            reel.raycastTarget = false;
            reel.preserveAspect = true;

            return new Puff { Node = node, Reel = reel, Halo = halo };
        }

        /// <summary>Lights the halo under a widget already lent. Only the bolt in flight wants one.</summary>
        static void Glow(Puff puff, Color tint, float size)
        {
            puff.Halo.rectTransform.sizeDelta = new Vector2(size, size);
            puff.Halo.rectTransform.anchoredPosition = Vector2.zero;
            puff.Halo.color = Pal.A(Pal.Lift(tint, .5f), .8f);
        }

        /// <summary>
        /// Hands a widget back when its reel finishes, and on a timer if the reel cannot say so.
        ///
        /// <b>The timer is not belt and braces, it is the only guarantee</b>: a <c>Flipbook</c>
        /// whose image is destroyed or whose frames are empty never raises <c>OnFinished</c>, and
        /// a widget that is never given back is a leak on a board that asks for twenty a second.
        /// <see cref="Give"/> is idempotent so the two cannot double up.
        /// </summary>
        void Ends(Puff puff, float after)
        {
            if (puff.Film != null) puff.Film.OnFinished = () => Give(puff);

            Tween.After(after + .08f, () => Give(puff), puff.Node);
        }

        /// <summary>
        /// Puts a widget back, once.
        ///
        /// Every tween on the node is killed first: a widget handed back while its flight is still
        /// running would be moved across the board by a shot that has already landed, which is the
        /// pooling bug this project has met before under another name (a flipbook left painting
        /// into an image that had been re-sized for something else).
        /// </summary>
        void Give(Puff puff)
        {
            if (puff == null || !puff.Out) return;
            puff.Out = false;

            if (!puff.Node) return;

            Tween.KillAll(puff.Node);
            Flipbook.Detach(puff.Reel);
            puff.Film = null;

            puff.Reel.color = Color.clear;
            puff.Halo.color = Color.clear;
            puff.Node.gameObject.SetActive(false);

            _spare.Push(puff);
        }

        /// <summary>The bolt itself: the turret's own comet, aimed, with a light under its head.</summary>
        Puff Round(Wards.WardModel model, int colour, Color tint, Vector2 at, float angle)
        {
            var frames = ShotArt(model, colour);

            // The shared round, drained of colour so it can take the ward's, is what is drawn when
            // the projectile pack is not in this checkout. It is one sprite rather than a reel, so
            // it is wrapped as one - a missing bake costs the animation and never the bolt.
            if (frames == null || frames.Length == 0)
            {
                var round = Piece("bullet");
                frames = round != null ? new[] { round } : null;
            }

            if (frames == null || frames.Length == 0)
            {
                return Lend(new[] { Art.Glow(96, 2.0f) }, Pal.A(Pal.Lift(tint, .5f), 1f),
                            Cell * .5f, at, angle, 1f, true, .5f);
            }

            // Sized by the frame's *width*, which is the comet's own width because the bake frames
            // it that tightly — so this number means "a bolt is two thirds of a gem across" and
            // stays true when a reel is re-baked into a different shape.
            float scale = BoltScale(model);

            var puff = Lend(frames, Color.white, Cell * scale, at, angle, 30f, true, HeadAt);
            Glow(puff, tint, Cell * 1.7f * scale);
            return puff;
        }

        // ------------------------------------------------------------------ the exchange
        /// <summary>How long a ward's recoil frames take to play out.</summary>
        const float RecoilFor = .18f;

        /// <summary>
        /// Where a bolt's head sits in its own frame, measured from the bottom.
        ///
        /// <para>
        /// <b>Declared here and read by the bake</b> (<c>SiegeShotBake</c> references this
        /// constant), so the number that frames the render and the number that positions the
        /// sprite are one number. Two would be two, and the failure is a comet whose head is not
        /// where the shot is — visible only as a bolt that seems to land slightly early, which is
        /// exactly the kind of wrongness nobody can name.
        /// </para>
        /// <para>
        /// Above a half because a comet is nearly all tail: the head leads and the trail has the
        /// rest of the frame to lie in.
        /// </para>
        /// </summary>
        public const float HeadAt = .82f;

        /// <summary>
        /// Where the barrel sits in a muzzle flash's own frame, measured from the bottom.
        ///
        /// <b>Low, because a flash is all in front of the gun.</b> Anchored in the middle like an
        /// impact, half of every flash was drawn behind the turret that threw it — and half of
        /// every reel was empty air, which came off the size of the thing on the board. Read by
        /// <c>SiegeShotBake</c>, which frames the render around it.
        /// </summary>
        public const float MuzzleAt = .22f;

        /// <summary>
        /// How long a bolt is in the air.
        ///
        /// <para>
        /// <b>Longer than it was, twice, and the art is the reason both times.</b> A round
        /// crossing the hill in six hundredths of a second is a dot teleporting whatever is drawn
        /// on it — fine while it was a dot, and it throws away a fourteen-frame comet. Doubled
        /// again after play: at a fifth of a second the animation was still over before it could
        /// be looked at, which was reported as not being able to see it at all.
        ///
        /// <para>
        /// <b>It is deliberately longer than the cadence now, and that is a change of shape rather
        /// than of degree.</b> A ward fires every <c>SiegeTuning.FireEvery</c> (.22), so a flight
        /// of up to .40 puts two of a ward's own bolts in the air at once — the line reads as a
        /// stream of comets rather than as one thing at a time, which is what makes a trail
        /// visible at all. What stops that being a hose is the cadence, which was slowed for the
        /// same verdict and cannot go further without losing the level.
        /// </para>
        /// </para>
        /// </summary>
        const float ShortestFlight = .20f, LongestFlight = .40f;

        /// <summary>
        /// How wide the turret itself is drawn, as a multiple of a cell, and how tall.
        ///
        /// <b>Here rather than at the places that build the sprite</b>, because
        /// <see cref="BarrelGap"/> is measured as a fraction of the turret's own picture and has
        /// to be converted into board units by exactly the width that picture is drawn at. There
        /// were three copies of these two numbers - the board, the loadout panel and the render -
        /// and a copy that drifts is a bolt leaving from beside the gun rather than out of it.
        /// </summary>
        public const float BodyWide = 1.72f, BodyTall = 2.15f;

        /// <summary>
        /// How far a twin-barrelled turret's barrels sit from its middle, as a fraction of the
        /// turret's own picture.
        ///
        /// <b>Measured off the art rather than chosen</b>: the seven twin hulls all carry their
        /// barrels at .091 to .107 of the sprite's width either side of the middle, so one number
        /// serves all of them and a hull re-cut is the thing that would move it.
        /// </summary>
        const float BarrelGap = .097f;

        /// <summary>
        /// How much of the barrel gap two bolts still have between them when they arrive.
        ///
        /// <b>Nearly all of it, because converging is what made two bolts read as one.</b> Bolts
        /// aimed at one point are a third of a cell apart at the muzzle and touching a few
        /// hundredths of a second later, which over a flight of a fifth of a second is a single
        /// comet with a wide start. Held apart they are two comets crossing the hill side by
        /// side, which is what a twin-barrelled turret is supposed to look like.
        /// </summary>
        public const float ApartOnArrival = .8f;

        /// <summary>
        /// How many barrels this turret is <em>drawn</em> with, which is how many bolts leave it.
        ///
        /// <para>
        /// <b>A fact about the hull, so it is keyed on the rung and never on the id.</b> The art
        /// tool's own rule is that the hull <em>is</em> the shelf rung (T1 for order 1, T20 for
        /// order 20), so a table of ids would go stale the next time the shelf is re-rung - which
        /// has already happened twice (invariants 37ax, 37ay). Reading <c>Order</c> means the
        /// barrels follow the picture wherever a turret is moved to.
        /// </para>
        /// <para>
        /// <b>It is drawing and nothing else.</b> The board fires one bolt, it lands once, it
        /// deals its damage once and it plays one sound; what a second barrel adds is a second
        /// flash and a second comet. Nothing here may reach the rules, or a turret would be
        /// paying twice for a purchase that bought a picture.
        /// </para>
        /// <para>
        /// <b>Hulls T11 to T17 all carry two barrels, at the same spacing</b>, so this is a list
        /// of rungs rather than a test: turning one on is one number. T18 to T20 carry a single
        /// wide mount with the mouths drawn onto it, which alpha cannot separate - those want
        /// offsets read off the picture by eye rather than measured, so they stay at one.
        /// </para>
        /// <para>
        /// <b>The list is shorter than the band on purpose.</b> It is the owner's, turret by
        /// turret, after looking at each on a device - measuring says which hulls *could* fire
        /// twice and only playing says which *should*. T11, T12, T13 and T16 are one entry each
        /// whenever they are wanted.
        /// </para>
        /// </summary>
        /// <b>A switch rather than a static table</b>, and the reason is the gate rather than
        /// taste: a static field on a <c>MonoBehaviour</c> needs the type initialised, which the
        /// offline runner cannot do — so a table here quietly takes `ATurretDrawnWithTwoBarrels`
        /// out of every run but the Editor's, which is the one nobody makes on the way past.
        public static int Barrels(Wards.WardModel model)
        {
            if (model == null) return 1;

            switch (model.Order)
            {
                case 11: case 14: case 15: case 17: return 2;
                default: return 1;
            }
        }

        /// <summary>
        /// How far barrel <paramref name="barrel"/> of <paramref name="model"/> sits from the
        /// middle of the turret, in board units, given the cell it is drawn at.
        ///
        /// <b>One answer, because there are three callers.</b> The board draws a turret firing,
        /// <c>WardFiringStage</c> draws the same turret firing on the loadout panel, and
        /// <c>Tools/render_siege.py</c> mirrors both - and a preview that fired down the middle
        /// while the board fired from two barrels is exactly the disagreement the panel exists to
        /// rule out. It takes the cell rather than reading this class's own, so the panel can
        /// hand it the cell it is drawing at.
        /// </summary>
        public static float BarrelStep(Wards.WardModel model, int barrel, float cell)
        {
            int barrels = Barrels(model);
            if (barrels < 2) return 0f;

            return (barrel * 2f - (barrels - 1)) * cell * BodyWide * BarrelGap;
        }

        /// <summary>
        /// How much of a lone muzzle flash each barrel's is drawn at.
        ///
        /// <b>Well under one, and that is the half that decides whether any of this reads.</b> A
        /// flash is 2.7 cells across and the barrels are a third of a cell apart, so two at full
        /// size are one blob with the centres 12% of their own width apart - which is not a wide
        /// flash, it is the same flash twice as bright. <b>Down if it ever needs to read harder,
        /// never up.</b>
        /// </summary>
        public static float BarrelFlare(Wards.WardModel model) => Barrels(model) > 1 ? .70f : 1f;

        void Bolt(SiegeBolt shot)
        {
            var post = _posts[shot.Ward];
            var ward = _board.Wards[shot.Ward];
            var tint = TintOf(ward.Colour);

            Vector2 muzzle = new Vector2(PostX(shot.Ward), _lineY + Cell * 1.0f);

            Mob mob = null;
            for (int i = 0; i < _mob.Count; i++) if (_mob[i].Id == shot.Raider) mob = _mob[i];
            if (mob == null) return;

            Vector2 to = mob.Node.anchoredPosition;

            // The turret's own recoil frames, walked by `Charge`. Frames rather than a tween,
            // because the pack drew the barrels moving and a scale-punch over a still turret is
            // the cheaper lie - which is what the first cut was, and what came back as "I didn't
            // like the firing animation".
            post.Recoil = RecoilFor;

            var dir = to - muzzle;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            // The kick, on top of the pack's own frames. A turret that only cycles frames stays
            // put; one that is shoved backwards and springs forward has weight.
            var body = post.Body;
            if (body != null)
            {
                Tween.KillChannel(body, "kick");
                Tween.Run(RecoilFor, Ease.OutQuad, t =>
                {
                    if (!body) return;
                    float k = t < .3f ? t / .3f : 1f - (t - .3f) / .7f;
                    body.rectTransform.anchoredPosition =
                        new Vector2(0f, Cell * .06f - Cell * .13f * k);
                    body.rectTransform.localScale =
                        new Vector3(1f + k * .07f, 1f - k * .06f, 1f);
                }, body, "kick");
            }

            float flight = Mathf.Clamp(dir.magnitude / (Cell * 26f), ShortestFlight, LongestFlight);

            // **One bolt in the rules, one per barrel on the screen.** A twin-barrelled turret
            // that fires down the middle reads as a turret with one barrel painted on it, so each
            // barrel throws its own flash and its own comet. Only the last one lands: the impact,
            // the damage figure and the kill are the board's one bolt, and drawing them twice
            // would say the purchase hits twice.
            int barrels = Barrels(ward.Model);

            // **A ring of light off the muzzle, once, from the middle of the turret**, because
            // this is the moment the player's own move pays out and a turret pays out once. Drawn
            // here rather than inside `Flash` for exactly that reason: two rings a sixth of a cell
            // apart is one ring at twice the brightness, which reads as a brighter turret rather
            // than as a second barrel.
            Shockwave(muzzle, Pal.Lift(tint, .5f), 2.2f, .22f);

            float flare = BarrelFlare(ward.Model);

            for (int b = 0; b < barrels; b++)
            {
                float step = BarrelStep(ward.Model, b, Cell);
                var from = muzzle + new Vector2(step, 0f);

                // **The bolts stay apart rather than converging, which was the whole reason the
                // first cut read as one shot.** Aimed at the raider they start a third of a cell
                // apart and are on top of each other within a few hundredths of a second - so what
                // a player sees is two comets for one frame and a single comet for the rest of a
                // flight that is a fifth of a second long. Each one lands beside the raider
                // instead, keeping most of the gap the whole way, and the impact is still drawn on
                // the raider: an eighth of a cell off centre is invisible under a hit drawn three
                // cells wide, and two parallel comets are not.
                var land = to + new Vector2(step * ApartOnArrival, 0f);

                var aim = land - from;
                float lean = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;

                // **The whole shot, and it is deliberately more than a dot crossing a gap.** What
                // came back from play was that the firing was boring, and the fix is not one
                // bigger thing - it is that a shot has *four* beats a player can see: the barrel
                // kicks, the muzzle throws light, something with a tail crosses the hill, and it
                // arrives.
                Flash(from, lean, ward.Model, ward.Colour, tint, flare);

                var round = Round(ward.Model, ward.Colour, tint, from, lean);
                var node = round.Node;
                bool lands = b == barrels - 1;

                Tween.Run(flight, Ease.Linear, t =>
                {
                    if (!node) return;
                    node.anchoredPosition = Vector2.Lerp(from, land, t);

                    // It grows a little on the way in, which is a cheap read of "coming toward
                    // you" on a board with no depth - and it is the only thing about the bolt this
                    // class animates, because the fourteen frames under it are doing the rest.
                    node.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t);
                }, node).OnDone(() =>
                {
                    Give(round);
                    if (lands) Land(to, tint, ward.Model, ward.Colour, angle, shot);
                });
            }

            // **One sound at one pitch for all four wards.** It used to be pitched per ward
            // (1.18 / 1.07 / 0.96 / 0.85) so a player could hear which colour they had just fed
            // without looking away from the field. Withdrawn by the owner after playing it: four
            // pitches of one clip read as four different sound effects rather than as four
            // sources of one, which is the opposite of what the spread was for. The reading is
            // gone with it and that is accepted - what says which ward is firing is the board,
            // where the bolts visibly leave their own turret. Do not put the spread back without
            // asking; it has been heard and rejected.
            // **`.20f`, and the number is about density rather than about one bolt.** A lit
            // line fires about eighteen a second across four wards, so three or four copies
            // are sounding at any instant - roughly +5 dB over a single one - which is why
            // the turrets read as loud while every individual bolt sits at the same matched
            // level as everything else in the set. It went .32 -> .20 -> .12, which is 8.5 dB
            // below the rest of the set: a bolt is now deliberately *under* the matched level,
            // because what a player hears is never one of them. To nudge further, .09 is another
            // 2.5 dB down; below about .07 the four authored pitches stop being tellable apart,
            // which is the reading this sound exists to carry.
            //
            // What must never be reached for instead is `SiegeTuning.FireEvery`. It is the
            // same density from the other end and it is a *rule* - .26 loses the ward line
            // outright (invariant 37k), and every number in this mode is held by
            // `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`. A mixing complaint is fixed
            // in the mix.
            Audio.Sfx("shot", .12f);
            if (shot.Weak) Audio.SfxVaried("chime2", .2f);
        }

        /// <summary>
        /// The muzzle flash: the projectile's own, from the pack that drew the projectile.
        ///
        /// <para>
        /// <b>The pack fires as three parts and all three are played</b>, which is what its own
        /// demo does — a flash, a comet, an impact. Showing the middle one alone is judging a
        /// sentence by its verb, and it is also the difference between a turret that emits
        /// something and a turret that <em>fires</em>.
        /// </para>
        /// <para>
        /// It falls back to the shared white <c>flash</c> reel, and then to a plain pop, because a
        /// missing reel must cost the thing it draws and never a white rectangle (invariant 7b).
        /// </para>
        /// </summary>
        void Flash(Vector2 at, float angle, Wards.WardModel model, int colour, Color tint,
                   float size)
        {
            var frames = MuzzleArt(model, colour);
            bool own = frames != null && frames.Length > 0;

            if (!own) frames = Blast("flash");

            if (frames == null || frames.Length == 0)
            {
                Pop(at, tint, .9f, .16f);
                return;
            }

            // The pack's own flash is drawn pointing along the shot; the shared one is a radial
            // burst with no direction in it, so it is spun instead of aimed.
            var puff = Lend(frames, own ? Color.white : Pal.A(Pal.Lift(tint, .55f), 1f),
                            Cell * (own ? 2.7f : 1.7f) * size, at,
                            own ? angle : Random.Range(0f, 360f), 33f, false,
                            own ? MuzzleAt : .5f);

            Ends(puff, own ? .32f : .3f);
        }

        void Land(Vector2 at, Color tint, Wards.WardModel model, int colour, float angle,
                  SiegeBolt shot)
        {
            var frames = HitArt(model, colour);

            if (frames != null && frames.Length > 0)
                Ends(Lend(frames, Color.white,
                          Cell * (shot.Killed ? 4.1f : 3.2f) * BoltScale(model), at,
                          angle, 35f, false, .5f), .35f);

            Pop(at, shot.Weak ? Pal.Gold : tint, shot.Weak ? 1.9f : 1.2f, .24f);

            // A double is drawn as a *different kind* of hit rather than a bigger one: gold, a
            // ring, and sparks. It is the mode's one rule and the board is where it is said.
            if (shot.Weak)
            {
                Burst.Sparks(_fx, at, Pal.Gold, 8, Cell * 2.4f, Cell * .2f, .38f);
                Shockwave(at, Pal.Gold, 1.7f, .26f);
            }

            Number(shot.Raider, at, shot.Damage, shot.Weak);

            // **The bolt's own voice, and on the kill rather than on every landing.** A lit
            // line lands about eighteen hits a second (see `Number`, which tallies them for
            // exactly that reason), so a sound on each would double the busiest thing in the
            // mix to say something the damage figure already says. A kill is the beat worth
            // marking. Its own slot rather than `burst`, which a firepot's and a storm's kills
            // still play: a turret's kill gets the turret's voice.
            if (shot.Killed) Audio.SfxVaried("zap", .42f);

            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == shot.Raider && _mob[i].Body != null)
                    Tween.Punch(_mob[i].Body.transform, .1f, .14f);
        }
    }
}
