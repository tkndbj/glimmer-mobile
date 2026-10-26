namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>What a long match is worth on the ladder and the Infinite lane</b> (MODES.md 37fk): a run
    /// of exactly four merges into a gem bomb, and a run of five or more fires a salvo of rockets
    /// from the field at the hill. Every figure the two payoffs run on, in one place.
    ///
    /// <para>
    /// <b>Never on the Shuffle lane, and that is one predicate rather than a promise.</b> The lane
    /// sells its own long-match payoff as a card (<c>power_gems</c>, MODES.md 59m), so a board
    /// only forges when its rules say so (<c>SiegeRules.LongMatches</c>, read off
    /// <see cref="SiegeLayout.LongMatches"/>) <em>and</em> it holds the plain line
    /// (<see cref="SiegeBoosts.IsIdentity"/>) - two independent locks, either of which keeps a
    /// Shuffle board exactly the board it was.
    /// </para>
    /// <para>
    /// <b>It draws nothing from any stream</b> (invariant 41). Which run forges, where the bomb
    /// stands, what a blast takes and whom a rocket is aimed at are all read off the field and the
    /// hill as they stand, so the refill that follows deals exactly the gems it would have dealt
    /// and every shipped seed still deals its field.
    /// </para>
    /// <para>
    /// <b>Both payoffs only ever add</b>: a merge pays the run's own fuel <em>and</em>
    /// <see cref="MergeGems"/> more, a blast clears gems that pay their own colours, and a rocket
    /// is damage no ward was asked for. So every holdability gate measured without them is still
    /// a floor (a rung holdable on the starter line without them is holdable with them); what
    /// moves is how easily a run comes in under its star lines, which are the owner's sweep to set
    /// (37cb).
    /// </para>
    /// </summary>
    public static class SiegeLongMatch
    {
        // ------------------------------------------------------------------ the bomb
        /// <summary>The run length that merges into a gem bomb: exactly four.</summary>
        public const int BombRun = 4;

        /// <summary>
        /// How far a bomb's blast reaches, in cells, counted the way a king walks (Chebyshev) - so
        /// one is the three-by-three square round the bomb.
        /// </summary>
        public const int BombReach = 1;

        /// <summary>
        /// <b>What merging pays, on top of the run's own fuel</b>, in gems' worth of the run's
        /// colour. The owner's rule (2026-10-26): a merge is not free, it feeds the turret - and
        /// paying twice is fine. Three of the four gems clear and pay as any match does (the fourth
        /// <em>is</em> the bomb), and the merge adds this, so a four that forges pays five gems'
        /// worth where a plain four paid four.
        /// </summary>
        public const int MergeGems = 2;

        // ------------------------------------------------------------------ the salvo
        /// <summary>The run length from which a match fires a salvo instead of forging: five.</summary>
        public const int SalvoRun = 5;

        /// <summary>
        /// How many rockets a run of <paramref name="length"/> fires: three for five, one more for
        /// every gem past it, never more than <see cref="MostRockets"/>. Nought below
        /// <see cref="SalvoRun"/>.
        /// </summary>
        public static int RocketsFor(int length)
        {
            if (length < SalvoRun) return 0;

            int rockets = length - 2;
            return rockets > MostRockets ? MostRockets : rockets;
        }

        /// <summary>The most rockets one run may fire, however long it is.</summary>
        public const int MostRockets = 5;

        /// <summary>
        /// What one rocket takes off a <em>baseline</em> raider (health at ten tenths): a bit over a
        /// third of a creeper (<see cref="SiegeTuning.CreeperHealth"/>). <b>Some damage, but not
        /// that much</b> - the owner's words - so a salvo is a real blow on the hill and never a
        /// substitute for the line.
        /// </summary>
        public const int RocketDamage = 70;

        /// <summary>
        /// <b>How a rocket keeps up as the hill toughens</b>, in per cent of the hill's own health
        /// bonus: at 130 a raider carrying +100% health (twenty tenths) takes +130% from a rocket.
        /// The owner's figure (2026-10-26: "scale 1.3 with the health bonus of enemies on the
        /// hill"), so a salvo is worth a little more of a late raider than of an early one -
        /// enough to stay worth having in the Infinite lane's deep waves, never a share that grows
        /// without bound faster than the hill does.
        /// </summary>
        public const int RocketScalePercent = 130;

        /// <summary>
        /// What one rocket takes off a raider whose health is <paramref name="healthTenths"/>
        /// tenths of its kind's own (<see cref="SiegeSurge.HealthTenths"/>): the baseline
        /// <see cref="RocketDamage"/>, plus <see cref="RocketScalePercent"/> of the bonus.
        ///
        /// <b>Integer throughout</b> - the project's rule about a float deciding a hit - and never
        /// below one, for <c>SiegeSurge.Hurt</c>'s reason.
        /// </summary>
        public static int RocketDamageAt(int healthTenths)
        {
            int bonus = healthTenths - 10;
            if (bonus < 0) bonus = 0;

            // Hundredths of the baseline: a hundred, plus the bonus (tenths) times the slope.
            int scale = 100 + bonus * RocketScalePercent / 10;
            int damage = RocketDamage * scale / 100;

            return damage < 1 ? 1 : damage;
        }

        /// <summary>
        /// Seconds from the moment a salvo's beat begins to the moment its rockets land, in the
        /// run's own clock. Booked exactly as a match's fuel is (invariant 37s): the rocket may not
        /// land before the drawing of it does, so the view flies it for this long (stretched by
        /// whatever bends the clock) and the model applies the damage when it arrives.
        /// </summary>
        public const float RocketFlight = .74f;

        /// <summary>
        /// When a salvo fired on beat <paramref name="depth"/> of a turn lands, counted from the
        /// turn: the swap (or a tapped bomb's fuse, which the view burns for the same
        /// <see cref="SiegeTuning.SwapFor"/>), the beats before it, and the flight.
        /// </summary>
        public static float RocketLands(int depth)
            => SiegeTuning.SwapFor + (depth > 1 ? depth - 1 : 0) * SiegeTuning.BeatFor + RocketFlight;
    }
}
