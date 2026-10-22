namespace GlimmerGrove.Layout
{
    /// <summary>
    /// When each beat of the first steps' ceremonies lands, in seconds from the panel appearing:
    /// the turret delivered (<c>WardGiftOverlay</c>), the chests delivered (<c>ChestGiftOverlay</c>)
    /// and the welcome bonus's day stamped (<c>WelcomeStepOverlay</c>).
    ///
    /// <para>
    /// <b>In Domain because timing is the one thing about a ceremony nothing else can prove</b>
    /// (CRAFT.md: timing rules live in Domain and are tested). <c>GiftTempoTests</c> holds three
    /// promises: every sequence is in order, an impact lands exactly when its fall ends (the
    /// shake, the dust and the sound are scheduled on the impact, the motion on the fall, and the
    /// two must not drift apart), and a player is never kept from the way onward for longer than
    /// <see cref="MostToKey"/> - a payoff a new player has to wait out is a payoff that costs them.
    /// </para>
    /// </summary>
    public static class GiftTempo
    {
        /// <summary>The longest any of these ceremonies may hold its way onward back.</summary>
        public const float MostToKey = 2.4f;

        // ------------------------------------------------------------- the turret
        /// <summary>The room darkens and the light comes down, before anything falls.</summary>
        public const float TurretHush = .35f;

        /// <summary>The turret leaves the top of the screen.</summary>
        public const float TurretDrop = .55f;

        /// <summary>How long it falls. Short: a slam is fast, and the weight is in the stop.</summary>
        public const float TurretFall = .30f;

        /// <summary>It lands: squash, shake, dust, debris, the shockwave and the sound.</summary>
        public const float TurretImpact = TurretDrop + TurretFall;

        /// <summary>How long the squash takes to spring back.</summary>
        public const float TurretSettle = .45f;

        /// <summary>The name, once the dust has had its moment.</summary>
        public const float TurretName = TurretImpact + .50f;

        /// <summary>The line saying what it does.</summary>
        public const float TurretNote = TurretName + .20f;

        /// <summary>The way onward: EQUIP.</summary>
        public const float TurretKey = TurretNote + .30f;

        // ------------------------------------------------------------- the chests
        /// <summary>The room darkens, as for the turret.</summary>
        public const float ChestHush = .35f;

        /// <summary>The first chest leaves the top of the screen.</summary>
        public const float ChestDrop = .55f;

        /// <summary>How long each chest falls.</summary>
        public const float ChestFall = .30f;

        /// <summary>Between one chest's landing and the next: two thumps, never one.</summary>
        public const float ChestGap = .26f;

        /// <summary>When chest <paramref name="i"/> lands.</summary>
        public static float ChestImpact(int i) => ChestDrop + ChestFall + ChestGap * (i < 0 ? 0 : i);

        /// <summary>The caption, a beat after the last chest of <paramref name="count"/> lands.</summary>
        public static float ChestCaption(int count) => ChestImpact(count < 1 ? 0 : count - 1) + .45f;

        /// <summary>The way onward: OPEN.</summary>
        public static float ChestKey(int count) => ChestCaption(count) + .30f;

        // ------------------------------------------------------------- the welcome day
        /// <summary>The stamp leaves its height over the day's box, once the panel has arrived.</summary>
        public const float StampDrop = .70f;

        /// <summary>How long the stamp falls.</summary>
        public const float StampFall = .20f;

        /// <summary>It lands on the box: the tick, the dust ring, the thump.</summary>
        public const float StampImpact = StampDrop + StampFall;

        /// <summary>The line saying where the quest stands.</summary>
        public const float StampLine = StampImpact + .30f;

        /// <summary>The keys - REMIND ME and NOT NOW, or a lone GOT IT.</summary>
        public const float StampKeys = StampLine + .30f;
    }
}
