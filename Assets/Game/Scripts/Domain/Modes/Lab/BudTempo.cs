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
        public const float Burst = .22f;

        /// <summary>
        /// The most the whole chain after one tap may take.
        ///
        /// <para>
        /// <b>Doubled, and it is the answer to the one complaint this mode kept getting.</b>
        /// Reported as <em>"the animations happen too fast"</em>, and the number that caused it
        /// is this one: every single thing a wave draws — the wind-up, the ripple, the petals,
        /// the fall, the colour landing — is a fraction of <see cref="Wave"/>, which is this
        /// divided by the chain. At 3.60s the shipped finale's eight-wave tap gave each wave
        /// .45s, out of which the wind-up got .18s and the burst .27s; the petals of a burst
        /// were on screen for half a second and the whole grove fell in .167s, which is a
        /// teleport rather than a fall. Nothing was wrong with any one effect and none of them
        /// had time to be seen.
        /// </para>
        /// <para>
        /// The bound itself is not negotiable and has not moved in kind — a chain must still
        /// end, the rate must still give way, and a nine-wave cascade must still not be a
        /// nine-second freeze. What moved is where it sits: this is a mode commissioned to be
        /// generous (invariant 20k), its biggest chain is the best thing that happens in it,
        /// and the ceiling was set where a cascade could not be watched. Seven seconds for the
        /// deepest chain the ladder distinguishes is a carnival; 3.6 was a flicker.
        /// </para>
        /// </summary>
        public const float Ceiling = 8.00f;

        /// <summary>
        /// One wave at its full length, when there is room for it.
        ///
        /// <para>
        /// <b>Raised a second time, and the second raise bought a different thing from the
        /// first.</b> The first was about whether a gesture could be <em>seen</em>; this one is
        /// about whether a wave can be dealt <em>one flower at a time</em>. A wave of thirteen
        /// is thirteen separate things as far as the player is concerned, and the ripple that
        /// deals them (<see cref="Spread"/>) has to fit inside the burn alongside the hold and
        /// the fall. At a .55s burn there was no room: the ripple was squeezed until most of the
        /// wave went off together, which is the flat flicker the stagger exists to prevent. At
        /// .70s the thirteen are genuinely sequential and the grove still lands on time.
        /// </para>
        /// </summary>
        public const float WaveFull = 1.10f;

        /// <summary>And the floor under it, however far the chain runs.</summary>
        public const float MinWave = .46f;

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
            float charge = wave * .42f;
            if (charge < .14f) charge = .14f;
            return charge > .40f ? .40f : charge;
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

            float heave = .026f + (wave - 1) * .022f;
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
            float step = beat * .155f;
            if (step < .024f) step = .024f;
            return step > .115f ? .115f : step;
        }

        /// <summary>
        /// How much of a wave the ripple is allowed to take.
        ///
        /// It may never reach the whole of it, or a long wave would still be going off when the
        /// next one started and the chain would stop reading as waves at all — which is the one
        /// thing the stagger was added to improve.
        /// </summary>
        public const float Spread = .62f;

        /// <summary>
        /// And how much of it a wave's <em>cocoons</em> may take, which is nearly all of it.
        ///
        /// <para>
        /// <b>A critter getting out is the one thing in this mode that may not share a frame
        /// with another of itself.</b> Everything else a wave deals is a variation on the same
        /// event — thirteen flowers bursting is one gesture said thirteen times, and a ripple
        /// through it reads as a sweep. Four cocoons opening is four separate payoffs, each with
        /// its own note, halo, shockwave and creature, and dealt inside a third of a second they
        /// are one indistinguishable pile of gold.
        /// </para>
        /// <para>
        /// So they get their own, wider allowance. It stops short of the whole beat rather than
        /// running past it, which is what keeps the greeting bounded: a ripple that outlived its
        /// wave would go on opening cocoons after the chain that opened them had finished, and
        /// the last of them would arrive over the word at the end.
        /// </para>
        /// </summary>
        public const float GreetSpread = .95f;

        /// <summary>
        /// Where the nth flower of a wave of this many falls inside that ripple.
        ///
        /// <para>
        /// <b>The step gives way before the allowance does, and the version that clamped the
        /// other way round was silently drawing most of a wave at once.</b> It used to be
        /// <c>min(nth × step, most)</c> — so on a wave of thirteen the first four were dealt
        /// apart and the remaining nine all landed on the cap, in the same frame, which is
        /// exactly the flat flicker this function exists to break up and it got worse the bigger
        /// the wave got. The fix is to shorten the *step* until the whole set fits, so every
        /// flower of every wave is dealt at a distinct moment and the last one lands on the
        /// allowance rather than nine of them piling onto it.
        /// </para>
        /// <para>
        /// It reads as one long ripple on a big wave and as three clear beats on a small one,
        /// which is the right way round: a wave of three is three things the player can count,
        /// and a wave of thirteen is a wave.
        /// </para>
        /// </summary>
        public static float StaggerAt(int nth, int inWave, float beat)
            => StaggerAt(nth, inWave, beat, Spread);

        /// <summary>The same ripple over a share of the beat the caller chooses.</summary>
        public static float StaggerAt(int nth, int inWave, float beat, float spread)
        {
            if (nth <= 0 || inWave <= 1) return 0f;
            if (nth >= inWave) nth = inWave - 1;
            if (spread < 0f) spread = 0f; else if (spread > 1f) spread = 1f;

            float most = beat * spread;
            float step = StaggerStep(beat);

            float fit = most / (inWave - 1);
            if (step > fit) step = fit;

            return step * nth;
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
            float life = Burn(wave) * 2.1f;
            return life < .62f ? .62f : life;
        }

        // ------------------------------------------------------------------ the chain
        /// <summary>How long a bolt takes to lash from a burst to the flower beside it.</summary>
        public static float Strike(float wave)
        {
            float strike = Burn(wave) * .36f;
            return strike < .10f ? .10f : strike;
        }

        /// <summary>And how long it lingers after it has landed.</summary>
        public static float Linger(float wave)
        {
            float hold = Burn(wave) * .30f;
            return hold < .09f ? .09f : hold;
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

            float bloom = .06f + (waves - BudChain.CountFrom) * .055f;
            return bloom > .28f ? .28f : bloom;
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
            return wave < .26f ? wave : .26f;
        }

        /// <summary>
        /// How long the grove is given to land after the last wave, before anything else is
        /// allowed to happen to it.
        ///
        /// <para>
        /// <b>The beat before a celebration is part of the celebration.</b> The word used to
        /// slam in while the last flowers were still in the air and the whole board was
        /// repainted underneath it in the same frame — reported as the board resetting
        /// suddenly. This is the hold and the fall of the wave that has just ended, which is
        /// exactly how long the grove needs, so it is derived rather than chosen: a shorter
        /// board settles sooner and a deeper chain is not made to wait for a grove that has
        /// already stopped moving.
        /// </para>
        /// </summary>
        public static float Landing(int waves)
        {
            float burn = Burn(Wave(waves));
            return Settle(burn) + Rain(burn);
        }

        /// <summary>
        /// The word at the end, which is the one beat outside the cascade's ceiling.
        ///
        /// It is the longest single hold in the mode on purpose. Everything before it is the
        /// chain doing something; this is the game telling the player what they did, and a
        /// congratulation that leaves before it has been read is not one.
        /// </summary>
        public const float Fanfare = 1.60f;

        /// <summary>From this many waves a chain is worth a flash and a haptic.</summary>
        public const int BigFrom = 4;

        /// <summary>How hard the grove is shaken by a chain this far along.</summary>
        public static float Shake(int waves)
        {
            if (waves < BudChain.CountFrom) return 0f;

            float amount = 4f + (waves - BudChain.CountFrom) * 3.6f;
            return amount > 26f ? 26f : amount;
        }

        /// <summary>What a burst's note is pitched at, climbing through a chain.</summary>
        public static float Pitch(int wave)
        {
            if (wave < 1) wave = 1;

            float pitch = .88f + (wave - 1) * .09f;
            return pitch > 1.8f ? 1.8f : pitch;
        }

        /// <summary>The grove arriving, middle outward.</summary>
        public const float Entrance = .90f;

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
        public const float Hush = .95f;

        // ------------------------------------------------------------------ the grove falling
        /// <summary>
        /// How long a wave's fall is given, inside the beat that threw it.
        ///
        /// The grove has to be back on the ground before the next wave charges, or two waves are
        /// moving the same flowers at once — the bug this mode has paid for twice.
        /// </summary>
        /// <remarks>
        /// <b>A share of the burn and nothing else, which is a correctness change as much as a
        /// pacing one.</b> It used to carry a floor of .12s, and a floor on a fraction of
        /// something can exceed the thing it is a fraction of: at the shortest beats the floor
        /// won, the grove was still falling when the next wave charged, and two gestures were
        /// on one transform — which is the exact fault this mode has paid for twice.
        /// <c>TheGroveIsBackOnTheGroundBeforeItsOwnWaveEnds</c> is the line under it now, and it
        /// could not have been written while the floor was there.
        /// </remarks>
        public static float Rain(float burn)
        {
            if (burn < 0f) burn = 0f;

            float over = (burn - Settle(burn)) * .92f;
            return over > .48f ? .48f : over;
        }

        /// <summary>
        /// How long a wave's bursts are left standing on their own before the grove comes down
        /// on top of them.
        ///
        /// <para>
        /// <b>The beat that made a cascade read as one thing collapsing rather than as two
        /// things happening at once.</b> The fall used to be dealt in the same frame as the
        /// bursts it was falling into, so the flower above a burst started moving while the
        /// burst was still opening — and the player, who is watching the thing they caused,
        /// had it covered by the consequence of it before they had seen it. A tenth of a second
        /// of nothing is enough: the bunch goes, it is alone for an instant, and <em>then</em>
        /// the grove drops.
        /// </para>
        /// <para>
        /// It is taken out of the fall's own allowance rather than added beside it, which is
        /// <see cref="Rainfall"/>'s lesson in the one place that had not learned it: a hold
        /// added to a duration that was already the whole of the wave is a grove still falling
        /// when the next wave charges, which is two gestures on one transform and the fault this
        /// mode's view has paid for twice. <c>TheGroveIsBackOnTheGroundBeforeItsOwnWaveEnds</c>
        /// holds the pair together.
        /// </para>
        /// </summary>
        public static float Settle(float burn)
        {
            if (burn < 0f) burn = 0f;

            float hold = burn * .30f;
            return hold > .26f ? .26f : hold;
        }

        /// <summary>
        /// And how long one piece takes, given how many rows it has to travel.
        ///
        /// <b>Taller falls take longer, which is the whole of what makes a board feel heavy.</b>
        /// A flower dropping five rows and one dropping a single row in the same time reads as
        /// teleporting, and it is the commonest reason a falling board looks cheap. Bounded by
        /// the wave either way.
        /// </summary>
        public static float FallOver(float over, float rows)
        {
            if (over < 0f) over = 0f;
            if (rows < 1f) rows = 1f;

            float share = over * (.42f + .15f * rows);
            return share > over ? over : share;
        }

        /// <summary>Where one piece falls inside the wave's own ripple, so a column is not a wall.</summary>
        public static float RainAt(int nth, float over)
        {
            if (nth <= 0) return 0f;

            // Eight rather than six, because the widest shipped grove is eight columns and a
            // wrap shorter than the board deals two columns at once for no reason the player
            // can see. The share is bigger for the same reason the burst ripple's is: a grove
            // that comes down in one sheet is one event, and a grove that comes down column by
            // column is a grove.
            float step = over * .075f;
            float delay = (nth % 8) * step;
            return delay > over * .45f ? over * .45f : delay;
        }

        /// <summary>
        /// The whole of one piece's fall: when it starts, and how long it then takes.
        ///
        /// <para>
        /// <b>The two halves have to be bounded together, and they were not.</b>
        /// <see cref="Rain"/> promises the grove is back on the ground before the next wave
        /// charges, and <see cref="FallOver"/> keeps a fall inside that allowance — but the
        /// ripple's delay was <em>added</em> to the result, so a piece late in the ripple
        /// finished a third of a wave past the bound the comment claimed. That is two waves
        /// moving the same flowers at once, which is the fault this mode has paid for twice.
        /// A piece falls in what is left of the wave after its own wait, so a late one falls
        /// faster rather than later, and the sum is the wave's allowance whatever the ripple
        /// does.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>The fall is chosen first and the ripple gives way, and it used to be the other way
        /// round.</b> A piece's wait was taken out of the allowance and whatever was left became
        /// its fall — so the same drop, over the same distance, took up to <em>45% less time</em>
        /// depending only on which column it happened to be in. That is a board whose pieces fall
        /// at several different speeds inside one wave, and it was reported as the fall being
        /// sudden and stuttery: at the far end of the ripple a five-row drop was covering a third
        /// of a cell per frame. How long a thing takes to fall is a fact about how far it is
        /// falling and nothing else, so <see cref="FallOver"/> is asked first and the ripple is
        /// trimmed to fit in what is left. A tall drop therefore rides at the front of the wave
        /// with no wait at all, which is right — it has the furthest to come.
        /// </remarks>
        public static void Rainfall(int nth, float rows, float over,
                                    out float delay, out float fall)
        {
            fall = FallOver(over, rows);

            float room = over - fall;
            if (room < 0f) room = 0f;

            delay = RainAt(nth, over);
            if (delay > room) delay = room;
        }

        // ------------------------------------------------------- what would pop, breathing
        /// <summary>
        /// How much a flower that would set something off swells, and how slowly.
        ///
        /// <b>The smallest motion in the mode, deliberately.</b> It is drawn on many flowers at
        /// once and it must never compete with anything that is actually happening — it is a
        /// standing invitation rather than an event, so it is quieter than the white flower's
        /// breath and far quieter than a wind-up.
        /// </summary>
        public const float PopsSwell = .055f, PopsBreath = 1.7f;

        // ------------------------------------------------------- what a freed critter answers with
        /// <summary>
        /// How far a critter that is already out swells when a wave goes off near it.
        ///
        /// <para>
        /// <b>One pulse, and it is the only gesture a freed critter gets.</b> It used to be a
        /// punch — a damped sine through three half-cycles, which is a <em>wobble</em> — on a
        /// critter that was still a child of its cell, so a wave that burst the flower which had
        /// fallen onto its square span it right round with the cell as well (`BudView.Wind` turns
        /// the whole tile). A creature the player has just let out being spun by the scenery is
        /// the one thing on this board that should look settled, and it was the least settled
        /// thing on it. So the rotation is gone with the cell, and what is left is a single
        /// swell and back: the grove is saying *they are still there* rather than shaking them.
        /// </para>
        /// <para>
        /// Bigger than a flower's breath (<see cref="PopsSwell"/>) because it answers an event
        /// rather than standing as an invitation, and smaller than a wind-up because a critter
        /// that is out has nothing left to be decided about it.
        /// </para>
        /// </summary>
        public const float FreedPump = .20f;

        // ------------------------------------------------------------ and the moment they get out
        /// <summary>
        /// The greeting: how long a critter that has just been freed is held apart from the noise
        /// of the shell breaking, how far they swell in it, and the ring that closes around them.
        ///
        /// <para>
        /// <b>The payoff was drawn in the same register as the packaging, so it could not be
        /// seen.</b> Everything a cocoon opening draws — the star behind it, the shell whitening
        /// and going, the chips, two shockwaves, sparks, embers, a halo — is about the
        /// <em>cocoon</em>, and the creature arrived in the middle of all of it as one more thing
        /// moving. It was reported as no emphasis at all, on a build that was drawing eight
        /// separate effects. What was missing is not another effect: it is a beat where the
        /// creature is the only thing moving.
        /// </para>
        /// <para>
        /// So the greeting is deliberately <b>bigger and slower than the wave-answer pulse</b>
        /// (<see cref="FreedPump"/>) and lands after the shell's own noise has finished, which is
        /// what makes one gesture read as an event and the other as an acknowledgement. The ring
        /// closes <em>inward</em> over the first third and holds for the rest, so it is still
        /// there while the creature is swelling inside it.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Lengthened with <see cref="Ceiling"/> and by the same fraction. These are the one
        /// set of durations in this file that are constants rather than shares of the beat —
        /// deliberately, because what a creature getting out is worth does not depend on how
        /// deep the chain that freed them happened to run — but a constant beside a beat that
        /// has moved is a constant that has quietly changed its meaning, and the greeting was
        /// tuned to be the slowest thing on the board.
        /// </remarks>
        public const float FreedHold = .66f, FreedGreet = .44f;

        /// <summary>
        /// How wide the greeting ring is at rest, and where it closes in from.
        ///
        /// <para>
        /// <b>It hugs the creature rather than the cell.</b> A critter is drawn at .46 of a cell
        /// and stands at <c>FreedScale</c>, so it is a little over half a cell wide; a ring much
        /// past this one reaches into the squares beside it and is read as a shockwave — which
        /// this mode already draws two of on the same frame, and which says <em>something went
        /// off here</em> rather than <em>this one</em>.
        /// </para>
        /// </summary>
        public const float FreedRing = 1.15f, FreedRingFrom = 2.10f;

        /// <summary>How much of the greeting the ring spends closing, and how long it outlives it.</summary>
        public const float FreedRingClose = .34f, FreedRingOver = 1.25f;

        /// <summary>
        /// And how much of the creature's own swell the ring takes while it holds.
        ///
        /// <para>
        /// <b>A share rather than the whole of it, because only one thing may be growing.</b>
        /// Given the full <see cref="FreedGreet"/> the ring closed in and then went most of the
        /// way back out — measured, from 2.10 down to 1.09 and back to 1.32 — which reads as a
        /// bounce, and a bounce is the ring competing with the creature it is drawn around
        /// rather than holding it.
        /// </para>
        /// </summary>
        public const float FreedRingSwell = FreedGreet * .35f;

        /// <summary>
        /// And how long they take to reach the counter, and how small they are when they get
        /// there.
        ///
        /// <para>
        /// <b>A critter that is out does not stay in a square, because a square is the one thing
        /// the grove is allowed to rearrange.</b> Freeing empties that cell in the model — which
        /// is the whole point, since the grove then falls into it and the chain compounds — so
        /// anything left standing there is standing where a flower is about to come to rest. The
        /// alternative was to make the square a post the grove may not move, and it was built and
        /// measured: it takes the cascades out of the boards, because a chain compounds *by*
        /// falling into the hole a burst makes. See <c>CLAUDE.md</c> for the table.
        /// </para>
        /// <para>
        /// So they leave, and where they go is the answer to the only question the board is
        /// keeping score of: the critters readout. It ticks for them anyway, so the flight makes
        /// a number that was already changing into somewhere the reward visibly <em>went</em> —
        /// which is what the old row of standing critters was for, moved somewhere the falling
        /// grove cannot reach.
        /// </para>
        /// </summary>
        public const float FreedLeave = .46f;

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
    }
}
