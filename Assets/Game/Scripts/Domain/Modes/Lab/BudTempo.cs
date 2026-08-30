namespace GlimmerGrove.Modes
{
    /// <summary>
    /// How long everything in a grove takes, and where the satchel sits under it.
    ///
    /// <para>
    /// <b>Here rather than beside the paint, for <c>FallTempo</c>'s and <c>KeeperTempo</c>'s
    /// reason.</b> Motion is the one subsystem whose failures show up only in play, so the
    /// arithmetic has to be reachable without an Editor.
    /// </para>
    /// <para>
    /// <b>The rate gives way, and on this mode that is the whole tuning problem.</b> A nine-wave
    /// chain is the best thing that happens here and it must not become a nine-second freeze — so
    /// the cascade is bounded and each wave is faster the longer the chain is. But it must not
    /// become a blur either: <see cref="MinWave"/> is a floor under the per-wave beat, because a
    /// chain the eye cannot follow pays out nothing, and the whole mode is the paying out.
    /// </para>
    /// </summary>
    public static class BudTempo
    {
        /// <summary>A bud opening, before its pollen reaches anything.</summary>
        public const float Burst = .16f;

        /// <summary>The most the whole chain after one tap may take.</summary>
        public const float Ceiling = 3.60f;

        /// <summary>One wave at its full length, when there is room for it.</summary>
        public const float WaveFull = .62f;

        /// <summary>And the floor under it, however far the chain runs.</summary>
        public const float MinWave = .26f;

        // ------------------------------------------------------------------ the charge
        /// <summary>
        /// How long a bunch spins before it goes off.
        ///
        /// <para>
        /// <b>This is the half of a wave that was missing, and it is why the chain did not
        /// land.</b> Reported from play as <em>"it happens too fast"</em>, and the fault was not
        /// only the rate: a wave went from "nothing" to "gone" with no moment in between, so
        /// there was never an instant where the player could see <em>which flowers had matched</em>
        /// — which is the whole thing they just did. A wave is now two beats. First the bunch
        /// spins in place, faster and brighter, which points at itself; then it bursts.
        /// </para>
        /// <para>
        /// It is a fraction of the wave rather than a constant, so a nine-wave chain shortens
        /// the wind-up along with everything else and the whole cascade still lands inside
        /// <see cref="Ceiling"/>. The floor under it is what stops the longest chains losing the
        /// charge altogether, because a chain that reverts to blinking at wave six is a chain
        /// that stops paying out exactly where it should pay most.
        /// </para>
        /// </summary>
        public static float Charge(float wave)
        {
            float charge = wave * .40f;
            if (charge < .11f) charge = .11f;
            return charge > .28f ? .28f : charge;
        }

        /// <summary>And how long is left of the wave once the charge has had its share.</summary>
        public static float Burn(float wave)
        {
            float burn = wave - Charge(wave);
            return burn < .06f ? .06f : burn;
        }

        // ------------------------------------------------------- the shape of a wind-up
        /// <summary>
        /// <b>A chain escalates in amplitude, never in duration, and that is forced rather than
        /// chosen.</b>
        ///
        /// <para>
        /// The obvious way to make a deep chain feel bigger is to give its later waves more
        /// time. It is not available here and it is worth understanding why: <see cref="Wave"/>
        /// divides <see cref="Ceiling"/> across the whole chain, so every wave of a nine-wave
        /// cascade is <em>shorter</em> than the single wave of an ordinary tap. Lengthening the
        /// late ones would either break the ceiling — a nine-second freeze, which is what the
        /// ceiling exists to prevent — or steal from the early ones, which is a chain that
        /// starts blurred and ends legible, exactly backwards.
        /// </para>
        /// <para>
        /// So what grows is how far a flower travels, not how long it takes: each wave winds up
        /// <em>bigger</em> than the last in the same or less time, which reads as accelerating
        /// rather than as dragging. Everything below is amplitude for that reason.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>Front-loaded onto the first three waves, because that is the whole chain the mode
        /// ships.</b> <c>b01_thicket</c> is one board whose best opening tap runs three waves,
        /// and most taps run one or two — so a ladder that spread its range over nine spent
        /// almost all of it on waves nobody reaches. The first version did exactly that and the
        /// escalation was invisible in play.
        /// </remarks>
        public const float SwellFrom = .62f;

        /// <summary>How much more each wave of a chain swells than the one before it.</summary>
        public const float SwellStep = .22f;

        /// <summary>
        /// And the ceiling on it.
        ///
        /// <para>
        /// A flower is drawn at about .72 of its cell, so a scale of 2.20 makes it a half again
        /// wider than the cell it stands in. That overlap is wanted — a bunch is three or more
        /// flowers <em>touching</em>, so they swell into each other and the bunch reads as one
        /// thing under pressure rather than three things growing — but past this it stops being
        /// a bunch crowding and starts being a grid that has lost its shape.
        /// </para>
        /// </summary>
        public const float SwellMost = 1.20f;

        /// <summary>How much bigger a flower gets at the top of its wind-up, on this wave.</summary>
        public static float Swell(int wave)
        {
            if (wave < 1) wave = 1;

            float swell = SwellFrom + (wave - 1) * SwellStep;
            return swell > SwellMost ? SwellMost : swell;
        }

        /// <summary>
        /// How far a flower dips <em>before</em> it swells.
        ///
        /// <para>
        /// <b>The anticipation, and it is what separates "about to explode" from "getting
        /// bigger".</b> A shape that only ever grows is being inflated by something outside it;
        /// a shape that gathers itself first is doing it on purpose. It costs a fraction of a
        /// beat that the wind-up was spending on its slowest, least interesting part anyway —
        /// the first sliver of an accelerating curve, where almost nothing is happening.
        /// </para>
        /// <para>
        /// Constant across the chain while <see cref="Swell"/> escalates, so there is exactly
        /// one thing growing wave to wave. The crouch is the <em>tell</em>: it means the same
        /// thing every time it happens, which is what lets it be read at a glance on the ninth
        /// wave as well as on the first.
        /// </para>
        /// </summary>
        public const float Recoil = .20f;

        /// <summary>The share of a wind-up spent gathering, before it starts to grow.</summary>
        public const float Crouch = .26f;

        /// <summary>
        /// Where the growing stops and the <b>hold</b> begins, as a share of the wind-up.
        ///
        /// <para>
        /// <b>This is the fix for the thing that made the first version of all this invisible,
        /// and it is worth stating exactly.</b> The curve accelerated — <c>v²</c> — all the way
        /// to the burst, which sounds right and is wrong: an accelerating curve is near its
        /// destination only at the very end, so measured over the charge, a flower was within 5%
        /// of its peak size for <b>3% of the beat, about 1.6 frames at 60fps</b>. The peak was a
        /// flash, not a state. Raising <see cref="SwellFrom"/> against that changes the number
        /// nobody can see and nothing else, which is precisely what it did: reported from play as
        /// no change at all, on a build that was running the new code.
        /// </para>
        /// <para>
        /// So the flower now <em>arrives</em> at full size and <em>sits there</em> — decelerating
        /// into the hold rather than accelerating past it — and spends about a third of its
        /// wind-up at peak instead of a frame and a half. That is what makes a size legible. The
        /// anticipation is carried by the crouch, which is what an anticipation is for; asking
        /// the growth curve to do it as well is what cost the dwell.
        /// </para>
        /// </summary>
        public const float Peak = .66f;

        /// <summary>
        /// The scale a winding flower is drawn at, a fraction <paramref name="t"/> of the way
        /// through its charge on this <paramref name="wave"/>.
        ///
        /// <para>
        /// One function rather than a curve in the view, for <c>GladeFanfare.Hop</c>'s reason:
        /// the numbers that decide whether a gesture reads as a build or as a wobble are worth
        /// having a test on. Three phases — it gathers to <c>1 - Recoil</c>, springs out to
        /// <c>1 + Swell(wave)</c>, and holds there until it goes off. See <see cref="Peak"/> for
        /// why the hold is the part that matters.
        /// </para>
        /// </summary>
        public static float WindScale(float t, int wave)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

            if (t <= Crouch)
            {
                // Out-quad down: it gathers quickly and is already waiting by the time the
                // growth takes over, so the two phases never look like one soft wobble.
                float u = t / Crouch;
                return 1f - Recoil * (1f - (1f - u) * (1f - u));
            }

            float full = 1f + Swell(wave);
            if (t >= Peak) return full;

            // Out-quad up: off the mark hard and easing into the hold, so the size is reached
            // early and kept rather than touched on the last frame.
            float v = (t - Crouch) / (Peak - Crouch);
            return (1f - Recoil) + (Swell(wave) + Recoil) * (1f - (1f - v) * (1f - v));
        }

        /// <summary>
        /// How far toward white a winding flower is taken, a fraction <paramref name="t"/>
        /// through its charge.
        ///
        /// <para>
        /// Capped at <see cref="Matched"/> until the flower has stopped growing, and only then
        /// pushed to <see cref="Critical"/>. The cap is the older rule and its reasoning is
        /// unchanged: the charge exists to show <em>which</em> flowers matched, and a bunch that
        /// goes fully white has thrown that away in the fraction of a second it was meant to be
        /// saying it. What the hold adds is somewhere safe to spend the rest — by then the
        /// player has had the whole crouch and spring to read the colour, so the last stretch is
        /// free to say "and now it is going to go off".
        /// </para>
        /// </summary>
        public static float WindWhite(float t)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

            if (t <= Peak) return Matched * (t / Peak) * (t / Peak);

            float v = (t - Peak) / (1f - Peak);
            return Matched + (Critical - Matched) * v;
        }

        /// <summary>How white a flower is by the time it has finished growing.</summary>
        public const float Matched = .62f;

        /// <summary>And by the time it goes off.</summary>
        public const float Critical = .92f;

        /// <summary>
        /// How hard the whole thicket heaves on a wave this far along.
        ///
        /// <para>
        /// The chain's escalation said at grove scale rather than at flower scale, and the half
        /// that was missing: the board's answer to a wave was a shake plus a punch of between
        /// 1.2% and 3.6%, which is under the threshold at which a scale change on a whole screen
        /// is noticed at all. A player watching thirteen flowers go off has no attention left for
        /// a 2% nudge on one of them, so the thing that has to grow is the thing they cannot
        /// avoid looking at.
        /// </para>
        /// </summary>
        public static float Heave(int wave)
        {
            if (wave < 1) return 0f;

            float heave = .022f + (wave - 1) * .020f;
            return heave > .085f ? .085f : heave;
        }

        /// <summary>How far a flower spins through its wind-up, in degrees, on this wave.</summary>
        public static float WindSpin(int wave)
        {
            if (wave < 1) wave = 1;

            float spin = SpinFrom + (wave - 1) * SpinStep;
            return spin > SpinMost ? SpinMost : spin;
        }

        /// <summary>A little over one turn, which is what reads as a wind-up rather than a twitch.</summary>
        public const float SpinFrom = 420f;

        /// <summary>Faster every wave, for <see cref="SwellFrom"/>'s reason — amplitude, not time.</summary>
        public const float SpinStep = 80f;

        /// <summary>
        /// And the ceiling. Past about two turns inside a tenth of a second a spin stops being a
        /// direction and becomes a flicker, which says nothing at all.
        /// </summary>
        public const float SpinMost = 760f;

        // ------------------------------------------------------------------ inside one wave
        /// <summary>
        /// How long apart two flowers of the <em>same</em> wave go off.
        ///
        /// <para>
        /// <b>A wave is a ripple, not a frame.</b> Everything in one wave bursts at the same
        /// instant as far as the model is concerned, and drawing it that way is what made the
        /// biggest tap on the shipped board — thirteen flowers — read as a single flat flicker
        /// rather than as thirteen things happening. A few tens of milliseconds between them is
        /// enough for the eye to count them, and it costs the wave nothing because the ripple is
        /// bounded to a fraction of the beat: however many go off, the wave still ends on time.
        /// </para>
        /// </summary>
        public static float StaggerStep(float beat)
        {
            float step = beat * .11f;
            if (step < .016f) step = .016f;
            return step > .062f ? .062f : step;
        }

        /// <summary>Where the nth flower of a wave of this many falls inside that ripple.</summary>
        public static float StaggerAt(int nth, int inWave, float beat)
        {
            if (nth <= 0 || inWave <= 1) return 0f;

            float step = StaggerStep(beat);
            float delay = nth * step;

            // The ripple may never eat the beat it lives in, or a long wave would still be
            // going off when the next one starts and the chain would stop reading as waves.
            float most = beat * .45f;
            return delay > most ? most : delay;
        }

        // ------------------------------------------------------------------ one burst
        /// <summary>
        /// How long a flower's petals stay in the air after it has gone off.
        ///
        /// Deliberately longer than the beat that spawned them: the shrapnel of one wave is
        /// still falling while the next wave charges, which is what makes a long chain look like
        /// one continuous event rather than a row of separate ones.
        /// </summary>
        public static float Shrapnel(float wave)
        {
            float life = Burn(wave) * 1.9f;
            return life < .42f ? .42f : life;
        }

        // ------------------------------------------------------------------ the chain
        /// <summary>How long a bolt takes to lash from a burst to the flower beside it.</summary>
        public static float Strike(float wave)
        {
            float strike = Burn(wave) * .34f;
            return strike < .07f ? .07f : strike;
        }

        /// <summary>And how long it lingers after it has landed.</summary>
        public static float Linger(float wave)
        {
            float hold = Burn(wave) * .26f;
            return hold < .06f ? .06f : hold;
        }

        /// <summary>
        /// How brightly the screen answers a wave this far along, as a fraction of white.
        ///
        /// <b>Nought below <c>BudChain.CountFrom</c>, deliberately.</b> A flash on every single
        /// tap is a flash that says nothing, and this mode's ordinary tap is one wave.
        /// </summary>
        public static float Bloom(int waves)
        {
            if (waves < BudChain.CountFrom) return 0f;

            float bloom = .05f + (waves - BudChain.CountFrom) * .045f;
            return bloom > .26f ? .26f : bloom;
        }

        /// <summary>How long a chain of this many waves runs for.</summary>
        public static float Cascade(int waves)
        {
            if (waves <= 0) return 0f;
            return Wave(waves) * waves;
        }

        /// <summary>How long one wave of a chain of this many gets.</summary>
        public static float Wave(int waves)
        {
            if (waves <= 0) return 0f;

            float share = Ceiling / waves;
            if (share > WaveFull) share = WaveFull;
            return share < MinWave ? MinWave : share;
        }

        /// <summary>How long the running count is given to spring.</summary>
        public static float CountPop(int waves)
        {
            float wave = Wave(waves) * .8f;
            return wave < .18f ? wave : .18f;
        }

        /// <summary>
        /// The word at the end, which is the one beat outside the cascade's ceiling.
        ///
        /// It is the longest single hold in the mode on purpose. Everything before it is the
        /// chain doing something; this is the game telling the player what they did, and a
        /// congratulation that leaves before it has been read is not one.
        /// </summary>
        public const float Fanfare = 1.15f;

        /// <summary>From this many waves a chain is worth a flash and a haptic.</summary>
        public const int BigFrom = 4;

        /// <summary>How hard the grove is shaken by a chain this far along.</summary>
        public static float Shake(int waves)
        {
            if (waves < BudChain.CountFrom) return 0f;

            float amount = 3f + (waves - BudChain.CountFrom) * 3.2f;
            return amount > 22f ? 22f : amount;
        }

        /// <summary>What a burst's note is pitched at, climbing through a chain.</summary>
        public static float Pitch(int wave)
        {
            if (wave < 1) wave = 1;

            float pitch = .88f + (wave - 1) * .09f;
            return pitch > 1.8f ? 1.8f : pitch;
        }

        /// <summary>The grove arriving, middle outward.</summary>
        public const float Entrance = .70f;

        public static float EntranceDelay(int x, int y, int width, int height)
        {
            if (width <= 1 && height <= 1) return 0f;

            float cx = (width - 1) * .5f, cy = (height - 1) * .5f;
            float dx = x - cx, dy = y - cy;
            float far = cx > cy ? cx : cy;
            if (far <= 0f) return 0f;

            float across = dx < 0f ? -dx : dx;
            float down = dy < 0f ? -dy : dy;
            float distance = across > down ? across : down;

            return distance / far * Entrance * .60f;
        }

        /// <summary>The grove's own celebration when the last critter is out.</summary>
        public const float Hush = .70f;

        // ------------------------------------------------------------------ the hint
        /// <summary>How long the mark takes to arrive on the flower it is pointing at.</summary>
        public const float HintArrive = .42f;

        /// <summary>
        /// How long the ripple that shows what the tap would set off takes to cross the grove.
        ///
        /// <b>The mark says where and the ripple says how much, and the second is what a hint on
        /// this mode is actually worth.</b> Everywhere else a hint is a way past a board that has
        /// stopped somebody; here the boards do not stop anybody (invariant 20k), so what a hint
        /// buys is the <em>big</em> version of a move they could have made anyway. Showing only
        /// the cell would sell the smaller half of that.
        /// </summary>
        public const float HintRipple = .55f;

        /// <summary>
        /// How long the mark stands before it gives up and the hint is counted as taken.
        ///
        /// Long enough to think with and short enough that a phone put down mid-level is not
        /// still being pointed at when it comes back. It is not a deadline on anything: the mark
        /// going away costs nothing and the tap it named is still there.
        /// </summary>
        public const float HintHold = 12f;

        /// <summary>One breath of the ring around a marked flower.</summary>
        public const float HintPulse = 1.05f;

        // ------------------------------------------------------------------ the finish
        /// <summary>
        /// How long the rings take to cross the grove when the last critter is out.
        ///
        /// Inside <see cref="Hush"/> on purpose: the finale is a beat the player is already
        /// waiting through, so everything in it has to fit rather than lengthen it.
        /// </summary>
        public const float Sweep = .52f;

        /// <summary>Where the nth of this many freed critters leaps, inside the hush.</summary>
        public static float CheerAt(int nth, int of)
        {
            if (nth <= 0 || of <= 1) return 0f;

            float step = Hush * .42f / (of - 1);
            return nth * step;
        }
    }
}
