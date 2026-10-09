using System;
using System.Collections.Generic;
using GlimmerGrove.Wards;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What the player's chosen turrets do beyond putting a bolt into a raider.
    ///
    /// <para>
    /// <b>Every one of these is an addition to a bolt that has already landed at full strength,
    /// and that is the load-bearing rule of the whole loadout.</b> A siege's par is
    /// <c>SiegeTuning.Par</c> - the hill's health over the most one match could ever be worth -
    /// and it is computed against the baseline bolt. A turret that hit <em>softer</em> would push
    /// three stars out of reach of whoever chose it, which is a grade decided by a purchase and
    /// the one thing invariant 39 refuses outright. A turret that hits harder only makes par
    /// over-state what a good run needs, which is the direction invariant 22 says to err in and
    /// exactly what invariant 37w already accepted when cogs shipped.
    /// </para>
    /// <para>
    /// <b>So the primary hit is computed and applied by <c>Shoot</c> before anything here
    /// runs.</b> Nothing in this file may reduce it, and there is no path through which it could:
    /// every method below either damages <em>somebody else</em>, adds a lasting state, or gives
    /// fuel back.
    /// </para>
    /// <para>
    /// <b>Its own file because it is the seam between two features.</b> The hill and the field are
    /// content; the line is the player. Keeping the ability rules here means a new turret is one
    /// case in one switch, and it means the rest of the clock reads the same as it did when every
    /// ward was identical.
    /// </para>
    /// </summary>
    public sealed partial class SiegeBoard
    {
        /// <summary>
        /// Runs a ward's ability after its bolt has landed.
        ///
        /// <para>
        /// <b>Extra hits are reported as ordinary bolts marked <c>Extra</c></b> rather than as a
        /// record of their own. The view already knows how to draw a bolt arriving from a ward at
        /// a raider, and a second kind of record would be a second drawing path that could come to
        /// disagree with it - which is how a mode ends up with an effect nobody can tell from
        /// another. The flag exists so the view can draw a splash smaller than the shot that
        /// caused it, and for no other reason.
        /// </para>
        /// </summary>
        void Ability(SiegeWard ward, int index, SiegeRaider target, int damage)
        {
            if (target == null) return;

            var model = ward.Model;

            switch (model.Ability)
            {
                case WardAbility.Splash:
                    Spread(ward, index, target, Share(damage, model.Magnitude), model.Extent);
                    break;

                case WardAbility.Chain:
                    Arc(ward, index, target, Share(damage, model.Magnitude), model.Extent);
                    break;

                case WardAbility.Frost:
                    target.Freeze(model.Magnitude, model.Extent / 10f);
                    break;

                // **Asked rather than set**, because a stun is the one lasting state here that is
                // refused while it is running instead of refreshed - see `SiegeRaider.Stagger` for
                // why a refreshing one would never end.
                case WardAbility.Stun:
                    target.Stagger(model.Extent / 10f);
                    break;

                case WardAbility.Pierce:
                    if (model.Extent > 0 && ward.Shots % model.Extent == 0)
                        Lance(ward, index, target, Share(damage, model.Magnitude));
                    break;

                case WardAbility.Ember:
                    target.Kindle(Share(damage, model.Magnitude), model.Extent / 10f, index);
                    break;

                // Rend and Beacon are not applied here at all: a rend changes what the *primary*
                // hit is worth and is read by `SiegeTuning.DamageTo`, and a beacon changes what
                // the ward holds and is read once when it is built. Said out loud rather than left
                // as a missing case, because a reader looking for a turret's effect has to be told
                // where it lives.
                case WardAbility.Siphon:
                case WardAbility.Rend:
                case WardAbility.Beacon:
                case WardAbility.None:
                default:
                    break;
            }
        }

        /// <summary>
        /// A share of a bolt, in tenths, never below one.
        ///
        /// <b>Never nought</b>, for <c>SiegeTuning.DamageTo</c>'s reason: an extra hit that reads
        /// as landing and takes nothing is a turret the player will believe is broken, and at a
        /// low rank against a small share the division gets there.
        /// </summary>
        static int Share(int damage, int tenths)
        {
            int share = damage * tenths / 10;
            return share < 1 ? 1 : share;
        }

        /// <summary>
        /// Everything standing in the box around what a splash turret hit.
        /// <paramref name="via"/> is how a build's splash is drawn (<see cref="SiegeVia"/>);
        /// a turret's own is <see cref="SiegeVia.None"/>, as it always was.
        /// </summary>
        void Spread(SiegeWard ward, int index, SiegeRaider target, int damage, int reach,
                    SiegeVia via = SiegeVia.None)
        {
            if (reach < 1) reach = 1;

            int row = SiegeTuning.RowOf(target.March);

            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (other == target || !other.Alive || !other.OnTheHill) continue;

                if (Math.Abs(SiegeTuning.RowOf(other.March) - row) > reach) continue;
                if (Math.Abs(other.Column - target.Column) > reach) continue;

                Splinter(ward, index, other, damage, via, via == SiegeVia.None ? -1 : target.Id);
            }
        }

        /// <summary>
        /// The nearest raiders after the one a chain turret hit.
        ///
        /// <b>Nearest by how far down the hill they are</b>, which is what the view can draw as an
        /// arc going somewhere legible - and what makes a chain worth more the more raiders are
        /// bunched, which is the shape that tells it apart from a splash.
        /// </summary>
        void Arc(SiegeWard ward, int index, SiegeRaider target, int damage, int extra,
                 SiegeVia via = SiegeVia.None)
        {
            if (extra < 1) extra = 1;

            // **A build's chain is drawn hop by hop**, each one leaving the raider the last one
            // reached, which is what lightning travelling through a crowd looks like. A turret's
            // own keeps the drawing it always had.
            var from = target;

            for (int hop = 0; hop < extra; hop++)
            {
                SiegeRaider next = null;
                float best = float.MaxValue;

                for (int i = 0; i < _raiders.Count; i++)
                {
                    var other = _raiders[i];
                    if (other == target || !other.Alive || !other.OnTheHill) continue;
                    if (_arced.Contains(other.Id)) continue;

                    float gap = Math.Abs(other.March - target.March)
                              + Math.Abs(other.Column - target.Column) * .04f;

                    if (gap >= best) continue;

                    best = gap;
                    next = other;
                }

                if (next == null) break;

                _arced.Add(next.Id);
                Splinter(ward, index, next, damage, via, via == SiegeVia.None ? -1 : from.Id);
                from = next;
            }

            _arced.Clear();
        }

        /// <summary>Everything in the lane a pierce turret fired down.</summary>
        void Lance(SiegeWard ward, int index, SiegeRaider target, int damage,
                   SiegeVia via = SiegeVia.None)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var other = _raiders[i];
                if (other == target || !other.Alive || !other.OnTheHill) continue;
                if (other.Column != target.Column) continue;

                Splinter(ward, index, other, damage, via);
            }
        }

        /// <summary>
        /// One extra hit: takes the health, reports the bolt, and takes the raider off the hill
        /// through the one door if that killed it.
        /// </summary>
        void Splinter(SiegeWard ward, int index, SiegeRaider other, int damage,
                      SiegeVia via = SiegeVia.None, int from = -1)
        {
            // **Plating blunts everything that is not the colour it answers to**, which is where
            // the bulwark's shield went when the lock made it unreachable on a primary hit. A
            // splash, a chain and a lance are the only things in this mode that can reach a colour
            // the player has not fed, so the raider that is armour against exactly that is the one
            // thing on the hill that must be met with its own colour. See `SiegeBoard.Through`.
            damage = Through(ward, other, damage);

            if (damage < 1) damage = 1;

            // Through the one door (`SiegeBoard.Fight.cs`). A partner shot that found a boss
            // behind its guard took nothing and reports nothing, so the view draws the guard.
            damage = Wound(other, damage);
            if (damage <= 0) return;

            bool killed = Fell(other);
            if (killed) Fallen(ward, index, other);

            _report.Bolts.Add(new SiegeBolt(index, other.Id, damage,
                                            ward.StrongAgainst(other.Colour), killed, true,
                                            via, from));
        }

        /// <summary>
        /// Everything a kill by <paramref name="ward"/> pays: the turret's own refund, and what
        /// the build adds (<see cref="SiegeBoosts.SiphonTenths"/>, <see cref="SiegeBoosts.LeechHealth"/>,
        /// <see cref="SiegeBoosts.BlastTenths"/>).
        ///
        /// <b>One door for the three places a ward's bolt kills</b> - the primary hit, a
        /// partner shot and a thorn - so a build's kill effects cannot be true at one of them
        /// and not another. A blast is a splash of the body's own full health thrown at its
        /// neighbours, through <see cref="Spread"/>, so a chain of kills is a chain of blasts.
        /// </summary>
        void Fallen(SiegeWard ward, int index, SiegeRaider fallen)
        {
            Refund(ward);

            if (_boosts.IsIdentity) return;

            if (_boosts.LeechHealth > 0 && ward.Alive && ward.Health < ward.Full)
            {
                ward.Health += _boosts.LeechHealth;
                if (ward.Health > ward.Full) ward.Health = ward.Full;
            }

            if (_boosts.BlastTenths > 0 && fallen != null && !fallen.Boss)
            {
                int blast = fallen.MaxHealth * _boosts.BlastTenths / 10;
                if (blast > 0) Spread(ward, index, fallen, blast, 1, SiegeVia.Blast);
            }

            // And whatever the attack cards make of a death (`SiegeBoard.Arsenal`).
            Perished(fallen, index);
        }

        /// <summary>
        /// What the build grants on top of a turret's own ability, after the bolt has landed.
        ///
        /// <para>
        /// <b>Through exactly the doors <see cref="Ability"/> uses</b> - <see cref="Spread"/>,
        /// <see cref="Arc"/>, <see cref="Lance"/>, <c>Freeze</c>, <c>Kindle</c>, <c>Stagger</c>,
        /// <c>Hex</c> - so a granted splash is drawn as a splash and a granted burn ticks as a
        /// burn, and the rule that nothing here may reduce the primary hit holds by construction.
        /// Nothing on the plain line (<see cref="SiegeBoosts.IsIdentity"/>).
        /// </para>
        /// </summary>
        void Augment(SiegeWard ward, int index, SiegeRaider target, int damage, int element)
        {
            if (_boosts.IsIdentity || target == null) return;

            var boosts = _boosts;

            // **The attack cards first** (`SiegeBoard.Arsenal`): a ricochet off this kill, the
            // pellets, the missiles, the quake, the pool, the hydra, the wild element. Before the
            // execute below, which may end this method early.
            Volley(ward, index, target, damage, element);

            if (boosts.SplashTenths > 0)
                Spread(ward, index, target, Share(damage, boosts.SplashTenths), boosts.SplashReach,
                       SiegeVia.Splash);

            if (boosts.ChainTenths > 0 && boosts.ChainHops > 0)
                Arc(ward, index, target, Share(damage, boosts.ChainTenths), boosts.ChainHops,
                    SiegeVia.Arc);

            if (boosts.PierceEvery > 0 && ward.Shots % boosts.PierceEvery == 0)
                Lance(ward, index, target, Share(damage, boosts.PierceTenths), SiegeVia.Lance);

            if (boosts.FrostTenths > 0) target.Freeze(boosts.FrostTenths, boosts.FrostFor);

            if (boosts.BurnTenths > 0)
                target.Kindle(Share(damage, boosts.BurnTenths), boosts.BurnFor, index);

            if (boosts.Stuns()) target.Stagger(boosts.StunFor);

            if (boosts.Hexes()) target.Hex(boosts.HexFor);

            // A second landing of the same bolt, at a share, as a partner shot on the target.
            if (boosts.Twins() && target.Alive)
                Splinter(ward, index, target, Share(damage, boosts.TwinTenths), SiegeVia.Twin);

            // **The finish**: a body at or under the build's share of its full health is taken
            // outright. Never a boss - a boss is a fight, not a bar to race (37fe) - and through
            // the one door, so a hex and a stand's rules still read.
            if (boosts.ExecutePercent > 0 && target.Alive && !target.Boss
                && target.Health * 100 <= target.MaxHealth * boosts.ExecutePercent)
            {
                int took = Wound(target, target.Health);
                if (took <= 0) return;

                bool killed = Fell(target);
                if (killed) Fallen(ward, index, target);

                _report.Bolts.Add(new SiegeBolt(index, target.Id, took,
                                                ward.StrongAgainst(target.Colour), killed, true,
                                                SiegeVia.Execute));
            }
        }

        /// <summary>
        /// Hands a siphon turret some of its fuel back for a kill.
        ///
        /// <b>Capped at the ward's own capacity</b> like every other way fuel arrives, so a line
        /// that is clearing a wave cannot bank past what it could have banked from a match.
        /// </summary>
        void Refund(SiegeWard ward)
        {
            if (!ward.Alive) return;

            float back = 0f;

            if (ward.Model.Ability == WardAbility.Siphon && ward.Model.Magnitude > 0)
                back += ward.Model.Magnitude / 10f;

            // And the build's own siphon, through the same cap (`SiegeBoosts.SiphonTenths`).
            if (_boosts.SiphonTenths > 0) back += _boosts.SiphonTenths / 10f;

            if (back <= 0f) return;

            ward.Fuel = Math.Min(ward.Capacity, ward.Fuel + back);
        }

        /// <summary>
        /// Burns down whatever is alight.
        ///
        /// <para>
        /// <b>The remainder is carried rather than dropped</b> (<c>SiegeRaider.Smoulder</c>),
        /// because a burn is a rate per second sampled at a frame rate: truncating each frame
        /// would make an ember turret worth two-thirds of its authored strength on a fast phone
        /// and nothing at all on a slow one, which is a difficulty that varies with the device.
        /// </para>
        /// <para>
        /// <b>It is taken on a cadence and reported as a burn rather than as a bolt</b>
        /// (<c>SiegeTuning.BurnTick</c>, <see cref="SiegeBurn"/>), and the two halves are one
        /// change: the damage a burn does over its own seconds is unmoved, because the fraction
        /// is still carried, and what stops is a turret drawing thirty complete shots a second.
        /// </para>
        /// <para>
        /// <b>The last instalment is paid on the beat the burn ends</b> rather than waiting for a
        /// boundary that will never come. Without it an ember whose seconds ran out mid-interval
        /// would silently keep whatever it had accumulated - a turret paying less than the number
        /// on its card, in a way no arithmetic anywhere else could see.
        /// </para>
        /// </summary>
        void Smoulder(float dt)
        {
            for (int i = 0; i < _raiders.Count; i++)
            {
                var raider = _raiders[i];

                if (raider.Chill > 0f) raider.Chill = Math.Max(0f, raider.Chill - dt);

                // A well's slow, aged beside the chill it is joined with (`SiegeRaider.Pace`).
                if (raider.Drag > 0f) raider.Drag = Math.Max(0f, raider.Drag - dt);

                // **Both halves of a stun run down here**, in the one place the board already
                // ages what a bolt left behind. `Steady` outlives `Stun` by
                // `SiegeTuning.StunRest`, which is what stops the next bolt renewing it.
                if (raider.Stun > 0f) raider.Stun = Math.Max(0f, raider.Stun - dt);
                if (raider.Steady > 0f) raider.Steady = Math.Max(0f, raider.Steady - dt);

                if (!raider.Alive || raider.Burn <= 0f) continue;

                float span = Math.Min(dt, raider.Burn);
                raider.Burn -= span;
                raider.Smoulder += raider.BurnRate * span;

                // **The cadence, and the burn ending is a boundary too.** Counted down by the
                // span the burn really ran rather than by `dt`, so the instalment a burn's last
                // fraction of a second earns is the fraction it earned.
                raider.Sear -= span;

                bool ended = raider.Burn <= 0f;
                if (raider.Sear > 0f && !ended) continue;

                // **`+=` rather than `=`**, so a frame long enough to cross a boundary does not
                // push the next one a whole interval into the future - which on a slow phone
                // would make a burn pay fewer instalments than on a fast one, the very drift the
                // carried remainder exists to refuse.
                if (!ended) raider.Sear += SiegeTuning.BurnTick;

                int took = (int)raider.Smoulder;
                if (took < 1) continue;

                raider.Smoulder -= took;

                // Through the one door (`SiegeBoard.Fight.cs`). A burn on a boss behind its
                // guard ticks for nothing and the tick is not reported; the seconds still burn
                // down, so a burn lit before a guard is a burn the guard ate rather than one held
                // over.
                took = Wound(raider, took);
                if (took <= 0) continue;

                bool killed = Fell(raider);

                // A kill by fire pays the ward that lit it, where it still stands - the build's
                // half only, because a siphon turret's own refund has always been for a bolt.
                if (killed && !_boosts.IsIdentity
                    && raider.BurnFrom >= 0 && raider.BurnFrom < _wards.Length)
                    Fallen(_wards[raider.BurnFrom], raider.BurnFrom, raider);

                // A fire no ward lit (a meteor's, `SiegeBoard.Arsenal`) still counts as a death
                // to the attack cards. Nothing on the plain line, where no such fire is lit.
                else if (killed) Perished(raider, -1);

                _report.Burns.Add(new SiegeBurn(raider.BurnFrom, raider.Id, took, killed));
            }
        }

        /// <summary>Scratch for <see cref="Arc"/>. One list, because a bolt is resolved at once.</summary>
        readonly HashSet<int> _arced = new HashSet<int>();
    }
}
