using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// The clocks and distances the attack cards run on (<see cref="SiegeBoard"/>'s arsenal).
    ///
    /// <para>
    /// <b>The mode's, never content</b>, for <c>ShuffleDeck.Weights</c>' reason: a card names how
    /// strong it is, and how a meteor falls or how long a vortex holds is what the card <em>is</em>.
    /// Public so a fixture can hold a card to the clock it runs on.
    /// </para>
    /// </summary>
    public static class SiegeArsenal
    {
        /// <summary>Seconds between one tesla discharge and the next, on every standing ward.</summary>
        public const float TeslaEvery = 1.5f;

        /// <summary>How far down the hill a raider must be before a coil reaches it (0 the top, 1 the line).</summary>
        public const float TeslaFloor = .12f;

        /// <summary>How far across, in lanes, a coil reaches either side of its own post.</summary>
        public const float TeslaSpan = 1.6f;

        /// <summary>Seconds between one bite of a toxic pool and the next.</summary>
        public const float ToxicTick = .5f;

        /// <summary>The most pools the hill holds at once: one a box, and the hill is twenty boxes.</summary>
        public const int MostPools = SiegeTuning.Lanes * SiegeTuning.BlastRows;

        /// <summary>How far a quake throws what it rolls over back up the hill, as a share of the slope.</summary>
        public const float QuakeShove = .06f;

        /// <summary>The chill a shatter leaves on what it hits: tenths of the march, and seconds.</summary>
        public const int ShatterChillTenths = 5;

        public const float ShatterChillFor = 1.5f;

        /// <summary>Seconds a spread fire burns for.</summary>
        public const float WildfireFor = 3f;

        /// <summary>Seconds between a meteor's shadow and its landing - the warning is the picture.</summary>
        public const float MeteorFall = .7f;

        /// <summary>The burn a meteor leaves on what it lands on: plain tenths a second, and seconds.</summary>
        public const int MeteorBurnTenths = 2;

        public const float MeteorBurnFor = 2f;

        /// <summary>Seconds a vortex holds, and between its bites.</summary>
        public const float VortexFor = 3f, VortexTick = .5f;

        /// <summary>How fast a vortex drags a body to its eye: share of the slope a second, and lanes a second.</summary>
        public const float VortexDrag = .22f, VortexGather = 2.5f;

        /// <summary>Seconds the fused ray takes to sweep the hill from one edge to the other, and between its bites.</summary>
        public const float RayFor = 2f, RayTick = .1f;

        /// <summary>How wide the ray is, in lanes.</summary>
        public const float RayWidth = 1f;

        /// <summary>Seconds between a nuke's warning and its landing.</summary>
        public const float NukeFall = .9f;

        /// <summary>
        /// What wild magic adds in each element: fire's burn (tenths of the bolt a second, for
        /// seconds), venom's pool (plain tenths a tick, for seconds), ice's chill (tenths of the
        /// march, for seconds) and lightning's arc (tenths of the bolt, hops).
        /// </summary>
        public const int WildBurnTenths = 3, WildVenomTenths = 2, WildChillTenths = 6,
                         WildArcTenths = 5, WildArcHops = 2;

        public const float WildBurnFor = 2f, WildVenomFor = 3f, WildChillFor = 2f;

        /// <summary>A hydra's heads split this many generations deep, each at half the last.</summary>
        public const int HydraGenerations = 2;
    }

    /// <summary>A toxic pool on one box of the hill, as the view draws it.</summary>
    public readonly struct SiegePool
    {
        public readonly int Lane, Row;

        /// <summary>Seconds left, and the seconds it was laid for.</summary>
        public readonly float Left, For;

        public SiegePool(int lane, int row, float left, float laid)
        {
            Lane = lane;
            Row = row;
            Left = left;
            For = laid;
        }
    }

    /// <summary>
    /// <b>The arsenal: everything the Shuffle lane's attack cards make the line do</b> that no
    /// turret on the shelf does - a pellet spread, a ricochet, missiles, a quake rolling down a
    /// row, toxic pools, tesla coils, spin-up, a shatter, wildfire, meteors, a vortex, a nuke, the
    /// fused ray, a hydra's heads and wild magic.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, and that is three locks rather than one promise.</b> Every
    /// entry point here returns on <see cref="SiegeBoosts.IsIdentity"/> before it reads anything,
    /// so a chapter and the Infinite lane - which hold <see cref="SiegeBoosts.None"/> - never get
    /// past a first line; every figure it reads is nought on a build holding none of these cards,
    /// so an empty Shuffle build plays the plain board frame for frame
    /// (<c>ShuffleTests.TheIdentityBoostChangesNothing</c>); and every random draw comes off the
    /// build's own stream (<see cref="SiegeBoosts.Roll100"/>), never the field's or the hill's
    /// (invariant 41). What it writes into the report is new lists and new tags
    /// (<see cref="SiegeReport.Volleys"/>, <see cref="SiegeBolt.Via"/>), which the plain line
    /// leaves empty and <see cref="SiegeVia.None"/>.
    /// </para>
    /// <para>
    /// <b>Every hit goes through the doors every other hit does</b>: <c>Splinter</c> for a hit a
    /// ward dealt (so plating and a ward's kill effects read as they always have) and
    /// <see cref="Hit"/> for one nobody's barrel fired - a meteor, a vortex, the ray, a nuke -
    /// which soaks on a bulwark exactly as a partner shot does and takes health through
    /// <c>Wound</c>. Nothing here may reduce a primary bolt (invariant 42): it runs after the
    /// bolt has landed, and every figure is a hit on somebody, a lasting state, or a clock.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ state
        // All of it is the build's and none of it is in the save: a run is not stored (59g).
        int[] _spin;
        float _tesla, _meteorClock, _meteorIn, _vortexClock, _vortexLeft, _vortexTick;
        float _rayClock, _rayLeft, _rayTick, _nukeIn;
        bool _meteorFalling, _nukeFalling, _nuking;
        int _meteorLane, _meteorRow, _kills;
        float _vortexMarch, _vortexLane, _rayLane;

        struct Pool
        {
            public int Lane, Row, Ward, Tenths;
            public float Left, For, Tick;
        }

        readonly List<Pool> _pools = new List<Pool>(SiegeArsenal.MostPools);
        readonly List<SiegePool> _poolsDrawn = new List<SiegePool>(SiegeArsenal.MostPools);
        readonly HashSet<int> _struck = new HashSet<int>();
        readonly List<SiegeRaider> _pick = new List<SiegeRaider>(24);

        // ------------------------------------------------------------------ what the view reads
        /// <summary>Unbroken shots ward <paramref name="ward"/> has spun up, nought on the plain line.</summary>
        public int SpinOf(int ward) => _spin == null || ward < 0 || ward >= _spin.Length ? 0 : _spin[ward];

        /// <summary>How far spun up a ward is, nought to one: what the view heats its barrels by.</summary>
        public float SpinShare(int ward)
            => _boosts.SpinMost <= 0 ? 0f : Math.Min(1f, SpinOf(ward) / (float)_boosts.SpinMost);

        /// <summary>The toxic pools on the hill, as the view draws them. Empty on the plain line.</summary>
        public IReadOnlyList<SiegePool> Pools
        {
            get
            {
                _poolsDrawn.Clear();
                for (int i = 0; i < _pools.Count; i++)
                    _poolsDrawn.Add(new SiegePool(_pools[i].Lane, _pools[i].Row, _pools[i].Left, _pools[i].For));
                return _poolsDrawn;
            }
        }

        /// <summary>Whether a vortex is open, where its eye is (march, lane) and how long it has left.</summary>
        public bool VortexOpen => _vortexLeft > 0f;

        public float VortexMarch => _vortexMarch;

        public float VortexLane => _vortexLane;

        public float VortexLeft => _vortexLeft;

        /// <summary>Whether the fused ray is sweeping, and which lane it stands over (fractional).</summary>
        public bool RayLive => _rayLeft > 0f;

        public float RayLane => _rayLane;

        // ------------------------------------------------------------------ spin-up
        void SpinUp(int ward)
        {
            if (_boosts.IsIdentity || _boosts.SpinStep <= 0) return;

            if (_spin == null || _spin.Length != _wards.Length) _spin = new int[_wards.Length];
            if (_spin[ward] < _boosts.SpinMost) _spin[ward]++;
        }

        void Unspin(int ward)
        {
            if (_spin != null && ward >= 0 && ward < _spin.Length) _spin[ward] = 0;
        }

        // ------------------------------------------------------------------ on a primary bolt
        /// <summary>
        /// What the attack cards add to a primary bolt that has landed at full strength. Called
        /// from <c>Augment</c>, which is already shut on the plain line.
        /// </summary>
        void Volley(SiegeWard ward, int index, SiegeRaider target, int damage, int element)
        {
            var b = _boosts;

            if (!target.Alive && b.RicochetTenths > 0 && b.RicochetBounces > 0)
                Ricochet(ward, index, target, Share(damage, b.RicochetTenths), b.RicochetBounces);

            if (b.ScatterTenths > 0 && b.ScatterPellets > 0)
                Scatter(ward, index, target, Share(damage, b.ScatterTenths), b.ScatterPellets);

            if (b.MissileEvery > 0 && b.MissileCount > 0 && ward.Shots % b.MissileEvery == 0)
                Missiles(ward, index, b.MissileCount, b.MissileTenths);

            if (b.QuakeEvery > 0 && b.QuakeTenths > 0 && ward.Shots % b.QuakeEvery == 0)
                Quake(ward, index, target, Share(damage, b.QuakeTenths));

            if (b.ToxicTenths > 0 && b.ToxicFor > 0f)
                Lay(target, index, b.ToxicTenths, b.ToxicFor);

            if (b.HydraTenths > 0 && b.HydraHeads > 0)
            {
                _struck.Clear();
                _struck.Add(target.Id);
                Hydra(ward, index, target, Share(damage, b.HydraTenths), b.HydraHeads,
                      SiegeArsenal.HydraGenerations);
            }

            if (element >= 0) Wild(ward, index, target, damage, element);
        }

        /// <summary>
        /// A killing bolt goes on: to the nearest raider still standing, and on again from there
        /// each time it kills, up to <paramref name="bounces"/> times.
        /// </summary>
        void Ricochet(SiegeWard ward, int index, SiegeRaider from, int damage, int bounces)
        {
            _struck.Clear();
            _struck.Add(from.Id);

            for (int n = 0; n < bounces; n++)
            {
                var next = Nearest(from, _struck);
                if (next == null) return;

                _struck.Add(next.Id);
                Splinter(ward, index, next, damage, SiegeVia.Ricochet, from.Id);

                if (next.Alive) return;
                from = next;
            }
        }

        /// <summary>
        /// Pellets at the ward's next targets: its own colour furthest down first, as
        /// <c>Aim</c> would have picked them, and then anything else furthest down.
        /// </summary>
        void Scatter(SiegeWard ward, int index, SiegeRaider target, int damage, int pellets)
        {
            _struck.Clear();
            _struck.Add(target.Id);

            for (int n = 0; n < pellets; n++)
            {
                SiegeRaider own = null, any = null;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || _struck.Contains(r.Id)) continue;

                    bool mine = ward.Unbound ? !r.Boss : r.Colour == ward.Colour;
                    if (mine && (own == null || r.March > own.March)) own = r;
                    if (any == null || r.March > any.March) any = r;
                }

                var hit = own ?? any;
                if (hit == null) return;

                _struck.Add(hit.Id);
                Splinter(ward, index, hit, damage, SiegeVia.Pellet);
            }
        }

        /// <summary>Missiles at raiders picked at random off the build's stream, any colour.</summary>
        void Missiles(SiegeWard ward, int index, int count, int tenths)
        {
            for (int n = 0; n < count; n++)
            {
                var hit = Any(null);
                if (hit == null) return;

                Splinter(ward, index, hit, _boosts.PlainShot(tenths, hit.Kind), SiegeVia.Missile);
            }
        }

        /// <summary>
        /// A shockwave rolling across the target's whole row band: everything in it is hit and
        /// thrown back up the hill. A splash is a box; a quake is a row and a shove.
        /// </summary>
        void Quake(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            int row = SiegeTuning.RowOf(target.March);
            _report.Volleys.Add(new SiegeVolley(SiegeVia.Quake, target.March, target.Column, 0, index));

            if (target.Alive) target.Shove(SiegeArsenal.QuakeShove);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == target || !Touchable(r) || SiegeTuning.RowOf(r.March) != row) continue;

                Splinter(ward, index, r, damage, SiegeVia.Quake, target.Id);
                if (r.Alive) r.Shove(SiegeArsenal.QuakeShove);
            }
        }

        /// <summary>Lays a pool on the box <paramref name="at"/> stands in, or refreshes the one there.</summary>
        void Lay(SiegeRaider at, int ward, int tenths, float seconds)
        {
            int lane = at.Column, row = SiegeTuning.RowOf(at.March);

            for (int i = 0; i < _pools.Count; i++)
            {
                var pool = _pools[i];
                if (pool.Lane != lane || pool.Row != row) continue;

                if (seconds > pool.Left) pool.Left = seconds;
                if (seconds > pool.For) pool.For = seconds;
                if (tenths > pool.Tenths) pool.Tenths = tenths;
                pool.Ward = ward;
                _pools[i] = pool;
                return;
            }

            if (_pools.Count >= SiegeArsenal.MostPools) return;

            _pools.Add(new Pool
            {
                Lane = lane, Row = row, Ward = ward, Tenths = tenths,
                Left = seconds, For = seconds, Tick = SiegeArsenal.ToxicTick,
            });
        }

        /// <summary>
        /// Heads off a bolt to raiders picked at random, each splitting again at half strength
        /// until <paramref name="generations"/> run out. <see cref="_struck"/> keeps one burst
        /// from biting a body twice.
        /// </summary>
        void Hydra(SiegeWard ward, int index, SiegeRaider from, int damage, int heads, int generations)
        {
            if (generations <= 0 || damage < 1) return;

            for (int n = 0; n < heads; n++)
            {
                var hit = Any(_struck);
                if (hit == null) return;

                _struck.Add(hit.Id);
                Splinter(ward, index, hit, damage, SiegeVia.Hydra, from.Id);
                Hydra(ward, index, hit, damage / 2, heads, generations - 1);
            }
        }

        /// <summary>
        /// Wild magic: the element rolled onto this bolt adds its own effect - fire burns, venom
        /// pools, ice chills, lightning arcs. The colours are the ward colours' elements, which is
        /// also the reel the view draws the bolt in (<see cref="SiegeBolt.Element"/>).
        /// </summary>
        void Wild(SiegeWard ward, int index, SiegeRaider target, int damage, int element)
        {
            switch (element)
            {
                case 0:
                    if (target.Alive)
                        target.Kindle(Share(damage, SiegeArsenal.WildBurnTenths),
                                      SiegeArsenal.WildBurnFor, index);
                    break;

                case 1:
                    Lay(target, index, SiegeArsenal.WildVenomTenths, SiegeArsenal.WildVenomFor);
                    break;

                case 2:
                    if (target.Alive)
                        target.Freeze(SiegeArsenal.WildChillTenths, SiegeArsenal.WildChillFor);
                    break;

                default:
                    Arc(ward, index, target, Share(damage, SiegeArsenal.WildArcTenths),
                        SiegeArsenal.WildArcHops, SiegeVia.Arc);
                    break;
            }
        }

        // ------------------------------------------------------------------ on a death
        /// <summary>
        /// What the attack cards make of a raider falling, whoever felled it: the nuke's count, a
        /// shatter, wildfire. From <c>Fallen</c> (shut on the plain line) and from
        /// <see cref="Hit"/>, so a body a meteor killed counts exactly as one a bolt did.
        /// </summary>
        void Perished(SiegeRaider fallen, int ward)
        {
            if (_boosts.IsIdentity || fallen == null) return;

            // **A nuke's own kills are not counted toward the next one**, or one nuke over a
            // crowded hill would be the fuse of the next, and a legendary would be a loop.
            if (_boosts.NukeEvery > 0 && !_nuking && ++_kills >= _boosts.NukeEvery && !_nukeFalling)
            {
                _kills = 0;
                _nukeFalling = true;
                _nukeIn = SiegeArsenal.NukeFall;
                _report.Volleys.Add(new SiegeVolley(SiegeVia.Nuke, .5f, (SiegeTuning.Lanes - 1) * .5f,
                                                    SiegeTuning.BlastRows, -1, SiegeArsenal.NukeFall));
            }

            if (fallen.Boss) return;

            if (_boosts.ShatterTenths > 0 && (fallen.Chill > 0f || fallen.Stun > 0f))
                Shatter(fallen, ward);

            if (_boosts.WildfireReach > 0 && fallen.Burn > 0f)
                Wildfire(fallen, ward);
        }

        /// <summary>
        /// A body that died frozen bursts: everything within reach takes a share of its full
        /// health and is chilled. A chilled body killed by the burst bursts in turn - which is the
        /// card, and it ends because every burst needs a body that has not yet fallen.
        /// </summary>
        void Shatter(SiegeRaider fallen, int ward)
        {
            int reach = _boosts.ShatterReach;
            int damage = Math.Max(1, fallen.MaxHealth * _boosts.ShatterTenths / 10);
            int row = SiegeTuning.RowOf(fallen.March), lane = fallen.Column;

            _report.Volleys.Add(new SiegeVolley(SiegeVia.Shatter, fallen.March, lane, reach, ward));

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == fallen || !Touchable(r) || !Within(r, lane, row, reach)) continue;

                Hit(ward, r, damage, SiegeVia.Shatter, fallen.Id);
                if (r.Alive) r.Freeze(SiegeArsenal.ShatterChillTenths, SiegeArsenal.ShatterChillFor);
            }
        }

        /// <summary>A body that died burning sets everything within reach alight at its own fire or the card's, whichever is fiercer.</summary>
        void Wildfire(SiegeRaider fallen, int ward)
        {
            int reach = _boosts.WildfireReach;
            int row = SiegeTuning.RowOf(fallen.March), lane = fallen.Column;
            int rate = Math.Max(fallen.BurnRate, _boosts.PlainShot(_boosts.WildfireTenths, SiegeKind.Creeper));
            int from = fallen.BurnFrom >= 0 ? fallen.BurnFrom : ward;

            bool caught = false;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == fallen || !Touchable(r) || !Within(r, lane, row, reach)) continue;

                r.Kindle(rate, SiegeArsenal.WildfireFor, from);
                caught = true;
            }

            if (caught)
                _report.Volleys.Add(new SiegeVolley(SiegeVia.Wildfire, fallen.March, lane, reach, from));
        }

        // ------------------------------------------------------------------ the clocks
        /// <summary>Every attack card that runs on a clock of its own. Shut on the plain line.</summary>
        void Arsenal(float dt)
        {
            if (_boosts.IsIdentity) return;

            Tesla(dt);
            Seep(dt);
            Meteors(dt);
            Vortices(dt);
            Rays(dt);
            Nukes(dt);
        }

        /// <summary>
        /// Every standing ward discharges at the raiders nearest the line in its own reach, fuel
        /// or none: a coil is the one way a dry ward fights.
        /// </summary>
        void Tesla(float dt)
        {
            if (_boosts.TeslaTenths <= 0 || _boosts.TeslaArcs <= 0) { _tesla = 0f; return; }

            _tesla += dt;
            if (_tesla < SiegeArsenal.TeslaEvery) return;
            _tesla -= SiegeArsenal.TeslaEvery;

            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                float post = PostLane(w);

                _struck.Clear();
                for (int n = 0; n < _boosts.TeslaArcs; n++)
                {
                    SiegeRaider best = null;

                    for (int i = 0; i < _raiders.Count; i++)
                    {
                        var r = _raiders[i];
                        if (!Touchable(r) || _struck.Contains(r.Id)) continue;
                        if (r.March < SiegeArsenal.TeslaFloor) continue;
                        if (Math.Abs(r.Lane + r.Drift - post) > SiegeArsenal.TeslaSpan) continue;
                        if (best == null || r.March > best.March) best = r;
                    }

                    if (best == null) break;

                    _struck.Add(best.Id);
                    Splinter(ward, w, best, _boosts.PlainShot(_boosts.TeslaTenths, best.Kind), SiegeVia.Tesla);
                }
            }
        }

        /// <summary>The pools bite whatever stands in their box, and dry up.</summary>
        void Seep(float dt)
        {
            for (int p = _pools.Count - 1; p >= 0; p--)
            {
                var pool = _pools[p];
                pool.Left -= dt;
                pool.Tick -= dt;

                if (pool.Tick <= 0f)
                {
                    pool.Tick += SiegeArsenal.ToxicTick;

                    for (int i = 0; i < _raiders.Count; i++)
                    {
                        var r = _raiders[i];
                        if (!Touchable(r) || !Within(r, pool.Lane, pool.Row, 0)) continue;

                        Hit(pool.Ward, r, _boosts.PlainShot(pool.Tenths, r.Kind), SiegeVia.Toxic, -1);
                    }
                }

                if (pool.Left <= 0f) _pools.RemoveAt(p);
                else _pools[p] = pool;
            }
        }

        /// <summary>
        /// A meteor on the thickest crowd: its shadow first (<see cref="SiegeArsenal.MeteorFall"/>),
        /// then the landing on whoever is in the box and the boxes round it by then. The clock
        /// waits, full, for a hill with something on it.
        /// </summary>
        void Meteors(float dt)
        {
            if (_meteorFalling)
            {
                _meteorIn -= dt;
                if (_meteorIn > 0f) return;

                _meteorFalling = false;
                float march = (_meteorRow + .5f) / SiegeTuning.BlastRows;
                _report.Volleys.Add(new SiegeVolley(SiegeVia.Meteor, march, _meteorLane, 1, -1));

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || !Within(r, _meteorLane, _meteorRow, 1)) continue;

                    Hit(-1, r, _boosts.PlainShot(_boosts.MeteorTenths, r.Kind), SiegeVia.Meteor, -1);
                    if (r.Alive)
                        r.Kindle(_boosts.PlainShot(SiegeArsenal.MeteorBurnTenths, r.Kind),
                                 SiegeArsenal.MeteorBurnFor, -1);
                }

                return;
            }

            if (_boosts.MeteorEvery <= 0f || _boosts.MeteorTenths <= 0) { _meteorClock = 0f; return; }

            _meteorClock += dt;
            if (_meteorClock < _boosts.MeteorEvery) return;
            _meteorClock = _boosts.MeteorEvery;

            if (!Thickest(out _meteorLane, out _meteorRow)) return;

            _meteorClock = 0f;
            _meteorFalling = true;
            _meteorIn = SiegeArsenal.MeteorFall;
            _report.Volleys.Add(new SiegeVolley(SiegeVia.Meteor, (_meteorRow + .5f) / SiegeTuning.BlastRows,
                                                _meteorLane, 1, -1, SiegeArsenal.MeteorFall));
        }

        /// <summary>
        /// A vortex on the thickest crowd: for <see cref="SiegeArsenal.VortexFor"/> seconds it
        /// gathers every body within a box of its eye - dragged across to its lane, dragged back
        /// up the slope to its row, slowed to a crawl - and bites them on a cadence.
        /// </summary>
        void Vortices(float dt)
        {
            if (_vortexLeft > 0f)
            {
                _vortexLeft -= dt;
                _vortexTick -= dt;

                bool bite = _vortexTick <= 0f;
                if (bite) _vortexTick += SiegeArsenal.VortexTick;

                int row = SiegeTuning.RowOf(_vortexMarch);
                int lane = (int)Math.Floor(_vortexLane + .5f);

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || r.Boss || !Within(r, lane, row, 1)) continue;

                    float gather = Math.Min(1f, dt * SiegeArsenal.VortexGather);
                    r.Drift += (_vortexLane - r.Lane - r.Drift) * gather;
                    if (r.March > _vortexMarch)
                        r.March = Math.Max(_vortexMarch, r.March - dt * SiegeArsenal.VortexDrag);
                    r.Freeze(9, .2f);

                    if (bite) Hit(-1, r, _boosts.PlainShot(_boosts.VortexTenths, r.Kind), SiegeVia.Vortex, -1);
                }

                if (_vortexLeft < 0f) _vortexLeft = 0f;
                return;
            }

            if (_boosts.VortexEvery <= 0f || _boosts.VortexTenths <= 0) { _vortexClock = 0f; return; }

            _vortexClock += dt;
            if (_vortexClock < _boosts.VortexEvery) return;
            _vortexClock = _boosts.VortexEvery;

            if (!Thickest(out int at, out int band)) return;

            _vortexClock = 0f;
            _vortexLeft = SiegeArsenal.VortexFor;
            _vortexTick = SiegeArsenal.VortexTick;
            _vortexLane = at;
            _vortexMarch = (band + .5f) / SiegeTuning.BlastRows;
            _report.Volleys.Add(new SiegeVolley(SiegeVia.Vortex, _vortexMarch, _vortexLane, 1, -1));
        }

        /// <summary>
        /// The four wards fuse one beam and sweep it across the hill, edge to edge, biting every
        /// body in the lane it stands over. Waits, full, for a hill with something on it.
        /// </summary>
        void Rays(float dt)
        {
            if (_rayLeft > 0f)
            {
                _rayLeft -= dt;
                _rayTick -= dt;

                float swept = 1f - Math.Max(0f, _rayLeft) / SiegeArsenal.RayFor;
                _rayLane = -.5f + swept * SiegeTuning.Lanes;

                if (_rayTick > 0f) return;
                _rayTick += SiegeArsenal.RayTick;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r)) continue;
                    if (Math.Abs(r.Lane + r.Drift - _rayLane) > SiegeArsenal.RayWidth * .5f) continue;

                    Hit(-1, r, _boosts.PlainShot(_boosts.RayTenths, r.Kind), SiegeVia.Ray, -1);
                }

                if (_rayLeft < 0f) _rayLeft = 0f;
                return;
            }

            if (_boosts.RayEvery <= 0f || _boosts.RayTenths <= 0) { _rayClock = 0f; return; }

            _rayClock += dt;
            if (_rayClock < _boosts.RayEvery) return;
            _rayClock = _boosts.RayEvery;

            if (Any(null) == null) return;

            _rayClock = 0f;
            _rayLeft = SiegeArsenal.RayFor;
            _rayTick = 0f;
            _rayLane = -.5f;
            _report.Volleys.Add(new SiegeVolley(SiegeVia.Ray, .5f, _rayLane, 0, -1));
        }

        /// <summary>A nuke lands: every body on the hill but a boss loses its share of its full health.</summary>
        void Nukes(float dt)
        {
            if (!_nukeFalling) return;

            _nukeIn -= dt;
            if (_nukeIn > 0f) return;

            _nukeFalling = false;
            _report.Volleys.Add(new SiegeVolley(SiegeVia.Nuke, .5f, (SiegeTuning.Lanes - 1) * .5f,
                                                SiegeTuning.BlastRows, -1));

            _nuking = true;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || r.Boss) continue;

                Hit(-1, r, Math.Max(1, r.MaxHealth * _boosts.NukePercent / 100), SiegeVia.Nuke, -1, soaks: false);
            }

            _nuking = false;
        }

        // ------------------------------------------------------------------ the doors
        /// <summary>
        /// One hit nobody's barrel fired, or one a ward's pool or burst dealt. Through
        /// <c>Splinter</c> when it is a ward's, so plating and a kill's refund read as they always
        /// have; otherwise soaked on a bulwark as a partner shot is, taken through
        /// <c>Wound</c>, and reported with no ward.
        /// </summary>
        void Hit(int ward, SiegeRaider r, int damage, SiegeVia via, int from, bool soaks = true)
        {
            if (damage < 1) damage = 1;

            if (ward >= 0 && ward < _wards.Length)
            {
                Splinter(_wards[ward], ward, r, damage, via, from);
                return;
            }

            if (soaks && r.Kind == SiegeKind.Bulwark)
                damage = Math.Max(1, damage * SiegeTuning.ShieldSoakTenths / 10);

            int took = Wound(r, damage);
            if (took <= 0) return;

            bool killed = Fell(r);
            if (killed) Perished(r, -1);

            _report.Bolts.Add(new SiegeBolt(-1, r.Id, took, false, killed, true, via, from));
        }

        /// <summary>A raider a card may touch: alive, on the hill, and not a boss still walking on.</summary>
        static bool Touchable(SiegeRaider r) => r != null && r.Alive && r.OnTheHill && !r.Impervious;

        /// <summary>Whether a raider stands within <paramref name="reach"/> boxes of a box.</summary>
        static bool Within(SiegeRaider r, int lane, int row, int reach)
            => Math.Abs(SiegeTuning.RowOf(r.March) - row) <= reach && Math.Abs(r.Column - lane) <= reach;

        /// <summary>The raider nearest <paramref name="to"/> down the hill and across it, not yet struck.</summary>
        SiegeRaider Nearest(SiegeRaider to, HashSet<int> struck)
        {
            SiegeRaider next = null;
            float best = float.MaxValue;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == to || !Touchable(r) || struck.Contains(r.Id)) continue;

                float gap = Math.Abs(r.March - to.March) + Math.Abs(r.Column - to.Column) * .1f;
                if (gap >= best) continue;

                best = gap;
                next = r;
            }

            return next;
        }

        /// <summary>A raider picked at random off the build's own stream, or null on an empty hill.</summary>
        SiegeRaider Any(HashSet<int> not)
        {
            _pick.Clear();
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (Touchable(r) && (not == null || !not.Contains(r.Id))) _pick.Add(r);
            }

            if (_pick.Count == 0) return null;
            return _pick[(_boosts.Roll100() * 100 + _boosts.Roll100()) % _pick.Count];
        }

        /// <summary>
        /// The box holding the most raiders: the nearer the line the better on a tie, then the
        /// left. False on an empty hill, and a card waiting on it waits.
        /// </summary>
        bool Thickest(out int lane, out int row)
        {
            lane = 0;
            row = 0;

            int most = 0;
            for (int y = SiegeTuning.BlastRows - 1; y >= 0; y--)
                for (int x = 0; x < SiegeTuning.Lanes; x++)
                {
                    int count = 0;
                    for (int i = 0; i < _raiders.Count; i++)
                        if (Touchable(_raiders[i]) && Within(_raiders[i], x, y, 0)) count++;

                    if (count <= most) continue;

                    most = count;
                    lane = x;
                    row = y;
                }

            return most > 0;
        }

        /// <summary>
        /// Where post <paramref name="ward"/> stands across the hill, in lanes: the wards are
        /// spread evenly under the lanes, which is what the view draws.
        /// </summary>
        float PostLane(int ward)
            => _wards.Length <= 0 ? 0f
             : (ward + .5f) * SiegeTuning.Lanes / _wards.Length - .5f;
    }
}
