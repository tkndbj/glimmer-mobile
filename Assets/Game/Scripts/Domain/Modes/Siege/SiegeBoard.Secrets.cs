using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// <b>The secrets and evolutions</b> (MODES.md 59n): what a Shuffle build's unlocked powers
    /// (<see cref="SiegePower"/>) make the board do - a storm cloud over the hill, steam
    /// geysers, blades round every post, a plague, comets thrown off the gem board, a rainbow
    /// off a cascade, a frozen lower hill, a black hole, a fire dragon, a golden dome, a second
    /// sun, a meteor shower, time stopping; and the evolved cards' titan shells, sniper rounds,
    /// fortress walls, cluster bombs, ball lightning, missile swarms, blizzards, comet shards and
    /// a prism.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, by the arsenal's three locks</b> (59i): every entry returns
    /// on <see cref="SiegeBoosts.AnyPower"/> before it reads anything, which is false on
    /// <see cref="SiegeBoosts.None"/> and on every build that has unlocked nothing, so a chapter,
    /// the Infinite lane and an empty Shuffle build play exactly the board they played before this
    /// file existed (<c>ShuffleTests.TheIdentityBoostChangesNothing</c>); every random draw is the
    /// build's own stream (<see cref="SiegeBoosts.Roll100"/>), never the field's or the hill's
    /// (invariant 41); and what it writes is new <see cref="SiegeVia"/> tags and new lists.
    /// </para>
    /// <para>
    /// <b>Every hit goes through the doors every other hit does</b> - <c>Splinter</c> for a
    /// ward's, <c>Hit</c> for one nobody's barrel fired - and every strength is tenths of a plain
    /// bolt under the build (<see cref="SiegeBoosts.PlainShot"/>), so a power climbs with the
    /// damage cards and never past what the ramp compounds into: a power makes a run stronger
    /// and never makes one endless. A blocked blow, a time stop and a mended wall are bounded
    /// per wave or per clock (<see cref="SiegeSecrets"/>).
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        // ------------------------------------------------------------------ state
        // All of it is the run's and none of it is in the save (59g).
        float _storm, _steam, _blade, _spike, _orbClock;
        float _horizonClock, _horizonLeft, _horizonTick, _horizonLane, _horizonMarch;
        float _dragonClock, _dragonLeft, _dragonLane, _dragonMarch, _dragonRow;
        float _solarClock, _solarIn, _armageddon, _timeClock, _timeStop, _prismClock, _prismIn;
        bool _solarCharging, _prismCharging;
        int _solarLane, _dome;

        readonly HashSet<int> _scorched = new HashSet<int>();
        readonly HashSet<int> _picked = new HashSet<int>();

        /// <summary>A hit booked to land on a box later: a meteor of a shower, a bomblet, a shard.</summary>
        struct Pending
        {
            public SiegeVia Via;
            public int Lane, Row, Reach, Tenths, Ward, BurnTenths;
            public float In, BurnFor;
        }

        readonly List<Pending> _pending = new List<Pending>(16);
        readonly List<Pending> _due = new List<Pending>(8);

        /// <summary>A comet or rainbow bolt thrown from the field, landing on a raider.</summary>
        struct Comet
        {
            public SiegeVia Via;
            public int Raider, Tenths;
            public float In;
        }

        readonly List<Comet> _comets = new List<Comet>(4);
        readonly List<Comet> _landed = new List<Comet>(4);

        sealed class Orb
        {
            public int Ward;
            public float Lane, March, Tick;
            public readonly HashSet<int> Hit = new HashSet<int>();
        }

        readonly List<Orb> _orbs = new List<Orb>(SiegeSecrets.MostOrbs);
        readonly Stack<Orb> _spareOrbs = new Stack<Orb>(SiegeSecrets.MostOrbs);
        readonly List<SiegeOrb> _orbsDrawn = new List<SiegeOrb>(SiegeSecrets.MostOrbs);

        struct Blizzard
        {
            public int Lane, Row, Ward;
            public float Left, For, Tick;
        }

        readonly List<Blizzard> _blizzards = new List<Blizzard>(SiegeSecrets.MostBlizzards);
        readonly List<SiegeBlizzard> _blizzardsDrawn = new List<SiegeBlizzard>(SiegeSecrets.MostBlizzards);

        // ------------------------------------------------------------------ what the view reads
        /// <summary>Whether this run's build holds <paramref name="power"/>. Never on the plain line.</summary>
        public bool Holds(SiegePower power) => _boosts.Has(power);

        /// <summary>Whether a black hole is open, where its eye is (march, lane) and how long it has left.</summary>
        public bool HorizonOpen => _horizonLeft > 0f;

        public float HorizonMarch => _horizonMarch;

        public float HorizonLane => _horizonLane;

        public float HorizonLeft => _horizonLeft;

        /// <summary>Whether the fire dragon is flying, and where its head is (lane, march).</summary>
        public bool DragonLive => _dragonLeft > 0f;

        public float DragonLane => _dragonLane;

        public float DragonMarch => _dragonMarch;

        /// <summary>Blows the golden dome may still block this wave. Nought without it.</summary>
        public int DomeLeft => _boosts.Has(SiegePower.GoldenDome) ? _dome : 0;

        /// <summary>How far the second sun has charged its shot, nought to one, and down which lane.</summary>
        public float SolarCharge => _solarCharging ? 1f - Math.Max(0f, _solarIn) / SiegeSecrets.SolarCharge : 0f;

        public int SolarLane => _solarLane;

        /// <summary>How far the prism has charged, nought to one.</summary>
        public float PrismCharge => _prismCharging ? 1f - Math.Max(0f, _prismIn) / SiegeSecrets.PrismCharge : 0f;

        /// <summary>Seconds of stopped time left, or nought.</summary>
        public float TimeStopLeft => _timeStop;

        /// <summary>The balls of lightning climbing the hill. Empty without them.</summary>
        public IReadOnlyList<SiegeOrb> Orbs
        {
            get
            {
                _orbsDrawn.Clear();
                for (int i = 0; i < _orbs.Count; i++)
                    _orbsDrawn.Add(new SiegeOrb(_orbs[i].Lane, _orbs[i].March, _orbs[i].Ward));
                return _orbsDrawn;
            }
        }

        /// <summary>The blizzards standing on the hill. Empty without them.</summary>
        public IReadOnlyList<SiegeBlizzard> Blizzards
        {
            get
            {
                _blizzardsDrawn.Clear();
                for (int i = 0; i < _blizzards.Count; i++)
                    _blizzardsDrawn.Add(new SiegeBlizzard(_blizzards[i].Lane, _blizzards[i].Row,
                                                          _blizzards[i].Left, _blizzards[i].For));
                return _blizzardsDrawn;
            }
        }

        /// <summary>
        /// How much faster the line fires this frame: <see cref="SiegeSecrets.TimeHaste"/> inside a
        /// time stop, and exactly one everywhere else - on the plain line always, so a cooldown run
        /// down by <c>dt * Haste</c> is run down by <c>dt</c>.
        /// </summary>
        float Haste => _timeStop > 0f ? SiegeSecrets.TimeHaste : 1f;

        // ------------------------------------------------------------------ the clocks
        /// <summary>Every power that runs on a clock of its own. Shut on every build that holds none.</summary>
        void Secrets(float dt)
        {
            if (!_boosts.AnyPower) return;

            Landing(dt);
            Storms(dt);
            Geysers(dt);
            Blades(dt);
            Ice(dt);
            Horizon(dt);
            Dragon(dt);
            Solar(dt);
            Armageddon(dt);
            Chronos(dt);
            Lightning(dt);
            Snowing(dt);
            Prisms(dt);
        }

        /// <summary>Thunderhead: lightning out of the cloud onto the raiders furthest down, on a clock.</summary>
        void Storms(float dt)
        {
            if (!_boosts.Has(SiegePower.Thunderhead)) return;

            _storm += dt;
            if (_storm < SiegeSecrets.StormEvery) return;
            _storm = SiegeSecrets.StormEvery;

            _picked.Clear();
            int struck = 0;

            for (int n = 0; n < SiegeSecrets.StormStrikes; n++)
            {
                SiegeRaider best = null;
                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || _picked.Contains(r.Id)) continue;
                    if (best == null || r.March > best.March) best = r;
                }

                if (best == null) break;

                _picked.Add(best.Id);
                struck++;

                Hit(-1, best, _boosts.PlainShot(SiegeSecrets.StormTenths, best.Kind), SiegeVia.Storm, -1);
                best.Stagger(SiegeSecrets.StormStun);
            }

            if (struck == 0) return;

            _storm = 0f;
            Announce(SiegeVia.Storm, .5f, (SiegeTuning.Lanes - 1) * .5f, struck, -1);
        }

        /// <summary>Frostfire: a geyser of steam under the thickest crowd that scalds, chills and burns.</summary>
        void Geysers(float dt)
        {
            if (!_boosts.Has(SiegePower.Frostfire)) return;

            _steam += dt;
            if (_steam < SiegeSecrets.SteamEvery) return;
            _steam = SiegeSecrets.SteamEvery;

            if (!Thickest(out int lane, out int row)) return;
            _steam = 0f;

            Announce(SiegeVia.Steam, (row + .5f) / SiegeTuning.BlastRows, lane, 1, -1);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || !Within(r, lane, row, 1)) continue;

                Hit(-1, r, _boosts.PlainShot(SiegeSecrets.SteamTenths, r.Kind), SiegeVia.Steam, -1);
                if (!r.Alive) continue;

                r.Freeze(SiegeSecrets.SteamChillTenths, SiegeSecrets.SteamChillFor);
                r.Kindle(_boosts.PlainShot(SiegeSecrets.SteamBurnTenths, r.Kind), SiegeSecrets.SteamBurnFor, -1);
            }
        }

        /// <summary>Blade orbit: every post's blades cut whatever has walked near the line in its reach.</summary>
        void Blades(float dt)
        {
            if (!_boosts.Has(SiegePower.BladeOrbit)) return;

            _blade += dt;
            if (_blade < SiegeSecrets.BladeEvery) return;
            _blade = SiegeSecrets.BladeEvery;

            bool thrown = false;

            for (int w = 0; w < _wards.Length; w++)
            {
                if (!_wards[w].Alive) continue;

                // Out at the body furthest down in the post's reach...
                float post = PostLane(w);
                SiegeRaider mark = null;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || Math.Abs(r.Lane + r.Drift - post) > SiegeSecrets.BladeSpan) continue;
                    if (mark == null || r.March > mark.March) mark = r;
                }

                if (mark == null) continue;
                thrown = true;

                // ...and back through the one nearest it, which is where a boomerang comes home.
                _picked.Clear();
                _picked.Add(mark.Id);
                var back = Nearest(mark, _picked);

                Hit(w, mark, _boosts.PlainShot(SiegeSecrets.BladeTenths, mark.Kind), SiegeVia.Blade, -1);
                if (back != null)
                    Hit(w, back, _boosts.PlainShot(SiegeSecrets.BladeTenths, back.Kind), SiegeVia.Blade, mark.Id);
            }

            if (thrown) _blade = 0f;
        }

        /// <summary>Permafrost: the lower hill is ice - everything on it is chilled, and the ice spikes on a clock.</summary>
        void Ice(float dt)
        {
            if (!_boosts.Has(SiegePower.Permafrost)) return;

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (Touchable(r) && r.March >= SiegeSecrets.IceFrom)
                    r.Freeze(SiegeSecrets.IceChillTenths, SiegeSecrets.IceChillFor);
            }

            _spike += dt;
            if (_spike < SiegeSecrets.SpikeEvery) return;
            _spike -= SiegeSecrets.SpikeEvery;

            // **The ice spikes whether or not anything stands on it**, so the power is seen
            // working on a hill the line is holding well up the slope.
            Announce(SiegeVia.Permafrost, (SiegeSecrets.IceFrom + 1f) * .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || r.March < SiegeSecrets.IceFrom) continue;

                Hit(-1, r, _boosts.PlainShot(SiegeSecrets.SpikeTenths, r.Kind), SiegeVia.Permafrost, -1);
            }
        }

        /// <summary>
        /// Event horizon: a black hole on the thickest crowd. For its life it drags everything
        /// near it in and bites it; when it shuts it crushes every body near its eye left under a
        /// share of its health, and strikes the rest.
        /// </summary>
        void Horizon(float dt)
        {
            if (!_boosts.Has(SiegePower.EventHorizon)) return;

            int reach = SiegeSecrets.HorizonReach;

            if (_horizonLeft > 0f)
            {
                _horizonLeft -= dt;
                _horizonTick -= dt;

                bool bite = _horizonTick <= 0f;
                if (bite) _horizonTick += SiegeSecrets.HorizonTick;

                int row = SiegeTuning.RowOf(_horizonMarch);
                int lane = (int)Math.Floor(_horizonLane + .5f);

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || r.Boss || !Within(r, lane, row, reach)) continue;

                    float gather = Math.Min(1f, dt * SiegeSecrets.HorizonGather);
                    r.Drift += (_horizonLane - r.Lane - r.Drift) * gather;
                    if (r.March > _horizonMarch)
                        r.March = Math.Max(_horizonMarch, r.March - dt * SiegeSecrets.HorizonDrag);
                    r.Freeze(9, .2f);

                    if (bite) Hit(-1, r, _boosts.PlainShot(SiegeSecrets.HorizonTenths, r.Kind), SiegeVia.Horizon, -1);
                }

                if (_horizonLeft > 0f) return;

                _horizonLeft = 0f;
                Crush(lane, row);
                return;
            }

            _horizonClock += dt;
            if (_horizonClock < SiegeSecrets.HorizonEvery) return;
            _horizonClock = SiegeSecrets.HorizonEvery;

            if (!Thickest(out int at, out int band)) return;

            _horizonClock = 0f;
            _horizonLeft = SiegeSecrets.HorizonFor;
            _horizonTick = SiegeSecrets.HorizonTick;
            _horizonLane = at;
            _horizonMarch = (band + .5f) / SiegeTuning.BlastRows;

            Announce(SiegeVia.Horizon, _horizonMarch, _horizonLane, reach, -1, SiegeSecrets.HorizonFor);
        }

        /// <summary>The hole shutting: a body near the eye under the crush's share is taken whole, the rest struck.</summary>
        void Crush(int lane, int row)
        {
            Announce(SiegeVia.Horizon, _horizonMarch, _horizonLane, SiegeSecrets.HorizonReach, -1);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || !Within(r, lane, row, SiegeSecrets.HorizonReach)) continue;

                bool weak = !r.Boss && Within(r, lane, row, 1)
                            && (long)r.Health * 100 <= (long)r.MaxHealth * SiegeSecrets.HorizonCrushPercent;

                if (weak) Hit(-1, r, r.Health, SiegeVia.Horizon, -1, soaks: false);
                else Hit(-1, r, _boosts.PlainShot(SiegeSecrets.HorizonCrushTenths, r.Kind), SiegeVia.Horizon, -1);
            }
        }

        /// <summary>
        /// Fire dragon: a dragon flies across the hill through its thickest row, swaying as it
        /// goes, and burns every body it passes once a flight.
        /// </summary>
        void Dragon(float dt)
        {
            if (!_boosts.Has(SiegePower.FireDragon)) return;

            if (_dragonLeft > 0f)
            {
                _dragonLeft -= dt;

                float t = 1f - Math.Max(0f, _dragonLeft) / SiegeSecrets.DragonFor;
                _dragonLane = -1f + t * (SiegeTuning.Lanes + 1f);
                _dragonMarch = _dragonRow + SiegeSecrets.DragonSway * (float)Math.Sin(t * Math.PI * 3.0);

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || _scorched.Contains(r.Id)) continue;
                    if (Math.Abs(r.Lane + r.Drift - _dragonLane) > SiegeSecrets.DragonNear) continue;
                    if (Math.Abs(r.March - _dragonMarch) > SiegeSecrets.DragonBand) continue;

                    _scorched.Add(r.Id);
                    Hit(-1, r, _boosts.PlainShot(SiegeSecrets.DragonTenths, r.Kind), SiegeVia.Dragon, -1);
                    if (r.Alive)
                        r.Kindle(_boosts.PlainShot(SiegeSecrets.DragonBurnTenths, r.Kind), SiegeSecrets.DragonBurnFor, -1);
                }

                if (_dragonLeft > 0f) return;

                _dragonLeft = 0f;
                _scorched.Clear();
                return;
            }

            _dragonClock += dt;
            if (_dragonClock < SiegeSecrets.DragonEvery) return;
            _dragonClock = SiegeSecrets.DragonEvery;

            if (!Thickest(out _, out int row)) return;

            _dragonClock = 0f;
            _dragonLeft = SiegeSecrets.DragonFor;
            _dragonRow = (row + .5f) / SiegeTuning.BlastRows;
            _dragonMarch = _dragonRow;
            _dragonLane = -1f;
            _scorched.Clear();

            Announce(SiegeVia.Dragon, _dragonRow, -1f, 0, -1, SiegeSecrets.DragonFor);
        }

        /// <summary>Solar cannon: the second sun charges and burns the whole of the busiest lane.</summary>
        void Solar(float dt)
        {
            if (!_boosts.Has(SiegePower.SolarCannon)) return;

            if (_solarCharging)
            {
                _solarIn -= dt;
                if (_solarIn > 0f) return;

                _solarCharging = false;
                Announce(SiegeVia.Solar, .5f, _solarLane, 0, -1);

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || r.Column != _solarLane) continue;

                    Hit(-1, r, _boosts.PlainShot(SiegeSecrets.SolarTenths, r.Kind), SiegeVia.Solar, -1);
                    if (r.Alive)
                        r.Kindle(_boosts.PlainShot(SiegeSecrets.SolarBurnTenths, r.Kind), SiegeSecrets.SolarBurnFor, -1);
                }

                return;
            }

            _solarClock += dt;
            if (_solarClock < SiegeSecrets.SolarEvery) return;
            _solarClock = SiegeSecrets.SolarEvery;

            int lane = Crowded();
            if (lane < 0) return;

            _solarClock = 0f;
            _solarCharging = true;
            _solarIn = SiegeSecrets.SolarCharge;
            _solarLane = lane;

            Announce(SiegeVia.Solar, .5f, lane, 0, -1, SiegeSecrets.SolarCharge);
        }

        /// <summary>
        /// Armageddon: the sky reddens and a shower of meteors is booked onto bodies picked off
        /// the build's stream, each landing a beat after the last.
        /// </summary>
        void Armageddon(float dt)
        {
            if (!_boosts.Has(SiegePower.Armageddon)) return;

            _armageddon += dt;
            if (_armageddon < SiegeSecrets.ArmageddonEvery) return;
            _armageddon = SiegeSecrets.ArmageddonEvery;

            if (!Peopled()) return;
            _armageddon = 0f;

            // **Reach nought is the sky**, a meteor's shadow carries its box's reach (one).
            Announce(SiegeVia.Armageddon, .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1, SiegeSecrets.ArmageddonFall);

            for (int n = 0; n < SiegeSecrets.ArmageddonMeteors; n++)
            {
                var r = Any(null);
                if (r == null) break;

                float fall = SiegeSecrets.ArmageddonFall + n * SiegeSecrets.ArmageddonGap;
                Book(SiegeVia.Armageddon, r.Column, SiegeTuning.RowOf(r.March), 1, SiegeSecrets.ArmageddonTenths,
                     -1, fall, SiegeSecrets.ArmageddonBurnTenths, SiegeSecrets.ArmageddonBurnFor);
            }
        }

        /// <summary>
        /// Time stop: the hill stands still - the hourglass's own stop (<c>_still</c>), so nothing
        /// walks and nothing swings - and the line fires faster (<see cref="Haste"/>) until it
        /// starts again.
        /// </summary>
        void Chronos(float dt)
        {
            if (!_boosts.Has(SiegePower.TimeStop)) return;

            if (_timeStop > 0f)
            {
                _timeStop = Math.Max(0f, _timeStop - dt);
                if (_timeStop <= 0f) Announce(SiegeVia.TimeStop, .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1);
                return;
            }

            _timeClock += dt;
            if (_timeClock < SiegeSecrets.TimeEvery) return;
            _timeClock = SiegeSecrets.TimeEvery;

            // **Never on a hill that has only just started walking again** (`SiegeBoard.Schools`,
            // `MayStill`): the stop waits, full, for the thaw's walk to pass.
            if (!Peopled() || !MayStill) return;

            _timeClock = 0f;
            _timeStop = SiegeSecrets.TimeFor;
            if (_still < SiegeSecrets.TimeFor) _still = SiegeSecrets.TimeFor;

            Announce(SiegeVia.TimeStop, .5f, (SiegeTuning.Lanes - 1) * .5f, 0, -1, SiegeSecrets.TimeFor);
        }

        /// <summary>Ball lightning: every post looses a ball that climbs its lane, zapping what it passes once.</summary>
        void Lightning(float dt)
        {
            if (!_boosts.Has(SiegePower.BallLightning)) return;

            _orbClock += dt;
            if (_orbClock >= SiegeSecrets.OrbEvery)
            {
                _orbClock = SiegeSecrets.OrbEvery;

                if (Peopled())
                {
                    _orbClock = 0f;

                    for (int w = 0; w < _wards.Length && _orbs.Count < SiegeSecrets.MostOrbs; w++)
                    {
                        if (!_wards[w].Alive) continue;

                        var orb = _spareOrbs.Count > 0 ? _spareOrbs.Pop() : new Orb();
                        orb.Ward = w;
                        orb.Lane = PostLane(w);
                        orb.March = 1f;
                        orb.Tick = 0f;
                        orb.Hit.Clear();
                        _orbs.Add(orb);

                        Announce(SiegeVia.Orb, 1f, orb.Lane, 0, w);
                    }
                }
            }

            for (int o = _orbs.Count - 1; o >= 0; o--)
            {
                var orb = _orbs[o];
                orb.March -= dt / SiegeSecrets.OrbClimb;
                orb.Tick -= dt;

                if (orb.Tick <= 0f)
                {
                    orb.Tick += SiegeSecrets.OrbTick;

                    for (int i = 0; i < _raiders.Count; i++)
                    {
                        var r = _raiders[i];
                        if (!Touchable(r) || orb.Hit.Contains(r.Id)) continue;
                        if (Math.Abs(r.Lane + r.Drift - orb.Lane) > SiegeSecrets.OrbSpan) continue;
                        if (Math.Abs(r.March - orb.March) > SiegeSecrets.OrbBand) continue;

                        orb.Hit.Add(r.Id);
                        Hit(orb.Ward, r, _boosts.PlainShot(SiegeSecrets.OrbTenths, r.Kind), SiegeVia.Orb, -1);
                        r.Stagger(SiegeSecrets.OrbStun);
                    }
                }

                if (orb.March > 0f) continue;

                _orbs.RemoveAt(o);
                _spareOrbs.Push(orb);
            }
        }

        /// <summary>Diamond dust: each blizzard freezes its boxes solid and bites on a cadence, until it blows out.</summary>
        void Snowing(float dt)
        {
            for (int b = _blizzards.Count - 1; b >= 0; b--)
            {
                // A blizzard laid by a shatter this loop's hits set off is appended past `b`
                // and waits for the next frame; the one being read is written back by index.
                var storm = _blizzards[b];
                storm.Left -= dt;
                storm.Tick -= dt;

                bool bite = storm.Tick <= 0f;
                if (bite) storm.Tick += SiegeSecrets.DustTick;
                _blizzards[b] = storm;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var r = _raiders[i];
                    if (!Touchable(r) || !Within(r, storm.Lane, storm.Row, 1)) continue;

                    r.Freeze(SiegeSecrets.DustFreezeTenths, SiegeSecrets.DustFreezeFor);
                    if (bite) Hit(storm.Ward, r, _boosts.PlainShot(SiegeSecrets.DustTenths, r.Kind), SiegeVia.Dust, -1);
                }

                if (_blizzards[b].Left <= 0f) _blizzards.RemoveAt(b);
            }
        }

        /// <summary>Prism lance: the laser guns charge a prism, which splits into beams at the healthiest bodies.</summary>
        void Prisms(float dt)
        {
            if (!_boosts.Has(SiegePower.PrismLance)) return;

            if (_prismCharging)
            {
                _prismIn -= dt;
                if (_prismIn > 0f) return;

                _prismCharging = false;
                Announce(SiegeVia.Prism, 1f, (SiegeTuning.Lanes - 1) * .5f, 0, -1);

                _picked.Clear();
                for (int n = 0; n < SiegeSecrets.PrismBeams; n++)
                {
                    var r = Strongest(_picked);
                    if (r == null) break;

                    _picked.Add(r.Id);
                    Hit(-1, r, _boosts.PlainShot(SiegeSecrets.PrismTenths, r.Kind), SiegeVia.Prism, -1);
                }

                return;
            }

            _prismClock += dt;
            if (_prismClock < SiegeSecrets.PrismEvery) return;
            _prismClock = SiegeSecrets.PrismEvery;

            if (!Peopled() || !AnyLaserStanding()) return;

            _prismClock = 0f;
            _prismCharging = true;
            _prismIn = SiegeSecrets.PrismCharge;

            Announce(SiegeVia.Prism, 1f, (SiegeTuning.Lanes - 1) * .5f, 0, -1, SiegeSecrets.PrismCharge);
        }

        // ------------------------------------------------------------------ what lands later
        /// <summary>Lands every booked box hit and every comet whose flight is over.</summary>
        void Landing(float dt)
        {
            _due.Clear();
            for (int p = _pending.Count - 1; p >= 0; p--)
            {
                var hit = _pending[p];
                hit.In -= dt;

                if (hit.In > 0f) { _pending[p] = hit; continue; }

                _pending.RemoveAt(p);
                _due.Add(hit);
            }

            // Landed in the order booked: the list was walked backwards to remove.
            for (int d = _due.Count - 1; d >= 0; d--) Land(_due[d]);

            _landed.Clear();
            for (int c = _comets.Count - 1; c >= 0; c--)
            {
                var comet = _comets[c];
                comet.In -= dt;

                if (comet.In > 0f) { _comets[c] = comet; continue; }

                _comets.RemoveAt(c);
                _landed.Add(comet);
            }

            for (int c = _landed.Count - 1; c >= 0; c--) Strike(_landed[c]);
        }

        void Land(Pending hit)
        {
            Announce(hit.Via, (hit.Row + .5f) / SiegeTuning.BlastRows, hit.Lane, hit.Reach, hit.Ward);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || !Within(r, hit.Lane, hit.Row, hit.Reach)) continue;

                Hit(hit.Ward, r, _boosts.PlainShot(hit.Tenths, r.Kind), hit.Via, -1);
                if (r.Alive && hit.BurnTenths > 0)
                    r.Kindle(_boosts.PlainShot(hit.BurnTenths, r.Kind), hit.BurnFor, hit.Ward);
            }
        }

        /// <summary>A comet or a rainbow bolt arriving: on the body it was thrown at, or the one furthest down if that one has gone.</summary>
        void Strike(Comet comet)
        {
            SiegeRaider target = null;
            for (int i = 0; i < _raiders.Count; i++)
                if (_raiders[i].Id == comet.Raider && Touchable(_raiders[i])) { target = _raiders[i]; break; }

            if (target == null) target = Foremost();
            if (target == null) return;

            int lane = target.Column, row = SiegeTuning.RowOf(target.March);
            bool comets = comet.Via == SiegeVia.Comet;

            Announce(comet.Via, target.March, lane, comets ? 1 : 0, -1);

            Hit(-1, target, _boosts.PlainShot(comet.Tenths, target.Kind), comet.Via, -1);
            if (!comets) return;

            if (target.Alive)
                target.Kindle(_boosts.PlainShot(SiegeSecrets.CometBurnTenths, target.Kind), SiegeSecrets.CometBurnFor, -1);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == target || !Touchable(r) || !Within(r, lane, row, 1)) continue;

                Hit(-1, r, _boosts.PlainShot(SiegeSecrets.CometSplashTenths, r.Kind), SiegeVia.Comet, target.Id);
            }
        }

        void Book(SiegeVia via, int lane, int row, int reach, int tenths, int ward, float landsIn,
                  int burnTenths = 0, float burnFor = 0f)
        {
            if (lane < 0) lane = 0;
            if (lane >= SiegeTuning.Lanes) lane = SiegeTuning.Lanes - 1;
            if (row < 0) row = 0;
            if (row >= SiegeTuning.BlastRows) row = SiegeTuning.BlastRows - 1;

            _pending.Add(new Pending
            {
                Via = via, Lane = lane, Row = row, Reach = reach, Tenths = tenths, Ward = ward,
                In = landsIn, BurnTenths = burnTenths, BurnFor = burnFor,
            });

            Announce(via, (row + .5f) / SiegeTuning.BlastRows, lane, reach, ward, landsIn);
        }

        // ------------------------------------------------------------------ on the field
        /// <summary>
        /// What a build's field secrets throw off one beat of a cascade: a comet for every power
        /// gem that went off (<see cref="SiegePower.GemComets"/>), and a rainbow bolt off every
        /// beat past the first (<see cref="SiegePower.RainbowCascade"/>). Booked to land a flight
        /// later and recorded on the beat so the view draws them leaving the gems. Draws from no
        /// stream: whom each is thrown at is read off the hill.
        /// </summary>
        void Gemmed(SiegeBeat beat, int depth)
        {
            if (!_boosts.AnyPower) return;

            if (_boosts.Has(SiegePower.GemComets))
            {
                // A comet off every power gem forged on this beat and every one that went off,
                // in that order, so a match of four is a comet the moment it is made.
                int thrown = 0;
                for (int f = 0; f < beat.Forged.Count && thrown < SiegeSecrets.MostComets; f++, thrown++)
                    Throw(beat, SiegeVia.Comet, beat.Forged[f].Cell, beat.Forged[f].Colour,
                          Strongest(null), SiegeSecrets.CometTenths);

                for (int s = 0; s < beat.Sprung.Count && thrown < SiegeSecrets.MostComets; s++)
                {
                    var spark = beat.Sprung[s];
                    if (!SiegeCharms.IsPower(spark.Charm)) continue;

                    Throw(beat, SiegeVia.Comet, spark.Cell, spark.Colour, Strongest(null), SiegeSecrets.CometTenths);
                    thrown++;
                }
            }

            if (_boosts.Has(SiegePower.RainbowCascade) && depth >= 2 && beat.Cleared.Count > 0)
            {
                int tenths = Math.Min(SiegeSecrets.RainbowTenths * (depth - 1), SiegeSecrets.MostRainbowTenths);
                Throw(beat, SiegeVia.Rainbow, beat.Cleared[beat.Cleared.Count / 2], -1, Foremost(), tenths);
            }
        }

        /// <summary>
        /// Throws one comet or rainbow bolt from <paramref name="cell"/> at <paramref name="target"/>.
        /// <b>Thrown whether or not anything stands on the hill</b> - a payoff that went off in
        /// silence would read as a broken gem - so a throw at an empty hill is aimed at its middle
        /// (raider -1) and picks whoever is there when it lands, or bursts on the ground.
        /// </summary>
        void Throw(SiegeBeat beat, SiegeVia via, int cell, int colour, SiegeRaider target, int tenths)
        {
            int at = target != null ? target.Id : -1;

            _comets.Add(new Comet { Via = via, Raider = at, Tenths = tenths, In = SiegeSecrets.CometFlight });
            beat.Comets.Add(new SiegeComet(cell, at, colour, via));
        }

        // ------------------------------------------------------------------ on a bolt
        /// <summary>
        /// What the evolved cards add to a primary bolt that has landed at full strength: a titan
        /// shell, a sniper round, a cluster of bomblets, a missile swarm. From <c>Augment</c>,
        /// which is already shut on the plain line.
        /// </summary>
        void Empowered(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            if (!_boosts.AnyPower) return;

            var b = _boosts;

            if (b.Has(SiegePower.TitanShells) && ward.Shots % SiegeSecrets.ShellEvery == 0)
                Shell(ward, index, target, damage);

            if (b.Has(SiegePower.Deadeye) && ward.Shots % SiegeSecrets.SnipeEvery == 0)
            {
                var mark = Strongest(null);
                if (mark != null) Splinter(ward, index, mark, Share(damage, SiegeSecrets.SnipeTenths), SiegeVia.Snipe);
            }

            if (b.Has(SiegePower.ClusterBombs) && ward.Shots % SiegeSecrets.ClusterEvery == 0)
                Cluster(index, target);

            // **An evolved shatter brings its own frost**, so a body this line hits is one a
            // shatter can take: without it the evolution would wait on a card it never named.
            if (b.Has(SiegePower.DiamondDust) && target.Alive)
                target.Freeze(SiegeSecrets.DustChillTenths, SiegeSecrets.DustChillFor);

            if (b.Has(SiegePower.Swarm) && b.MissileEvery > 0 && ward.Shots % b.MissileEvery == 0)
            {
                _picked.Clear();
                for (int n = 0; n < SiegeSecrets.SwarmMissiles; n++)
                {
                    var r = Any(_picked);
                    if (r == null) break;

                    _picked.Add(r.Id);
                    Splinter(ward, index, r, _boosts.PlainShot(SiegeSecrets.SwarmTenths, r.Kind), SiegeVia.Swarm);
                }
            }
        }

        /// <summary>A titan shell: the bolt lands again, heavy, and blows up the box round it.</summary>
        void Shell(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            int lane = target.Column, row = SiegeTuning.RowOf(target.March);
            Announce(SiegeVia.Shell, target.March, lane, 1, index);

            if (target.Alive)
                Splinter(ward, index, target, Share(damage, SiegeSecrets.ShellTenths), SiegeVia.Shell);

            int blast = Share(damage, SiegeSecrets.ShellBlastTenths);
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == target || !Touchable(r) || !Within(r, lane, row, 1)) continue;

                Splinter(ward, index, r, blast, SiegeVia.Shell, target.Id);
            }
        }

        /// <summary>Cluster bombs: bomblets lobbed into the boxes round the target, going off after a fuse.</summary>
        void Cluster(int index, SiegeRaider target)
        {
            int lane = target.Column, row = SiegeTuning.RowOf(target.March);
            int used = 0;

            for (int n = 0; n < SiegeSecrets.ClusterBombs; n++)
            {
                // One of the eight boxes round the target, never one twice in a cluster.
                int pick = Roll() % 8;
                for (int tries = 0; tries < 8 && (used & (1 << pick)) != 0; tries++) pick = (pick + 1) % 8;
                used |= 1 << pick;

                int at = pick < 4 ? pick : pick + 1;
                int dx = at % 3 - 1, dy = at / 3 - 1;

                Book(SiegeVia.Bomblet, lane + dx, row + dy, 0, SiegeSecrets.ClusterTenths, index,
                     SiegeSecrets.ClusterFuse + n * .05f);
            }
        }

        int Roll() => _boosts.Roll100();

        // ------------------------------------------------------------------ on a death, a blow, a wave
        /// <summary>What the powers make of a raider falling: a plague bursting out of a poisoned body. From <c>Perished</c>.</summary>
        void Mourned(SiegeRaider fallen, int ward)
        {
            if (!_boosts.AnyPower || fallen == null || fallen.Boss) return;
            if (!_boosts.Has(SiegePower.Plague)) return;

            int lane = fallen.Column, row = SiegeTuning.RowOf(fallen.March);

            bool poisoned = false;
            for (int i = 0; i < _pools.Count && !poisoned; i++)
                if (_pools[i].Lane == lane && _pools[i].Row == row) poisoned = true;

            if (!poisoned) return;

            Announce(SiegeVia.Plague, fallen.March, lane, 1, ward);

            int tenths = Math.Max(_boosts.ToxicTenths, SiegeSecrets.PlaguePoolTenths);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = lane + dx, y = row + dy;
                    if (x < 0 || x >= SiegeTuning.Lanes || y < 0 || y >= SiegeTuning.BlastRows) continue;
                    LayAt(x, y, ward, tenths, SiegeSecrets.PlaguePoolFor);
                }

            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (r == fallen || !Touchable(r) || !Within(r, lane, row, 1)) continue;

                Hit(ward, r, _boosts.PlainShot(SiegeSecrets.PlagueTenths, r.Kind), SiegeVia.Plague, fallen.Id);
            }
        }

        /// <summary>Diamond dust: a shatter leaves a blizzard standing on the box it burst in. From <c>Shatter</c>.</summary>
        void Snowed(int lane, int row, int ward)
        {
            if (!_boosts.Has(SiegePower.DiamondDust)) return;

            for (int b = 0; b < _blizzards.Count; b++)
            {
                if (_blizzards[b].Lane != lane || _blizzards[b].Row != row) continue;

                var storm = _blizzards[b];
                storm.Left = storm.For;
                _blizzards[b] = storm;
                return;
            }

            if (_blizzards.Count >= SiegeSecrets.MostBlizzards) return;

            _blizzards.Add(new Blizzard
            {
                Lane = lane, Row = row, Ward = ward,
                Left = SiegeSecrets.DustFor, For = SiegeSecrets.DustFor, Tick = SiegeSecrets.DustTick,
            });

            Announce(SiegeVia.Dust, (row + .5f) / SiegeTuning.BlastRows, lane, 1, ward);
        }

        /// <summary>Comet storm: a landed meteor breaks into shards that rain on the boxes round it. From <c>Meteors</c>.</summary>
        void Shards(int lane, int row)
        {
            if (!_boosts.Has(SiegePower.CometStorm)) return;

            int span = SiegeSecrets.ShardScatter * 2 + 1;
            int tenths = Math.Max(1, _boosts.MeteorTenths / 2);

            for (int n = 0; n < SiegeSecrets.StormShards; n++)
            {
                int dx = Roll() % span - SiegeSecrets.ShardScatter;
                int dy = Roll() % span - SiegeSecrets.ShardScatter;

                Book(SiegeVia.Shard, lane + dx, row + dy, 0, tenths, -1, SiegeSecrets.ShardFall + n * .07f,
                     _boosts.Arsenal.MeteorBurnTenths, _boosts.Arsenal.MeteorBurnFor);
            }
        }

        /// <summary>
        /// Golden dome: answers whether this blow was blocked, spending one of the wave's blocks.
        /// From <c>Swing</c>; false on every build without the dome, so the blow lands as it did.
        /// </summary>
        bool Domed(int ward, SiegeRaider raider)
        {
            if (!_boosts.AnyPower || !_boosts.Has(SiegePower.GoldenDome) || _dome <= 0) return false;

            _dome--;
            Announce(SiegeVia.Dome, raider.March, raider.Lane + raider.Drift, _dome, ward);
            return true;
        }

        /// <summary>Fortress: a blow that landed is answered by the wall it struck. From <c>Swing</c>.</summary>
        void Reflected(int ward, SiegeRaider raider)
        {
            if (!_boosts.AnyPower || !_boosts.Has(SiegePower.Fortress) || !Touchable(raider)) return;

            Hit(ward, raider, _boosts.PlainShot(SiegeSecrets.ReflectTenths, raider.Kind), SiegeVia.Reflect, -1);
        }

        /// <summary>What a wave stepping out does under the powers: the dome raised again, the walls mended. From <c>Mustered</c>.</summary>
        void Rallied()
        {
            if (!_boosts.AnyPower) return;

            if (_boosts.Has(SiegePower.GoldenDome))
            {
                _dome = SiegeSecrets.DomeBlocks;
                Announce(SiegeVia.Dome, 1f, (SiegeTuning.Lanes - 1) * .5f, _dome, -1);
            }

            if (!_boosts.Has(SiegePower.Fortress)) return;

            for (int w = 0; w < _wards.Length; w++)
            {
                var ward = _wards[w];
                if (!ward.Alive) continue;

                int mend = Math.Max(1, ward.Full * SiegeSecrets.FortressMendPercent / 100);
                ward.Health = Math.Min(ward.Full, ward.Health + mend);
                Announce(SiegeVia.Fortress, 1f, PostLane(w), 0, w);
            }
        }

        // ------------------------------------------------------------------ readings
        void Announce(SiegeVia via, float march, float lane, int reach, int ward, float landsIn = 0f)
            => _report.Volleys.Add(new SiegeVolley(via, march, lane, reach, ward, landsIn));

        /// <summary>Whether anything on the hill may be touched. Draws nothing.</summary>
        bool Peopled()
        {
            for (int i = 0; i < _raiders.Count; i++) if (Touchable(_raiders[i])) return true;
            return false;
        }

        /// <summary>The body furthest down the hill, or null.</summary>
        SiegeRaider Foremost()
        {
            SiegeRaider best = null;
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (Touchable(r) && (best == null || r.March > best.March)) best = r;
            }

            return best;
        }

        /// <summary>The body holding the most health, not in <paramref name="not"/>; the one further down on a tie.</summary>
        SiegeRaider Strongest(HashSet<int> not)
        {
            SiegeRaider best = null;
            for (int i = 0; i < _raiders.Count; i++)
            {
                var r = _raiders[i];
                if (!Touchable(r) || (not != null && not.Contains(r.Id))) continue;

                if (best == null || r.Health > best.Health || (r.Health == best.Health && r.March > best.March))
                    best = r;
            }

            return best;
        }

        /// <summary>The lane holding the most bodies, the leftmost on a tie, or -1 on an empty hill.</summary>
        int Crowded()
        {
            int lane = -1, most = 0;
            for (int x = 0; x < SiegeTuning.Lanes; x++)
            {
                int count = 0;
                for (int i = 0; i < _raiders.Count; i++)
                    if (Touchable(_raiders[i]) && _raiders[i].Column == x) count++;

                if (count <= most) continue;
                most = count;
                lane = x;
            }

            return lane;
        }

        bool AnyLaserStanding()
        {
            for (int w = 0; w < _wards.Length; w++) if (_wards[w].Alive && Lasered(w)) return true;
            return false;
        }
    }
}
