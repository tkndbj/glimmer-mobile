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
    }
}
