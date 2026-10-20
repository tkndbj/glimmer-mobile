namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The clocks, reaches and strengths the school cards and the five ascensions run on
    /// (<c>SiegeBoard.Schools</c>, MODES.md 59r).
    ///
    /// <para>
    /// <b>The mode's, never content and never the published tuning</b>, for
    /// <see cref="SiegeSecrets"/>' reason: an ascension's note says what it does in words, and a
    /// figure here is the picture a player learns. A card's own magnitude, extent and most are
    /// the card's (<c>ShuffleCards.All</c>) and tune live like every other card (59l).
    /// </para>
    /// <para>
    /// <b>Never endless, never a frozen hill.</b> Every hit is struck in tenths of a plain primary
    /// bolt under the build (<c>SiegeBoosts.PlainShot</c>) or a share of the bolt that set it off,
    /// so a school climbs with the damage cards and never past what the ramp compounds into; the
    /// only finishing blow (the ice age's shatter) takes a body already under a quarter of its
    /// health, the execution card's rule. And <b>every hold is a stun</b>
    /// (<c>SiegeRaider.Stagger</c>), which refuses itself until <c>SiegeTuning.StunRest</c> after
    /// the last one ended, so no pile of frost and lightning can hold a body for good:
    /// <c>ShuffleSchoolTests.NoBuildHoldsTheHillStill</c> measures it.
    /// </para>
    /// </summary>
    public static class SiegeSchools
    {
        /// <summary>
        /// The walking a school's hold leaves after it before another hold of any kind may land, in
        /// seconds (<see cref="SiegeRaider.Daze"/>): twice a stun turret's, so a body under the
        /// coldest, stormiest build still walks for most of its life.
        /// </summary>
        public const float HoldRest = 2f;

        // ------------------------------------------------------------ laser
        /// <summary>Refractor: the least shots between two prisms, and how many beams the first copy splits into.</summary>
        public const int RefractLeast = 3, RefractBeams = 3;

        // ------------------------------------------------------------ poison
        /// <summary>Seconds between two bites of venom, and how long a dose lasts after the last hit that added to it.</summary>
        public const float VenomTick = .5f, VenomFor = 4f;

        /// <summary>
        /// The venom a build carries when no card set its strength (contagion or the blight on their
        /// own): plain tenths a stack a bite, and the most stacks. So a poison card is never a card
        /// that waits on another to do anything.
        /// </summary>
        public const int BaseVenomTenths = 2, BaseVenomMost = 4;

        /// <summary>Stacks the blight heart adds on every hit, and the most a rot may soften a blow, per cent.</summary>
        public const int BlightDose = 1, MostRot = 75;

        // ------------------------------------------------------------ fire
        /// <summary>Seconds between two bites of a flame on the ground, and the burn it leaves (plain tenths a second, seconds).</summary>
        public const float FlameTick = .5f, FlameBurnFor = 2f;

        public const int FlameBurnTenths = 2;

        /// <summary>The least a burn must last before a combust, in seconds.</summary>
        public const float LeastCombust = 1f;

        /// <summary>The most burning bodies whose leap one inferno beat draws (the rule spreads from every one).</summary>
        public const int MostLeapsShown = 6;

        /// <summary>The inferno heart's own fire on every hit: plain tenths a second, seconds.</summary>
        public const int InfernoBurnTenths = 2;

        public const float InfernoBurnFor = 3f;

        // ------------------------------------------------------------ frost
        /// <summary>The least shots between two cold snaps.</summary>
        public const int ColdSnapLeast = 3;

        /// <summary>The winter heart's chill (seconds), and the freeze a pile of chills ends in (seconds).</summary>
        public const float WinterChillFor = 1.5f, WinterFreeze = .8f;

        /// <summary>The most extra damage a brittle body may take, per cent.</summary>
        public const int MostBrittle = 100;

        // ------------------------------------------------------------ storm
        /// <summary>Static: how far across the discharge reaches (lanes), and the least hits that charge it.</summary>
        public const float StaticSpan = 1.6f;

        public const int StaticLeast = 2;

        /// <summary>Thunderclap: the least shots between two, the stun it lays (seconds).</summary>
        public const int ThunderLeast = 3;

        public const float ThunderStun = .5f;

        /// <summary>The storm heart's own arc on every hit, in tenths of the bolt that set it off.</summary>
        public const int JoltTenths = 5;

        // ------------------------------------------------------------ the laser grid
        /// <summary>Where the fence stands (0 the top, 1 the line), how deep it cuts, its cadence and its bite.</summary>
        public const float GridMarch = .70f, GridBand = .05f, GridTick = .2f;

        public const int GridTenths = 5;

        /// <summary>The sky laser: seconds between strikes, its charge, the bodies it strikes and how hard.</summary>
        public const float OrbitalEvery = 7f, OrbitalCharge = .9f;

        public const int OrbitalStrikes = 3, OrbitalTenths = 45;

        // ------------------------------------------------------------ necromancy
        /// <summary>The most ghouls on the hill at once.</summary>
        public const int MostGhouls = 6;

        /// <summary>A ghoul's climb (share of the hill a second), its swerve (lanes a second), its life and how near it bursts.</summary>
        public const float GhoulClimb = .32f, GhoulSwerve = 1.6f, GhoulFor = 5f, GhoulNear = .07f, GhoulWide = .6f;

        /// <summary>A ghoul's burst: plain tenths on its box and the boxes round it, and the venom it leaves.</summary>
        public const int GhoulTenths = 30, GhoulVenom = 2;

        // ------------------------------------------------------------ the volcano
        /// <summary>Seconds between eruptions, and the rumble before one.</summary>
        public const float VolcanoEvery = 10f, VolcanoRumble = .7f;

        /// <summary>Lava bombs in an eruption, how far round the crater they land (boxes), their fall and the gap between them.</summary>
        public const int LavaBombs = 7, LavaScatter = 2;

        public const float LavaFall = .6f, LavaGap = .14f;

        /// <summary>A bomb's strength and its burn (plain tenths a second, seconds).</summary>
        public const int LavaTenths = 18, LavaBurnTenths = 4;

        public const float LavaBurnFor = 3f;

        /// <summary>The lava river down the crater's lane: how long it runs, its cadence, its bite and its burn.</summary>
        public const float RiverFor = 4f, RiverTick = .5f, RiverBurnFor = 2f;

        public const int RiverTenths = 6, RiverBurnTenths = 3;

        // ------------------------------------------------------------ the ice age
        /// <summary>Seconds between ice ages, the cold before one, and how long the hill stands frozen.</summary>
        public const float IceAgeEvery = 15f, IceAgeComing = .6f, IceAgeFreeze = 1.8f;

        /// <summary>A body at or under this share of its health shatters, and the shards' strength.</summary>
        public const int IceAgeShatterPercent = 25, IceAgeShardTenths = 8;

        /// <summary>The chill the thaw leaves: tenths of the march, and seconds past the freeze.</summary>
        public const int IceAgeChillTenths = 5;

        public const float IceAgeChillFor = 3f;

        // ------------------------------------------------------------ the tempest
        /// <summary>Seconds between tempests, how long one takes to reach the line, its cadence and its sway (lanes).</summary>
        public const float TempestEvery = 9f, TempestFor = 2.4f, TempestTick = .15f, TempestSway = .45f;

        /// <summary>How far round its eye it strikes (lanes across, share of the hill down), its strength, stun and most bodies a tick.</summary>
        public const float TempestSpan = 1.6f, TempestBand = .2f, TempestStun = .35f;

        public const int TempestTenths = 16, TempestStrikes = 4;
    }
}
