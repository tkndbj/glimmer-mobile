using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a run's <em>build</em> does to the line: every multiplier, chance and granted ability
    /// a siege board reads on its way through a bolt, a blow, a kill, a muster and a deal.
    ///
    /// <para>
    /// <b>The seam the Shuffle lane plugs into, and an identity everywhere else.</b> A board is
    /// built holding <see cref="None"/>, whose every figure is the plain one - a hundred per
    /// cent, nought chance, no granted ability - so each hook below answers the number it was
    /// handed and every shipped chapter plays bit for bit the board it played before this file
    /// existed (<c>ShuffleBoostTests.TheIdentityBoostChangesNothing</c> holds that over a whole
    /// rung). The Shuffle lane hands its board a live one, filled in by <c>ShuffleBuild</c> each
    /// time a card is taken, and the board reads it the same way.
    /// </para>
    /// <para>
    /// <b>Why one object rather than hooks on the layout or the ward.</b> A build changes
    /// mid-run - a card is taken every two waves - and a layout is frozen content, a ward is
    /// built once. What changes mid-run has to be read live, from one place, so a card taken on
    /// wave six reaches the very next bolt. And it is one object on the <em>board</em> rather
    /// than four on the wards because most of what a card buys is a fact about the line, not a
    /// seat: the same hand plays every turret.
    /// </para>
    /// <para>
    /// <b>Integer arithmetic throughout for anything that decides a hit</b>, hundredths held as
    /// <c>int</c> percentages and divided once, which is this project's hard-won rule about a
    /// float deciding a threshold. The clocks (fire rate, regeneration, a stop) are floats
    /// because the clock already is.
    /// </para>
    /// <para>
    /// <b>Its own random stream</b> (<see cref="_rng"/>), never the field's and never the hill's:
    /// a crit roll per bolt would otherwise shift which lane the next raider walks in and which
    /// gem the next refill deals, and invariant 41 is that a stream's draw count is content.
    /// </para>
    /// </summary>
    public sealed class SiegeBoosts
    {
        /// <summary>The plain line. Shared and never written: every setter below refuses it.</summary>
        public static readonly SiegeBoosts None = new SiegeBoosts(0u, frozen: true);

        readonly bool _frozen;
        uint _rng;

        /// <summary>A live build, rolling its chances off <paramref name="seed"/>.</summary>
        public SiegeBoosts(uint seed) : this(seed, false) { }

        SiegeBoosts(uint seed, bool frozen)
        {
            _frozen = frozen;
            _rng = seed == 0u ? 2463534242u : seed;
        }

        /// <summary>Whether this is the plain line - the one every chapter plays.</summary>
        public bool IsIdentity => _frozen;

        // ------------------------------------------------------------------ the bolt
        /// <summary>What a bolt is worth, per cent of the plain one. A hundred is the plain bolt.</summary>
        public int DamagePercent { get; private set; } = 100;

        /// <summary>Extra per cent against a brute or a bulwark on top of <see cref="DamagePercent"/>.</summary>
        public int HeavyPercent { get; private set; }

        /// <summary>The chance a bolt lands at <see cref="CritPercent"/>, per hundred.</summary>
        public int CritChance { get; private set; }

        /// <summary>What a crit is worth, per cent. Two hundred is double.</summary>
        public int CritPercent { get; private set; } = 200;

        /// <summary>
        /// Extra per cent of damage and rate a ward below <see cref="DesperateBelow"/> per cent of
        /// its health fires with. Nought is no such rule.
        /// </summary>
        public int DesperatePercent { get; private set; }

        public int DesperateBelow { get; private set; } = 30;

        /// <summary>How often a ward fires, per cent of the plain cadence. A hundred is the plain one.</summary>
        public int FirePercent { get; private set; } = 100;

        /// <summary>What a bolt costs in fuel, per cent of the plain cost.</summary>
        public int FuelShotPercent { get; private set; } = 100;

        /// <summary>What a gem is worth in fuel, per cent of the plain figure.</summary>
        public int FuelGemPercent { get; private set; } = 100;

        /// <summary>
        /// Per cent of every ward's fuel also poured into every <em>other</em> standing ward when a
        /// match lands. Nought is the plain line, where a colour feeds its own ward alone.
        /// </summary>
        public int SpillPercent { get; private set; }

        // ------------------------------------------------------------------ the abilities
        /// <summary>A splash on every bolt, in tenths of it, over <see cref="SplashReach"/> cells. Nought is none.</summary>
        public int SplashTenths { get; private set; }

        public int SplashReach { get; private set; } = 1;

        /// <summary>An arc off every bolt to <see cref="ChainHops"/> more raiders, in tenths. Nought is none.</summary>
        public int ChainTenths { get; private set; }

        public int ChainHops { get; private set; }

        /// <summary>Every <see cref="PierceEvery"/>-th bolt lances its lane at <see cref="PierceTenths"/>. Nought is never.</summary>
        public int PierceEvery { get; private set; }

        public int PierceTenths { get; private set; }

        /// <summary>A chill on every bolt, in tenths of the march, for <see cref="FrostFor"/> seconds. Nought is none.</summary>
        public int FrostTenths { get; private set; }

        public float FrostFor { get; private set; }

        /// <summary>A burn on every bolt, in tenths of it a second, for <see cref="BurnFor"/> seconds. Nought is none.</summary>
        public int BurnTenths { get; private set; }

        public float BurnFor { get; private set; }

        /// <summary>The chance a bolt stuns, per hundred, for <see cref="StunFor"/> seconds.</summary>
        public int StunChance { get; private set; }

        public float StunFor { get; private set; }

        /// <summary>The chance a bolt hexes its target, per hundred, for <see cref="HexFor"/> seconds.</summary>
        public int HexChance { get; private set; }

        public float HexFor { get; private set; }

        /// <summary>A bolt finishes a raider at or under this per cent of its health. Nought is never.</summary>
        public int ExecutePercent { get; private set; }

        /// <summary>The chance a bolt lands a second time at <see cref="TwinTenths"/>, per hundred.</summary>
        public int TwinChance { get; private set; }

        public int TwinTenths { get; private set; } = 5;

        /// <summary>Tenths of a raider's full health thrown at its neighbours when it falls. Nought is none.</summary>
        public int BlastTenths { get; private set; }

        /// <summary>
        /// How much of the hill a ward's bolt reaches off its own colour, in tenths, when nothing
        /// of its own is standing. Nought is the lock (invariant 37bl): a ward fires at its own
        /// colour and at a boss, and at nothing else.
        /// </summary>
        public int OffColourTenths { get; private set; }

        // ------------------------------------------------------------------ the kill
        /// <summary>Fuel handed back to the ward on every kill, in tenths. Nought is none.</summary>
        public int SiphonTenths { get; private set; }

        /// <summary>Health handed back to the ward on every kill. Nought is none.</summary>
        public int LeechHealth { get; private set; }

        // ------------------------------------------------------------------ the line
        /// <summary>What a ward can take, per cent of its own figure.</summary>
        public int GuardPercent { get; private set; } = 100;

        /// <summary>What a ward holds, per cent of its own figure.</summary>
        public int CapacityPercent { get; private set; } = 100;

        /// <summary>Charges a ward may bank over the mode's own cap.</summary>
        public int ExtraCharges { get; private set; }

        /// <summary>What an overcharge is worth, per cent.</summary>
        public int OverchargePercent { get; private set; } = 100;

        /// <summary>Taken off every blow at the line, never below one. Nought is the plain blow.</summary>
        public int Armour { get; private set; }

        /// <summary>What a raider takes for landing a blow, per cent of a plain bolt. Nought is nothing.</summary>
        public int ThornsPercent { get; private set; }

        /// <summary>The chance a raider reaching the line is thrown back up the hill, per hundred.</summary>
        public int RepelChance { get; private set; }

        /// <summary>Seconds between one point of health coming back on every standing ward. Nought is never.</summary>
        public float RegenEvery { get; private set; }

        /// <summary>Fallen wards this build may still stand back up, one at a time, at half health.</summary>
        public int SecondWinds { get; private set; }

        /// <summary>Whether the whole line stands back up once when its last ward falls.</summary>
        public int Phoenixes { get; private set; }

        // ------------------------------------------------------------------ the hill
        /// <summary>How fast the hill walks, per cent. A hundred is the plain pace.</summary>
        public int HillPacePercent { get; private set; } = 100;

        /// <summary>The quiet between waves, per cent of the plain one.</summary>
        public int RestPercent { get; private set; } = 100;

        /// <summary>Tenths of every standing ward's tube poured in when a wave steps out. Nought is none.</summary>
        public int WaveFuelTenths { get; private set; }

        /// <summary>Seconds the hill stands still when a wave steps out. Nought is never.</summary>
        public float WaveStill { get; private set; }

        /// <summary>Charges banked on random standing wards when a wave steps out.</summary>
        public int WaveCharges { get; private set; }

        // ------------------------------------------------------------------ the arsenal
        // **What changes how the line attacks**, rather than how hard (`SiegeBoard.Arsenal`).
        // Every figure is nought on the plain line and on a build holding none of the attack
        // cards, and every reading of one asks it first, so a figure nobody holds is a branch
        // nobody takes - `ShuffleTests.TheIdentityBoostChangesNothing` holds the empty build to
        // the plain line frame by frame. "Tenths" are tenths of the hit that set it off; "plain
        // tenths" are tenths of a plain primary bolt under this build's damage (`PlainShot`).

        /// <summary>Pellets thrown after every primary bolt at the ward's next targets, in tenths of it. Nought is none.</summary>
        public int ScatterTenths { get; private set; }

        public int ScatterPellets { get; private set; }

        /// <summary>A killing primary bolt bounces on, in tenths of it, up to <see cref="RicochetBounces"/> times.</summary>
        public int RicochetTenths { get; private set; }

        public int RicochetBounces { get; private set; }

        /// <summary>Every <see cref="MissileEvery"/>-th shot launches <see cref="MissileCount"/> missiles, each plain tenths.</summary>
        public int MissileEvery { get; private set; }

        public int MissileCount { get; private set; }

        public int MissileTenths { get; private set; }

        /// <summary>Every <see cref="QuakeEvery"/>-th shot rolls a shockwave down its row band, in tenths of the bolt.</summary>
        public int QuakeEvery { get; private set; }

        public int QuakeTenths { get; private set; }

        /// <summary>Every primary bolt leaves a toxic pool in its box biting plain tenths a tick, for <see cref="ToxicFor"/> seconds.</summary>
        public int ToxicTenths { get; private set; }

        public float ToxicFor { get; private set; }

        /// <summary>Every standing ward zaps <see cref="TeslaArcs"/> raiders near the line on a cadence, fuel or none, for plain tenths each.</summary>
        public int TeslaTenths { get; private set; }

        public int TeslaArcs { get; private set; }

        /// <summary>Per cent of fire rate each unbroken shot adds, up to <see cref="SpinMost"/> shots. Nought is none.</summary>
        public int SpinStep { get; private set; }

        public int SpinMost { get; private set; }

        /// <summary>A chilled or stunned raider that falls shatters, in tenths of its own full health, over <see cref="ShatterReach"/> boxes.</summary>
        public int ShatterTenths { get; private set; }

        public int ShatterReach { get; private set; } = 1;

        /// <summary>A burning raider that falls sets everything within <see cref="WildfireReach"/> boxes alight. Nought is never.</summary>
        public int WildfireReach { get; private set; }

        /// <summary>The least a spread fire burns for, in plain tenths a second.</summary>
        public int WildfireTenths { get; private set; }

        /// <summary>Seconds between meteors on the thickest crowd, each plain tenths. Nought is never.</summary>
        public float MeteorEvery { get; private set; }

        public int MeteorTenths { get; private set; }

        /// <summary>Seconds between vortices on the thickest crowd, each biting plain tenths a tick. Nought is never.</summary>
        public float VortexEvery { get; private set; }

        public int VortexTenths { get; private set; }

        /// <summary>Kills between nukes, each taking <see cref="NukePercent"/> of every raider's full health. Nought is never.</summary>
        public int NukeEvery { get; private set; }

        public int NukePercent { get; private set; }

        /// <summary>Seconds between the line's fused ray sweeping the hill, biting plain tenths a tick. Nought is never.</summary>
        public float RayEvery { get; private set; }

        public int RayTenths { get; private set; }

        /// <summary>Every primary bolt splits into <see cref="HydraHeads"/> heads at tenths of it, and each head once more at half that.</summary>
        public int HydraTenths { get; private set; }

        public int HydraHeads { get; private set; }

        /// <summary>The chance a primary bolt rolls a wild element, per hundred. Nought is never.</summary>
        public int WildChance { get; private set; }

        // ------------------------------------------------------------------ writing
        /// <summary>
        /// Puts every figure back to the plain line. <c>ShuffleBuild.Compile</c> starts here and
        /// folds every card held back on, so a build is always a function of the cards and never
        /// of the order they were taken in.
        /// </summary>
        public void Reset()
        {
            Guard();

            DamagePercent = 100; HeavyPercent = 0; CritChance = 0; CritPercent = 200;
            DesperatePercent = 0; DesperateBelow = 30;
            FirePercent = 100; FuelShotPercent = 100; FuelGemPercent = 100; SpillPercent = 0;
            SplashTenths = 0; SplashReach = 1; ChainTenths = 0; ChainHops = 0;
            PierceEvery = 0; PierceTenths = 0; FrostTenths = 0; FrostFor = 0f;
            BurnTenths = 0; BurnFor = 0f; StunChance = 0; StunFor = 0f; HexChance = 0; HexFor = 0f;
            ExecutePercent = 0; TwinChance = 0; TwinTenths = 5; BlastTenths = 0; OffColourTenths = 0;
            SiphonTenths = 0; LeechHealth = 0;
            GuardPercent = 100; CapacityPercent = 100; ExtraCharges = 0; OverchargePercent = 100;
            Armour = 0; ThornsPercent = 0; RepelChance = 0; RegenEvery = 0f;
            SecondWinds = 0; Phoenixes = 0;
            HillPacePercent = 100; RestPercent = 100;
            WaveFuelTenths = 0; WaveStill = 0f; WaveCharges = 0;
            ScatterTenths = 0; ScatterPellets = 0; RicochetTenths = 0; RicochetBounces = 0;
            MissileEvery = 0; MissileCount = 0; MissileTenths = 0; QuakeEvery = 0; QuakeTenths = 0;
            ToxicTenths = 0; ToxicFor = 0f; TeslaTenths = 0; TeslaArcs = 0; SpinStep = 0; SpinMost = 0;
            ShatterTenths = 0; ShatterReach = 1; WildfireReach = 0; WildfireTenths = 0;
            MeteorEvery = 0f; MeteorTenths = 0; VortexEvery = 0f; VortexTenths = 0;
            NukeEvery = 0; NukePercent = 0; RayEvery = 0f; RayTenths = 0;
            HydraTenths = 0; HydraHeads = 0; WildChance = 0;
        }

        void Guard()
        {
            if (_frozen) throw new InvalidOperationException("SiegeBoosts.None is the plain line and is never written");
        }

        // One setter per figure rather than public setters, so a build is written through named
        // doors a test can call and the identity cannot be written at all.
        public void AddDamage(int percent) { Guard(); DamagePercent += percent; }
        public void AddHeavy(int percent) { Guard(); HeavyPercent += percent; }
        public void AddCrit(int chance, int percent) { Guard(); CritChance += chance; if (percent > CritPercent) CritPercent = percent; }
        public void SetDesperate(int percent, int below) { Guard(); DesperatePercent += percent; DesperateBelow = below; }
        public void AddFire(int percent) { Guard(); FirePercent += percent; }
        public void AddFuelShot(int percent) { Guard(); FuelShotPercent += percent; if (FuelShotPercent < 20) FuelShotPercent = 20; }
        public void AddFuelGem(int percent) { Guard(); FuelGemPercent += percent; }
        public void AddSpill(int percent) { Guard(); SpillPercent += percent; }
        public void SetSplash(int tenths, int reach) { Guard(); SplashTenths = tenths; SplashReach = reach < 1 ? 1 : reach; }
        public void SetChain(int tenths, int hops) { Guard(); ChainTenths = tenths; ChainHops = hops; }
        public void SetPierce(int every, int tenths) { Guard(); PierceEvery = every; PierceTenths = tenths; }
        public void SetFrost(int tenths, float seconds) { Guard(); FrostTenths = tenths > 9 ? 9 : tenths; FrostFor = seconds; }
        public void SetBurn(int tenths, float seconds) { Guard(); BurnTenths = tenths; BurnFor = seconds; }
        public void SetStun(int chance, float seconds) { Guard(); StunChance = chance; StunFor = seconds; }
        public void SetHex(int chance, float seconds) { Guard(); HexChance = chance; HexFor = seconds; }
        public void SetExecute(int percent) { Guard(); ExecutePercent = percent; }
        public void SetTwin(int chance, int tenths) { Guard(); TwinChance = chance; TwinTenths = tenths; }
        public void SetBlast(int tenths) { Guard(); BlastTenths = tenths; }
        public void SetOffColour(int tenths) { Guard(); OffColourTenths = tenths > 10 ? 10 : tenths; }
        public void AddSiphon(int tenths) { Guard(); SiphonTenths += tenths; }
        public void AddLeech(int health) { Guard(); LeechHealth += health; }
        public void AddGuard(int percent) { Guard(); GuardPercent += percent; }
        public void AddCapacity(int percent) { Guard(); CapacityPercent += percent; }
        public void AddCharges(int charges) { Guard(); ExtraCharges += charges; }
        public void AddOvercharge(int percent) { Guard(); OverchargePercent += percent; }
        public void AddArmour(int points) { Guard(); Armour += points; }
        public void AddThorns(int percent) { Guard(); ThornsPercent += percent; }
        public void SetRepel(int chance) { Guard(); RepelChance = chance > 100 ? 100 : chance; }
        public void SetRegen(float every) { Guard(); RegenEvery = every; }
        public void AddSecondWinds(int winds) { Guard(); SecondWinds += winds; }
        public void AddPhoenix(int lives) { Guard(); Phoenixes += lives; }
        public void AddHillPace(int percent) { Guard(); HillPacePercent += percent; if (HillPacePercent < 40) HillPacePercent = 40; }
        public void AddRest(int percent) { Guard(); RestPercent += percent; }
        public void SetWaveFuel(int tenths) { Guard(); WaveFuelTenths = tenths; }
        public void SetWaveStill(float seconds) { Guard(); WaveStill = seconds; }
        public void SetWaveCharges(int charges) { Guard(); WaveCharges = charges; }
        public void SetScatter(int tenths, int pellets) { Guard(); ScatterTenths = tenths; ScatterPellets = pellets; }
        public void SetRicochet(int tenths, int bounces) { Guard(); RicochetTenths = tenths; RicochetBounces = bounces; }
        public void SetMissiles(int every, int count, int tenths) { Guard(); MissileEvery = every < 1 ? 1 : every; MissileCount = count; MissileTenths = tenths; }
        public void SetQuake(int every, int tenths) { Guard(); QuakeEvery = every < 1 ? 1 : every; QuakeTenths = tenths; }
        public void SetToxic(int tenths, float seconds) { Guard(); ToxicTenths = tenths; ToxicFor = seconds; }
        public void SetTesla(int tenths, int arcs) { Guard(); TeslaTenths = tenths; TeslaArcs = arcs; }
        public void SetSpin(int step, int most) { Guard(); SpinStep = step; SpinMost = most; }
        public void SetShatter(int tenths, int reach) { Guard(); ShatterTenths = tenths; ShatterReach = reach < 1 ? 1 : reach; }
        public void SetWildfire(int reach, int tenths) { Guard(); WildfireReach = reach; WildfireTenths = tenths; }
        public void SetMeteor(float every, int tenths) { Guard(); MeteorEvery = every; MeteorTenths = tenths; }
        public void SetVortex(float every, int tenths) { Guard(); VortexEvery = every; VortexTenths = tenths; }
        public void SetNuke(int every, int percent) { Guard(); NukeEvery = every; NukePercent = percent; }
        public void SetRay(float every, int tenths) { Guard(); RayEvery = every; RayTenths = tenths; }
        public void SetHydra(int tenths, int heads) { Guard(); HydraTenths = tenths; HydraHeads = heads; }
        public void SetWild(int chance) { Guard(); WildChance = chance > 100 ? 100 : chance; }

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// What a bolt that would have been worth <paramref name="damage"/> is worth under this
        /// build, against <paramref name="kind"/>. Never below one once it was anything, for
        /// <c>SiegeTuning.DamageTo</c>'s reason.
        /// </summary>
        public int Bolt(int damage, SiegeKind kind, out bool crit)
        {
            crit = false;
            if (damage <= 0) return 0;
            if (_frozen) return damage;

            int percent = DamagePercent;
            if (HeavyPercent > 0 && (kind == SiegeKind.Brute || kind == SiegeKind.Bulwark))
                percent += HeavyPercent;

            int hit = damage * percent / 100;

            if (CritChance > 0 && Chance(CritChance))
            {
                crit = true;
                hit = hit * CritPercent / 100;
            }

            return hit < 1 ? 1 : hit;
        }

        /// <summary>A bolt from a ward holding <paramref name="healthPercent"/> of its health: the desperate bonus.</summary>
        public int Desperate(int damage, int healthPercent)
        {
            if (_frozen || DesperatePercent <= 0 || healthPercent > DesperateBelow) return damage;

            int hit = damage * (100 + DesperatePercent) / 100;
            return hit < 1 ? 1 : hit;
        }

        /// <summary>Seconds until a ward may fire again, from the mode's own cadence.</summary>
        public float FireEvery(float every, int healthPercent)
        {
            if (_frozen) return every;

            int percent = FirePercent;
            if (DesperatePercent > 0 && healthPercent <= DesperateBelow) percent += DesperatePercent;

            return percent <= 0 ? every : every * 100f / percent;
        }

        /// <summary>What a bolt costs, from the mode's own figure.</summary>
        public float FuelShot(float fuel) => _frozen ? fuel : fuel * FuelShotPercent / 100f;

        /// <summary>What a gem is worth, from the mode's own figure.</summary>
        public float FuelGem(float fuel) => _frozen ? fuel : fuel * FuelGemPercent / 100f;

        /// <summary>What an overcharge is worth, from the tube's own figure.</summary>
        public int Overcharge(int damage) => _frozen || damage <= 0 ? damage : Math.Max(1, damage * OverchargePercent / 100);

        /// <summary>What a blow at the line takes off a ward, never below one once it was anything.</summary>
        public int Blow(int damage)
        {
            if (_frozen || damage <= 0 || Armour <= 0) return damage;

            int hit = damage - Armour;
            return hit < 1 ? 1 : hit;
        }

        /// <summary>What a swinger takes for landing a blow, or nought.</summary>
        public int Thorns(int shot) => _frozen || ThornsPercent <= 0 ? 0 : Math.Max(1, shot * ThornsPercent / 100);

        /// <summary>The quiet between waves, from the mode's own.</summary>
        public float Rest(float rest) => _frozen ? rest : rest * RestPercent / 100f;

        /// <summary>How fast the hill walks, as a multiplier on the march.</summary>
        public float HillPace => _frozen ? 1f : HillPacePercent / 100f;

        /// <summary>
        /// The quickest a spun-up ward may fire, in seconds. A floor, because what it protects is
        /// the frame: a ward firing every frame would be a rate decided by the device rather
        /// than by the build.
        /// </summary>
        public const float QuickestShot = .06f;

        /// <summary>
        /// A cooldown <paramref name="cool"/> after <paramref name="shots"/> unbroken shots of
        /// spin-up (<see cref="SpinStep"/>), never under <see cref="QuickestShot"/>. The cooldown
        /// handed in on the plain line and on a build with no spin.
        /// </summary>
        public float Spun(float cool, int shots)
        {
            if (_frozen || SpinStep <= 0 || shots <= 0) return cool;

            int held = shots < SpinMost ? shots : SpinMost;
            float spun = cool * 100f / (100 + SpinStep * held);
            return spun < QuickestShot ? QuickestShot : spun;
        }

        /// <summary>
        /// What a hit worth <paramref name="tenths"/> of a plain primary bolt takes off
        /// <paramref name="kind"/> under this build - the figure every timed attack (a meteor, a
        /// tesla arc, a ray) is struck at, so a damage card makes those heavier too.
        /// </summary>
        public int PlainShot(int tenths, SiegeKind kind)
        {
            if (_frozen || tenths <= 0) return 0;

            int plain = SiegeTuning.ShotDamage * SiegeTuning.WeakMultiplier * tenths / 10;
            return Bolt(plain < 1 ? 1 : plain, kind, out _);
        }

        /// <summary>
        /// The element a primary bolt is struck in under wild magic (a ward colour, 0-3), or -1.
        /// <b>Rolls nothing</b> on the plain line or a build without it, so no stream moves.
        /// </summary>
        public int WildElement()
        {
            if (_frozen || WildChance <= 0 || !Chance(WildChance)) return -1;
            return Roll100() % 4;
        }

        /// <summary>Whether this bolt lands a second time.</summary>
        public bool Twins() => !_frozen && TwinChance > 0 && Chance(TwinChance);

        /// <summary>Whether this bolt stuns.</summary>
        public bool Stuns() => !_frozen && StunChance > 0 && Chance(StunChance);

        /// <summary>Whether this bolt hexes.</summary>
        public bool Hexes() => !_frozen && HexChance > 0 && Chance(HexChance);

        /// <summary>Whether a raider reaching the line is thrown back.</summary>
        public bool Repels() => !_frozen && RepelChance > 0 && Chance(RepelChance);

        /// <summary>
        /// Takes one second wind, answering whether there was one to take. The count is on the
        /// build rather than the ward so three winds raise any three falls, in any order.
        /// </summary>
        public bool TakeSecondWind()
        {
            if (_frozen || SecondWinds <= 0) return false;
            SecondWinds--;
            return true;
        }

        /// <summary>Takes the phoenix, answering whether there was one.</summary>
        public bool TakePhoenix()
        {
            if (_frozen || Phoenixes <= 0) return false;
            Phoenixes--;
            return true;
        }

        /// <summary>One roll of this build's own stream, under a hundred.</summary>
        public int Roll100()
        {
            if (_frozen) return 0;

            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;

            return (int)(Avalanche(_rng) % 100u);
        }

        bool Chance(int perHundred) => perHundred >= 100 || Roll100() < perHundred;

        /// <summary>lowbias32, so a slice of the stream is as good as the whole (invariant 37cj).</summary>
        static uint Avalanche(uint x)
        {
            x ^= x >> 16;
            x = unchecked(x * 0x7feb352du);
            x ^= x >> 15;
            x = unchecked(x * 0x846ca68bu);
            x ^= x >> 16;
            return x;
        }
    }
}
