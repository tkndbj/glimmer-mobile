namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One power a Shuffle build can unlock beyond its cards: a <b>secret</b> (a set of cards
    /// held together) or an <b>evolution</b> (one card held to its most). The build says which
    /// powers it holds (<see cref="SiegeBoosts.Has"/>); the rules are <c>SiegeBoard.Secrets</c>
    /// and the drawing <c>SiegeView.Secrets</c>.
    ///
    /// <para>
    /// <b>A bit on the build and never a figure</b>, so a power is on or off and a card's
    /// arithmetic is untouched by it. <b>The ordinals are bit positions and permanent</b>:
    /// appended to, never renumbered, below sixty-four.
    /// </para>
    /// </summary>
    public enum SiegePower
    {
        None = 0,

        // ------------------------------------------------------------ secrets
        Thunderhead = 1,
        Frostfire = 2,
        BladeOrbit = 3,
        Plague = 4,
        GemComets = 5,
        Permafrost = 6,
        EventHorizon = 7,
        FireDragon = 8,
        GoldenDome = 9,
        RainbowCascade = 10,
        SolarCannon = 11,
        Armageddon = 12,
        TimeStop = 13,

        // ------------------------------------------------------------ evolutions
        TitanShells = 20,
        Deadeye = 21,
        Fortress = 22,
        ClusterBombs = 23,
        BallLightning = 24,
        Swarm = 25,
        DiamondDust = 26,
        CometStorm = 27,
        PrismLance = 28,

        // ------------------------------------------------------------ ascensions (MODES.md 59r)
        // A school's keystone held with its points (`ShuffleSchools`); the rules are
        // `SiegeBoard.Schools`, the clocks `SiegeSchools`.
        LaserGrid = 29,
        Necromancy = 30,
        Volcano = 31,
        IceAge = 32,
        Tempest = 33,
    }

    /// <summary>
    /// The clocks, reaches and strengths the secret and evolution powers run on
    /// (<c>SiegeBoard.Secrets</c>).
    ///
    /// <para>
    /// <b>The mode's, never content and never the published tuning</b>, for
    /// <see cref="SiegeArsenal"/>'s reason, and one more: a power's note says its numbers in words
    /// (59l), so a figure here is the sentence a player reads. <b>Every hit is struck in tenths of
    /// a plain primary bolt under the build</b> (<c>SiegeBoosts.PlainShot</c>), so it climbs with
    /// the damage cards and never with anything the ramp could not outgrow: a power makes a run
    /// stronger and never makes one endless.
    /// </para>
    /// </summary>
    public static class SiegeSecrets
    {
        // ------------------------------------------------------------ the secrets
        /// <summary>Thunderhead: seconds between strikes, raiders struck, strength, and the stun.</summary>
        public const float StormEvery = 3f, StormStun = .4f;
        public const int StormStrikes = 3, StormTenths = 18;

        /// <summary>Frostfire: seconds between geysers, strength, the chill (tenths, seconds) and the burn (plain tenths a second, seconds).</summary>
        public const float SteamEvery = 5f, SteamChillFor = 2f, SteamBurnFor = 3f;
        public const int SteamTenths = 15, SteamChillTenths = 6, SteamBurnTenths = 3;

        /// <summary>Blade orbit: seconds between throws, how far across a post reaches (lanes), and each cut's strength.</summary>
        public const float BladeEvery = 1.2f, BladeSpan = 1.6f;
        public const int BladeTenths = 7;

        /// <summary>Plague: the burst's strength, the pools' bite (plain tenths a tick) and how long they stay.</summary>
        public const int PlagueTenths = 6, PlaguePoolTenths = 2;
        public const float PlaguePoolFor = 3f;

        /// <summary>Gem comets: a comet's strength, its splash, its burn, and the most one beat throws.</summary>
        public const int CometTenths = 30, CometSplashTenths = 12, CometBurnTenths = 4, MostComets = 3;
        public const float CometBurnFor = 2f;

        /// <summary>Seconds a comet or a rainbow bolt flies from the field before it lands.</summary>
        public const float CometFlight = .55f;

        /// <summary>Rainbow cascade: strength per beat past the first, and the most one bolt carries.</summary>
        public const int RainbowTenths = 8, MostRainbowTenths = 32;

        /// <summary>Permafrost: where the ice begins (0 the top, 1 the line), its chill, and its spikes.</summary>
        public const float IceFrom = .5f, IceChillFor = .3f, SpikeEvery = 2f;
        public const int IceChillTenths = 4, SpikeTenths = 8;

        /// <summary>Event horizon: seconds between holes, how long one stands, its bite and its crush.</summary>
        public const float HorizonEvery = 11f, HorizonFor = 3f, HorizonTick = .5f;
        public const float HorizonDrag = .26f, HorizonGather = 2.8f;
        public const int HorizonReach = 2, HorizonTenths = 5, HorizonCrushTenths = 20, HorizonCrushPercent = 30;

        /// <summary>Fire dragon: seconds between flights, a flight's length, how near it passes, its strength and burn.</summary>
        public const float DragonEvery = 9f, DragonFor = 2.2f, DragonNear = .9f, DragonBand = .14f;
        public const float DragonBurnFor = 3f, DragonSway = .12f;
        public const int DragonTenths = 16, DragonBurnTenths = 4;

        /// <summary>Golden dome: blows it blocks each wave.</summary>
        public const int DomeBlocks = 4;

        /// <summary>Solar cannon: seconds between shots, the charge before one, its strength and burn.</summary>
        public const float SolarEvery = 8f, SolarCharge = .9f, SolarBurnFor = 3f;
        public const int SolarTenths = 40, SolarBurnTenths = 5;

        /// <summary>Armageddon: seconds between showers, meteors in one, the gap between them, the fall, strength.</summary>
        public const float ArmageddonEvery = 12f, ArmageddonGap = .12f, ArmageddonFall = .8f, ArmageddonBurnFor = 2f;
        public const int ArmageddonMeteors = 8, ArmageddonTenths = 20, ArmageddonBurnTenths = 3;

        /// <summary>Time stop: seconds between stops, how long one lasts, and how much faster the line fires inside it.</summary>
        public const float TimeEvery = 15f, TimeFor = 4f, TimeHaste = 2f;

        // ------------------------------------------------------------ the evolutions
        /// <summary>Titan shells: every how many shots, the shell's share of the bolt, and its blast's share (tenths).</summary>
        public const int ShellEvery = 5, ShellTenths = 20, ShellBlastTenths = 10;

        /// <summary>Deadeye: every how many shots, and the shot's share of the bolt (tenths).</summary>
        public const int SnipeEvery = 4, SnipeTenths = 30;

        /// <summary>Fortress: share of full health mended every wave (per cent), and what a blow reflects.</summary>
        public const int FortressMendPercent = 20, ReflectTenths = 15;

        /// <summary>Cluster bombs: every how many shots, bomblets, the fuse, and strength.</summary>
        public const int ClusterEvery = 3, ClusterBombs = 3, ClusterTenths = 10;
        public const float ClusterFuse = .45f;

        /// <summary>Ball lightning: seconds between launches, its climb, its zap's cadence, reach, strength and stun.</summary>
        public const float OrbEvery = 4f, OrbClimb = 2.6f, OrbTick = .2f, OrbSpan = 1f, OrbBand = .1f, OrbStun = .25f;
        public const int OrbTenths = 8;

        /// <summary>The most orbs on the hill at once: one a post.</summary>
        public const int MostOrbs = SiegeLayout.MaxWards;

        /// <summary>Swarm: micro missiles in a swarm, and each one's strength.</summary>
        public const int SwarmMissiles = 8, SwarmTenths = 5;

        /// <summary>Diamond dust: how long a blizzard stands, its bite's cadence and strength, its freeze, and the most at once.</summary>
        public const float DustFor = 2.5f, DustTick = .5f, DustFreezeFor = .6f;
        public const int DustTenths = 4, DustFreezeTenths = 9, MostBlizzards = 3;

        /// <summary>Diamond dust's own frost on every bolt: tenths of the march, and seconds.</summary>
        public const int DustChillTenths = 3;
        public const float DustChillFor = 1.5f;

        /// <summary>Comet storm: shards a meteor breaks into, how far they scatter (boxes), and the fall.</summary>
        public const int StormShards = 4, ShardScatter = 2;
        public const float ShardFall = .35f;

        /// <summary>Prism lance: seconds between prisms, the charge, beams, and each beam's strength.</summary>
        public const float PrismEvery = 6f, PrismCharge = .6f;
        public const int PrismBeams = 6, PrismTenths = 30;
    }
}
