using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>A flame on one box of the hill, as the view draws it (<c>fire_trail</c>).</summary>
    public readonly struct SiegeFlame
    {
        public readonly int Lane, Row;

        /// <summary>Seconds left, and the seconds it was laid for.</summary>
        public readonly float Left, For;

        public SiegeFlame(int lane, int row, float left, float laid)
        {
            Lane = lane;
            Row = row;
            Left = left;
            For = laid;
        }
    }

    /// <summary>A ghoul climbing the hill (<see cref="SiegePower.Necromancy"/>), as the view draws it.</summary>
    public readonly struct SiegeGhoul
    {
        /// <summary>The ghoul's own number, and the raider it rose out of.</summary>
        public readonly int Id, From;

        /// <summary>What the raider it rose from was, for its picture.</summary>
        public readonly SiegeKind Kind;

        public readonly int Colour;

        /// <summary>Where it is: lanes across (fractional), and down the hill (0 the top, 1 the line).</summary>
        public readonly float Lane, March;

        /// <summary>Its life left, one to nought.</summary>
        public readonly float Life;

        public SiegeGhoul(int id, int from, SiegeKind kind, int colour, float lane, float march, float life)
        {
            Id = id;
            From = from;
            Kind = kind;
            Colour = colour;
            Lane = lane;
            March = march;
            Life = life;
        }
    }

    /// <summary>
    /// <b>The schools</b> (MODES.md 59r): what a Shuffle build's element cards and ascensions make
    /// the board do - a beam that grows on one target, a prism, every shot a laser; venom that
    /// stacks, rot, contagion, the blight; burning ground, a body that bursts, fire that leaps; a
    /// freeze every few shots, brittle bodies, a winter that piles chills into ice; static, a
    /// thunderclap, lightning that stuns - and the five summits: the laser grid and its sky laser,
    /// the dead rising as ghouls, a volcano, an ice age and a lightning tempest.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, by the arsenal's three locks</b> (59i): every entry returns on
    /// <see cref="SiegeBoosts.AnySchool"/> - false on <see cref="SiegeBoosts.None"/> and on every
    /// build holding no element card - before it reads anything, so a chapter, the Infinite lane and
    /// an empty Shuffle build play exactly the board they played before this file existed
    /// (<c>ShuffleTests.TheIdentityBoostChangesNothing</c>); every random draw is the build's own
    /// stream (41); what it writes is new <see cref="SiegeVia"/> tags and new lists.
    /// </para>
    /// <para>
    /// <b>Every hit goes through the doors every other hit does</b> - <c>Splinter</c> for a ward's,
    /// <see cref="Hit"/> for one nobody's barrel fired - and every strength is tenths of a plain
    /// bolt under the build or a share of the bolt that set it off, so a school climbs with the
    /// damage cards and stays something the ramp outgrows. <b>Every hold is a stun</b>
    /// (<see cref="SiegeRaider.Encase"/>, <see cref="SiegeRaider.Stagger"/>), refused while a body is
    /// still recovering from the last, so a hill of frost and lightning always walks
    /// (<c>ShuffleSchoolTests.NoBuildHoldsTheHillStill</c>).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ state
        // All of it is the run's and none of it is in the save (59g).
        int[] _focusOn, _focusStacks;
        float _infernoClock;

        struct Flame
        {
            public int Lane, Row, Ward, Tenths;
            public float Left, For, Tick;
        }

        readonly List<Flame> _flames = new List<Flame>(SiegeArsenal.MostPools);
        readonly List<SiegeFlame> _flamesDrawn = new List<SiegeFlame>(SiegeArsenal.MostPools);
        // **One scratch set a rule**, never shared: a hit inside one rule can kill, and a death runs
        // contagion and necromancy, which must not empty a set another rule is still walking.
        readonly HashSet<int> _jolted = new HashSet<int>(), _refracted = new HashSet<int>(),
                              _photoned = new HashSet<int>(), _contagious = new HashSet<int>(),
                              _discharged = new HashSet<int>(), _orbited = new HashSet<int>();
        readonly List<SiegeRaider> _alight = new List<SiegeRaider>(24);

        // The laser grid.
        float _gridTick, _orbitalClock, _orbitalIn;
        bool _orbitalCharging;
        readonly List<int> _orbitalMarks = new List<int>(SiegeSchools.OrbitalStrikes);

        // Necromancy.
        sealed class Ghoul
        {
            public int Id, From, Colour;
            public SiegeKind Kind;
            public float Lane, March, Left;
        }

        readonly List<Ghoul> _ghouls = new List<Ghoul>(SiegeSchools.MostGhouls);
        readonly Stack<Ghoul> _spareGhouls = new Stack<Ghoul>(SiegeSchools.MostGhouls);
        readonly List<SiegeGhoul> _ghoulsDrawn = new List<SiegeGhoul>(SiegeSchools.MostGhouls);
        int _ghoulIds;

        // The volcano.
        float _volcanoClock, _rumble, _riverLeft, _riverTick;
        bool _rumbling;
        int _craterLane, _craterRow, _riverLane, _riverFrom;

        // The ice age.
        float _iceAgeClock, _iceAgeIn, _iceAgeLeft;
        bool _iceAgeComing;

        // The tempest.
        float _tempestClock, _tempestLeft, _tempestTick, _tempestLane, _tempestMarch, _tempestBase;
        readonly HashSet<int> _tempested = new HashSet<int>();

        // ------------------------------------------------------------------ what the view reads
        /// <summary>The flames on the hill (<c>fire_trail</c>). Empty on every board without them.</summary>
        public IReadOnlyList<SiegeFlame> Flames
        {
            get
            {
                _flamesDrawn.Clear();
                for (int i = 0; i < _flames.Count; i++)
                    _flamesDrawn.Add(new SiegeFlame(_flames[i].Lane, _flames[i].Row, _flames[i].Left, _flames[i].For));
                return _flamesDrawn;
            }
        }

        /// <summary>The raiders the sky laser has marked and is charging on, and how far charged it is, nought to one.</summary>
        public IReadOnlyList<int> OrbitalMarks => _orbitalMarks;

        public float OrbitalCharge
            => _orbitalCharging ? 1f - Math.Max(0f, _orbitalIn) / SiegeSchools.OrbitalCharge : 0f;

        /// <summary>The ghouls climbing the hill. Empty without necromancy.</summary>
        public IReadOnlyList<SiegeGhoul> Ghouls
        {
            get
            {
                _ghoulsDrawn.Clear();
                for (int i = 0; i < _ghouls.Count; i++)
                {
                    var g = _ghouls[i];
                    _ghoulsDrawn.Add(new SiegeGhoul(g.Id, g.From, g.Kind, g.Colour, g.Lane, g.March,
                                                    Math.Max(0f, g.Left) / SiegeSchools.GhoulFor));
                }

                return _ghoulsDrawn;
            }
        }

        /// <summary>Whether the volcano is rumbling, and where its crater is (lane, row).</summary>
        public bool VolcanoRumbling => _rumbling;

        public int CraterLane => _craterLane;

        public int CraterRow => _craterRow;

        /// <summary>Seconds of lava river left, the lane it runs down and the row it runs from.</summary>
        public float RiverLeft => _riverLeft;

        public int RiverLane => _riverLane;

        public int RiverFrom => _riverFrom;

        /// <summary>Whether the cold of an ice age is coming, and seconds of frozen hill left.</summary>
        public bool IceAgeComing => _iceAgeComing;

        public float IceAgeLeft => _iceAgeLeft;

        /// <summary>Whether the tempest is tearing down the hill, and where its eye is (lane, march).</summary>
        public bool TempestLive => _tempestLeft > 0f;

        public float TempestLane => _tempestLane;

        public float TempestMarch => _tempestMarch;

        /// <summary>Whether <paramref name="r"/> is poisoned: venom on it, or standing in a toxic pool.</summary>
        public bool Poisoned(SiegeRaider r)
        {
            if (r == null || !r.Alive) return false;
            if (r.Envenomed) return true;

            int lane = r.Column, row = SiegeTuning.RowOf(r.March);
            for (int i = 0; i < _pools.Count; i++)
                if (_pools[i].Lane == lane && _pools[i].Row == row) return true;

            return false;
        }

        static bool Chilled(SiegeRaider r) => r.Chill > 0f || r.Stun > 0f;

        // ------------------------------------------------------------------ the hooks
        /// <summary>
        /// <b>A stopped hill starts again walking</b> (the owner, 2026-10-20: "no raider ... 100%
        /// slowed and cannot walk"). When an hourglass or a time stop runs out, every body is given
        /// <see cref="SiegeTuning.StunRest"/> of steadiness, so the stun a build lays the next frame
        /// is refused (<see cref="SiegeRaider.Stagger"/>) and two holds never meet end to end. From
        /// <c>Walk</c>, on the frame the stop ends - and the hill walks <see cref="SiegeSchools.HoldRest"/>
        /// before a wave's hourglass may stop it again (<see cref="MayStill"/>) - on a build holding an element card or a power,
        /// so the plain line, an empty build and every build of the cards before this drop play the
        /// board they always played (`TheIdentityBoostChangesNothing`).
        /// </summary>
        void Thawed()
        {
            if (!_boosts.AnySchool && !_boosts.AnyPower) return;

            _thaw = SiegeSchools.HoldRest;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r.Alive && r.Steady < SiegeSchools.HoldRest) r.Steady = SiegeSchools.HoldRest;
            }
        }

        /// <summary>Seconds since a stopped hill started walking again are still within its grace. See <see cref="Thawed"/>.</summary>
        float _thaw;

        /// <summary>
        /// Whether a build's own stop (a wave's hourglass, a time stop) may stop the hill: never
        /// while another is running and never inside the walk a thaw has just granted, so two stops
        /// never extend each other or meet end to end. The player's hourglass charm is theirs and
        /// is never asked. Always on a build holding neither an element card nor a power, which is
        /// every build before this drop.
        /// </summary>
        bool MayStill => (_still <= 0f && _thaw <= 0f) || (!_boosts.AnySchool && !_boosts.AnyPower);

        /// <summary>
        /// What a hit takes off a body under the schools' afflictions (the blight, brittle). From
        /// <c>Wound</c>, the one door every point of harm goes through, so every source the mode has
        /// is multiplied without being taught about either. The hit itself on every other board.
        /// </summary>
        int Afflict(SiegeRaider raider, int damage)
        {
            var b = _boosts;
            if (!b.AnySchool || (b.BlightPercent <= 0 && b.BrittlePercent <= 0)) return damage;

            return b.Afflicted(damage, b.BlightPercent > 0 && Poisoned(raider), b.BrittlePercent > 0 && Chilled(raider));
        }

        /// <summary>A poisoned raider's blow under the rot. From <c>Swing</c>; the blow itself on every other board.</summary>
        int Rot(SiegeRaider raider, int damage)
        {
            if (!_boosts.AnySchool || _boosts.RotPercent <= 0 || !Poisoned(raider)) return damage;
            return _boosts.Rotted(damage);
        }

        /// <summary>
        /// What the school cards add to a primary bolt that has landed at full strength. From
        /// <c>Augment</c>, which is already shut on the plain line.
        /// </summary>
        void Schooled(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            var b = _boosts;
            if (!b.AnySchool || target == null) return;

            if (b.FocusPercent > 0) Focus(ward, index, target, damage);

            if (b.RefractEvery > 0 && ward.Shots % b.RefractEvery == 0) Refract(ward, index, target, damage);

            // **Every shot a laser** - but a laser gun's own beam already goes on (`Lase`), and two
            // beams off one barrel would be one card counted twice.
            if (b.PhotonHits > 0 && !Lasered(index)) Photon(ward, index, target, damage);

            if (b.VenomTenths > 0) target.Envenom(1, b.VenomCap, SiegeSchools.VenomFor);
            if (b.BlightPercent > 0) target.Envenom(SiegeSchools.BlightDose, b.VenomCap, SiegeSchools.VenomFor);

            if (b.TrailTenths > 0)
                LayFlame(target.Column, SiegeTuning.RowOf(target.March), index, b.TrailTenths, b.TrailFor);

            if (b.InfernoReach > 0 && target.Alive)
                target.Kindle(b.PlainShot(SiegeSchools.InfernoBurnTenths, target.Kind), SiegeSchools.InfernoBurnFor, index);

            if (b.ColdSnapEvery > 0 && ward.Shots % b.ColdSnapEvery == 0 && target.Alive && target.Encase(b.ColdSnapFor))
                Announce(SiegeVia.Freeze, target.March, target.Lane + target.Drift, 0, index);

            if (b.WinterStacks > 0 && target.Alive) Winter(index, target);

            if (b.StaticHits > 0 && target.Alive && ++target.Charge >= b.StaticHits) Discharge(ward, index, target);

            if (b.ThunderEvery > 0 && ward.Shots % b.ThunderEvery == 0) Thunderclap(ward, index, target);

            // The storm heart's own arc: every shot jumps to the body nearest the one it struck.
            if (b.ConductTenths > 0)
            {
                _jolted.Clear();
                _jolted.Add(target.Id);
                var next = Nearest(target, _jolted);
                if (next != null)
                    Splinter(ward, index, next, Share(damage, SiegeSchools.JoltTenths), SiegeVia.Jolt, target.Id);
            }
        }

        /// <summary>
        /// What the schools make of a raider falling: contagion passes its venom on, necromancy raises
        /// it. From <c>Perished</c>, so a body a venom tick killed counts as one a bolt did.
        /// </summary>
        void Departed(SiegeRaider fallen, int ward)
        {
            if (!_boosts.AnySchool || fallen == null || fallen.Boss) return;

            bool poisoned = fallen.Venom > 0 && fallen.Venomed > 0f;
            if (!poisoned)
            {
                int lane = fallen.Column, row = SiegeTuning.RowOf(fallen.March);
                for (int i = 0; i < _pools.Count && !poisoned; i++)
                    if (_pools[i].Lane == lane && _pools[i].Row == row) poisoned = true;
            }

            if (!poisoned) return;

            if (_boosts.ContagionCount > 0) Contagion(fallen, ward);
            if (_boosts.Has(SiegePower.Necromancy)) RaiseGhoul(fallen);
        }

        // ------------------------------------------------------------------ laser
        /// <summary>Focus lens: a beam on the bolt's target that grows with every hit in a row on it.</summary>
        void Focus(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            if (_focusOn == null || _focusOn.Length != _wards.Length)
            {
                _focusOn = new int[_wards.Length];
                _focusStacks = new int[_wards.Length];
                for (int i = 0; i < _focusOn.Length; i++) _focusOn[i] = -1;
            }

            if (_focusOn[index] == target.Id)
            {
                if (_focusStacks[index] < _boosts.FocusMost) _focusStacks[index]++;
            }
            else
            {
                _focusOn[index] = target.Id;
                _focusStacks[index] = 1;
            }

            if (!target.Alive)
            {
                _focusOn[index] = -1;
                _focusStacks[index] = 0;
                return;
            }

            long beam = (long)damage * _focusStacks[index] * _boosts.FocusPercent / 100;
            Splinter(ward, index, target, beam < 1 ? 1 : beam > int.MaxValue ? int.MaxValue : (int)beam, SiegeVia.Focus);
        }

        /// <summary>How many hits in a row ward <paramref name="ward"/> has focused, for the view's beam. Nought without the lens.</summary>
        public int FocusOf(int ward) => _focusStacks == null || ward < 0 || ward >= _focusStacks.Length ? 0 : _focusStacks[ward];

        /// <summary>Refractor: the bolt splits at its target into beams at the toughest raiders on the hill.</summary>
        void Refract(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            _refracted.Clear();
            _refracted.Add(target.Id);

            int share = Share(damage, _boosts.RefractTenths);
            for (int n = 0; n < _boosts.RefractBeams; n++)
            {
                var r = Strongest(_refracted);
                if (r == null) break;

                _refracted.Add(r.Id);
                Splinter(ward, index, r, share, SiegeVia.Refract, target.Id);
            }
        }

        /// <summary>Photon heart: the bolt goes on as a laser into the raiders nearest the one it struck.</summary>
        void Photon(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            int share = Share(damage, _boosts.PhotonTenths);

            _photoned.Clear();
            _photoned.Add(target.Id);

            var from = target;
            for (int n = 0; n < _boosts.PhotonHits; n++)
            {
                var next = Nearest(from, _photoned);
                if (next == null) break;

                _photoned.Add(next.Id);
                Splinter(ward, index, next, share, SiegeVia.Laser, from.Id);
                from = next;
            }
        }

        // ------------------------------------------------------------------ poison
        /// <summary>Contagion: a poisoned body's death hands its venom to the raiders nearest it.</summary>
        void Contagion(SiegeRaider fallen, int ward)
        {
            int stacks = Math.Max(fallen.Venom, SiegeSchools.BlightDose);
            int lane = fallen.Column, row = SiegeTuning.RowOf(fallen.March);

            _contagious.Clear();
            _contagious.Add(fallen.Id);

            // **The place first, then each body it reached** (reach nought): the view draws the
            // spores from the one to the others in the order they were announced.
            bool announced = false;
            for (int n = 0; n < _boosts.ContagionCount; n++)
            {
                var next = Nearest(fallen, _contagious);
                if (next == null || !Within(next, lane, row, _boosts.ContagionReach)) break;

                _contagious.Add(next.Id);
                if (!next.Envenom(stacks, _boosts.VenomCap, SiegeSchools.VenomFor)) continue;

                if (!announced)
                {
                    Announce(SiegeVia.Contagion, fallen.March, fallen.Lane + fallen.Drift, _boosts.ContagionReach, ward);
                    announced = true;
                }

                Announce(SiegeVia.Contagion, next.March, next.Lane + next.Drift, 0, ward);
            }
        }

        /// <summary>Venom bites on its own cadence, through the one door, until the dose runs out.</summary>
        void Venoms(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r.Venom <= 0) continue;

                if (!r.Alive) { r.Venom = 0; continue; }

                float span = Math.Min(dt, r.Venomed);
                r.Venomed -= dt;
                r.VenomSear -= span;

                bool ended = r.Venomed <= 0f;
                if (r.VenomSear <= 0f || ended)
                {
                    if (!ended) r.VenomSear += SiegeSchools.VenomTick;

                    // **Venom is not soaked by a shield**: it is in the body, which is the poison
                    // school's edge against the armour a deep wave is made of.
                    if (Touchable(r))
                        Hit(-1, r, _boosts.PlainShot(_boosts.VenomBite * r.Venom, r.Kind), SiegeVia.Venom, -1, soaks: false);
                }

                if (ended)
                {
                    r.Venom = 0;
                    r.Venomed = 0f;
                }
            }
        }

        // ------------------------------------------------------------------ fire
        /// <summary>Lays a flame on a box, or feeds the one burning there.</summary>
        void LayFlame(int lane, int row, int ward, int tenths, float seconds)
        {
            for (int i = 0; i < _flames.Count; i++)
            {
                var flame = _flames[i];
                if (flame.Lane != lane || flame.Row != row) continue;

                if (seconds > flame.Left) flame.Left = seconds;
                if (seconds > flame.For) flame.For = seconds;
                if (tenths > flame.Tenths) flame.Tenths = tenths;
                flame.Ward = ward;
                _flames[i] = flame;
                return;
            }

            if (_flames.Count >= SiegeArsenal.MostPools) return;

            _flames.Add(new Flame
            {
                Lane = lane, Row = row, Ward = ward, Tenths = tenths,
                Left = seconds, For = seconds, Tick = SiegeSchools.FlameTick,
            });
        }

        /// <summary>The flames bite and set alight whatever stands in their box, and burn down.</summary>
        void Burning(float dt)
        {
            for (int f = _flames.Count - 1; f >= 0; f--)
            {
                var flame = _flames[f];
                flame.Left -= dt;
                flame.Tick -= dt;

                if (flame.Tick <= 0f)
                {
                    flame.Tick += SiegeSchools.FlameTick;

                    for (int i = 0; i < _raiders.Count; i++)
                    {
                        var r = _raiders[i];
                        if (!Touchable(r) || !Within(r, flame.Lane, flame.Row, 0)) continue;

                        Hit(flame.Ward, r, _boosts.PlainShot(flame.Tenths, r.Kind), SiegeVia.Flame, -1);
                        if (r.Alive)
                            r.Kindle(_boosts.PlainShot(SiegeSchools.FlameBurnTenths, r.Kind), SiegeSchools.FlameBurnFor,
                                     flame.Ward);
                    }
                }

                if (flame.Left <= 0f) _flames.RemoveAt(f);
                else _flames[f] = flame;
            }
        }

        /// <summary>Combust: how long each body has burned without a break, and the burst when it has burned long enough.</summary>
        void Kindled(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!r.Alight) { r.Alit = 0f; continue; }

                r.Alit += dt;
                if (_boosts.CombustTenths <= 0 || r.Combusted || r.Alit < _boosts.CombustAfter || !Touchable(r)) continue;

                Combust(r);
            }
        }

        /// <summary>A body that has burned long enough bursts: it and everything round it struck and set alight. Once a body.</summary>
        void Combust(SiegeRaider r)
        {
            r.Combusted = true;

            int lane = r.Column, row = SiegeTuning.RowOf(r.March);
            int rate = Math.Max(r.BurnRate, _boosts.PlainShot(SiegeSchools.FlameBurnTenths, SiegeKind.Creeper));
            int from = r.BurnFrom;

            Announce(SiegeVia.Combust, r.March, lane, 1, from);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (!Touchable(other) || !Within(other, lane, row, 1)) continue;

                Hit(-1, other, _boosts.PlainShot(_boosts.CombustTenths, other.Kind), SiegeVia.Combust, other == r ? -1 : r.Id);
                if (other.Alive && other != r) other.Kindle(rate, SiegeSchools.FlameBurnFor, from);
            }
        }

        /// <summary>
        /// Inferno heart: on its clock, every burning body sets the bodies within reach alight - at
        /// its own fire or theirs, the fiercer. A spread is a burn, never a hit, so it climbs with
        /// nothing but the fire that was already there.
        /// </summary>
        void Inferno(float dt)
        {
            if (_boosts.InfernoReach <= 0) return;

            _infernoClock += dt;
            if (_infernoClock < _boosts.InfernoEvery) return;
            _infernoClock -= _boosts.InfernoEvery;

            // The burning are counted before any spread, so a body caught this beat waits a beat.
            _alight.Clear();
            for (int i = 0; i < _raiders.Count; i++)
                if (Touchable(_raiders[i]) && _raiders[i].Alight) _alight.Add(_raiders[i]);

            // **The place, then each body it caught** (reach nought), for the view to draw the leap -
            // a few sources a beat at most, so a burning crowd is a picture and not a flood.
            int shown = 0;
            for (int a = 0; a < _alight.Count; a++)
            {
                var source = _alight[a];
                int lane = source.Column, row = SiegeTuning.RowOf(source.March);
                bool drawn = false;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (r == source || !Touchable(r) || r.Alight || !Within(r, lane, row, _boosts.InfernoReach)) continue;

                    r.Kindle(source.BurnRate, SiegeSchools.FlameBurnFor, source.BurnFrom);
                    if (shown >= SiegeSchools.MostLeapsShown) continue;

                    if (!drawn)
                    {
                        Announce(SiegeVia.Inferno, source.March, source.Lane + source.Drift, _boosts.InfernoReach, source.BurnFrom);
                        drawn = true;
                    }

                    Announce(SiegeVia.Inferno, r.March, r.Lane + r.Drift, 0, source.BurnFrom);
                }

                if (drawn) shown++;
            }
        }

        // ------------------------------------------------------------------ frost
        /// <summary>Winter heart: every shot chills, and the chills pile until the body freezes solid.</summary>
        void Winter(int index, SiegeRaider target)
        {
            target.Freeze(_boosts.WinterTenths, SiegeSchools.WinterChillFor);
            if (++target.Rime < _boosts.WinterStacks) return;

            // **Held at the pile until a freeze takes** rather than spent on a refusal, so a body
            // still recovering from the last hold freezes on the first hit after it can.
            target.Rime = _boosts.WinterStacks;
            if (!target.Encase(SiegeSchools.WinterFreeze)) return;

            target.Rime = 0;
            Announce(SiegeVia.Freeze, target.March, target.Lane + target.Drift, 0, index);
        }

        // ------------------------------------------------------------------ storm
        /// <summary>Static: a charged body discharges into itself and the raiders nearest it.</summary>
        void Discharge(SiegeWard ward, int index, SiegeRaider target)
        {
            target.Charge = 0;

            Splinter(ward, index, target, _boosts.PlainShot(_boosts.StaticTenths, target.Kind), SiegeVia.Static);

            _discharged.Clear();
            _discharged.Add(target.Id);

            float lane = target.Lane + target.Drift;
            for (int n = 0; n < _boosts.StaticArcs; n++)
            {
                var next = Nearest(target, _discharged);
                if (next == null || Math.Abs(next.Lane + next.Drift - lane) > SiegeSchools.StaticSpan) break;

                _discharged.Add(next.Id);
                Splinter(ward, index, next, _boosts.PlainShot(_boosts.StaticTenths, next.Kind), SiegeVia.Static, target.Id);
            }
        }

        /// <summary>Thunderclap: lightning out of the sky onto the target's box, striking and stunning everything in it.</summary>
        void Thunderclap(SiegeWard ward, int index, SiegeRaider target)
        {
            int lane = target.Column, row = SiegeTuning.RowOf(target.March);
            Announce(SiegeVia.Thunderclap, target.March, lane, 1, index);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || !Within(r, lane, row, 1)) continue;

                Splinter(ward, index, r, _boosts.PlainShot(_boosts.ThunderTenths, r.Kind), SiegeVia.Thunderclap, target.Id);
                if (r.Alive) r.Daze(SiegeSchools.ThunderStun);
            }
        }

        /// <summary>Whether a hit of <paramref name="via"/> is lightning, which the storm heart stuns with.</summary>
        static bool IsLightning(SiegeVia via)
            => via == SiegeVia.Arc || via == SiegeVia.Tesla || via == SiegeVia.Storm || via == SiegeVia.Orb
               || via == SiegeVia.Static || via == SiegeVia.Thunderclap || via == SiegeVia.Jolt || via == SiegeVia.Tempest;

        /// <summary>
        /// Storm heart: every body lightning struck this step is stunned - through
        /// <see cref="SiegeRaider.Daze"/>, so a body under a storm still walks between holds.
        /// </summary>
        void Conducted()
        {
            if (_boosts.ConductTenths <= 0) return;

            float stun = _boosts.ConductTenths / 10f;
            var bolts = _report.Bolts;

            for (int b = 0; b < bolts.Count; b++)
            {
                if (!IsLightning(bolts[b].Via)) continue;

                int id = bolts[b].Raider;
                for (int i = 0; i < _raiders.Count; i++)
                {
                    if (_raiders[i].Id != id) continue;
                    if (_raiders[i].Alive) _raiders[i].Daze(stun);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ the clock
        /// <summary>Every school rule that runs on a clock. Shut on every build holding none.</summary>
        void Schools(float dt)
        {
            if (!_boosts.AnySchool) return;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r.Encased > 0f) r.Encased = Math.Max(0f, r.Encased - dt);
            }

            Venoms(dt);
            Burning(dt);
            Kindled(dt);
            Inferno(dt);

            Grid(dt);
            Haunt(dt);
            Volcano(dt);
            IceAge(dt);
            Tempests(dt);

            // Last, so every lightning hit of the step - the line's, the cloud's, the tempest's -
            // is one the storm heart answers.
            Conducted();
        }

        // ------------------------------------------------------------------ the laser grid
        /// <summary>
        /// The laser grid: a fence of light across the hill that cuts every body crossing it, and a
        /// sky laser that marks the toughest raiders, charges, and strikes them.
        /// </summary>
        void Grid(float dt)
        {
            if (!_boosts.Has(SiegePower.LaserGrid)) return;

            _gridTick += dt;
            if (_gridTick >= SiegeSchools.GridTick)
            {
                _gridTick -= SiegeSchools.GridTick;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || Math.Abs(r.March - SiegeSchools.GridMarch) > SiegeSchools.GridBand) continue;

                    Hit(-1, r, _boosts.PlainShot(SiegeSchools.GridTenths, r.Kind), SiegeVia.Grid, -1);
                }
            }

            if (_orbitalCharging)
            {
                _orbitalIn -= dt;
                if (_orbitalIn > 0f) return;

                _orbitalCharging = false;
                _orbited.Clear();

                for (int m = 0; m < _orbitalMarks.Count; m++)
                {
                    // On the body it marked, or the toughest left if that one is gone.
                    SiegeRaider r = null;
                    for (int i = 0; i < _raiders.Count; i++)
                        if (_raiders[i].Id == _orbitalMarks[m] && Touchable(_raiders[i])) { r = _raiders[i]; break; }

                    if (r == null || _orbited.Contains(r.Id)) r = Strongest(_orbited);
                    if (r == null) break;

                    _orbited.Add(r.Id);
                    Announce(SiegeVia.Orbital, r.March, r.Lane + r.Drift, m, -1);
                    Hit(-1, r, _boosts.PlainShot(SiegeSchools.OrbitalTenths, r.Kind), SiegeVia.Orbital, -1, soaks: false);
                }

                _orbitalMarks.Clear();
                return;
            }

            _orbitalClock += dt;
            if (_orbitalClock < SiegeSchools.OrbitalEvery) return;
            _orbitalClock = SiegeSchools.OrbitalEvery;

            if (!Peopled()) return;

            _orbitalClock = 0f;
            _orbitalCharging = true;
            _orbitalIn = SiegeSchools.OrbitalCharge;
            _orbitalMarks.Clear();
            _orbited.Clear();

            for (int n = 0; n < SiegeSchools.OrbitalStrikes; n++)
            {
                var r = Strongest(_orbited);
                if (r == null) break;

                _orbited.Add(r.Id);
                _orbitalMarks.Add(r.Id);
                Announce(SiegeVia.Orbital, r.March, r.Lane + r.Drift, n, -1, SiegeSchools.OrbitalCharge);
            }
        }

        // ------------------------------------------------------------------ necromancy
        /// <summary>A poisoned body rises as a ghoul where it fell, while the hill holds fewer than the most.</summary>
        void RaiseGhoul(SiegeRaider fallen)
        {
            if (_ghouls.Count >= SiegeSchools.MostGhouls) return;

            var g = _spareGhouls.Count > 0 ? _spareGhouls.Pop() : new Ghoul();
            g.Id = ++_ghoulIds;
            g.From = fallen.Id;
            g.Kind = fallen.Kind;
            g.Colour = fallen.Colour;
            g.Lane = fallen.Lane + fallen.Drift;
            g.March = fallen.March;
            g.Left = SiegeSchools.GhoulFor;
            _ghouls.Add(g);

            Announce(SiegeVia.Ghoul, g.March, g.Lane, 0, -1);
        }

        /// <summary>
        /// Every ghoul climbs the hill toward the raider nearest above it, and bursts on the first it
        /// reaches - or where it stands when its life runs out.
        /// </summary>
        void Haunt(float dt)
        {
            for (int i = _ghouls.Count - 1; i >= 0; i--)
            {
                // A burst may kill a poisoned body and raise another ghoul: it is appended past `i`
                // and walks from the next frame.
                if (i >= _ghouls.Count) continue;

                var g = _ghouls[i];
                g.Left -= dt;

                SiegeRaider mark = null;
                float best = float.MaxValue;
                for (int r = 0; r < _raiders.Count; r++)
                {
                    var raider = _raiders[r];
                    if (!Touchable(raider) || raider.March > g.March + SiegeSchools.GhoulNear) continue;

                    float gap = (g.March - raider.March) + Math.Abs(raider.Lane + raider.Drift - g.Lane) * .15f;
                    if (gap >= best) continue;

                    best = gap;
                    mark = raider;
                }

                if (mark != null)
                {
                    float want = mark.Lane + mark.Drift;
                    float step = SiegeSchools.GhoulSwerve * dt;
                    g.Lane = Math.Abs(want - g.Lane) <= step ? want : g.Lane + Math.Sign(want - g.Lane) * step;
                }

                g.March -= SiegeSchools.GhoulClimb * dt;

                bool reached = mark != null && Math.Abs(mark.March - g.March) <= SiegeSchools.GhoulNear
                               && Math.Abs(mark.Lane + mark.Drift - g.Lane) <= SiegeSchools.GhoulWide;

                if (!reached && g.Left > 0f && g.March > 0f) continue;

                _ghouls.RemoveAt(i);
                Burst(g);
                _spareGhouls.Push(g);
            }
        }

        /// <summary>A ghoul bursting: its box and the boxes round it struck and poisoned.</summary>
        void Burst(Ghoul g)
        {
            int lane = (int)Math.Floor(g.Lane + .5f);
            if (lane < 0) lane = 0;
            if (lane >= SiegeTuning.Lanes) lane = SiegeTuning.Lanes - 1;
            int row = SiegeTuning.RowOf(Math.Max(0f, g.March));

            Announce(SiegeVia.Ghoul, Math.Max(0f, g.March), g.Lane, 1, -1);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || !Within(r, lane, row, 1)) continue;

                Hit(-1, r, _boosts.PlainShot(SiegeSchools.GhoulTenths, r.Kind), SiegeVia.Ghoul, -1, soaks: false);
                if (r.Alive) r.Envenom(SiegeSchools.GhoulVenom, _boosts.VenomCap, SiegeSchools.VenomFor);
            }
        }

        // ------------------------------------------------------------------ the volcano
        /// <summary>
        /// The volcano: it rumbles under the thickest crowd, erupts - lava bombs raining round the
        /// crater - and pours a lava river down the crater's lane to the line.
        /// </summary>
        void Volcano(float dt)
        {
            if (!_boosts.Has(SiegePower.Volcano)) return;

            if (_riverLeft > 0f)
            {
                _riverLeft -= dt;
                _riverTick -= dt;

                if (_riverTick <= 0f)
                {
                    _riverTick += SiegeSchools.RiverTick;

                    for (int i = 0; i < _raiders.Count; i++)
                    {
                        var r = _raiders[i];
                        if (!Touchable(r) || r.Column != _riverLane || SiegeTuning.RowOf(r.March) < _riverFrom) continue;

                        Hit(-1, r, _boosts.PlainShot(SiegeSchools.RiverTenths, r.Kind), SiegeVia.Lava, -1);
                        if (r.Alive)
                            r.Kindle(_boosts.PlainShot(SiegeSchools.RiverBurnTenths, r.Kind), SiegeSchools.RiverBurnFor, -1);
                    }
                }

                if (_riverLeft < 0f) _riverLeft = 0f;
            }

            if (_rumbling)
            {
                _rumble -= dt;
                if (_rumble > 0f) return;

                Erupt();
                return;
            }

            _volcanoClock += dt;
            if (_volcanoClock < SiegeSchools.VolcanoEvery) return;
            _volcanoClock = SiegeSchools.VolcanoEvery;

            if (!Thickest(out _craterLane, out _craterRow)) return;

            _volcanoClock = 0f;
            _rumbling = true;
            _rumble = SiegeSchools.VolcanoRumble;
            Announce(SiegeVia.Volcano, (_craterRow + .5f) / SiegeTuning.BlastRows, _craterLane,
                     SiegeSchools.LavaScatter, -1, SiegeSchools.VolcanoRumble);
        }

        void Erupt()
        {
            _rumbling = false;
            Announce(SiegeVia.Volcano, (_craterRow + .5f) / SiegeTuning.BlastRows, _craterLane, SiegeSchools.LavaScatter, -1);

            int span = SiegeSchools.LavaScatter * 2 + 1;
            for (int n = 0; n < SiegeSchools.LavaBombs; n++)
            {
                int dx = Roll() % span - SiegeSchools.LavaScatter;
                int dy = Roll() % span - SiegeSchools.LavaScatter;

                Book(SiegeVia.Lava, _craterLane + dx, _craterRow + dy, 0, SiegeSchools.LavaTenths, -1,
                     SiegeSchools.LavaFall + n * SiegeSchools.LavaGap, SiegeSchools.LavaBurnTenths, SiegeSchools.LavaBurnFor);
            }

            _riverLeft = SiegeSchools.RiverFor;
            _riverTick = 0f;
            _riverLane = _craterLane;
            _riverFrom = _craterRow;
        }

        // ------------------------------------------------------------------ the ice age
        /// <summary>
        /// The ice age: on its clock - and never inside a stopped hill, so two holds never meet - the
        /// cold comes down, the whole hill freezes solid, and every body already under a quarter of
        /// its health shatters into shards that strike its neighbours. What is left thaws chilled.
        /// </summary>
        void IceAge(float dt)
        {
            if (!_boosts.Has(SiegePower.IceAge)) return;

            if (_iceAgeLeft > 0f) _iceAgeLeft = Math.Max(0f, _iceAgeLeft - dt);

            if (_iceAgeComing)
            {
                _iceAgeIn -= dt;
                if (_iceAgeIn > 0f) return;

                Glaciate();
                return;
            }

            _iceAgeClock += dt;
            if (_iceAgeClock < SiegeSchools.IceAgeEvery) return;
            _iceAgeClock = SiegeSchools.IceAgeEvery;

            if (_still > 0f || !Peopled()) return;

            _iceAgeComing = true;
            _iceAgeIn = SiegeSchools.IceAgeComing;
            Announce(SiegeVia.IceAge, .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1, SiegeSchools.IceAgeComing);
        }

        void Glaciate()
        {
            _iceAgeComing = false;
            _iceAgeClock = 0f;
            _iceAgeLeft = SiegeSchools.IceAgeFreeze;

            Announce(SiegeVia.IceAge, .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1);

            _alight.Clear();
            for (int i = 0; i < _raiders.Count; i++)
                if (Touchable(_raiders[i]) && !_raiders[i].Boss) _alight.Add(_raiders[i]);

            for (int a = 0; a < _alight.Count; a++)
            {
                var r = _alight[a];
                if (!r.Alive) continue;

                bool weak = (long)r.Health * 100 <= (long)r.MaxHealth * SiegeSchools.IceAgeShatterPercent;
                if (!weak)
                {
                    r.Encase(SiegeSchools.IceAgeFreeze);
                    r.Freeze(SiegeSchools.IceAgeChillTenths, SiegeSchools.IceAgeFreeze + SiegeSchools.IceAgeChillFor);
                    continue;
                }

                int lane = r.Column, row = SiegeTuning.RowOf(r.March);
                Announce(SiegeVia.IceAge, r.March, r.Lane + r.Drift, 1, -1);
                Hit(-1, r, r.Health, SiegeVia.IceAge, -1, soaks: false);

                // The shards strike its neighbours. They never shatter anything themselves, so one
                // ice age is one pass over the hill.
                for (int i = 0; i < _raiders.Count; i++)
                {
                    var other = _raiders[i];
                    if (other == r || !Touchable(other) || !Within(other, lane, row, 1)) continue;

                    Hit(-1, other, _boosts.PlainShot(SiegeSchools.IceAgeShardTenths, other.Kind), SiegeVia.IceAge, r.Id);
                }
            }
        }

        // ------------------------------------------------------------------ the tempest
        /// <summary>
        /// The tempest: a tornado of lightning tears down the busiest lane from the crest to the line,
        /// swaying as it goes, striking and stunning the bodies round its eye - each once a pass.
        /// </summary>
        void Tempests(float dt)
        {
            if (!_boosts.Has(SiegePower.Tempest)) return;

            if (_tempestLeft > 0f)
            {
                _tempestLeft -= dt;
                _tempestTick -= dt;

                float t = 1f - Math.Max(0f, _tempestLeft) / SiegeSchools.TempestFor;
                _tempestMarch = -.05f + t * 1.05f;
                _tempestLane = _tempestBase + SiegeSchools.TempestSway * (float)Math.Sin(t * Math.PI * 2.5);

                if (_tempestTick <= 0f)
                {
                    _tempestTick += SiegeSchools.TempestTick;

                    for (int n = 0; n < SiegeSchools.TempestStrikes; n++)
                    {
                        SiegeRaider best = null;
                        float near = float.MaxValue;

                        for (int i = 0; i < _raiders.Count; i++)
                        {
                            var r = _raiders[i];
                            if (!Touchable(r) || _tempested.Contains(r.Id)) continue;

                            float across = Math.Abs(r.Lane + r.Drift - _tempestLane);
                            float down = Math.Abs(r.March - _tempestMarch);
                            if (across > SiegeSchools.TempestSpan || down > SiegeSchools.TempestBand) continue;

                            float gap = down + across * .1f;
                            if (gap >= near) continue;

                            near = gap;
                            best = r;
                        }

                        if (best == null) break;

                        _tempested.Add(best.Id);
                        Hit(-1, best, _boosts.PlainShot(SiegeSchools.TempestTenths, best.Kind), SiegeVia.Tempest, -1);
                        if (best.Alive) best.Daze(SiegeSchools.TempestStun);
                    }
                }

                if (_tempestLeft > 0f) return;

                _tempestLeft = 0f;
                _tempested.Clear();
                return;
            }

            _tempestClock += dt;
            if (_tempestClock < SiegeSchools.TempestEvery) return;
            _tempestClock = SiegeSchools.TempestEvery;

            int lane = Crowded();
            if (lane < 0) return;

            _tempestClock = 0f;
            _tempestLeft = SiegeSchools.TempestFor;
            _tempestTick = 0f;
            _tempestBase = lane;
            _tempestLane = lane;
            _tempestMarch = -.05f;
            _tempested.Clear();

            Announce(SiegeVia.Tempest, 0f, lane, 0, -1, SiegeSchools.TempestFor);
        }
    }
}
