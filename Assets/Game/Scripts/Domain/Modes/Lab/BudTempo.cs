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
        /// <para>
        /// <b>And a third time, for the grove coming down.</b> A fall's length is now its
        /// distance at a fixed speed (<see cref="Pace"/>) rather than a share of the beat, which
        /// is what stopped the tall drops tearing — but a speed and a fixed allowance cannot both
        /// be honoured, so past about five rows the allowance still wins and the drop still
        /// hurries. At a .70s burn that bit at four rows on a seven-high grove, which is most of
        /// the deep drops the finale produces. .85s moves it to six and costs the chain nothing
        /// at all past seven waves, where <see cref="Ceiling"/> is what binds and this is not
        /// reached.
        /// </para>
        /// </summary>
        public const float WaveFull = 1.25f;

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
        /// <remarks>
        /// <b>And then it was cut to a third, which is the same number corrected twice in
        /// opposite directions — the part worth keeping.</b> It went from a flat .34 to
        /// .62–1.20 because the wind-up was reported as invisible, and the raise is not what
        /// fixed that: <see cref="Peak"/> was. A flower reaching its full size on the last frame
        /// is a flash whatever size it reaches, and one that arrives early and <em>holds</em> is
        /// legible at any size at all. With the dwell in place the size was free to come back
        /// down, and it had to: reported from play as <em>"when a chain reaction happens, it is
        /// too much"</em>, which is precisely what thirteen flowers each swelling to half again
        /// wider than their own cell look like when they do it together. The ladder still
        /// climbs and it climbs a third as far — what says "this is a deeper wave" is the
        /// crouch, the hold and <see cref="Heave"/> at grove scale, and the swell is now the
        /// smallest of the four rather than the loudest.
        /// </remarks>
        public const float SwellFrom = .30f;

        /// <summary>How much more each wave of a chain swells than the one before it.</summary>
        public const float SwellStep = .10f;

        /// <summary>
        /// And the ceiling on it.
        ///
        /// <para>
        /// A flower is drawn at about .72 of its cell, so a scale of 1.52 leaves it a little
        /// wider than the cell it stands in. That overlap is wanted and is all of it that is —
        /// a bunch is three or more flowers <em>touching</em>, so they crowd into each other and
        /// read as one thing under pressure rather than three things growing. Past about this
        /// the grove stops being a grid: at 2.20 a flower was half again wider than its square,
        /// which is a board losing its shape every time a wave lands on it, and it is also what
        /// pushed an edge flower far enough past the board to be seen outside it.
        /// </para>
        /// </summary>
        public const float SwellMost = .52f;

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

        /// <summary>
        /// A third of a turn: a flower <em>leaning</em> into what it is about to do.
        ///
        /// <para>
        /// <b>It was 420° — a full turn and a sixth — and that was the loudest half of the
        /// complaint <see cref="SwellFrom"/> answers.</b> Past about a half turn a wind-up stops
        /// being a lean and becomes a spin, and a spin is the one gesture in this mode that says
        /// nothing about <em>which</em> flowers matched: it is the same whirl whatever colour is
        /// underneath it, so thirteen of them at once is thirteen things moving and nothing
        /// being said. A lean keeps the flower's face pointed at the player, which is where the
        /// colour is.
        /// </para>
        /// </summary>
        public const float SpinFrom = 120f;

        /// <summary>Faster every wave, for <see cref="SwellFrom"/>'s reason — amplitude, not time.</summary>
        public const float SpinStep = 26f;

        /// <summary>
        /// And the ceiling, which is now a little over a half turn. Past that a wind-up this
        /// short is a flicker, and a flicker says nothing at all.
        /// </summary>
        public const float SpinMost = 190f;

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
        /// The fastest a piece may ever travel, in cells a second.
        ///
        /// <para>
        /// <b>This is the number the fall is built out of, and expressing it any other way is
        /// what made the grove skip.</b> A duration was a <em>share of the wave</em> — a fall of
        /// one row took .42 of the allowance and each further row added .15 — which saturates:
        /// past about four rows every drop took the whole allowance, so a six-row drop and a
        /// four-row drop finished together and the six-row one was half again faster. Inside one
        /// wave a board therefore fell at three different speeds at once, the tallest drop was
        /// the fastest, and the tallest drop is the one everybody is looking at. Reported as the
        /// flowers falling <em>"too suddenly, not smooth, like they skip frames"</em>, and
        /// measured it really was: the deepest drop on the shipped finale covered better than a
        /// third of a cell in a frame, which for a shape .72 of a cell wide is a picture jumping
        /// half its own width between one frame and the next.
        /// </para>
        /// <para>
        /// A speed is the right shape because it is the thing that has to be bounded: the eye
        /// reads a moving picture as continuous while it overlaps itself frame to frame, so what
        /// matters is how far a flower goes in a sixtieth of a second and not what fraction of a
        /// beat it took. In cells rather than in pixels because a cell is the one length this
        /// mode has that is the same on every phone — <c>BudView</c> sizes everything off it, so
        /// a bound in cells is a bound in <em>flower widths</em> at any resolution, which is what
        /// the eye is actually measuring against.
        /// </para>
        /// <para>
        /// Ten cells a second is a sixth of a cell a frame at 60fps, so a flower always overlaps
        /// itself by better than three quarters. It is a <em>peak</em> rather than an average —
        /// see <see cref="Curve"/> — and it is the peak because that is where the tearing is.
        /// </para>
        /// </summary>
        public const float Pace = 10f;

        /// <summary>
        /// The shape of a fall: distance covered is <c>t</c> to this power.
        ///
        /// <para>
        /// <b>It is also exactly the ratio of a fall's fastest instant to its average</b>, which
        /// is why it lives beside <see cref="Pace"/> and why <see cref="Falling"/> can be one
        /// line. A curve <c>t^c</c> covering a distance <c>d</c> in a time <c>f</c> has speed
        /// <c>c·d·t^(c-1)/f</c>, so its last instant is <c>c</c> times its mean.
        /// </para>
        /// <para>
        /// <b>Gentler than gravity, and that is a drawing decision rather than a physical one.</b>
        /// Real gravity is <c>t²</c> and peaks at twice its average, which spends the whole
        /// budget on the last few frames — the ones already closest to tearing. 1.40 still
        /// accelerates the whole way down, which is what says a thing was dropped rather than
        /// slid, and it costs a fifth off the peak.
        /// </para>
        /// </summary>
        public const float Curve = 1.40f;

        /// <summary>
        /// How long one piece takes to come down, given how many rows it has to travel.
        ///
        /// <para>
        /// <b>Its distance at one fixed speed, and nothing may clamp it.</b> That is rule 2 of
        /// <see cref="BudStage"/> and it is the whole of what makes a board feel heavy: a flower
        /// dropping five rows and one dropping a single row in the same time reads as teleporting,
        /// and it is the commonest reason a falling board looks cheap.
        /// </para>
        /// <para>
        /// <b>The clamp this replaced is the fault, not a detail of it.</b> A fall used to be
        /// fitted into a per-wave budget — the distance at this pace <em>or</em> whatever was
        /// left, whichever was smaller — so the moment a wave ran short the tall drops were the
        /// ones made to hurry, and inside one wave a six-row drop fell half again faster than the
        /// one-row drop beside it. Measured on the shipped finale that came to better than a
        /// third of a cell in a frame, which for a shape .72 of a cell wide is a picture jumping
        /// half its own width between one frame and the next: reported as the flowers falling
        /// <em>"too suddenly, not smooth, like they skip frames"</em>, which is exactly what it
        /// was. A budget that cannot be met is met by lengthening the <em>wave</em>, and the
        /// ceiling over the whole chain is met by squeezing the slack — never by moving a piece
        /// faster than the eye.
        /// </para>
        /// </summary>
        public static float Falling(float rows)
        {
            if (rows < 1f) rows = 1f;

            return rows * Curve / Pace;
        }

        // ------------------------------------------------------------------ the score
        /// <summary>
        /// How long a bunch gathers before the first of it goes off.
        ///
        /// A constant rather than a share of the wave, which is the change <see cref="BudStage"/>
        /// made possible: a wave is now as long as what happens in it, so the wind-up no longer
        /// has to be squeezed to make room for the fall. It is slack, so a chain that has to meet
        /// <see cref="Ceiling"/> takes it out of here before it takes it out of anything moving.
        /// </summary>
        public const float Wind = .30f;

        /// <summary>Between two flowers of one wave going off, at most.</summary>
        public const float BurstStep = .055f;

        /// <summary>
        /// And the most the whole ripple may take, however many are in the wave.
        ///
        /// The step shortens until the set fits rather than the tail being clipped, so a wave of
        /// three is three clear beats and a wave of thirteen is one long ripple — and neither is
        /// a clump, which is what the ripple exists to prevent.
        /// </summary>
        public const float BurstBody = .34f;

        /// <summary>After the burst that sent it, colour lands on the flower beside it.</summary>
        public const float WashLag = .085f;

        /// <summary>And after the burst that hit it, a cocoon answers.</summary>
        public const float CrackLag = .10f;

        /// <summary>
        /// The least there may ever be between two critters getting out.
        ///
        /// <b>A floor rather than a share, because what has to be true is about a person.</b>
        /// Each of these carries a sound, a halo, a shockwave and a creature, and the chapter's
        /// finale frees ten on one wave. Whether the player sees each of them is not a fact about
        /// how long the wave happens to be.
        /// </summary>
        public const float GreetLag = .26f;

        /// <summary>
        /// Between a flower going off and the grove coming down into the hole.
        ///
        /// The player watches what they did, and <em>then</em> watches what was above it come
        /// down — which is the beat that makes a cascade read as one thing collapsing rather than
        /// as two unrelated events.
        /// </summary>
        public const float Hold = .11f;

        /// <summary>
        /// Between one piece of a column leaving and the piece above it following.
        ///
        /// It is what makes a column read as collapsing rather than sliding as a block, and it is
        /// a correctness rule as much as a look: two pieces of one column moving together overlap.
        /// </summary>
        public const float RowLag = .050f;

        /// <summary>A breath between one wave finishing and the next gathering.</summary>
        public const float WaveGap = .06f;

        /// <summary>
        /// How long the grove takes to say it has ripened one for the player.
        ///
        /// Slower than anything a bunch does, and the ring closes <b>inward</b> — which is why it
        /// needs a size to close from as well as one to close to. Everything else in this mode
        /// expands, so a contraction is unmistakable without being loud.
        /// </summary>
        public const float Ripen = .62f;

        /// <summary>Where the ripen's ring starts, in cells, and where it closes to.</summary>
        public const float RipenFrom = 2.30f, RipenTo = .80f;

        /// <summary>
        /// Where in a wave's ripple the grove answers it — the jolt, the ring, the shake, the flash.
        ///
        /// <b>On the body of the wave rather than on its first frame.</b> These used to fire the
        /// instant the wave's first flower went off, so on a thirteen-flower wave the screen had
        /// already answered before most of the wave existed.
        /// </summary>
        public const float AnswerAt = .45f;

        /// <summary>
        /// The most a chain's slack may be squeezed to meet <see cref="Ceiling"/>.
        ///
        /// Below this the pauses stop being pauses, and a chain that still does not fit is
        /// allowed to run long instead — which is the right way to be wrong, because the
        /// alternative is moving the pieces faster than the eye.
        /// </summary>
        public const float SlackFloor = .55f;


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
